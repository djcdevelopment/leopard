using System.Diagnostics;
using System.Text.Json;
using Tempo.Core.Ingest;

namespace Leopard.Host;

/// <summary>
/// Builds the Pipeline Explorer's trace for one log: the substrate flowing through each
/// parser stage (raw lines → lexed → classified → pulls → kept events) plus a small real
/// sample at each, and the per-pull trim collapse. The chain made legible — the user watches
/// their own multi-million-line log become a handful of kept events.
///
/// <para>Stages 1–3 are re-walked via the same public Tempo.Core path
/// <c>Tempo.Diagnostics/PretrimCounts</c> uses (LineReader → LineLexer → Classifier →
/// SessionBuilder), so the pre-trim counts Parse discards are recovered and match an existing
/// trusted diagnostic. Post-trim + categorical counts come from the normal <see cref="ParseResult"/>,
/// joined by pullId. Cost: one extra pass over the file (stages 1–3 only) on top of Parse —
/// cached at parse time, so paid once per night. Zero Tempo engine changes.</para>
/// </summary>
public static class PipelineTrace
{
    private const int MaxRowSamples = 6; // high-volume stages: show a sample, count is exact

    public static string BuildJson(string logPath, ParseResult parse, JsonSerializerOptions json)
    {
        var sw = Stopwatch.StartNew();

        long rawLines = 0, lexed = 0, classified = 0;
        var rawSample = new List<string>();
        var lexSample = new List<object>();
        var classifySample = new List<object>();

        var builder = new SessionBuilder(logPath);
        foreach (var line in LineReader.ReadLines(logPath))
        {
            rawLines++;
            if (rawSample.Count < MaxRowSamples && !string.IsNullOrWhiteSpace(line))
                rawSample.Add(Truncate(line, 160));

            var lx = LineLexer.LexLine(line);
            if (lx is null) continue;
            lexed++;
            if (lexSample.Count < MaxRowSamples)
                lexSample.Add(new { eventType = lx.EventType, fields = Truncate(lx.FieldsCsv, 110) });

            var evt = Classifier.Classify(lx);
            if (evt is null) continue;
            classified++;
            if (classifySample.Count < MaxRowSamples)
                classifySample.Add(new
                {
                    kind = evt.Kind ?? evt.RawKind,
                    spell = evt.SpellName,
                    amount = evt.Amount,
                    src = Short(evt.SourceGuid),
                    dst = Short(evt.DestGuid),
                });
            builder.Handle(evt);
        }
        builder.Complete();
        var segSessions = builder.GetSessions();

        // Per-pull pre-trim counts + the segment/trim drill-in rows. Pulls are bounded (tens),
        // so every pull is shown — no silent cap.
        long preTrimTotal = 0;
        int pullCount = 0;
        var segSample = new List<object>();
        var trimRows = new List<object>();
        foreach (var s in segSessions)
            foreach (var e in s.Encounters)
                foreach (var p in e.Pulls)
                {
                    preTrimTotal += p.Events.Count;
                    pullCount++;
                    segSample.Add(new { n = p.Num, encounter = e.Name, outcome = p.Outcome, events = p.Events.Count });

                    parse.ReplaysByPullId.TryGetValue(p.PullId, out var replay);
                    parse.CategoricalEventsByPullId.TryGetValue(p.PullId, out var cat);
                    trimRows.Add(new
                    {
                        n = p.Num,
                        encounter = e.Name,
                        outcome = p.Outcome,
                        preTrim = p.Events.Count,
                        kept = replay?.Events.Count ?? 0,
                        categorical = cat?.Count ?? 0,
                    });
                }

        long keptTotal = parse.ReplaysByPullId.Values.Sum(r => (long)r.Events.Count);
        long categoricalTotal = parse.CategoricalEventsByPullId.Values.Sum(c => (long)c.Count);
        sw.Stop();

        var stages = new object[]
        {
            new
            {
                id = "lex", title = "Lex",
                does = "Reads each raw log line into typed tokens. Blank and malformed lines fall away here.",
                seesLabel = "raw lines", countIn = rawLines,
                emitsLabel = "lexed lines", countOut = lexed,
                sampleKind = "raw", sample = rawSample.Cast<object>().ToList(),
            },
            new
            {
                id = "classify", title = "Classify",
                does = "Labels each lexed line as a specific combat event — and drops everything that isn't combat (chat, system noise).",
                seesLabel = "lexed lines", countIn = lexed,
                emitsLabel = "combat events", countOut = classified,
                sampleKind = "event", sample = classifySample,
            },
            new
            {
                id = "segment", title = "Segment",
                does = "Groups the event stream into pulls — individual boss attempts.",
                seesLabel = "combat events", countIn = classified,
                emitsLabel = "pulls", countOut = (long)pullCount,
                sampleKind = "segment", sample = segSample,
            },
            new
            {
                id = "trim", title = "Trim",
                does = "Keeps only the events that matter for each pull — deaths, interrupts, the biggest hits, key defensive casts — and drops the rest. This is the dramatic collapse.",
                seesLabel = "pre-trim events", countIn = preTrimTotal,
                emitsLabel = "kept events", countOut = keptTotal,
                sampleKind = "trim", sample = trimRows,
            },
        };

        var projections = new object[]
        {
            new
            {
                id = "categorical", title = "Categorical stream",
                does = "A wider pre-trim slice — auras, casts, heals, deaths — kept for moment-detection and grounded evidence. A different lens on the same pull.",
                count = categoricalTotal, tab = (string?)null,
            },
            new
            {
                id = "boxscore", title = "Box score → Ask",
                does = "The compact, exact summary the local model reads when you ask a question about the night.",
                count = (long?)null, tab = "leopard",
            },
            new
            {
                id = "trends", title = "Trends",
                does = "A worked projection: recent-window deltas plus per-pull coordination signals. One thing you can compute from the kept events — open the Trends tab to see it on your data.",
                count = (long?)null, tab = "trends",
            },
        };

        var trace = new
        {
            log = Path.GetFileName(logPath),
            walkSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
            stages,
            projections,
            totals = new
            {
                rawLines,
                lexed,
                classified,
                pulls = pullCount,
                preTrimEvents = preTrimTotal,
                keptEvents = keptTotal,
                categoricalEvents = categoricalTotal,
            },
        };
        return JsonSerializer.Serialize(trace, json);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

    // GUIDs aren't human-readable; keep enough to read as "real actor", drop the noise.
    private static string? Short(string? guid)
    {
        if (string.IsNullOrEmpty(guid)) return guid;
        return guid.Length <= 16 ? guid : guid.Substring(0, 14) + "…";
    }
}
