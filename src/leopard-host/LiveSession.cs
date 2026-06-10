using System.Text;
using System.Text.Json;
using Tempo.Contracts;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>One pull observed live (since Leopard started watching — not the parsed canon).</summary>
public sealed record LivePull(
    string PullId, string Boss, int? DifficultyId, string Difficulty, bool Kill,
    int DurationMs, int PlayerDeaths, double? BossEndPct, DateTimeOffset EndedAt, int TonightIndex);

public sealed record LiveStatus(
    bool Active, string? File, DateTimeOffset? WatchingSince, DateTimeOffset? LastLineAt,
    bool InEncounter, string? CurrentBoss, IReadOnlyList<LivePull> Pulls);

/// <summary>State: none | pending | ready | error. Evidence is the EXACT text sent to the model.</summary>
public sealed record LiveInsight(
    string State, string? InsightId, LivePull? Pull, string? Evidence, string? Text,
    string? Model, string? Error, DateTimeOffset? GeneratedAt);

/// <summary>
/// The live between-pull loop: Tempo's live ingest front (FileSystemLogMonitor + CombatLogParser,
/// the ADR-018 "post-wipe directive path") feeding a pre-generated grounded insight. Leopard's
/// role is facts → evidence → inference → record; the parsing is all Tempo, in-process.
///
/// Evidence is three layers (see docs/live-insight-design-brief.md): the pull (live typed events),
/// tonight's trajectory (in-memory), the all-time career (the same fan-in + CareerSummary text the
/// Ask career zoom reads). Every lifecycle event appends one line to live-insight.jsonl — the
/// replayable record the discoverlay composer tails and the critic-loop eval corpus reads.
/// </summary>
public sealed class LiveSession
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };
    private static readonly UTF8Encoding Utf8 = new(false);

    // Compact (not indented): one JSON object per line in the jsonl.
    private static readonly JsonSerializerOptions JsonlOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<LeopardConfig> _config;
    private readonly Func<List<RaidViewEncounter>> _careerInputs;
    private readonly string _jsonlPath;
    private readonly object _jsonlLock = new();
    private readonly object _stateLock = new();

    private readonly List<LivePull> _tonight = new();
    private CancellationTokenSource? _insightCts;
    private volatile bool _restartRequested;

    // Published snapshots (immutable records; endpoints read these without locks).
    private volatile LiveStatus _status = new(false, null, null, null, false, null, Array.Empty<LivePull>());
    private volatile LiveInsight _insight = new("none", null, null, null, null, null, null, null);

    public LiveSession(Func<LeopardConfig> config, Func<List<RaidViewEncounter>> careerInputs, string dataDir)
    {
        _config = config;
        _careerInputs = careerInputs;
        _jsonlPath = Path.Combine(dataDir, "live-insight.jsonl");
    }

    public LiveStatus Status => _status;
    public LiveInsight Insight => _insight;

    public void Start(CancellationToken appStopping) => _ = Task.Run(() => RunAsync(appStopping), appStopping);

    /// <summary>Called after a config save: re-pick the log file under the (possibly new) LogDir.</summary>
    public void Restart() => _restartRequested = true;

    public void RecordFeedback(string insightId, bool? useful, bool? grounded, string? comment)
        => AppendJsonl(new
        {
            kind = "feedback", v = 1, ts = DateTimeOffset.Now,
            insightId, useful, grounded,
            comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        });

    // ── Watch loop ──────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken outer)
    {
        while (!outer.IsCancellationRequested)
        {
            string? file = PickNewestLog(_config().LogDir);
            if (file is null)
            {
                _status = new(false, null, null, null, false, null, PullsSnapshot());
                try { await Task.Delay(10_000, outer); } catch (OperationCanceledException) { return; }
                continue;
            }

            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            // Supersede watcher: WoW opens a NEW WoWCombatLog-<ts>.txt per game session, so the
            // file-level monitor (which handles same-name rotation) isn't enough — re-pick newest.
            var supersede = Task.Run(async () =>
            {
                while (!watchCts.IsCancellationRequested)
                {
                    try { await Task.Delay(15_000, watchCts.Token); } catch (OperationCanceledException) { return; }
                    if (_restartRequested || PickNewestLog(_config().LogDir) != file)
                    {
                        _restartRequested = false;
                        watchCts.Cancel();
                    }
                }
            });

            try { await WatchFileAsync(file, watchCts.Token); }
            catch (OperationCanceledException) { /* superseded or stopping; loop re-picks */ }
            catch { try { await Task.Delay(5_000, outer); } catch (OperationCanceledException) { return; } }
            finally { watchCts.Cancel(); await supersede; }
        }
    }

    private static string? PickNewestLog(string logDir)
    {
        try
        {
            if (!Directory.Exists(logDir)) return null;
            return new DirectoryInfo(logDir).GetFiles("WoWCombatLog*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    private volatile string? _currentFilePath;

    private async Task WatchFileAsync(string file, CancellationToken ct)
    {
        var since = DateTimeOffset.Now;
        _currentFilePath = file;
        var parser = new CombatLogParser();
        await using var monitor = new FileSystemLogMonitor();

        // Per-pull trackers, reset on ENCOUNTER_START.
        string? boss = null;
        int? difficultyId = null;
        DateTimeOffset pullStartedAt = default;
        int playerDeaths = 0;
        var creatureHp = new Dictionary<string, (long Cur, long Max)>(StringComparer.Ordinal);

        _status = new(false, Path.GetFileName(file), since, null, false, null, PullsSnapshot());

        await foreach (var line in monitor.WatchAsync(file, ct))
        {
            var now = DateTimeOffset.Now;
            var e = parser.ParseLine(line);
            if (e is null)
            {
                // Still count raw activity so "active" reflects a writing log, not just parsed events.
                var s0 = _status;
                _status = s0 with { Active = true, LastLineAt = now };
                continue;
            }

            switch (e.EventKind)
            {
                case EventKind.EncounterStart:
                    boss = GetString(e.Extra, "bossName") ?? e.SpellName ?? "Unknown boss";
                    difficultyId = GetInt(e.Extra, "difficultyId");
                    pullStartedAt = e.Timestamp;
                    playerDeaths = 0;
                    creatureHp.Clear();
                    _status = new(true, Path.GetFileName(file), since, now, true, boss, PullsSnapshot());
                    // The overlay composer hides the insight panel during combat — this is its cue.
                    AppendJsonl(new { kind = "encounter", v = 1, ts = now, state = "started", boss, difficulty = DifficultyName(difficultyId) });
                    break;

                case EventKind.UnitDied:
                    if (e.TargetParticipantId?.StartsWith("Player-", StringComparison.Ordinal) == true)
                        playerDeaths++;
                    break;

                case EventKind.SpellDamage:
                case EventKind.SpellPeriodicDamage:
                    // Advanced-logging HP on the damage target — the boss while the raid attacks it.
                    var guid = GetString(e.Extra, "infoGuid");
                    if (guid?.StartsWith("Creature-", StringComparison.Ordinal) == true)
                    {
                        var cur = GetLong(e.Extra, "currentHp");
                        var max = GetLong(e.Extra, "maxHp");
                        if (cur is not null && max is > 0) creatureHp[guid] = (cur.Value, max.Value);
                    }
                    break;

                case EventKind.EncounterEnd:
                {
                    var endBoss = GetString(e.Extra, "bossName") ?? boss ?? "Unknown boss";
                    var kill = GetBool(e.Extra, "success") ?? false;
                    var durationMs = GetInt(e.Extra, "fightDurationMs")
                        ?? (pullStartedAt == default ? 0 : (int)(e.Timestamp - pullStartedAt).TotalMilliseconds);
                    // Largest-maxHp creature observed during the pull = the boss (heuristic; the
                    // canonical number comes from the real parse after the night).
                    double? bossEndPct = null;
                    if (kill) bossEndPct = 0;
                    else if (creatureHp.Count > 0)
                    {
                        var hp = creatureHp.Values.OrderByDescending(v => v.Max).First();
                        bossEndPct = Math.Round(100.0 * hp.Cur / hp.Max, 1);
                    }

                    var diffName = DifficultyName(difficultyId);
                    LivePull pull;
                    lock (_stateLock)
                    {
                        int idx = _tonight.Count(p => p.Boss == endBoss && p.DifficultyId == difficultyId) + 1;
                        pull = new LivePull(
                            Guid.NewGuid().ToString("N")[..12], endBoss, difficultyId, diffName,
                            kill, durationMs, playerDeaths, bossEndPct, e.Timestamp, idx);
                        _tonight.Add(pull);
                    }

                    AppendJsonl(new
                    {
                        kind = "pull", v = 1, ts = now, pullId = pull.PullId,
                        boss = pull.Boss, difficulty = pull.Difficulty, kill = pull.Kill,
                        durationMs = pull.DurationMs, playerDeaths = pull.PlayerDeaths,
                        bossEndPct = pull.BossEndPct, tonightIndex = pull.TonightIndex,
                    });

                    boss = null;
                    _status = new(true, Path.GetFileName(file), since, now, false, null, PullsSnapshot());

                    // Freshest pull wins: cancel any in-flight generation.
                    _insightCts?.Cancel();
                    var cts = new CancellationTokenSource();
                    _insightCts = cts;
                    _ = Task.Run(() => GenerateInsightAsync(pull, cts.Token), CancellationToken.None);
                    break;
                }
            }

            if (e.EventKind is not (EventKind.EncounterStart or EventKind.EncounterEnd))
            {
                var s = _status;
                _status = s with { Active = true, LastLineAt = now };
            }
        }
    }

    // ── Insight generation ──────────────────────────────────────────────────

    private const string SystemPrompt =
        "You are Leopard, a reflection engine for a World of Warcraft raid team. A pull just ended; " +
        "this is read between pulls, so be brief: 2-4 sentences, then ONE open question worth " +
        "discussing. Restate only the figures provided - never invent numbers. If GROUP COORDINATION " +
        "data is present, name the strongest change across the pulls (followership, entropy, peak " +
        "speed, deaths, boss %) and cite its exact figures. Speak to the trajectory across pulls, " +
        "not just the last one. If the WIPE CLASSIFICATION says CALLED WIPE, the raid reset on " +
        "purpose: acknowledge it in one sentence and do NOT coach the wipe.";

    private async Task GenerateInsightAsync(LivePull pull, CancellationToken ct)
    {
        var insightId = Guid.NewGuid().ToString("N")[..12];
        var cfg = _config();
        var url = string.IsNullOrWhiteSpace(cfg.LiveInferenceUrl)
            ? "http://127.0.0.1:8080/v1/chat/completions" : cfg.LiveInferenceUrl;
        var model = string.IsNullOrWhiteSpace(cfg.LiveModel) ? "local" : cfg.LiveModel;

        // Show pending immediately — the projection parse below takes seconds on a long log.
        _insight = new("pending", insightId, pull, null, null, model, null, null);
        AppendJsonl(new { kind = "insight", v = 1, ts = DateTimeOffset.Now, insightId, pullId = pull.PullId, state = "pending" });

        // v2a: run the REAL parse + projections between pulls. The model narrates the same
        // followership/entropy/peak-speed figures the Trends tab shows — projection-grade
        // evidence, not raw-event scalars. Falls back to the v1 layers if the parse fails.
        var coordination = TryBuildCoordinationText(pull);
        if (ct.IsCancellationRequested) return;
        var evidence = BuildEvidence(pull, coordination);
        _insight = new("pending", insightId, pull, evidence, null, model, null, null);

        var started = DateTimeOffset.Now;
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = evidence + "\n\nReflect briefly and end with one question worth raising with the group." },
                },
                temperature = 0.4,
                max_tokens = 300,
                stream = false,
            }, JsonlOpts);

            using var resp = await Http.PostAsync(url, new StringContent(body, Utf8, "application/json"), ct);
            var respText = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} from {url}: {Truncate(respText, 200)}");

            using var doc = JsonDocument.Parse(respText);
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("empty completion");

            var latencyMs = (int)(DateTimeOffset.Now - started).TotalMilliseconds;
            if (ct.IsCancellationRequested) return; // superseded while finishing — newer pull owns the card
            _insight = new("ready", insightId, pull, evidence, text, model, null, DateTimeOffset.Now);
            AppendJsonl(new
            {
                kind = "insight", v = 1, ts = DateTimeOffset.Now, insightId, pullId = pull.PullId,
                state = "ready", evidence, system = SystemPrompt, model, url,
                @params = new { temperature = 0.4, max_tokens = 300 }, text, latencyMs,
            });
        }
        catch (OperationCanceledException) { /* superseded by a newer pull */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            _insight = new("error", insightId, pull, evidence, null, model, ex.Message, null);
            AppendJsonl(new
            {
                kind = "insight", v = 1, ts = DateTimeOffset.Now, insightId, pullId = pull.PullId,
                state = "error", error = ex.Message, url,
            });
        }
    }

    /// <summary>
    /// The evidence block. This exact string is what the model reads AND what the card
    /// displays (display==send, same as every other Leopard surface). When the between-pull
    /// parse succeeds, the tonight layer is the PROJECTION table (authoritative, all pulls in
    /// the log); the in-memory observed-live list is only the fallback.
    /// </summary>
    private string BuildEvidence(LivePull pull, string? coordination)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PULL (just ended): {pull.Boss} ({pull.Difficulty}) - {(pull.Kill ? "KILL" : "WIPE")} " +
                      $"in {FormatDuration(pull.DurationMs)}, {pull.PlayerDeaths} player death(s)" +
                      (pull.BossEndPct is double p && !pull.Kill ? $", boss at ~{p:0.#}% (approximate)." : "."));

        if (coordination is not null)
        {
            sb.AppendLine();
            sb.AppendLine(coordination);
        }
        else
        {
            List<LivePull> prior;
            lock (_stateLock)
            {
                prior = _tonight.Where(t => t.Boss == pull.Boss && t.DifficultyId == pull.DifficultyId
                                            && t.PullId != pull.PullId).ToList();
            }
            if (prior.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"TONIGHT on this boss (observed live; this was pull #{pull.TonightIndex}):");
                foreach (var t in prior)
                    sb.AppendLine($"  #{t.TonightIndex}: {(t.Kill ? "KILL" : "WIPE")}" +
                                  (t.BossEndPct is double bp && !t.Kill ? $" at {bp:0.#}%" : "") +
                                  $", {t.PlayerDeaths} death(s), {FormatDuration(t.DurationMs)}");
                var bestPrior = prior.Where(t => !t.Kill && t.BossEndPct is not null).Select(t => t.BossEndPct!.Value)
                                     .DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(bestPrior) && !pull.Kill && pull.BossEndPct is double cur2)
                    sb.AppendLine($"  Best before this pull: {bestPrior:0.#}%. This pull: {cur2:0.#}%.");
            }
            else
            {
                sb.AppendLine("TONIGHT: first pull observed live on this boss this session.");
            }
        }

        sb.AppendLine();
        var career = TryBuildCareerText(pull);
        sb.AppendLine(career is not null
            ? "ALL-TIME (from parsed nights; tonight not yet included):\n" + career.Trim()
            : "ALL-TIME: no parsed history for this boss yet.");
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// v2a — the projections go live. Parses the live log (the night so far) with the real
    /// ParserPipeline and renders this boss's coordination table: per-pull followership /
    /// entropy / peak speed (TrendsProjection — the same proven math the Trends tab shows)
    /// plus the windowed rule-row deltas. Returns null on any failure (the evidence falls
    /// back to the observed-live layer; raid night must not care).
    /// </summary>
    private string? TryBuildCoordinationText(LivePull pull)
    {
        var file = _currentFilePath;
        if (file is null || !File.Exists(file)) return null;
        try
        {
            var parse = ParserPipeline.Parse(file);
            var encounters = EncountersProjection.ToEncounters(parse.Sessions);

            // Resolve this boss tonight: prefer name+difficulty; accept a unique name match
            // (Tempo's difficulty strings and our id-derived names can disagree on dungeons).
            var byName = encounters.Where(e =>
                string.Equals(e.Name, pull.Boss, StringComparison.OrdinalIgnoreCase)).ToList();
            var enc = byName.FirstOrDefault(e =>
                          string.Equals(e.Difficulty, pull.Difficulty, StringComparison.OrdinalIgnoreCase))
                      ?? (byName.Count == 1 ? byName[0] : null);
            if (enc is null) return null;

            var sb = new StringBuilder();
            if (TrendsProjection.TryBuildCoherenceWindow(encounters, parse.ReplaysByPullId, enc.Id, 10, out var coh)
                && coh.Points.Count > 0)
            {
                sb.AppendLine("GROUP COORDINATION tonight on this boss, per pull (followership 0-1, " +
                              "higher = moving together; entropy, higher = scattered; peak speed yd/s):");
                foreach (var pt in coh.Points)
                {
                    var line = $"  #{pt.PullN}: {pt.Outcome}, boss {pt.BossEndPctHp:0.#}%, " +
                               $"{pt.Deaths} deaths, {pt.DurationSec}s";
                    if (pt.FollowershipMean is double f) line += $", followership {f:0.00}";
                    if (pt.EntropyMean is double en) line += $", entropy {en:0.00}";
                    if (pt.PeakSpeed is double ps) line += $", peak speed {ps:0.0}";
                    sb.AppendLine(line);
                }

                // v2b — the six-signal pack (the RaidUI DiagStrip port): healer coverage and
                // spacing per pull, with the collapse moments (snaps) called out by the second.
                var covLines = new List<string>();
                var sigByPull = new Dictionary<string, PullSignalsDto>(StringComparer.Ordinal);
                foreach (var pt in coh.Points)
                {
                    if (!parse.ReplaysByPullId.TryGetValue(pt.PullId, out var replay) || replay.Frames.Count == 0)
                        continue;
                    PullSignalsDto sig;
                    try { sig = SignalsArtifact.BuildForReplay(replay); }
                    catch { continue; }
                    sigByPull[pt.PullId] = sig;
                    var a = sig.Aggregates;
                    var line = $"  #{pt.PullN}: coverage avg {a.CoverageAvg * 100:0}%, " +
                               $"min {a.CoverageMin * 100:0}% at {sig.Signals["coverage"].Peak.AtSec}s";
                    if (a.FragileSec > 0) line += $", fragile {a.FragileSec:0}s";
                    if (sig.Snaps.Count > 0)
                    {
                        var biggest = sig.Snaps.OrderByDescending(s => s.DropPct).First();
                        line += $", {sig.Snaps.Count} coverage snap(s), biggest -{biggest.DropPct}pp at {biggest.AtSec}s";
                    }
                    if (a.SpacingTightest > 0) line += $", tightest spacing {a.SpacingTightest:0.0}yd";
                    covLines.Add(line);
                }
                if (covLines.Count > 0)
                {
                    sb.AppendLine("HEALER COVERAGE & SPACING per pull (coverage = share of living " +
                                  "players within 30yd of a living healer; a snap = sudden coverage " +
                                  "collapse; fragile = seconds below 60% covered):");
                    foreach (var l in covLines) sb.AppendLine(l);
                }

                // The diff port (RaidUI DiffLens): contrast the just-ended pull against the best
                // prior pull tonight — any kill, else the deepest wipe. The single most pointed
                // question a between-pull card can ground: "what did the best one have?"
                var current = coh.Points[^1];
                var prior2 = coh.Points.Take(coh.Points.Count - 1).ToList();
                if (prior2.Count > 0)
                {
                    var best = prior2.LastOrDefault(p => string.Equals(p.Outcome, "kill", StringComparison.OrdinalIgnoreCase))
                               ?? prior2.OrderBy(p => p.BossEndPctHp).First();
                    var pullL = enc.Pulls.FirstOrDefault(p => p.Id == best.PullId);
                    var pullR = enc.Pulls.FirstOrDefault(p => p.Id == current.PullId);
                    if (pullL is not null && pullR is not null && best.PullId != current.PullId)
                    {
                        var diff = PullDiff.Build(enc.Name, enc.Difficulty, pullL, pullR,
                            sigByPull.GetValueOrDefault(best.PullId)?.Aggregates,
                            sigByPull.GetValueOrDefault(current.PullId)?.Aggregates);
                        sb.AppendLine($"THIS PULL (#{current.PullN}) vs YOUR BEST TONIGHT (#{best.PullN}, {best.Outcome}):");
                        sb.AppendLine($"  {diff.RuleHeadline}");
                        foreach (var m in diff.Metrics.Where(m => m.Wired))
                            sb.AppendLine($"  {m.Label}: {m.L}{m.Unit} -> {m.R}{m.Unit}" +
                                          (m.Dir != "flat" ? $" ({m.Dir})" : ""));
                    }
                }

                // The classify port (RaidUI rule tree, ADR-003/005/008): a deterministic verdict
                // on the just-ended wipe — including the called-wipe gate, so the model doesn't
                // earnestly coach an intentional reset.
                if (!string.Equals(current.Outcome, "kill", StringComparison.OrdinalIgnoreCase)
                    && parse.ReplaysByPullId.TryGetValue(current.PullId, out var curReplay)
                    && sigByPull.TryGetValue(current.PullId, out var curSig))
                {
                    Classification? cls = null;
                    try
                    {
                        cls = WipeClassifier.Classify(curReplay, WipeClassifier.AdaptSignals(curSig),
                            current.Outcome, current.BossEndPctHp);
                    }
                    catch { /* classification is additive evidence; never sink the card */ }

                    if (cls is not null)
                    {
                        if (cls.Kind == "called-wipe")
                        {
                            sb.AppendLine($"WIPE CLASSIFICATION (deterministic rule tree): CALLED WIPE " +
                                          $"({cls.CalledWipePattern}) - the raid reset on purpose; do not " +
                                          $"analyze this wipe as a failure.");
                        }
                        else
                        {
                            var line = $"WIPE CLASSIFICATION (deterministic rule tree): {cls.Kind} collapse, " +
                                       $"confidence {cls.Confidence}, onset ~{cls.InflectionMs / 1000}s";
                            if (cls.Affected.Count > 0 && cls.Kind != "systemic")
                                line += $"; most implicated: {string.Join(", ", cls.Affected.Take(4))}";
                            sb.AppendLine(line);
                            if (cls.Evidence.Count > 0)
                                sb.AppendLine("  top evidence: " +
                                    string.Join("; ", cls.Evidence.Take(3).Select(e => e.Reason)));
                        }
                    }
                }
            }
            if (TrendsProjection.TryBuildTrendsWindow(encounters, enc.Id, 6, out var win)
                && win.RuleRows.Count > 0)
            {
                sb.AppendLine("RECENT WINDOW vs PREVIOUS (deterministic deltas from the parser):");
                foreach (var r in win.RuleRows)
                    sb.AppendLine($"  {r.Label}: {r.Value} ({r.Delta} {r.Dir})");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
        }
        catch { return null; }
    }

    private string? TryBuildCareerText(LivePull pull)
    {
        try
        {
            var all = _careerInputs();
            var careerId = all.FirstOrDefault(e =>
                string.Equals(e.Name, pull.Boss, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Difficulty, pull.Difficulty, StringComparison.OrdinalIgnoreCase))?.CareerId;
            if (careerId is null) return null;
            var text = CareerSummary.Build(all, careerId);
            return text.StartsWith("(no career", StringComparison.Ordinal) ? null : text;
        }
        catch { return null; }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private IReadOnlyList<LivePull> PullsSnapshot() { lock (_stateLock) return _tonight.ToArray(); }

    private void AppendJsonl(object record)
    {
        try
        {
            var line = JsonSerializer.Serialize(record, JsonlOpts) + "\n";
            lock (_jsonlLock) File.AppendAllText(_jsonlPath, line, Utf8);
        }
        catch { /* the loop must survive a locked/full disk; the in-memory card still works */ }
    }

    private static string DifficultyName(int? id) => id switch
    {
        14 => "Normal", 15 => "Heroic", 16 => "Mythic", 17 => "LFR",          // raid
        1 => "Normal", 2 => "Heroic", 23 => "Mythic", 8 => "Mythic Keystone", // dungeon
        24 => "Timewalking",
        null => "Unknown", _ => $"Difficulty {id}",
    };

    private static string FormatDuration(int ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m" : $"{t.Minutes}m{t.Seconds:00}s";
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static string? GetString(Dictionary<string, object?>? x, string k)
        => x is not null && x.TryGetValue(k, out var v) ? v as string : null;
    private static int? GetInt(Dictionary<string, object?>? x, string k)
        => x is not null && x.TryGetValue(k, out var v) ? v switch { int i => i, long l => (int)l, _ => null } : null;
    private static long? GetLong(Dictionary<string, object?>? x, string k)
        => x is not null && x.TryGetValue(k, out var v) ? v switch { long l => l, int i => i, _ => null } : null;
    private static bool? GetBool(Dictionary<string, object?>? x, string k)
        => x is not null && x.TryGetValue(k, out var v) && v is bool b ? b : null;
}
