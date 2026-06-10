using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>
/// Invariant tests for the classify.js rule-tree port: the three gates, the called-wipe
/// patterns (ADR-008, retuned constants), fatality-shortcut tiering, and the affected set.
/// Signals are hand-built ComputedSignals so inflection placement is exact.
/// </summary>
public class WipeClassifierTests
{
    private static PullReplay Replay(int seconds, ReplayEntity[] entities, ReplayEvent[] events,
        IReadOnlyList<double>? bossHpBySec = null)
    {
        // 1 frame per second; everyone stands at fixed positions.
        var frames = Enumerable.Range(0, seconds).Select(s => new ReplayFrame
        {
            T = s * 1000,
            EntityPositions = entities.SelectMany((_, i) => new[] { 0.1 + i * 0.05, 0.5 }).ToArray(),
            EntityHp = bossHpBySec is null
                ? null
                : entities.Select((e, i) => e.Kind == "Boss" ? bossHpBySec[Math.Min(s, bossHpBySec.Count - 1)] : 1.0).ToArray(),
        }).ToArray();
        return new PullReplay
        {
            SchemaVersion = "v1", PullId = "p1", FrameStepMs = 1000,
            ArenaYd = new ArenaYd(100, 100),
            Entities = entities, Frames = frames, Events = events,
            PullParticipantIds = Array.Empty<string>(),
        };
    }

    private static ReplayEntity Player(string id) => new()
        { EntityId = $"Player-{id}", Kind = "Player", DisplayName = $"{id}-Realm" };
    private static ReplayEntity Boss() => new()
        { EntityId = "Creature-BOSS", Kind = "Boss", DisplayName = "Boss" };

    private static ReplayEvent Died(string playerId, long atMs) => new()
    {
        EventId = $"e{atMs}", PullId = "p1", Timestamp = "", PullTimeMs = atMs,
        EventKind = "UnitDied", TargetEntityId = $"Player-{playerId}",
    };

    private static ComputedSignal Quiet(string id, int seconds)
        => new(id, new double[seconds], 0, 0, null);

    private static ComputedSignal Strong(string id, int seconds, int inflectionMs)
    {
        // A spike well above its own median → strong contributor.
        var series = new double[seconds];
        series[Math.Min(seconds - 1, inflectionMs / 1000)] = 1.0;
        return new(id, series, 1.0, inflectionMs, inflectionMs);
    }

    [Fact]
    public void Gates_Kill_ShortPull_NoDeaths_AllReturnNull()
    {
        var players = new[] { Player("A"), Player("B") };
        var sigs = new[] { Quiet("coverage", 60) };

        var killed = Replay(60, players, new[] { Died("A", 30_000) });
        Assert.Null(WipeClassifier.Classify(killed, sigs, "kill", 0));

        var phantom = Replay(8, players, new[] { Died("A", 5_000) });
        Assert.Null(WipeClassifier.Classify(phantom, sigs, "wipe", 50));

        var deathless = Replay(60, players, Array.Empty<ReplayEvent>());
        Assert.Null(WipeClassifier.Classify(deathless, sigs, "wipe", 50));
    }

    [Fact]
    public void CalledWipe_EarlyReset_FirstDeathEarly_ShortPull_NoPriorInflection()
    {
        // 30s pull, first death at 2s (< 15% of 29s), no signal inflection before it.
        var players = new[] { Player("A"), Player("B"), Player("C") };
        var r = Replay(30, players, new[] { Died("A", 2_000) });
        var cls = WipeClassifier.Classify(r, new[] { Quiet("coverage", 30) }, "wipe", 95);

        Assert.NotNull(cls);
        Assert.Equal("called-wipe", cls!.Kind);
        Assert.Equal("early-reset", cls.CalledWipePattern);
        Assert.Empty(cls.Affected); // nobody is "at fault" on a reset
    }

    [Fact]
    public void CalledWipe_SynchronizedReset_RosterDiesTogether_NoStrongBuildup()
    {
        // 4 of 5 players die inside one 2s cluster at ~30s; the only inflection is at 5s —
        // outside the 10s pre-cluster window — so Pattern B fires.
        var players = new[] { Player("A"), Player("B"), Player("C"), Player("D"), Player("E") };
        var deaths = new[] { Died("A", 30_000), Died("B", 30_500), Died("C", 31_000), Died("D", 31_800) };
        var r = Replay(60, players, deaths);
        var sigs = new[] { Strong("group-spacing", 60, 5_000) };

        var cls = WipeClassifier.Classify(r, sigs, "wipe", 60);
        Assert.NotNull(cls);
        Assert.Equal("synchronized-reset", cls!.CalledWipePattern);
        Assert.Equal(30_000, cls.InflectionMs); // aligned to the cluster start
    }

    [Fact]
    public void SynchronizedCluster_WithStrongBuildup_IsNotCalled_ItIsSystemic()
    {
        // Same death cluster, but a STRONG inflection 4s before it — a genuine cascade.
        // Pattern B must not fire; 4/5 fatality (0.75–0.90 band) → systemic.
        var players = new[] { Player("A"), Player("B"), Player("C"), Player("D"), Player("E") };
        var deaths = new[] { Died("A", 30_000), Died("B", 30_500), Died("C", 31_000), Died("D", 31_800) };
        var r = Replay(60, players, deaths);
        var sigs = new[] { Strong("group-spacing", 60, 26_000), Strong("hp-variance", 60, 27_000) };

        var cls = WipeClassifier.Classify(r, sigs, "wipe", 60);
        Assert.NotNull(cls);
        Assert.Null(cls!.CalledWipePattern);
        Assert.Equal("systemic", cls.Kind);
        Assert.NotEmpty(cls.Affected);
    }

    [Fact]
    public void CalledWipe_LateThroughput_TailClusterOnPlateau()
    {
        // 100s pull: boss flat at 12% for the whole tail, all 3 deaths clustered in the
        // last 10% (≥95s), boss under 25% → late-throughput.
        var players = new[] { Player("A"), Player("B"), Player("C") };
        var entities = players.Append(Boss()).ToArray();
        var bossHp = Enumerable.Range(0, 100).Select(s => 0.12).ToList();
        var deaths = new[] { Died("A", 96_000), Died("B", 96_500), Died("C", 97_500) };
        var r = Replay(100, entities, deaths, bossHp);

        var cls = WipeClassifier.Classify(r, new[] { Quiet("coverage", 100) }, "wipe", 12);
        Assert.NotNull(cls);
        Assert.Equal("late-throughput", cls!.CalledWipePattern);
    }

    [Fact]
    public void FullFatality_Systemic_AffectedIsEveryone()
    {
        // Both players die, far apart in time (no cluster ≥2 within 3s) on a 60s pull →
        // no called-wipe pattern; fatality 100% → systemic, affected = all (short names).
        var players = new[] { Player("A"), Player("B") };
        var deaths = new[] { Died("A", 20_000), Died("B", 40_000) };
        var r = Replay(60, players, deaths);

        var cls = WipeClassifier.Classify(r, new[] { Quiet("coverage", 60) }, "wipe", 45);
        Assert.NotNull(cls);
        Assert.Equal("systemic", cls!.Kind);
        Assert.Equal(2, cls.Affected.Count);
        Assert.Contains("A", cls.Affected); // DisplayName "A-Realm" → short name "A"
    }

    [Fact]
    public void AdaptSignals_CoverageInverted_SnapBecomesOnset()
    {
        // A replay with a healer that walks away: coverage 1.0 then 0.0 → badness 0→1,
        // and the snap second becomes the coverage signal's inflection.
        var entities = new[]
        {
            new ReplayEntity { EntityId = "Player-H", Kind = "Player", DisplayName = "H", Role = "Healer" },
            new ReplayEntity { EntityId = "Player-D", Kind = "Player", DisplayName = "D" },
        };
        var frames = Enumerable.Range(0, 20).Select(s => new ReplayFrame
        {
            T = s * 1000,
            EntityPositions = s < 10 ? new[] { 0.1, 0.5, 0.2, 0.5 } : new[] { 0.9, 0.5, 0.2, 0.5 },
        }).ToArray();
        var replay = new PullReplay
        {
            SchemaVersion = "v1", PullId = "p1", FrameStepMs = 1000,
            ArenaYd = new ArenaYd(100, 100), Entities = entities, Frames = frames,
            Events = Array.Empty<ReplayEvent>(), PullParticipantIds = Array.Empty<string>(),
        };
        var adapted = WipeClassifier.AdaptSignals(SignalsArtifact.BuildForReplay(replay));
        var cov = adapted.First(s => s.Id == "coverage");

        Assert.True(cov.Series[15] > cov.Series[5]); // badness rises after the walk-off
        Assert.NotNull(cov.InflectionMs);            // the snap anchored the onset
        // The snap detector's ≤3s lookahead window can anchor up to 3s ahead of the drop
        // (same as the JS original) — accept the 7-13s band around the second-10 walk-off.
        Assert.True(cov.InflectionMs >= 7_000 && cov.InflectionMs <= 13_000);
    }
}
