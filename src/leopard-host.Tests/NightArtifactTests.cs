using System.Text.Json;
using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>
/// Shape tests for the three phase-2 per-night artifacts (.coverage.v1 / .segments.v1 /
/// .classify.v1) against the real fixture night — the modules' math is unit-tested in their
/// own suites; what these pin is the cached JSON contract the Explorer fetches. One parse,
/// three walks (the fixture is the same 8-wipe Belo'ren session ShapeArtifactTests uses).
/// </summary>
public class NightArtifactTests
{
    private static readonly Lazy<ParseResult> Parse = new(() =>
        ParserPipeline.Parse(Path.Combine(AppContext.BaseDirectory, "Fixtures", "belo-ren-8wipe-session.txt")));

    private static JsonSerializerOptions Json() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static JsonElement Pulls(string artifactJson)
    {
        var root = JsonDocument.Parse(artifactJson).RootElement;
        var encs = root.GetProperty("encounters");
        Assert.True(encs.GetArrayLength() >= 1);
        foreach (var prop in new[] { "encounterId", "encounterName", "difficulty", "pulls" })
            Assert.True(encs[0].TryGetProperty(prop, out _), $"missing {prop}");
        var pulls = encs[0].GetProperty("pulls");
        Assert.True(pulls.GetArrayLength() >= 1);
        return pulls;
    }

    [Fact]
    public void Coverage_artifact_perSecondSeries_alignedAndBounded()
    {
        var sawSeries = false;
        foreach (var p in Pulls(CoverageTimeline.BuildJson(Parse.Value, Json())).EnumerateArray())
        {
            foreach (var prop in new[] { "pullId", "n", "outcome", "coverage" })
                Assert.True(p.TryGetProperty(prop, out _), $"pull missing {prop}");
            var cov = p.GetProperty("coverage");
            if (cov.ValueKind == JsonValueKind.Null) continue;
            sawSeries = true;

            var sec = cov.GetProperty("seconds");
            var raid = sec.GetProperty("raidPct");
            var n = raid.GetArrayLength();
            Assert.True(n > 0);
            foreach (var key in new[] { "tankPct", "flexPct", "quality" })
                Assert.Equal(n, sec.GetProperty(key).GetArrayLength());
            foreach (var v in raid.EnumerateArray()) Assert.InRange(v.GetDouble(), 0, 100);
            foreach (var v in sec.GetProperty("quality").EnumerateArray()) Assert.InRange(v.GetDouble(), 0, 100);

            var sum = cov.GetProperty("summary");
            foreach (var prop in new[] { "avgRaidPct", "minQualityScore", "timeInFragileCoverageMs", "snappingPoints" })
                Assert.True(sum.TryGetProperty(prop, out _), $"summary missing {prop}");
        }
        Assert.True(sawSeries, "fixture night produced no coverage series at all");
    }

    [Fact]
    public void Segments_artifact_formationsBucketed_andPhasesDescribed()
    {
        var sawSegments = false;
        foreach (var p in Pulls(FormationSegments.BuildJson(Parse.Value, Json())).EnumerateArray())
        {
            foreach (var prop in new[] { "pullId", "n", "outcome", "segments", "phases" })
                Assert.True(p.TryGetProperty(prop, out _), $"pull missing {prop}");
            var segs = p.GetProperty("segments");
            if (segs.ValueKind == JsonValueKind.Null) continue;

            foreach (var s in segs.EnumerateArray())
            {
                Assert.Contains(s.GetProperty("formation").GetString(),
                    new[] { "stacked", "split", "dispersed" });
                Assert.True(s.GetProperty("startMs").GetInt32() < s.GetProperty("endMs").GetInt32());
            }
            if (segs.GetArrayLength() > 0)
            {
                sawSegments = true;
                Assert.Equal(JsonValueKind.String, p.GetProperty("phases").ValueKind);
            }
        }
        Assert.True(sawSegments, "fixture night produced no formation segments at all");
    }

    [Fact]
    public void Classify_artifact_verdictOrExplicitReason_neverSilence()
    {
        var sawVerdict = false;
        foreach (var p in Pulls(ClassifyArtifact.BuildJson(Parse.Value, Json())).EnumerateArray())
        {
            foreach (var prop in new[] { "pullId", "n", "outcome", "classification", "reason" })
                Assert.True(p.TryGetProperty(prop, out _), $"pull missing {prop}");

            var cls = p.GetProperty("classification");
            if (cls.ValueKind == JsonValueKind.Null)
            {
                // No verdict ⇒ a stated reason, never silence.
                Assert.Equal(JsonValueKind.String, p.GetProperty("reason").ValueKind);
                continue;
            }
            sawVerdict = true;
            Assert.Equal(JsonValueKind.Null, p.GetProperty("reason").ValueKind);
            Assert.Contains(cls.GetProperty("kind").GetString(),
                new[] { "systemic", "subgroup", "individual", "called-wipe" });
            Assert.Contains(cls.GetProperty("confidence").GetString(), new[] { "high", "med", "low" });
            Assert.True(cls.GetProperty("inflectionMs").GetInt32() >= 0);
        }
        Assert.True(sawVerdict, "an 8-wipe fixture night should classify at least one pull");
    }
}
