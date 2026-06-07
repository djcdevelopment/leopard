using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;
using Xunit;

namespace Leopard.Host.Tests;

public class ShapeArtifactTests
{
    private static JsonSerializerOptions Json() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static RaidViewPull Pull(string id, string outcome, double bossPct, long durMs, int deaths, string startedAt)
        => new(Id: id, N: 1, Outcome: outcome, BossEndPctHp: bossPct, DurationMs: durMs,
               AgeDays: 0, Deaths: deaths, Close: false, StartedAt: startedAt);

    private static RaidViewEncounter Enc(string id, string careerId, string name, string diff, params RaidViewPull[] pulls)
        => new(Id: id, CareerId: careerId, Name: name, Short: name, Difficulty: diff, Kind: "raid",
               Pulls: pulls, Kills: pulls.Count(p => p.Outcome == "kill"), BestPct: 100,
               LastSeen: "", SessionId: "s", SessionDate: "");

    // The load-bearing decision: wkdelta is CAREER-scoped, so a boss wiped one night and killed
    // another contrasts across nights. Per-night it would be one-sided (the data check that drove
    // the design: no single real night has a boss with both a kill and a wipe).
    [Fact]
    public void WkDelta_isCareerScoped_contrastsKillsAndWipesAcrossNights()
    {
        var night1 = Enc("e1", "belo__heroic", "Belo'ren", "Heroic",
            Pull("p1", "wipe", 40, 120000, 20, "2026-05-01T20:00:00Z"),
            Pull("p2", "wipe", 30, 120000, 18, "2026-05-01T20:05:00Z"));
        var night2 = Enc("e2", "belo__heroic", "Belo'ren", "Heroic",
            Pull("p3", "kill", 0, 180000, 3, "2026-05-08T20:10:00Z"));
        var all = new[] { night1, night2 };

        // Resolve by an encounterId from EITHER night — the career fans in both.
        Assert.True(ShapeProjection.TryBuildWkDelta(all, "e1", out var dto));
        Assert.Equal(1, dto.KillCount);
        Assert.Equal(2, dto.WipeCount);

        var deaths = dto.Rows.First(r => r.Label == "Avg deaths");
        Assert.NotNull(deaths.Kill);
        Assert.NotNull(deaths.Wipe);
        Assert.True(deaths.Kill < deaths.Wipe, $"kill avg deaths {deaths.Kill} should be < wipe {deaths.Wipe}");

        // Best progress: a kill is 0% by definition; the wipes' mean end-HP is > 0.
        var prog = dto.Rows.First(r => r.Label.StartsWith("Best progress"));
        Assert.Equal(0.0, prog.Kill);
        Assert.True(prog.Wipe > 0);
    }

    // One-sided is the common real case (a boss only wiped). The kill column must be null, not a
    // fabricated zero — the UI renders this as the "no kill yet" frame.
    [Fact]
    public void WkDelta_oneSided_whenNeverKilled_leavesKillColumnNull()
    {
        var enc = Enc("e", "belo__heroic", "Belo'ren", "Heroic",
            Pull("p1", "wipe", 40, 120000, 12, "2026-05-01T20:00:00Z"),
            Pull("p2", "wipe", 25, 120000, 9, "2026-05-01T20:05:00Z"));

        Assert.True(ShapeProjection.TryBuildWkDelta(new[] { enc }, "e", out var dto));
        Assert.Equal(0, dto.KillCount);
        Assert.Equal(2, dto.WipeCount);
        // The UI hides the whole kill column off KillCount==0. The MEASURED rows have null Kill;
        // "Best progress" is exempt — it's definitionally 0 (a kill = 0% boss HP), not a measurement.
        var deaths = dto.Rows.First(r => r.Label == "Avg deaths");
        Assert.Null(deaths.Kill);
        Assert.NotNull(deaths.Wipe);
        Assert.Equal(0.0, dto.Rows.First(r => r.Label.StartsWith("Best progress")).Kill);
    }

    // The per-night density artifact: builds, carries per-pull selector metadata, and any density
    // grid that IS present obeys the contract the heatmap renders against (normalized cells, the
    // busiest cell == 1.0, grid sized GridW*GridH).
    [Fact]
    public void Density_artifact_buildsWithSelectorMetadata_andInvariantsWhenPresent()
    {
        var parse = ParserPipeline.Parse(Fixture("belo-ren-8wipe-session.txt"));
        var root = JsonDocument.Parse(ShapeArtifact.BuildJson(parse, Json())).RootElement;

        var encs = root.GetProperty("encounters");
        Assert.True(encs.GetArrayLength() >= 1);

        var enc = encs[0];
        foreach (var prop in new[] { "encounterId", "careerId", "encounterName", "difficulty", "pulls" })
            Assert.True(enc.TryGetProperty(prop, out _), $"missing {prop}");

        var pulls = enc.GetProperty("pulls");
        Assert.True(pulls.GetArrayLength() >= 1);

        foreach (var p in pulls.EnumerateArray())
        {
            // Selector metadata is always present (drives the pull dropdown).
            foreach (var prop in new[] { "pullId", "n", "outcome", "endHpPct", "durationMs", "deaths", "hasMovement" })
                Assert.True(p.TryGetProperty(prop, out _), $"pull missing {prop}");

            if (p.TryGetProperty("density", out var d) && d.ValueKind != JsonValueKind.Null)
            {
                int gw = d.GetProperty("gridW").GetInt32();
                int gh = d.GetProperty("gridH").GetInt32();
                var cells = d.GetProperty("cells");
                Assert.Equal(gw * gh, cells.GetArrayLength());

                double maxCell = 0;
                foreach (var c in cells.EnumerateArray())
                {
                    double v = c.GetDouble();
                    Assert.InRange(v, 0.0, 1.0);
                    if (v > maxCell) maxCell = v;
                }

                int total = d.GetProperty("totalSamples").GetInt32();
                if (total > 0)
                    Assert.Equal(1.0, maxCell, 6); // normalized: the busiest cell is exactly 1
            }
        }
    }
}
