using System.Text;
using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;
using Leopard.Host;

// Content root = the exe's own directory, NOT the launcher's cwd — a double-clicked or
// Start-Process'd exe must find its wwwroot next to itself (the csproj copies it to bin).
// Default content root is Directory.GetCurrentDirectory(), which 404s the whole UI when
// launched from anywhere else (observed 2026-06-11).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(5280)); // loopback only
builder.Logging.ClearProviders();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
};

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leopard");
var cacheDir = Path.Combine(dataDir, "cache");
Directory.CreateDirectory(cacheDir);
var configPath = Path.Combine(dataDir, "config.json");
var utf8 = new UTF8Encoding(false);

string DefaultLogDir()
{
    foreach (var c in new[] { @"D:\World of Warcraft\_retail_\Logs", @"C:\Program Files (x86)\World of Warcraft\_retail_\Logs" })
        if (Directory.Exists(c)) return c;
    return @"D:\World of Warcraft\_retail_\Logs";
}
LeopardConfig LoadConfig()
{
    try { if (File.Exists(configPath)) return JsonSerializer.Deserialize<LeopardConfig>(File.ReadAllText(configPath), json) ?? new(DefaultLogDir()); }
    catch { }
    return new LeopardConfig(DefaultLogDir());
}
void SaveConfig(LeopardConfig c) => File.WriteAllText(configPath, JsonSerializer.Serialize(c, json), utf8);
string CachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.md");
}
string TrendsCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v2: selectable windows (4/6/8/10). The version suffix invalidates v1 single-window
    // artifacts so an unchanged log re-parses into the new shape (the parse step regenerates
    // any artifact whose cache file is absent).
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.trends.v2.json");
}
string TraceCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.trace.json");
}
string CareerCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.career.json");
}
string ShapeCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: per-pull density heatmaps. Version suffix lets a schema bump invalidate via re-parse.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.shape.v1.json");
}
string SignalsCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: the six-signal pack per pull (the RaidUI DiagStrip port). See SignalsArtifact.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.signals.v1.json");
}
string AffinityCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: the night's movement-affinity structure (who travels together). See MovementAffinity.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.affinity.v1.json");
}
string PlayersCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: per-pull per-player scores + archetypes (the RaidUI player-* suite). See PlayerScores.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.players.v1.json");
}
string CoverageCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: per-pull healing-coverage quality (per-second series + summary + snaps). See CoverageTimeline.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.coverage.v1.json");
}
string SegmentsCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: per-pull formation segments (movement phases). See FormationSegments.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.segments.v1.json");
}
string ClassifyCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    // v1: per-pull wipe classification (the rule-tree verdicts). See ClassifyArtifact.
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.classify.v1.json");
}

// Every parsed night's career-input encounters, fanned in (same loop the Roster/wkdelta/career-
// summary endpoints run inline; the live loop needs it as a callable).
List<RaidViewEncounter> LoadCareerInputs()
{
    var cfg = LoadConfig();
    var all = new List<RaidViewEncounter>();
    if (!Directory.Exists(cfg.LogDir)) return all;
    foreach (var f in new DirectoryInfo(cfg.LogDir).GetFiles("WoWCombatLog*.txt"))
    {
        var cc = CareerCachePath(f.Name, f.LastWriteTimeUtc);
        if (!File.Exists(cc)) continue;
        try
        {
            var encs = JsonSerializer.Deserialize<List<RaidViewEncounter>>(File.ReadAllText(cc), json);
            if (encs is not null) all.AddRange(encs);
        }
        catch { /* skip a corrupt/old artifact */ }
    }
    return all;
}

// The live between-pull loop (Tempo live ingest → evidence → local inference → jsonl record).
// See docs/live-insight-design-brief.md.
var live = new LiveSession(LoadConfig, LoadCareerInputs, dataDir);

app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "leopard-host" }));

app.MapGet("/api/config", () => Results.Json(LoadConfig(), json));
app.MapPut("/api/config", async (HttpRequest req) =>
{
    var c = await JsonSerializer.DeserializeAsync<LeopardConfig>(req.Body, json);
    if (c is null || string.IsNullOrWhiteSpace(c.LogDir)) return Results.BadRequest(new { error = "logDir required" });
    // Merge-on-null: the Setup tab PUTs { logDir } alone, and the optional fields
    // (live/provider entries) are only ever SET by hand-editing config.json — so an absent
    // field here means "keep what's on disk", never "clear it". Clearing = edit the file.
    var cur = LoadConfig();
    var merged = c with
    {
        LiveInferenceUrl = c.LiveInferenceUrl ?? cur.LiveInferenceUrl,
        LiveModel = c.LiveModel ?? cur.LiveModel,
        AskProviderUrl = c.AskProviderUrl ?? cur.AskProviderUrl,
        AskProviderApi = c.AskProviderApi ?? cur.AskProviderApi,
    };
    SaveConfig(merged);
    live.Restart(); // re-pick the newest log under the (possibly new) dir
    return Results.Json(merged, json);
});

// ── Live (between-pull insight) ── see docs/live-insight-design-brief.md ──
app.MapGet("/api/live/status", () => Results.Json(live.Status, json));
app.MapGet("/api/live/insight", () => Results.Json(live.Insight, json));
app.MapPost("/api/live/feedback", async (HttpRequest req) =>
{
    var f = await JsonSerializer.DeserializeAsync<FeedbackRequest>(req.Body, json);
    if (f is null || string.IsNullOrWhiteSpace(f.InsightId))
        return Results.BadRequest(new { error = "insightId required" });
    live.RecordFeedback(f.InsightId, f.Useful, f.Grounded, f.Comment);
    return Results.Json(new { ok = true });
});

app.MapGet("/api/logs", () =>
{
    var cfg = LoadConfig();
    if (!Directory.Exists(cfg.LogDir))
        return Results.Json(new { logDir = cfg.LogDir, exists = false, logs = Array.Empty<object>() }, json);
    var logs = new DirectoryInfo(cfg.LogDir).GetFiles("WoWCombatLog*.txt")
        .OrderByDescending(f => f.LastWriteTime)
        .Select(f => new
        {
            name = f.Name,
            sizeMb = Math.Round(f.Length / 1048576.0, 1),
            modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            parsed = File.Exists(CachePath(f.Name, f.LastWriteTimeUtc)),
        })
        .ToList();
    return Results.Json(new { logDir = cfg.LogDir, exists = true, logs }, json);
});

app.MapPost("/api/parse", async (HttpRequest req) =>
{
    var cfg = LoadConfig();
    var body = await JsonSerializer.DeserializeAsync<ParseRequest>(req.Body, json);
    var names = body?.Names ?? new List<string>();
    var results = new List<object>();
    foreach (var name in names)
    {
        var path = Path.Combine(cfg.LogDir, name);
        if (!File.Exists(path)) { results.Add(new { name, ok = false, error = "not found" }); continue; }
        try
        {
            var mtime = File.GetLastWriteTimeUtc(path);
            var cache = CachePath(name, mtime);
            var trendsCache = TrendsCachePath(name, mtime);
            var traceCache = TraceCachePath(name, mtime);
            var careerCache = CareerCachePath(name, mtime);
            var shapeCache = ShapeCachePath(name, mtime);
            var signalsCache = SignalsCachePath(name, mtime);
            var affinityCache = AffinityCachePath(name, mtime);
            var playersCache = PlayersCachePath(name, mtime);
            var coverageCache = CoverageCachePath(name, mtime);
            var segmentsCache = SegmentsCachePath(name, mtime);
            var classifyCache = ClassifyCachePath(name, mtime);
            // One parse feeds every artifact. Re-derive if ANY is missing, so a night parsed
            // before a surface existed regenerates that surface's artifact on next parse.
            if (!File.Exists(cache) || !File.Exists(trendsCache) || !File.Exists(traceCache) || !File.Exists(careerCache) || !File.Exists(shapeCache) || !File.Exists(signalsCache) || !File.Exists(affinityCache) || !File.Exists(playersCache) || !File.Exists(coverageCache) || !File.Exists(segmentsCache) || !File.Exists(classifyCache))
            {
                var parse = ParserPipeline.Parse(path);
                File.WriteAllText(cache, BoxScore.Build(parse), utf8);
                File.WriteAllText(trendsCache, TrendsArtifact.BuildJson(parse, json), utf8);
                // Trace re-walks stages 1–3 for the pre-trim substrate Parse discards.
                File.WriteAllText(traceCache, PipelineTrace.BuildJson(path, parse, json), utf8);
                // Career-input: this night's encounters (pull metadata only), the fan-in
                // substrate the Roster aggregates across nights. Small — no events/replays.
                File.WriteAllText(careerCache,
                    JsonSerializer.Serialize(EncountersProjection.ToEncounters(parse.Sessions), json), utf8);
                // Shape: per-pull density heatmaps for this night (needs replay frames).
                File.WriteAllText(shapeCache, ShapeArtifact.BuildJson(parse, json), utf8);
                // Signals: the six-signal diagnostic pack per pull (the RaidUI DiagStrip port).
                File.WriteAllText(signalsCache, SignalsArtifact.BuildJson(parse, json), utf8);
                // Affinity: the night's movement-group structure (who travels together).
                File.WriteAllText(affinityCache, MovementAffinity.BuildJson(parse, json), utf8);
                // Players: per-pull role-weighted scores + archetypes (the player-* suite).
                File.WriteAllText(playersCache, PlayerScores.BuildJson(parse, json), utf8);
                // Coverage: per-pull healing-coverage quality (per-second series + snaps).
                File.WriteAllText(coverageCache, CoverageTimeline.BuildJson(parse, json), utf8);
                // Segments: per-pull movement phases (stacked / split / dispersed).
                File.WriteAllText(segmentsCache, FormationSegments.BuildJson(parse, json), utf8);
                // Classify: per-pull wipe verdicts (rule tree + coverage quality model).
                File.WriteAllText(classifyCache, ClassifyArtifact.BuildJson(parse, json), utf8);
            }
            results.Add(new { name, ok = true, parsed = true });
        }
        catch (Exception ex) { results.Add(new { name, ok = false, error = ex.Message }); }
    }
    return Results.Json(new { results }, json);
});

app.MapGet("/api/boxscore", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = CachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "text/markdown");
});

// Trends artifact (per-encounter rule-row windows + coherence series), computed in-process
// by TrendsArtifact at parse time. Mirrors /api/boxscore but serves the cached JSON.
app.MapGet("/api/trends", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = TrendsCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Pipeline trace (per-stage substrate counts + samples + the trim collapse) for the
// Pipeline Explorer. Computed by PipelineTrace at parse time; served from cache.
app.MapGet("/api/trace", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = TraceCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Shape — density heatmaps (per pull, this night), served from the cached per-night artifact.
// The hero "long-exposure of a pull." 404 => parsed before Shape existed (re-parse in Setup).
app.MapGet("/api/shape/density", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = ShapeCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Signals — the six-signal diagnostic pack (spacing / coverage / deathsPerSec / followership /
// entropy / hpVariance per second, + snaps + aggregates) per pull. The RaidUI DiagStrip port;
// see docs/signals-artifact-port-brief.md. 404 => parsed before Signals existed (re-parse).
app.MapGet("/api/signals", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = SignalsCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Players — per-pull role-weighted player scores + archetypes (the RaidUI player-* port).
// 404 => parsed before Players existed (re-parse). Individual tier — group surfaces first
// per the product thesis; this serves the future player drill-down.
app.MapGet("/api/players", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = PlayersCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Coverage — per-pull healing-coverage quality (per-second raid/tank/flex % + quality score,
// snap markers, summary). The CoverageTimeline port surfaced. 404 => parsed before Coverage
// existed (re-parse in Setup).
app.MapGet("/api/coverage", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = CoverageCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Segments — per-pull formation phases (stacked / split / dispersed change-points). The
// FormationSegments port surfaced. 404 => parsed before Segments existed (re-parse in Setup).
app.MapGet("/api/segments", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = SegmentsCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Classify — per-pull wipe verdicts (systemic / subgroup / individual / called-wipe with
// confidence, evidence, coverage patterns). The WipeClassifier port surfaced. 404 => parsed
// before Classify existed (re-parse in Setup).
app.MapGet("/api/classify", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = ClassifyCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Affinity — the night's movement-group structure (the RaidUI affinity/clustering port):
// pairwise co-travel matrix + the emergent groups. 404 => parsed before Affinity existed.
app.MapGet("/api/affinity", (string name) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var cache = AffinityCachePath(name, File.GetLastWriteTimeUtc(path));
    if (!File.Exists(cache)) return Results.NotFound(new { error = "not parsed yet" });
    return Results.Text(File.ReadAllText(cache), "application/json");
});

// Diff — deterministic two-pull comparison (the RaidUI DiffLens port): 4 session metrics +
// 5 signal-aggregate metrics. Reads the cached career-input + signals artifacts, no re-parse.
// Both pulls must be the same boss. See PullDiff.
app.MapGet("/api/diff", (string name, string a, string b) =>
{
    var cfg = LoadConfig();
    var path = Path.Combine(cfg.LogDir, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "log not found" });
    var mtime = File.GetLastWriteTimeUtc(path);
    var careerCache = CareerCachePath(name, mtime);
    if (!File.Exists(careerCache)) return Results.NotFound(new { error = "not parsed yet" });

    var encs = JsonSerializer.Deserialize<List<RaidViewEncounter>>(File.ReadAllText(careerCache), json) ?? new();
    (RaidViewEncounter Enc, RaidViewPull Pull)? Find(string id)
    {
        foreach (var e in encs)
        {
            var hit = e.Pulls.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
            if (hit is not null) return (e, hit);
        }
        return null;
    }
    var left = Find(a);
    var right = Find(b);
    if (left is null || right is null) return Results.NotFound(new { error = "pull not found" });
    if (!string.Equals(left.Value.Enc.CareerId, right.Value.Enc.CareerId, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "cross_encounter", message = "Cannot diff pulls from different bosses" });

    SignalAggregatesDto? AggOf(string pullId)
    {
        var sc = SignalsCachePath(name, mtime);
        if (!File.Exists(sc)) return null;
        try
        {
            var night = JsonSerializer.Deserialize<SignalsNightDto>(File.ReadAllText(sc), json);
            return night?.Encounters.SelectMany(e => e.Pulls)
                .FirstOrDefault(p => string.Equals(p.PullId, pullId, StringComparison.Ordinal))?.Signals?.Aggregates;
        }
        catch { return null; }
    }

    var result = PullDiff.Build(left.Value.Enc.Name, left.Value.Enc.Difficulty,
        left.Value.Pull, right.Value.Pull, AggOf(a), AggOf(b));
    return Results.Json(result, json);
});

// Shape — kill-vs-wipe contrast, CAREER-scoped: fanned across every parsed night (like the
// Roster), because per night a boss is almost always all-kills or all-wipes. Computed live from
// the small per-night career-input artifacts; `TryBuildWkDelta` resolves the career by careerId.
app.MapGet("/api/shape/wkdelta", (string careerId) =>
{
    if (string.IsNullOrWhiteSpace(careerId)) return Results.BadRequest(new { error = "careerId required" });
    var cfg = LoadConfig();
    var all = new List<RaidViewEncounter>();
    if (Directory.Exists(cfg.LogDir))
    {
        foreach (var f in new DirectoryInfo(cfg.LogDir).GetFiles("WoWCombatLog*.txt"))
        {
            var cc = CareerCachePath(f.Name, f.LastWriteTimeUtc);
            if (!File.Exists(cc)) continue;
            try
            {
                var encs = JsonSerializer.Deserialize<List<RaidViewEncounter>>(File.ReadAllText(cc), json);
                if (encs is not null) all.AddRange(encs);
            }
            catch { /* skip a corrupt/old artifact rather than fail the whole contrast */ }
        }
    }
    // Any encounter sharing the careerId anchors the career resolve (fans in every night).
    var anyId = all.FirstOrDefault(e => string.Equals(e.CareerId, careerId, StringComparison.Ordinal))?.Id;
    if (anyId is null) return Results.NotFound(new { error = "career not found" });
    if (!ShapeProjection.TryBuildWkDelta(all, anyId, out var dto)) return Results.NotFound(new { error = "no contrast" });
    return Results.Json(dto, json);
});

// Career-arc grounding artifact: one boss's all-time story (the zoom above the per-night box
// score) as exact-figures text for Ask. Fans the career-inputs across nights and renders via
// CareerSummary — lets Ask answer "are we getting better at this boss?" from real history.
app.MapGet("/api/career-summary", (string careerId) =>
{
    if (string.IsNullOrWhiteSpace(careerId)) return Results.BadRequest(new { error = "careerId required" });
    var cfg = LoadConfig();
    var all = new List<RaidViewEncounter>();
    if (Directory.Exists(cfg.LogDir))
    {
        foreach (var f in new DirectoryInfo(cfg.LogDir).GetFiles("WoWCombatLog*.txt"))
        {
            var cc = CareerCachePath(f.Name, f.LastWriteTimeUtc);
            if (!File.Exists(cc)) continue;
            try
            {
                var encs = JsonSerializer.Deserialize<List<RaidViewEncounter>>(File.ReadAllText(cc), json);
                if (encs is not null) all.AddRange(encs);
            }
            catch { /* skip a corrupt/old artifact rather than fail the summary */ }
        }
    }
    if (!all.Any(e => string.Equals(e.CareerId, careerId, StringComparison.Ordinal)))
        return Results.NotFound(new { error = "career not found" });
    return Results.Text(CareerSummary.Build(all, careerId), "text/markdown");
});

// The Roster — fans in EVERY parsed night's career-input, groups by boss career, and
// aggregates the all-time roster. Recomputed live (cheap: small per-night JSON) so it
// always reflects whatever has been parsed. Nights without a career artifact are skipped.
app.MapGet("/api/career", () =>
{
    var cfg = LoadConfig();
    var all = new List<RaidViewEncounter>();
    if (Directory.Exists(cfg.LogDir))
    {
        foreach (var f in new DirectoryInfo(cfg.LogDir).GetFiles("WoWCombatLog*.txt"))
        {
            var cache = CareerCachePath(f.Name, f.LastWriteTimeUtc);
            if (!File.Exists(cache)) continue;
            try
            {
                var encs = JsonSerializer.Deserialize<List<RaidViewEncounter>>(File.ReadAllText(cache), json);
                if (encs is not null) all.AddRange(encs);
            }
            catch { /* skip a corrupt/old artifact rather than fail the whole roster */ }
        }
    }
    return Results.Text(CareerRoster.BuildJson(all, json), "application/json");
});

// Native folder picker — only works in the desktop shell (the WinForms thread owns the dialog).
app.MapPost("/api/pick-folder", () =>
{
    var form = MainForm.Instance;
    if (form is null) return Results.Json(new { available = false });
    string? picked = null;
    form.Invoke(() =>
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Pick your World of Warcraft Logs folder",
            UseDescriptionForTitle = true,
        };
        var cur = LoadConfig().LogDir;
        if (Directory.Exists(cur)) dlg.SelectedPath = cur;
        if (dlg.ShowDialog(form) == System.Windows.Forms.DialogResult.OK) picked = dlg.SelectedPath;
    });
    if (picked is null) return Results.Json(new { available = true, cancelled = true });
    SaveConfig(LoadConfig() with { LogDir = picked }); // preserve the live-inference fields
    live.Restart();
    return Results.Json(new { available = true, logDir = picked });
});

// Streaming reverse proxy used by both provider routes below. Forwards method, query,
// and body; streams the response (NDJSON or SSE) straight through.
var llm = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
async Task ProxyLlm(HttpContext ctx, string baseUrl, string path)
{
    var target = $"{baseUrl.TrimEnd('/')}/{path}{ctx.Request.QueryString}";
    using var msg = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);
    if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method))
    {
        msg.Content = new StreamContent(ctx.Request.Body);
        if (!string.IsNullOrEmpty(ctx.Request.ContentType))
            msg.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
    }
    try
    {
        using var resp = await llm.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)resp.StatusCode;
        ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        await resp.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }
    catch (Exception ex) { ctx.Response.StatusCode = 502; await ctx.Response.WriteAsync($"llm proxy error: {ex.Message}"); }
}

// The Ask/Explorer provider route (docs/provider-contract.md): /llm/* forwards to the
// CONFIGURED provider — AskProviderUrl in config.json, default Ollama. The UI reads
// AskProviderApi ("ollama" | "openai") from /api/config to pick the protocol it speaks
// through this route. Dev and prod both go through here (Vite proxies /llm to this host),
// so the config applies in every mode.
//
// 127.0.0.1, not "localhost", in the default: Ollama binds 127.0.0.1 only, and .NET's
// HttpClient resolves "localhost" to ::1 (IPv6) first on Windows — that attempt stalls ~2s
// and fails before any IPv4 fallback, which left the UI stuck on "Looking for a local model…".
const string DefaultProviderUrl = "http://127.0.0.1:11434";
app.Map("/llm/{**path}", (HttpContext ctx, string path) =>
    ProxyLlm(ctx, LoadConfig().AskProviderUrl ?? DefaultProviderUrl, path));

// Legacy fixed-target Ollama proxy — kept so anything still calling /ollama/* keeps working;
// the provider layer now uses /llm/*.
app.Map("/ollama/{**path}", (HttpContext ctx, string path) =>
    ProxyLlm(ctx, DefaultProviderUrl, path));

// Ship: serve the built leopard-web bundle (dev serves the UI via Vite instead).
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // The content-hashed asset files (index-<hash>.js/.css) are immutable and safe to cache
        // forever, but index.html points AT those hashes — if WebView2 caches the HTML it keeps
        // loading the previous bundle after a rebuild. Force the HTML to always revalidate.
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    }
});

await app.StartAsync();
live.Start(app.Lifetime.ApplicationStopping);

if (args.Contains("--headless"))
{
    // No desktop shell — just serve the API on :5280 until the process is stopped.
    // For cache backfills, scripting, and tests; the normal launch opens the window.
    Console.WriteLine("leopard-host: headless on http://localhost:5280/ (stop with Ctrl+C)");
    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
    await stop.Task;
}
else
{
    // Desktop shell: a maximized WebView2 pointed at the in-process host. WinForms +
    // WebView2 require STA, so the UI runs on its own STA thread; main thread waits.
    // Dev: WebView2 points at Vite (5273) for HMR; Vite proxies /api → 5280 and /ollama → 11434.
    // Prod: WebView2 points at this host's own static bundle (5280, served from wwwroot).
    var uiUrl = app.Environment.IsDevelopment() ? "http://localhost:5273/" : "http://localhost:5280/";
    var ui = new Thread(() =>
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new MainForm(uiUrl));
    });
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ui.Join();
}

await app.StopAsync();

public class MainForm : System.Windows.Forms.Form
{
    public static MainForm? Instance { get; private set; }

    public MainForm(string url)
    {
        Instance = this;
        Text = "Leopard";
        WindowState = System.Windows.Forms.FormWindowState.Maximized;
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        var wv = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
        Controls.Add(wv);
        Load += async (_, _) =>
        {
            await wv.EnsureCoreWebView2Async();
            wv.CoreWebView2.Navigate(url);
        };
    }
}

// LiveInferenceUrl/LiveModel: the between-pull insight endpoint (OpenAI chat-completions shape).
// Defaults live in LiveSession (llama-server on the 2nd B70, :8080) — null here means "default".
// AskProviderUrl/AskProviderApi: the Ask/Explorer provider entry per docs/provider-contract.md —
// a base URL plus the API it speaks ("ollama" | "openai"). Null/null = local Ollama on 11434.
// E.g. the dual-B70 path: AskProviderUrl http://127.0.0.1:8090, AskProviderApi "openai" (vllama).
public record LeopardConfig(string LogDir, string? LiveInferenceUrl = null, string? LiveModel = null,
    string? AskProviderUrl = null, string? AskProviderApi = null);
record ParseRequest(List<string> Names);
record FeedbackRequest(string InsightId, bool? Useful, bool? Grounded, string? Comment);
