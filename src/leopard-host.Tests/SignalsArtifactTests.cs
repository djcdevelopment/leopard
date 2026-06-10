using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>
/// Invariant tests for the six-signal pack port (RaidUI app/server.cjs buildPullSignals →
/// SignalsArtifact). The JS original had no committed fixtures, so parity here = the rules
/// asserted directly on synthetic replays: the coverage model (solo-healer rule, dead-healer
/// zero), the &lt;2-alive spacing null gate, snap threshold + dedup, population stddev, and
/// deaths bucketing. Arena is 100x100yd so normalized 0.1 = 10yd.
/// </summary>
public class SignalsArtifactTests
{
    private static PullReplay Replay(ReplayEntity[] entities, ReplayFrame[] frames,
        ReplayEvent[]? events = null, int frameStepMs = 1000)
        => new()
        {
            SchemaVersion = "v1",
            PullId = "p1",
            FrameStepMs = frameStepMs,
            ArenaYd = new ArenaYd(100, 100),
            Entities = entities,
            Frames = frames,
            Events = events ?? Array.Empty<ReplayEvent>(),
            PullParticipantIds = Array.Empty<string>(),
        };

    private static ReplayEntity Player(string id, string? role = null)
        => new() { EntityId = id, Kind = "Player", DisplayName = id, Role = role };

    private static ReplayFrame Frame(int t, double[] pos, double[]? hp = null)
        => new() { T = t, EntityPositions = pos, EntityHp = hp };

    private static ReplayEvent Died(long atMs) => new()
    {
        EventId = $"e{atMs}", PullId = "p1", Timestamp = "", PullTimeMs = atMs,
        EventKind = "UnitDied",
    };

    [Fact]
    public void Coverage_DpsInRangeOfHealer_FullyCovered()
    {
        // Healer at 10yd, dps at 30yd — 20yd apart, within the 30yd default range. The healer
        // itself has no healer peer but is the only healer alive → trivially covered.
        var r = Replay(
            new[] { Player("h", "Healer"), Player("d") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.3, 0.1 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Equal(1.0, sig.Aggregates.CoverageAvg, 3);
    }

    [Fact]
    public void Coverage_DpsOutOfRange_HalfCovered()
    {
        // 40yd apart > 30yd range: dps uncovered; the solo healer still counts as covered.
        var r = Replay(
            new[] { Player("h", "Healer"), Player("d") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.5, 0.1 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Equal(0.5, sig.Aggregates.CoverageAvg, 3);
    }

    [Fact]
    public void Coverage_DeadHealer_Zero()
    {
        // The healer is dead: no alive healers → the surviving dps is uncovered, and the dead
        // healer is excluded from the denominator entirely.
        var r = Replay(
            new[] { Player("h", "Healer"), Player("d") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.15, 0.1 }, new[] { 0.0, 1.0 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Equal(0.0, sig.Aggregates.CoverageAvg, 3);
    }

    [Fact]
    public void Spacing_LoneSurvivor_IsNull_NotZero()
    {
        // The original JS bug class this guards: a lone survivor must read null ("no data"),
        // never 0.0yd ("everyone stacked").
        var r = Replay(
            new[] { Player("a"), Player("b") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.4, 0.1 }, new[] { 1.0, 0.0 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Null(sig.Signals["spacing"].Values[0]);
    }

    [Fact]
    public void Spacing_TwoAlive_PairwiseYards()
    {
        // (0.1,0.1) vs (0.4,0.1) on a 100yd arena = 30yd apart.
        var r = Replay(
            new[] { Player("a"), Player("b") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.4, 0.1 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Equal(30.0, sig.Signals["spacing"].Values[0]!.Value, 1);
    }

    [Fact]
    public void Snaps_CoverageCollapse_DetectedOnce_WithDedup()
    {
        // Covered for seconds 0-2 (healer 20yd from dps), then the healer is 80yd away for
        // seconds 3-7 → coverage 1.0 → 0.5, a 50pp collapse. The ≤3s lookahead window sees it
        // from s=1; dedup must keep it to a single snap.
        var near = new[] { 0.1, 0.1, 0.3, 0.1 };
        var far = new[] { 0.1, 0.1, 0.9, 0.1 };
        var frames = Enumerable.Range(0, 8)
            .Select(s => Frame(s * 1000, s < 3 ? near : far)).ToArray();
        var r = Replay(new[] { Player("d"), Player("h", "Healer") }, frames);
        var sig = SignalsArtifact.BuildForReplay(r);

        Assert.Equal(1, sig.Aggregates.SnapCount);
        Assert.Equal(50, sig.Snaps[0].DropPct);
        Assert.Equal(0.5, sig.Aggregates.CoverageMin, 3);
        Assert.Equal(5, sig.Aggregates.FragileSec, 1); // seconds 3..7 below 60% covered
    }

    [Fact]
    public void HpVariance_PopulationStdDev()
    {
        // HP 1.0 and 0.5 → mean 0.75, population stddev 0.25.
        var r = Replay(
            new[] { Player("a"), Player("b") },
            new[] { Frame(0, new[] { 0.1, 0.1, 0.2, 0.1 }, new[] { 1.0, 0.5 }) });
        var sig = SignalsArtifact.BuildForReplay(r);
        Assert.Equal(0.25, sig.Signals["hpVariance"].Values[0]!.Value, 3);
    }

    [Fact]
    public void DeathsPerSec_BucketedByPullTime()
    {
        var frames = Enumerable.Range(0, 4).Select(s => Frame(s * 1000, new[] { 0.1, 0.1, 0.2, 0.1 })).ToArray();
        var r = Replay(new[] { Player("a"), Player("b") }, frames,
            new[] { Died(1500), Died(1700), Died(3200) });
        var sig = SignalsArtifact.BuildForReplay(r);

        Assert.Equal(2.0, sig.Signals["deathsPerSec"].Values[1]);
        Assert.Equal(1.0, sig.Signals["deathsPerSec"].Values[3]);
        Assert.Equal(3, sig.Aggregates.DeathsTotal);
    }

    [Fact]
    public void Followership_CommonDrift_HighFollowership_ZeroEntropy()
    {
        // Both players move by the identical vector: the centroid drifts (followership > 0)
        // and per-player velocity magnitudes are identical (entropy = 0).
        var r = Replay(
            new[] { Player("a"), Player("b") },
            new[]
            {
                Frame(0, new[] { 0.10, 0.10, 0.20, 0.20 }),
                Frame(1000, new[] { 0.15, 0.10, 0.25, 0.20 }),
            });
        var sig = SignalsArtifact.BuildForReplay(r);

        Assert.True(sig.Signals["followership"].Values[1] > 0.5);
        Assert.Equal(0.0, sig.Signals["entropy"].Values[1]!.Value, 3);
    }
}
