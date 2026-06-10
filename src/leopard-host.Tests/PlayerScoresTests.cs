using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>Invariant tests for the player-* suite port: the survival timing formula,
/// role-relative damage scoring, role-curved movement scores, cohesion/proximity awareness,
/// and archetype classification. Arena 100x100yd, 200ms frames.</summary>
public class PlayerScoresTests
{
    private static ReplayEntity Ent(string id, string role, string kind = "Player")
        => new() { EntityId = id, Kind = kind, DisplayName = id, Role = role };

    private static PullReplay Replay(ReplayEntity[] entities, double[][] frames,
        ReplayEvent[]? events = null)
        => new()
        {
            SchemaVersion = "v1", PullId = "p1", FrameStepMs = 200,
            ArenaYd = new ArenaYd(100, 100), Entities = entities,
            Frames = frames.Select((p, i) => new ReplayFrame { T = i * 200, EntityPositions = p }).ToArray(),
            Events = events ?? Array.Empty<ReplayEvent>(),
            PullParticipantIds = Array.Empty<string>(),
        };

    private static ReplayEvent Ev(string kind, string? src, string? tgt, long atMs,
        int? amount = null, int? spellId = null)
        => new()
        {
            EventId = $"e{atMs}{kind}", PullId = "p1", Timestamp = "", PullTimeMs = atMs,
            EventKind = kind, SourceEntityId = src, TargetEntityId = tgt,
            Amount = amount, SpellId = spellId, SpellName = spellId is null ? null : $"S{spellId}",
        };

    [Fact]
    public void Survival_NeverDied100_EarlyDeathHeavy_LateDeathMild_ExtraDeathsStack()
    {
        var entities = new[] { Ent("A", "RangedDps"), Ent("B", "RangedDps"), Ent("C", "RangedDps") };
        var frames = Enumerable.Range(0, 501).Select(_ => new double[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 }).ToArray();
        // 100s pull. A never dies; B dies at 90s; C dies at 10s and again at 50s.
        var events = new[]
        {
            Ev("UnitDied", null, "B", 90_000),
            Ev("UnitDied", null, "C", 10_000), Ev("UnitDied", null, "C", 50_000),
        };
        var sur = PlayerScores.ComputeSurvival(Replay(entities, frames, events), 100_000);

        Assert.Equal(100, sur["A"].SurvivalScore);
        Assert.Equal(45, sur["B"].SurvivalScore);  // round(0.9·50) = 45
        Assert.Equal(0, sur["C"].SurvivalScore);   // round(0.1·50)=5, −5 for the 2nd death
    }

    [Fact]
    public void Damage_RelativeToRolePeers_AverageIsFifty()
    {
        // Two ranged: A deals 2/3 of damage, B 1/3. Expected share = 0.5 each →
        // A ratio 1.333 → ~67; B ratio 0.667 → ~33.
        var entities = new[] { Ent("A", "RangedDps"), Ent("B", "RangedDps"), Ent("Boss", "", "Boss") };
        var frames = Enumerable.Range(0, 100).Select(_ => new double[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 }).ToArray();
        var events = new[]
        {
            Ev("SpellDamage", "A", "Boss", 1000, 2000, 1),
            Ev("SpellDamage", "B", "Boss", 2000, 1000, 2),
        };
        var replay = Replay(entities, frames, events);
        var scores = PlayerScores.Compute(replay).ToDictionary(s => s.EntityId);

        Assert.True(scores["A"].DamageScore > 60 && scores["A"].DamageScore < 75);
        Assert.True(scores["B"].DamageScore > 25 && scores["B"].DamageScore < 40);

        var dmg = PlayerScores.ComputeDamage(replay, 20_000);
        Assert.Equal(0.667, dmg["A"].DamageShare, 3);
        Assert.Equal(1.0, dmg["A"].BossRatio, 3);
    }

    [Fact]
    public void Movement_RoleCurves_StillDpsExcellent_StillHealerPenalized()
    {
        // Everyone perfectly still: stillness 1.0 → DPS 100 (capped), healer |1−0.65|·130 ≈ 54.
        var entities = new[] { Ent("D", "RangedDps"), Ent("H", "Healer") };
        var frames = Enumerable.Range(0, 50).Select(_ => new double[] { 0.5, 0.5, 0.3, 0.3 }).ToArray();
        var scores = PlayerScores.Compute(Replay(entities, frames)).ToDictionary(s => s.EntityId);

        Assert.Equal(100, scores["D"].MovementScore);
        Assert.InRange(scores["H"].MovementScore, 50, 60);
    }

    [Fact]
    public void Awareness_CohesionAndBossProximity()
    {
        // Melee M on the boss and with the group; ranged R 40yd away from both.
        var entities = new[] { Ent("M", "MeleeDps"), Ent("R", "RangedDps"), Ent("Boss", "", "Boss") };
        var frames = Enumerable.Range(0, 50)
            .Select(_ => new double[] { 0.50, 0.50, 0.90, 0.50, 0.52, 0.50 }).ToArray();
        var aw = PlayerScores.ComputeAwareness(Replay(entities, frames));

        Assert.Equal(100, aw["M"].BossProximityPct);  // 2yd from boss every frame
        Assert.Null(aw["R"].BossProximityPct);        // ranged: not applicable
        Assert.True(aw["M"].GroupCohesionPct > 90);   // centroid is between them; M ~20yd... both contribute
        Assert.Equal(0, aw["R"].InterruptCount);
    }

    [Fact]
    public void Archetypes_FragileDamage_And_SupportPillar()
    {
        // Fragile damage: high dmg + early death. Support pillar: high awareness, low dmg.
        var entities = new[]
        {
            Ent("Glass", "MeleeDps"), Ent("Pillar", "MeleeDps"), Ent("Boss", "", "Boss"),
        };
        // Glass and Pillar both sit on the boss (cohesion + proximity high for both).
        var frames = Enumerable.Range(0, 300)
            .Select(_ => new double[] { 0.50, 0.50, 0.52, 0.50, 0.51, 0.50 }).ToArray();
        var events = new[]
        {
            Ev("SpellDamage", "Glass", "Boss", 1000, 10_000, 1), // all the damage
            Ev("UnitDied", null, "Glass", 5_000),                // dies at 8% of a 60s pull
            Ev("SpellInterrupt", "Pillar", "Boss", 10_000),      // utility
            Ev("SpellInterrupt", "Pillar", "Boss", 30_000),
        };
        var scores = PlayerScores.Compute(Replay(entities, frames, events)).ToDictionary(s => s.EntityId);

        Assert.Equal("fragile_damage", scores["Glass"].Archetype);
        Assert.Equal("support_pillar", scores["Pillar"].Archetype);
    }
}
