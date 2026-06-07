using System.Text.Json;
using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

public class TrendsArtifactTests
{
    private static JsonSerializerOptions Json() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // Guards the v2 (selectable-window) artifact shape the Trends tab consumes: each encounter
    // carries a `windows` map keyed by the four sizes plus a default of 6. A regression here is
    // what the `.trends.v2.json` cache-version bump exists to force a re-parse for.
    [Fact]
    public void V2_artifact_carriesWindowsKeyedBySize_withDefault6()
    {
        var parse = ParserPipeline.Parse(Fixture("belo-ren-8wipe-session.txt"));
        var root = JsonDocument.Parse(TrendsArtifact.BuildJson(parse, Json())).RootElement;

        var encs = root.GetProperty("encounters");
        Assert.True(encs.GetArrayLength() >= 1);

        var enc = encs[0];
        Assert.Equal(6, enc.GetProperty("defaultWindow").GetInt32());

        var windows = enc.GetProperty("windows");
        foreach (var size in new[] { "4", "6", "8", "10" })
        {
            Assert.True(windows.TryGetProperty(size, out var w), $"missing window {size}");
            Assert.True(w.GetProperty("ruleRows").GetArrayLength() > 0, $"window {size} has no rule rows");
        }

        // coherences map is present and keyed the same way (null entries allowed when no replay).
        Assert.True(enc.TryGetProperty("coherences", out var coh));
        foreach (var size in new[] { "4", "6", "8", "10" })
            Assert.True(coh.TryGetProperty(size, out _), $"missing coherence window {size}");
    }
}
