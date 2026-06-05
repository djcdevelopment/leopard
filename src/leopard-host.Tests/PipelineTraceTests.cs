using System.Text.Json;
using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

public class PipelineTraceTests
{
    private static JsonSerializerOptions Json() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Trace_funnelMonotonic_conserves_andParitiesWithParse()
    {
        var path = Fixture("belo-ren-8wipe-session.txt");
        var parse = ParserPipeline.Parse(path);
        var root = JsonDocument.Parse(PipelineTrace.BuildJson(path, parse, Json())).RootElement;
        var totals = root.GetProperty("totals");

        long rawLines = totals.GetProperty("rawLines").GetInt64();
        long lexed = totals.GetProperty("lexed").GetInt64();
        long classified = totals.GetProperty("classified").GetInt64();
        long preTrim = totals.GetProperty("preTrimEvents").GetInt64();
        long kept = totals.GetProperty("keptEvents").GetInt64();
        int pulls = totals.GetProperty("pulls").GetInt32();

        // The funnel only narrows: raw >= lexed >= classified >= pre-trim (pull events) >= kept.
        Assert.True(lexed <= rawLines, $"lexed {lexed} > raw {rawLines}");
        Assert.True(classified <= lexed, $"classified {classified} > lexed {lexed}");
        Assert.True(preTrim <= classified, $"preTrim {preTrim} > classified {classified}");
        Assert.True(kept <= preTrim, $"kept {kept} > preTrim {preTrim}");

        // Conservation: totals.keptEvents == sum of the per-pull kept counts shown in the
        // trim drill-in. This guards the re-walk <-> Parse pullId join: a divergent pullId
        // would drop a pull's kept to 0 here and break equality.
        var trim = root.GetProperty("stages").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "trim");
        long sumKept = trim.GetProperty("sample").EnumerateArray()
            .Sum(r => (long)r.GetProperty("kept").GetInt32());
        Assert.Equal(kept, sumKept);

        // Parity: the standalone stage-1-3 re-walk must segment the same pull count as Parse.
        int parsePulls = parse.Sessions.Sum(s => s.Encounters.Sum(e => e.Pulls.Count));
        Assert.Equal(parsePulls, pulls);
        Assert.Equal(pulls, trim.GetProperty("sample").GetArrayLength());
    }
}
