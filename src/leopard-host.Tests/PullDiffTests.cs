using Tempo.Host.ViewerApi.Projections;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>Invariant tests for the two-pull diff port (RaidUI app/server.cjs buildDiff →
/// PullDiff): direction semantics (better-when-lower vs better-when-higher), the deaths
/// headline wording, severity tiers, and the wired/placeholder split when signals are
/// missing for either pull.</summary>
public class PullDiffTests
{
    private static RaidViewPull Pull(string id, int n, string outcome, double pct, long durMs, int deaths)
        => new(Id: id, N: n, Outcome: outcome, BossEndPctHp: pct, DurationMs: durMs,
               AgeDays: 0, Deaths: deaths, Close: false, StartedAt: "2026-06-09T20:00:00");

    private static SignalAggregatesDto Agg(double covAvg, double covMin, double tightest,
        double fragile, int snaps, int deaths, int dur)
        => new(covAvg, covMin, tightest, fragile, snaps, deaths, dur);

    [Fact]
    public void Headline_MoreDeaths_NamesTheDelta()
    {
        var d = PullDiff.Build("Boss", "Mythic",
            Pull("a", 1, "kill", 0, 90_000, 1), Pull("b", 2, "wipe", 30, 120_000, 3), null, null);
        Assert.Equal("Pull #2 took 2 more deaths.", d.RuleHeadline);
    }

    [Fact]
    public void Deaths_LowerIsBetter_HpLowerIsBetter_DurationHigherIsBetter()
    {
        var d = PullDiff.Build("Boss", "Mythic",
            Pull("a", 1, "wipe", 40, 90_000, 3), Pull("b", 2, "wipe", 25, 150_000, 1), null, null);
        var rows = d.Metrics.ToDictionary(m => m.Id);

        Assert.Equal("better", rows["deaths"].Dir);   // 3 → 1
        Assert.Equal("better", rows["end-hp"].Dir);   // 40% → 25% boss HP
        Assert.Equal("better", rows["duration"].Dir); // 90s → 150s (lived longer)
        Assert.Equal("worse", PullDiff.Build("Boss", "Mythic",
            Pull("a", 1, "wipe", 25, 90_000, 1), Pull("b", 2, "wipe", 40, 90_000, 1), null, null)
            .Metrics.First(m => m.Id == "end-hp").Dir);
    }

    [Fact]
    public void DeathSeverity_Tiers()
    {
        string SevFor(int lDeaths, int rDeaths) => PullDiff.Build("B", "M",
            Pull("a", 1, "wipe", 50, 60_000, lDeaths), Pull("b", 2, "wipe", 50, 60_000, rDeaths),
            null, null).Metrics.First(m => m.Id == "deaths").Sev;

        Assert.Equal("crit", SevFor(0, 3)); // +3 > 2
        Assert.Equal("warn", SevFor(0, 1));
        Assert.Equal("ok", SevFor(2, 1));
    }

    [Fact]
    public void Outcome_WipeToKill_IsBetter()
    {
        var d = PullDiff.Build("Boss", "Mythic",
            Pull("a", 1, "wipe", 30, 90_000, 2), Pull("b", 2, "kill", 0, 200_000, 2), null, null);
        var o = d.Metrics.First(m => m.Id == "outcome");
        Assert.Equal("better", o.Dir);
        Assert.Equal("Wipe", o.L);
        Assert.Equal("Kill", o.R);
    }

    [Fact]
    public void SignalRows_WiredWithAggregates_PlaceholdersWithout()
    {
        var aggA = Agg(0.97, 0.52, 0.4, 1, 2, 0, 60);
        var aggB = Agg(0.84, 0.20, 0.2, 8, 7, 0, 66);

        var wired = PullDiff.Build("B", "M",
            Pull("a", 1, "kill", 0, 60_000, 0), Pull("b", 2, "kill", 0, 66_000, 0), aggA, aggB);
        Assert.Equal(9, wired.Metrics.Count);
        Assert.All(wired.Metrics, m => Assert.True(m.Wired));
        // Coverage avg 97% → 84% is worse (higher is better).
        Assert.Equal("worse", wired.Metrics.First(m => m.Id == "rcov-avg").Dir);
        // Fragile 1s → 8s is worse (lower is better).
        Assert.Equal("worse", wired.Metrics.First(m => m.Id == "frag").Dir);

        var bare = PullDiff.Build("B", "M",
            Pull("a", 1, "kill", 0, 60_000, 0), Pull("b", 2, "kill", 0, 66_000, 0), aggA, null);
        Assert.Equal(5, bare.Metrics.Count(m => !m.Wired)); // all five diagnostic rows placeholder
        Assert.All(bare.Metrics.Where(m => !m.Wired), m => Assert.Null(m.L));
    }
}
