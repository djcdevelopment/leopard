using System.Text;
using System.Text.Json;
using Tempo.Core.Ingest;
using Leopard.Host;

var builder = WebApplication.CreateBuilder(args);
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
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.trends.json");
}
string TraceCachePath(string name, DateTime mtimeUtc)
{
    var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(cacheDir, $"{safe}__{mtimeUtc.Ticks}.trace.json");
}

app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "leopard-host" }));

app.MapGet("/api/config", () => Results.Json(LoadConfig(), json));
app.MapPut("/api/config", async (HttpRequest req) =>
{
    var c = await JsonSerializer.DeserializeAsync<LeopardConfig>(req.Body, json);
    if (c is null || string.IsNullOrWhiteSpace(c.LogDir)) return Results.BadRequest(new { error = "logDir required" });
    SaveConfig(c);
    return Results.Json(c, json);
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
            // One parse feeds every artifact. Re-derive if ANY is missing, so a night parsed
            // before a surface existed regenerates that surface's artifact on next parse.
            if (!File.Exists(cache) || !File.Exists(trendsCache) || !File.Exists(traceCache))
            {
                var parse = ParserPipeline.Parse(path);
                File.WriteAllText(cache, BoxScore.Build(parse), utf8);
                File.WriteAllText(trendsCache, TrendsArtifact.BuildJson(parse, json), utf8);
                // Trace re-walks stages 1–3 for the pre-trim substrate Parse discards.
                File.WriteAllText(traceCache, PipelineTrace.BuildJson(path, parse, json), utf8);
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
    SaveConfig(new LeopardConfig(picked));
    return Results.Json(new { available = true, logDir = picked });
});

// Proxy /ollama/* -> the local Ollama provider so the shipped UI (served from THIS
// host) reaches Ollama same-origin — mirrors the Vite dev proxy. Streams responses.
var ollama = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
app.Map("/ollama/{**path}", async (HttpContext ctx, string path) =>
{
    var target = $"http://localhost:11434/{path}{ctx.Request.QueryString}";
    using var msg = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);
    if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method))
    {
        msg.Content = new StreamContent(ctx.Request.Body);
        if (!string.IsNullOrEmpty(ctx.Request.ContentType))
            msg.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
    }
    try
    {
        using var resp = await ollama.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)resp.StatusCode;
        ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        await resp.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }
    catch (Exception ex) { ctx.Response.StatusCode = 502; await ctx.Response.WriteAsync($"ollama proxy error: {ex.Message}"); }
});

// Ship: serve the built leopard-web bundle (dev serves the UI via Vite instead).
app.UseDefaultFiles();
app.UseStaticFiles();

await app.StartAsync();

// Desktop shell: a maximized WebView2 pointed at the in-process host. WinForms +
// WebView2 require STA, so the UI runs on its own STA thread; main thread waits.
var ui = new Thread(() =>
{
    System.Windows.Forms.Application.EnableVisualStyles();
    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
    System.Windows.Forms.Application.Run(new MainForm("http://localhost:5280/"));
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();

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

record LeopardConfig(string LogDir);
record ParseRequest(List<string> Names);
