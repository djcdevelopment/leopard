using Tempo.Core.Ingest;

namespace Leopard.Host;

/// <summary>The classifier's per-signal input: a "badness" series (higher = tighter/worse)
/// plus the signal's worst moment and onset. Adapted from <see cref="SignalsArtifact"/>'s
/// per-second output — see <see cref="WipeClassifier.AdaptSignals"/> for the mapping.</summary>
public sealed record ComputedSignal(
    string Id, IReadOnlyList<double> Series, double Tightest, int TightestAtMs, int? InflectionMs);

public sealed record ClassificationEvidence(string SignalId, double Value, string Reason);

/// <summary>The healer whose behavior drove a classified coverage pattern.</summary>
public sealed record CoverageOffenderDto(string EntityId, string DisplayName, int AtMs);

/// <summary>kind: systemic | subgroup | individual | called-wipe. Affected carries entity
/// display short-names. CalledWipePattern is set only for called-wipes
/// (late-throughput | synchronized-reset | early-reset). CoveragePattern
/// (snap | tank-dip | edge-off) and Offender are set only when coverage is the top
/// evidence signal and the quality model resolved them (the 2026-04-23 follow-ups).</summary>
public sealed record Classification(
    string Kind, string Confidence, IReadOnlyList<string> Affected, int InflectionMs,
    IReadOnlyList<ClassificationEvidence> Evidence, string? CalledWipePattern,
    string? CoveragePattern = null, CoverageOffenderDto? Offender = null);

/// <summary>
/// The wipe-classification rule tree, ported from RaidUI's <c>classify.js</c>
/// (ADR-003 §3, ADR-005, ADR-008 — including the 2026-04-22 retune constants). Pure rules,
/// no probabilities: outcome/duration/death gates, the three called-wipe patterns, consensus
/// inflection, fatality-shortcut tiering, per-player z-score attribution, and signal-alignment
/// confidence. Third item of the 2026-06-09 unported-math audit.
///
/// <para>Deliberate deviations, documented: (1) signals come from the per-SECOND
/// <see cref="SignalsArtifact"/> pack via <see cref="AdaptSignals"/>, not the per-frame JS
/// ComputedSignal layer — every rule threshold here is ≥1.5s, so second resolution holds;
/// (2) the coverage-pattern tags and per-healer offender attribution are NOT ported — they
/// read the coverage-timeline quality model (per-healer centrality / edge-proximity), which
/// is the v2c port. The evidence coverage-gate uses the fragile-fraction branch only.</para>
/// </summary>
public static class WipeClassifier
{
    private const int InflectW = 1500;        // ms window around the consensus inflection
    private const int WarmupMs = 10_000;      // inflections before this are pull-open noise
    private const int MinDurationMs = 10_000;
    private const int AlignW = 45_000;        // ADR-005 §7
    private const double FatalFull = 0.90;
    private const double FatalSystemic = 0.75;
    private const double StrongContribK = 1.5;

    // ADR-008 called-wipe constants (2026-04-22 retune values).
    private const int CwClusterWindowMs = 3000;
    private const double CwLateTailFrac = 0.10;
    private const int CwPlateauLookbackMs = 15_000;
    private const double CwPlateauMaxDropPct = 2;
    private const double CwLateBossEndPctMax = 25;
    private const double CwLateClusterShare = 0.75;
    private const int CwSyncPreWindowMs = 10_000;
    private const double CwSyncRosterRatio = 0.75;
    private const double CwEarlyHeadFrac = 0.15;
    private const int CwEarlyMaxDurationMs = 45_000;

    private static readonly Dictionary<string, string> Phrases = new(StringComparer.Ordinal)
    {
        ["group-spacing"] = "spacing collapsed",
        ["deaths-per-sec"] = "deaths in a 3 s window",
        ["movement-entropy"] = "movement diverged",
        ["hp-variance"] = "HP spread opened",
        ["followership"] = "raid scattered",
        ["coverage"] = "coverage quality dropped",
    };

    // ── Signal adapter ──────────────────────────────────────────────────────

    /// <summary>
    /// Adapts the per-second signal pack into the classifier's "badness" convention
    /// (higher = worse), mirroring the JS diagnostics signals' directions:
    /// coverage → 1−value (snap second as onset, worst-coverage second as fallback);
    /// entropy / hpVariance / deathsPerSec → raw (peak = worst); followership → 1−value
    /// (scattered = bad); spacing → 1 − value/maxObserved (stacked = tight). Onset for the
    /// non-coverage, non-deaths signals = their worst second (the JS per-signal inflection
    /// detectors ride with the v2c port).
    /// </summary>
    public static IReadOnlyList<ComputedSignal> AdaptSignals(PullSignalsDto sig)
    {
        var outp = new List<ComputedSignal>();

        // Onset = the first RISE of >= 0.15 second-over-second, mirroring the JS signals'
        // jump detectors. A series that starts (and stays) bad has no onset — constant
        // badness must not register as an inflection at 0ms (that's pull-open state, and a
        // phantom inflection there blocks the called-wipe "no build-up" checks).
        static int? FirstJumpMs(double[] series)
        {
            for (var i = 1; i < series.Length; i++)
                if (series[i] - series[i - 1] >= 0.15) return i * 1000;
            return null;
        }

        ComputedSignal Make(string id, Func<double?, double> badness, string from, int? inflectionMs = null)
        {
            var src = sig.Signals[from].Values;
            var series = new double[src.Count];
            for (var i = 0; i < src.Count; i++) series[i] = badness(src[i]);
            var tightest = 0.0; var at = 0;
            for (var i = 0; i < series.Length; i++)
                if (series[i] > tightest) { tightest = series[i]; at = i; }
            return new ComputedSignal(id, series, tightest, at * 1000, inflectionMs ?? FirstJumpMs(series));
        }

        // No healers in the pull = coverage is structurally undefined, not maximally bad —
        // the JS signal emitted a zero-series that contributes nothing (same convention).
        if (sig.HealerCount == 0)
            outp.Add(new ComputedSignal("coverage",
                new double[sig.Signals["coverage"].Values.Count], 0, 0, null));
        else
            outp.Add(Make("coverage", v => 1 - (v ?? 1), "coverage",
                sig.Snaps.Count > 0 ? sig.Snaps[0].AtSec * 1000 : null));
        outp.Add(Make("deaths-per-sec", v => v ?? 0, "deathsPerSec"));
        outp.Add(Make("movement-entropy", v => v ?? 0, "entropy"));
        outp.Add(Make("hp-variance", v => v ?? 0, "hpVariance"));
        outp.Add(Make("followership", v => v is null ? 0 : 1 - Math.Min(1, v.Value), "followership"));

        var spacingVals = sig.Signals["spacing"].Values;
        var maxSpacing = spacingVals.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        outp.Add(Make("group-spacing",
            v => v is null || maxSpacing <= 0 ? 0 : 1 - v.Value / maxSpacing, "spacing"));

        return outp;
    }

    // ── Main classify ───────────────────────────────────────────────────────

    public static Classification? Classify(
        PullReplay replay, IReadOnlyList<ComputedSignal> signals,
        string outcome, double? bossEndPctHp, CoverageSeriesDto? coverage = null)
    {
        // (1) Outcome gate — kills aren't classified.
        if (!string.Equals(outcome, "wipe", StringComparison.OrdinalIgnoreCase)) return null;

        // (2) Duration gate — under 10s is a phantom pull.
        var durationMs = replay.Frames.Count > 0 ? replay.Frames[^1].T : 0;
        if (durationMs < MinDurationMs) return null;

        var players = replay.Entities.Where(e => e.Kind == "Player").ToList();
        var deathTimes = CollectPlayerDeathTimes(replay);
        if (deathTimes.Count == 0) return null;

        // ADR-008 called-wipe pre-check — before warm-up / fatality / rule tree.
        var calledPattern = DetectCalledWipePattern(replay, signals, deathTimes, durationMs,
            players.Count, bossEndPctHp);
        if (calledPattern is not null)
        {
            var cluster = FindDeathCluster(deathTimes, CwClusterWindowMs);
            var inflCw = cluster is not null ? (double)cluster.Value.Start
                : signals.Where(s => s.InflectionMs is not null).Select(s => (double)s.InflectionMs!.Value)
                    .DefaultIfEmpty(deathTimes[0]).ToList().Median();
            return new Classification("called-wipe", "high", Array.Empty<string>(),
                (int)Math.Round(inflCw), Array.Empty<ClassificationEvidence>(), calledPattern);
        }

        // (3) Warm-up gate.
        int? EffInflection(ComputedSignal s)
            => s.InflectionMs is int v && v >= WarmupMs ? v : null;

        // Consensus inflection: deaths-per-sec preferred → median of the rest →
        // first player death → pull midpoint.
        int inflectionMs;
        var deathsSig = signals.FirstOrDefault(s => s.Id == "deaths-per-sec");
        var inflections = signals.Select(EffInflection).Where(v => v is not null)
            .Select(v => (double)v!.Value).ToList();
        if (deathsSig is not null && EffInflection(deathsSig) is int dInf)
            inflectionMs = dInf;
        else if (inflections.Count > 0)
            inflectionMs = (int)Math.Round(inflections.Median());
        else
            inflectionMs = (int)deathTimes[0];

        // (6) Fatality shortcut tiering.
        var diedIds = CollectDiedPlayerIds(replay);
        var fatalityRatio = players.Count > 0 ? (double)diedIds.Count / players.Count : 0;

        string kind;
        List<string> affectedIds;
        if (fatalityRatio >= FatalFull)
        {
            kind = "systemic";
            affectedIds = players.Select(p => p.EntityId).ToList();
        }
        else if (fatalityRatio >= FatalSystemic)
        {
            kind = "systemic";
            var (scores, ids) = ComputeScores(replay, signals, inflectionMs, InflectW);
            affectedIds = TopQuartile(scores, ids);
            if (affectedIds.Count == 0) affectedIds = diedIds.ToList();
        }
        else
        {
            var (scores, ids) = ComputeScores(replay, signals, inflectionMs, InflectW);
            affectedIds = AffectedFromScores(scores, ids);
            var ratio = players.Count > 0 ? (double)affectedIds.Count / players.Count : 0;
            kind = ratio >= 0.75 ? "systemic"
                : ratio >= 0.20 ? "subgroup"
                : affectedIds.Count <= 2 ? "individual" : "subgroup";
        }

        // (7) Confidence — aligned signals within ALIGN_W; "high" needs 2+ strong.
        var aligned = 0; var strongAligned = 0;
        foreach (var s in signals)
        {
            var byInflection = EffInflection(s) is int ei && Math.Abs(ei - inflectionMs) <= AlignW;
            var byPeak = Math.Abs(s.TightestAtMs - inflectionMs) <= AlignW;
            if (byInflection || byPeak)
            {
                aligned++;
                if (IsStrongContributor(s)) strongAligned++;
            }
        }
        var confidence = strongAligned >= 2 ? "high" : aligned >= 2 ? "med" : "low";

        var evidence = BuildEvidence(signals, durationMs, inflectionMs, coverage);

        // Affected entityIds → short display names (first token before the hyphen).
        var nameById = replay.Entities.ToDictionary(e => e.EntityId, e => ShortName(e.DisplayName));
        var affected = affectedIds.Select(id => nameById.GetValueOrDefault(id, id)).ToList();

        // Coverage-pattern tag + named-healer offender (the 2026-04-23 follow-ups) — only
        // when coverage is the TOP evidence signal, same gate as the JS.
        string? coveragePattern = null;
        CoverageOffenderDto? offender = null;
        if (coverage is not null && evidence.Count > 0 && evidence[0].SignalId == "coverage")
        {
            coveragePattern = DetectCoveragePattern(coverage, inflectionMs);
            if (coveragePattern is not null)
                offender = DetectOffendingHealer(coveragePattern, coverage, inflectionMs, replay);
        }

        return new Classification(kind, confidence, affected, inflectionMs, evidence, null,
            coveragePattern, offender);
    }

    // ── Coverage pattern + offender (v2c — reads the CoverageTimeline quality model) ──

    /// <summary>snap (quality collapse → damage), tank-dip (tanks uniquely lost cover while
    /// the raid held), or edge-off (healers anchored, raid drifted to rim-of-range).</summary>
    public static string? DetectCoveragePattern(CoverageSeriesDto cov, int inflectionMs)
    {
        var s = cov.Summary;
        // Pattern A (priority): a damage-followed snap within ±10s of the inflection.
        foreach (var snap in s.SnappingPoints)
            if (snap.FollowedByDamageMs is not null && Math.Abs(snap.TimeMs - inflectionMs) <= 10_000)
                return "snap";
        // Pattern B: tanks dipped ≥20pp below the raid and under 60%. Guard: a pull with no
        // tanks cannot tank-dip (T=0 reads tankPct 0 every frame — a degenerate input the JS
        // original never met because real rosters always carry tanks).
        var hasTanks = cov.Frames.Count > 0 && cov.Frames[0].Tank.Total > 0;
        if (hasTanks && s.MinRaidPct - s.MinTankPct >= 20 && s.MinTankPct < 60) return "tank-dip";
        // Pattern C: chronically poor quality with no discrete snap → raid moved wrong.
        if (s.AvgQualityScore < 70 || s.MinQualityScore < 50) return "edge-off";
        return null;
    }

    /// <summary>Per-pattern named-healer attribution. snap = biggest centrality drop between
    /// the pre-drop quality-high frame and the snap frame; tank-dip = the healer who covered
    /// the tank before the dip and doesn't at it (highest prior centrality), else worst
    /// edge-proximity at the dip; edge-off = highest mean edge-proximity over ±3s.</summary>
    public static CoverageOffenderDto? DetectOffendingHealer(
        string pattern, CoverageSeriesDto cov, int inflectionMs, PullReplay replay)
    {
        var frames = cov.Frames;
        if (frames.Count == 0) return null;
        var stepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        int FrameFor(int t) => Math.Clamp(t / stepMs, 0, frames.Count - 1);
        string NameOf(string id) => ShortName(
            replay.Entities.FirstOrDefault(e => e.EntityId == id)?.DisplayName ?? id);

        if (pattern == "snap")
        {
            SnappingPointDto? pick = null;
            foreach (var s in cov.Summary.SnappingPoints)
            {
                if (Math.Abs(s.TimeMs - inflectionMs) > 10_000) continue;
                if (pick is null) { pick = s; continue; }
                var sDmg = s.FollowedByDamageMs is not null;
                var pDmg = pick.FollowedByDamageMs is not null;
                if (sDmg && !pDmg) { pick = s; continue; }
                if (sDmg == pDmg && Math.Abs(s.TimeMs - inflectionMs) < Math.Abs(pick.TimeMs - inflectionMs))
                    pick = s;
            }
            if (pick is null) return null;
            var snapFi = FrameFor(pick.TimeMs);
            var fromFi = Math.Max(0, snapFi - 10);
            var preFi = fromFi; var preQ = -1;
            for (var k = fromFi; k < snapFi; k++)
                if (frames[k].Quality.OverallScore > preQ) { preQ = frames[k].Quality.OverallScore; preFi = k; }
            var preBy = frames[preFi].PerHealer.ToDictionary(x => x.HealerId);
            string? bestId = null; var bestDrop = double.NegativeInfinity; var bestCovDrop = double.NegativeInfinity;
            foreach (var hNow in frames[snapFi].PerHealer)
            {
                if (!preBy.TryGetValue(hNow.HealerId, out var hPre)) continue;
                var cDrop = hPre.Centrality - hNow.Centrality;
                var covDrop = hPre.CoveredCount - (double)hNow.CoveredCount;
                if (cDrop > bestDrop || (cDrop == bestDrop && covDrop > bestCovDrop))
                { bestId = hNow.HealerId; bestDrop = cDrop; bestCovDrop = covDrop; }
            }
            return bestId is not null && bestDrop > 0
                ? new CoverageOffenderDto(bestId, NameOf(bestId), pick.TimeMs) : null;
        }

        if (pattern == "tank-dip")
        {
            var tankIds = replay.Entities
                .Where(e => e.Kind == "Player" && e.Role?.Contains("tank", StringComparison.OrdinalIgnoreCase) == true)
                .Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);
            if (tankIds.Count == 0) return null;
            var dipFi = -1; var dipPct = double.PositiveInfinity;
            var lo = inflectionMs - 3000; var hi = inflectionMs + 3000;
            for (var fi = 0; fi < frames.Count; fi++)
            {
                if (frames[fi].TimeMs < lo || frames[fi].TimeMs > hi) continue;
                if (frames[fi].Tank.Pct < dipPct) { dipPct = frames[fi].Tank.Pct; dipFi = fi; }
            }
            if (dipFi < 0)
                for (var fi = 0; fi < frames.Count; fi++)
                    if (frames[fi].Tank.Pct < dipPct) { dipPct = frames[fi].Tank.Pct; dipFi = fi; }
            if (dipFi < 0) return null;
            var refFi = Math.Max(0, dipFi - 15);

            // Healer-covers-tank via replay positions + default normalized range (the same
            // formula the reducer uses; the per-frame dto doesn't carry the raider id list).
            var arenaAvg = (replay.ArenaYd.Width + replay.ArenaYd.Height) / 2;
            var rangeNorm = arenaAvg > 0 ? CoverageTimeline.DefaultRangeYd / arenaAvg : 0;
            var idxById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < replay.Entities.Count; i++) idxById[replay.Entities[i].EntityId] = i;
            bool CoversTank(string healerId, int fi)
            {
                if (!idxById.TryGetValue(healerId, out var hIdx) || rangeNorm <= 0) return false;
                var pos = replay.Frames[Math.Min(fi, replay.Frames.Count - 1)].EntityPositions;
                var hx = pos[hIdx * 2]; var hy = pos[hIdx * 2 + 1];
                foreach (var tid in tankIds)
                {
                    if (!idxById.TryGetValue(tid, out var tIdx)) continue;
                    var dx = hx - pos[tIdx * 2]; var dy = hy - pos[tIdx * 2 + 1];
                    if (Math.Sqrt(dx * dx + dy * dy) <= rangeNorm) return true;
                }
                return false;
            }

            var droppers = frames[dipFi].PerHealer
                .Where(x => CoversTank(x.HealerId, refFi) && !CoversTank(x.HealerId, dipFi))
                .Select(x => x.HealerId).ToList();
            if (droppers.Count > 0)
            {
                var preBy = frames[refFi].PerHealer.ToDictionary(x => x.HealerId);
                var best = droppers.OrderByDescending(id => preBy.GetValueOrDefault(id)?.Centrality ?? 0).First();
                return new CoverageOffenderDto(best, NameOf(best), frames[dipFi].TimeMs);
            }
            var worst = frames[dipFi].PerHealer.OrderByDescending(x => x.EdgeProximityOfCovered).FirstOrDefault();
            return worst is not null
                ? new CoverageOffenderDto(worst.HealerId, NameOf(worst.HealerId), frames[dipFi].TimeMs) : null;
        }

        if (pattern == "edge-off")
        {
            var lo = inflectionMs - 3000; var hi = inflectionMs + 3000;
            var sum = new Dictionary<string, double>(StringComparer.Ordinal);
            var cnt = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fr in frames)
            {
                if (fr.TimeMs < lo || fr.TimeMs > hi) continue;
                foreach (var x in fr.PerHealer)
                {
                    sum[x.HealerId] = sum.GetValueOrDefault(x.HealerId) + x.EdgeProximityOfCovered;
                    cnt[x.HealerId] = cnt.GetValueOrDefault(x.HealerId) + 1;
                }
            }
            if (sum.Count == 0) return null;
            var bestId = sum.OrderByDescending(kv => kv.Value / Math.Max(1, cnt[kv.Key])).First().Key;
            var atMs = inflectionMs; var atEdge = double.NegativeInfinity;
            foreach (var fr in frames)
            {
                if (fr.TimeMs < lo || fr.TimeMs > hi) continue;
                var mine = fr.PerHealer.FirstOrDefault(x => x.HealerId == bestId);
                if (mine is not null && mine.EdgeProximityOfCovered > atEdge)
                { atEdge = mine.EdgeProximityOfCovered; atMs = fr.TimeMs; }
            }
            return new CoverageOffenderDto(bestId, NameOf(bestId), atMs);
        }

        return null;
    }

    // ── Called-wipe detection (ADR-008) ─────────────────────────────────────

    private static string? DetectCalledWipePattern(PullReplay replay,
        IReadOnlyList<ComputedSignal> signals, List<long> deathTimes,
        int durationMs, int rosterSize, double? bossEndPctHp)
    {
        if (deathTimes.Count == 0) return null;
        var cluster = FindDeathCluster(deathTimes, CwClusterWindowMs);
        if (cluster is null) return null;
        var (cStart, cEnd, cCount) = cluster.Value;
        var clusterSpan = cEnd - cStart;

        // Pattern A — late-fight throughput wipe: the cluster IS the mortality, in the last
        // 10%, boss under 25% and plateaued for the prior 15s.
        var tailStart = durationMs * (1 - CwLateTailFrac);
        var clusterShare = (double)cCount / deathTimes.Count;
        if (cCount >= 2 && cStart >= tailStart && clusterShare >= CwLateClusterShare
            && clusterSpan <= CwClusterWindowMs
            && bossEndPctHp is double pct && pct < CwLateBossEndPctMax
            && HasBossHpPlateau(replay, cStart, CwPlateauLookbackMs, CwPlateauMaxDropPct))
            return "late-throughput";

        // Pattern B — synchronized reset: ≥75% of the roster dies inside one ≤3s cluster
        // with no STRONG signal inflection in the 10s before it.
        if (cCount >= 2 && clusterSpan <= CwClusterWindowMs && rosterSize > 0
            && (double)cCount / rosterSize >= CwSyncRosterRatio)
        {
            var preLo = cStart - CwSyncPreWindowMs;
            var strongInWindow = signals.Any(s => s.InflectionMs is int inf
                && inf >= preLo && inf < cStart && IsStrongContributor(s));
            if (!strongInWindow) return "synchronized-reset";
        }

        // Pattern C — early-pull reset: first death in the first 15% of a <45s pull with
        // zero signal inflections before it.
        var firstDeath = deathTimes[0];
        if (durationMs < CwEarlyMaxDurationMs
            && firstDeath < durationMs * CwEarlyHeadFrac
            && !signals.Any(s => s.InflectionMs is int inf2 && inf2 >= 0 && inf2 < firstDeath))
            return "early-reset";

        return null;
    }

    private static (long Start, long End, int Count)? FindDeathCluster(List<long> times, int windowMs)
    {
        if (times.Count == 0) return null;
        if (times.Count == 1) return (times[0], times[0], 1);
        long bestStart = times[0], bestEnd = times[0];
        var bestCount = 1; var lo = 0;
        for (var hi = 0; hi < times.Count; hi++)
        {
            while (times[hi] - times[lo] > windowMs) lo++;
            var count = hi - lo + 1;
            if (count > bestCount) { bestCount = count; bestStart = times[lo]; bestEnd = times[hi]; }
        }
        return (bestStart, bestEnd, bestCount);
    }

    /// <summary>Boss HP plateau check over the lookback window before <paramref name="tMs"/> —
    /// the boss entity is the largest-maxHp non-player; HP series from frame EntityHp
    /// (normalized fractions × 100).</summary>
    private static bool HasBossHpPlateau(PullReplay replay, long tMs, int lookbackMs, double maxDropPct)
    {
        var bossIdx = -1;
        for (var i = 0; i < replay.Entities.Count; i++)
            if (replay.Entities[i].Kind == "Boss") { bossIdx = i; break; }
        if (bossIdx < 0) return false;

        var stepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        var loMs = tMs - lookbackMs;
        if (loMs < 0) return false;
        var loFi = (int)(loMs / stepMs);
        var hiFi = Math.Min(replay.Frames.Count - 1, (int)(tMs / stepMs));
        if (hiFi - loFi < 2) return false;

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var any = false;
        for (var fi = loFi; fi <= hiFi; fi++)
        {
            var hp = replay.Frames[fi].EntityHp;
            if (hp is null || bossIdx >= hp.Count) continue;
            var pct = hp[bossIdx] <= 1 ? hp[bossIdx] * 100 : hp[bossIdx];
            any = true;
            if (pct < min) min = pct;
            if (pct > max) max = pct;
        }
        return any && (max - min) < maxDropPct;
    }

    // ── Attribution (computeAffectedWithScores port) ────────────────────────

    /// <summary>Per-player z-score contributions at the inflection window: HP-below-mean,
    /// distance-from-centroid, speed-deviation-from-median, anti-followership (1 − mean dot
    /// with close co-movers), and a +2 bump for dying inside ±W. Direct port — this section
    /// works on replay frames and needed no adaptation.</summary>
    private static (double[] Scores, List<string> Ids) ComputeScores(
        PullReplay replay, IReadOnlyList<ComputedSignal> signals, int inflectionMs, int w)
    {
        var playerIdx = new List<int>();
        for (var i = 0; i < replay.Entities.Count; i++)
            if (replay.Entities[i].Kind == "Player") playerIdx.Add(i);
        var p = playerIdx.Count;
        var ids = playerIdx.Select(i => replay.Entities[i].EntityId).ToList();
        if (p == 0) return (Array.Empty<double>(), ids);

        var stepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        int FrameFor(int t) => Math.Clamp(t / stepMs, 0, replay.Frames.Count - 1);
        var fiC = FrameFor(inflectionMs);
        var frameC = replay.Frames[fiC];
        var scores = new double[p];
        var has = signals.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        // hp-variance: low HP at the center frame contributes most.
        if (has.Contains("hp-variance") && frameC.EntityHp is { } hp)
        {
            var vals = new List<(int P, double V)>();
            for (var k = 0; k < p; k++)
                if (playerIdx[k] < hp.Count) vals.Add((k, hp[playerIdx[k]]));
            if (vals.Count >= 2)
            {
                var m = vals.Average(x => x.V);
                var sd = Math.Sqrt(vals.Sum(x => (x.V - m) * (x.V - m)) / vals.Count);
                if (sd == 0) sd = 1;
                foreach (var (pk, v) in vals)
                    scores[pk] += Math.Clamp((m - v) / sd, 0, 5);
            }
        }

        // group-spacing: distance from the centroid — far = standing alone.
        if (has.Contains("group-spacing"))
        {
            var pos = frameC.EntityPositions;
            double cx = 0, cy = 0;
            for (var k = 0; k < p; k++) { cx += pos[playerIdx[k] * 2]; cy += pos[playerIdx[k] * 2 + 1]; }
            cx /= p; cy /= p;
            var d = new double[p];
            for (var k = 0; k < p; k++)
            {
                var dx = pos[playerIdx[k] * 2] - cx;
                var dy = pos[playerIdx[k] * 2 + 1] - cy;
                d[k] = Math.Sqrt(dx * dx + dy * dy);
            }
            AddZScores(scores, d);
        }

        // movement-entropy: |speed − median speed| at the center frame.
        if (has.Contains("movement-entropy") && fiC >= 1)
        {
            var prev = replay.Frames[fiC - 1].EntityPositions;
            var cur = frameC.EntityPositions;
            var sp = new double[p];
            for (var k = 0; k < p; k++)
            {
                var dx = cur[playerIdx[k] * 2] - prev[playerIdx[k] * 2];
                var dy = cur[playerIdx[k] * 2 + 1] - prev[playerIdx[k] * 2 + 1];
                sp[k] = Math.Sqrt(dx * dx + dy * dy) / stepMs;
            }
            var med = sp.OrderBy(x => x).ToArray()[p >> 1];
            AddZScores(scores, sp.Select(s => Math.Abs(s - med)).ToArray());
        }

        // followership: 1 − mean velocity-dot with close co-movers (the odd one out).
        if (has.Contains("followership") && fiC >= 1)
        {
            var prev = replay.Frames[fiC - 1].EntityPositions;
            var cur = frameC.EntityPositions;
            const double dGate = 0.10; const double eps = 1e-4;
            var vx = new double[p]; var vy = new double[p]; var spd = new double[p];
            for (var k = 0; k < p; k++)
            {
                vx[k] = cur[playerIdx[k] * 2] - prev[playerIdx[k] * 2];
                vy[k] = cur[playerIdx[k] * 2 + 1] - prev[playerIdx[k] * 2 + 1];
                spd[k] = Math.Sqrt(vx[k] * vx[k] + vy[k] * vy[k]) / stepMs;
            }
            var odd = new double[p];
            for (var a = 0; a < p; a++)
            {
                if (spd[a] < eps) { odd[a] = 1; continue; }
                double sumDot = 0; var pairs = 0;
                var xa = cur[playerIdx[a] * 2]; var ya = cur[playerIdx[a] * 2 + 1];
                for (var b = 0; b < p; b++)
                {
                    if (b == a || spd[b] < eps) continue;
                    var dx = xa - cur[playerIdx[b] * 2];
                    var dy = ya - cur[playerIdx[b] * 2 + 1];
                    if (dx * dx + dy * dy > dGate * dGate) continue;
                    sumDot += (vx[a] * vx[b] + vy[a] * vy[b]) / ((spd[a] * stepMs) * (spd[b] * stepMs));
                    pairs++;
                }
                odd[a] = pairs > 0 ? 1 - sumDot / pairs : 0;
            }
            AddZScores(scores, odd);
        }

        // deaths-per-sec: binary bump for dying inside ±W of the inflection.
        if (has.Contains("deaths-per-sec"))
        {
            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var k = 0; k < p; k++) byId[replay.Entities[playerIdx[k]].EntityId] = k;
            foreach (var ev in replay.Events)
            {
                if (ev.EventKind != "UnitDied" || ev.TargetEntityId is null) continue;
                if (ev.PullTimeMs < inflectionMs - w || ev.PullTimeMs > inflectionMs + w) continue;
                if (byId.TryGetValue(ev.TargetEntityId, out var k)) scores[k] += 2;
            }
        }

        return (scores, ids);
    }

    private static void AddZScores(double[] scores, IReadOnlyList<double> vals)
    {
        var n = vals.Count;
        if (n == 0) return;
        var m = vals.Average();
        var sd = Math.Sqrt(vals.Sum(v => (v - m) * (v - m)) / n);
        if (sd == 0) sd = 1;
        for (var k = 0; k < n; k++)
            scores[k] += Math.Clamp((vals[k] - m) / sd, 0, 5);
    }

    private static List<string> AffectedFromScores(double[] scores, List<string> ids)
    {
        var max = scores.DefaultIfEmpty(0).Max();
        var threshold = Math.Max(0.5, 0.5 * max);
        var affected = ids.Where((_, k) => scores[k] >= threshold).ToList();
        if (affected.Count == 0 && max > 0)
            affected = ids.Where((_, k) => scores[k] == max).ToList();
        return affected;
    }

    private static List<string> TopQuartile(double[] scores, List<string> ids)
    {
        var ranked = scores.Select((s, i) => (S: s, I: i)).Where(x => x.S > 0)
            .OrderByDescending(x => x.S).ToList();
        if (ranked.Count == 0) return new List<string>();
        var qn = Math.Max(1, (int)Math.Ceiling(scores.Length / 4.0));
        return ranked.Take(qn).Select(x => ids[x.I]).ToList();
    }

    // ── Evidence + helpers ──────────────────────────────────────────────────

    private static IReadOnlyList<ClassificationEvidence> BuildEvidence(
        IReadOnlyList<ComputedSignal> signals, int durationMs, int inflectionMs,
        CoverageSeriesDto? coverage)
    {
        var sec = inflectionMs / 1000;
        var entries = new List<(ClassificationEvidence E, double C)>();
        foreach (var s in signals)
        {
            var value = sec >= 0 && sec < s.Series.Count ? Math.Round(s.Series[sec], 6) : 0;
            double contribution;
            if (s.Id == "coverage" && coverage is not null)
            {
                // The 2026-04-23 coverage gate: promote coverage into evidence only when it
                // was SEVERE (≥15% of the pull in fragile coverage OR worst quality < 50)
                // AND LOCAL to the inflection (worst moment within ±3s, or a damage-followed
                // snap within ±3s). Otherwise contribution = 0 — coverage sorts last and the
                // pattern/offender machinery never fires on a pull where it isn't the story.
                var sum = coverage.Summary;
                var fragileFraction = durationMs > 0 ? (double)sum.TimeInFragileCoverageMs / durationMs : 0;
                var severe = fragileFraction >= 0.15 || sum.MinQualityScore < 50;
                var localByTight = Math.Abs(s.TightestAtMs - inflectionMs) <= 3000;
                var snapDelta = sum.SnappingPoints
                    .Where(sp => sp.FollowedByDamageMs is not null)
                    .Select(sp => (double)Math.Abs(sp.TimeMs - inflectionMs))
                    .DefaultIfEmpty(double.PositiveInfinity).Min();
                var local = localByTight || snapDelta <= 3000;
                if (severe && local && s.Tightest > 0)
                {
                    var effectiveDelta = snapDelta <= 3000 ? snapDelta : Math.Abs(s.TightestAtMs - inflectionMs);
                    var closeness0 = Math.Max(0, 1 - effectiveDelta / 10000.0);
                    var severityWeight = 0.5 + Math.Max(fragileFraction,
                        sum.MinQualityScore < 50 ? (50 - sum.MinQualityScore) / 100.0 : 0);
                    contribution = closeness0 * Math.Abs(s.Tightest) * severityWeight;
                }
                else contribution = 0;
            }
            else
            {
                var closeness = Math.Max(0, 1 - Math.Abs(s.TightestAtMs - inflectionMs) / 10000.0);
                var magnitude = Math.Max(Math.Abs(s.Tightest), 0.0001);
                contribution = closeness * magnitude;
            }
            entries.Add((new ClassificationEvidence(s.Id, value, Phrases.GetValueOrDefault(s.Id, s.Id)), contribution));
        }
        return entries.OrderByDescending(e => e.C).Take(5).Select(e => e.E).ToList();
    }

    private static bool IsStrongContributor(ComputedSignal s)
    {
        if (s.Series.Count == 0) return false;
        var absMag = Math.Abs(s.Tightest);
        if (absMag <= 0) return false;
        var med = s.Series.Select(Math.Abs).ToList().Median();
        return med == 0 ? absMag > 0 : absMag > StrongContribK * med;
    }

    private static List<long> CollectPlayerDeathTimes(PullReplay replay)
    {
        var playerIds = replay.Entities.Where(e => e.Kind == "Player").Select(e => e.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        return replay.Events
            .Where(ev => ev.EventKind == "UnitDied" && ev.TargetEntityId is { } tid
                && (tid.StartsWith("Player-", StringComparison.Ordinal) || playerIds.Contains(tid)))
            .Select(ev => ev.PullTimeMs).OrderBy(t => t).ToList();
    }

    private static HashSet<string> CollectDiedPlayerIds(PullReplay replay)
    {
        var playerIds = replay.Entities.Where(e => e.Kind == "Player").Select(e => e.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        return replay.Events
            .Where(ev => ev.EventKind == "UnitDied" && ev.TargetEntityId is { } tid
                && (tid.StartsWith("Player-", StringComparison.Ordinal) || playerIds.Contains(tid)))
            .Select(ev => ev.TargetEntityId!).ToHashSet(StringComparer.Ordinal);
    }

    private static string ShortName(string displayName)
    {
        var hyphen = displayName.IndexOf('-');
        return hyphen > 0 ? displayName[..hyphen] : displayName;
    }
}

internal static class MedianExtensions
{
    public static double Median(this List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        var n = s.Count;
        return n % 2 == 1 ? s[(n - 1) / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
    }
}
