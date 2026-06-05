using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;
using Xunit;

namespace Leopard.Host.Tests;

public class CareerRosterTests
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

    private static JsonDocument Roster(params RaidViewEncounter[] encs)
        => JsonDocument.Parse(CareerRoster.BuildJson(encs, Json()));

    [Fact]
    public void MergesSameBossAcrossNights_intoOneCareer()
    {
        var night1 = Enc("e1", "belo__heroic", "Belo'ren", "Heroic",
            Pull("p1", "wipe", 40, 120000, 5, "2026-05-01T20:00:00Z"),
            Pull("p2", "wipe", 30, 120000, 4, "2026-05-01T20:05:00Z"));
        var night2 = Enc("e2", "belo__heroic", "Belo'ren", "Heroic",
            Pull("p3", "wipe", 20, 120000, 3, "2026-05-08T20:00:00Z"),
            Pull("p4", "kill", 0, 180000, 1, "2026-05-08T20:10:00Z"));

        var bosses = Roster(night1, night2).RootElement.GetProperty("bosses");
        Assert.Equal(1, bosses.GetArrayLength());

        var b = bosses[0];
        Assert.Equal(4, b.GetProperty("attempts").GetInt32());
        Assert.Equal(1, b.GetProperty("kills").GetInt32());
        Assert.True(b.GetProperty("killed").GetBoolean());
        Assert.Equal(0.0, b.GetProperty("bestPct").GetDouble());     // a kill => best progress is 0% boss HP
        Assert.Equal(4, b.GetProperty("arc").GetArrayLength());      // arc spans both nights, re-numbered 1..4
        Assert.Equal(540000, b.GetProperty("totalTimeMs").GetInt64());
    }

    [Fact]
    public void HeroicAndMythic_areSeparateCareers()
    {
        var h = Enc("h", "belo__heroic", "Belo'ren", "Heroic", Pull("hp", "wipe", 50, 100000, 5, "2026-05-01T20:00:00Z"));
        var m = Enc("m", "belo__mythic", "Belo'ren", "Mythic", Pull("mp", "wipe", 80, 100000, 8, "2026-05-02T20:00:00Z"));

        Assert.Equal(2, Roster(h, m).RootElement.GetProperty("bosses").GetArrayLength());
    }

    [Fact]
    public void Direction_improving_whenLateProgressBeatsEarly()
    {
        // 9 wipes, boss end-HP trending down 90% -> 10% (getting closer to a kill).
        var pulls = Enumerable.Range(0, 9)
            .Select(i => Pull($"p{i}", "wipe", 90 - i * 10, 100000, 5, $"2026-05-01T20:{i:D2}:00Z"))
            .ToArray();

        var b = Roster(Enc("e", "belo__heroic", "Belo'ren", "Heroic", pulls)).RootElement.GetProperty("bosses")[0];
        Assert.Equal("improving", b.GetProperty("direction").GetString());
        Assert.Equal(10.0, b.GetProperty("bestPct").GetDouble());
    }

    [Fact]
    public void Integration_8wipeFixture_oneBossEightWipes()
    {
        var parse = ParserPipeline.Parse(Fixture("belo-ren-8wipe-session.txt"));
        var encs = EncountersProjection.ToEncounters(parse.Sessions);

        var bosses = JsonDocument.Parse(CareerRoster.BuildJson(encs, Json())).RootElement.GetProperty("bosses");
        Assert.Equal(1, bosses.GetArrayLength());

        var b = bosses[0];
        Assert.Equal(8, b.GetProperty("attempts").GetInt32());
        Assert.Equal(0, b.GetProperty("kills").GetInt32());
        Assert.False(b.GetProperty("killed").GetBoolean());
    }
}
