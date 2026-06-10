using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>
/// Invariant tests for the coverage quality model port (shape/coverage-timeline.js →
/// CoverageTimeline) and the classifier follow-ups it unlocks (pattern tags + named-healer
/// offender). Arena 100x100yd: normalized 0.1 = 10yd; default healer range 30yd → 0.3.
/// </summary>
public class CoverageTimelineTests
{
    private static ReplayEntity Ent(string id, string? role = null, string kind = "Player")
        => new() { EntityId = id, Kind = kind, DisplayName = $"{id}-Realm", Role = role };

    private static PullReplay Replay(ReplayEntity[] entities, double[][] positionsByFrame,
        ReplayEvent[]? events = null, int frameStepMs = 200)
        => new()
        {
            SchemaVersion = "v1", PullId = "p1", FrameStepMs = frameStepMs,
            ArenaYd = new ArenaYd(100, 100), Entities = entities,
            Frames = positionsByFrame.Select((p, i) => new ReplayFrame
                { T = i * frameStepMs, EntityPositions = p }).ToArray(),
            Events = events ?? Array.Empty<ReplayEvent>(),
            PullParticipantIds = Array.Empty<string>(),
        };

    [Fact]
    public void CenteredHealer_PerfectCentrality_HighScore()
    {
        // Healer exactly at the centroid of two raiders, both well inside range.
        var entities = new[] { Ent("H", "Healer"), Ent("A"), Ent("B") };
        var pos = new[] { 0.5, 0.5, /*A*/ 0.45, 0.5, /*B*/ 0.55, 0.5 };
        var cov = CoverageTimeline.Compute(Replay(entities, new[] { pos }));

        var f = cov.Frames[0];
        Assert.Equal(100, f.Raid.Pct);             // both raiders covered
        Assert.Equal(1.0, f.PerHealer[0].Centrality, 2);
        // edge proximity = 5yd/30yd ≈ 0.167 → score = 100·(0.5·1 + 0.5·(1−0.167)) ≈ 92
        Assert.InRange(f.Quality.OverallScore, 88, 95);
    }

    [Fact]
    public void EdgeOfRange_LowQuality_EvenThoughCovered()
    {
        // Raiders at 29yd — covered, but at the rim: same raid %, much worse quality.
        var entities = new[] { Ent("H", "Healer"), Ent("A"), Ent("B") };
        var pos = new[] { 0.5, 0.5, 0.21, 0.5, 0.79, 0.5 };
        var cov = CoverageTimeline.Compute(Replay(entities, new[] { pos }));

        var f = cov.Frames[0];
        Assert.Equal(100, f.Raid.Pct);
        // edge proximity ≈ 29/30 ≈ 0.97; centrality 1 (centroid is the healer) →
        // score ≈ 100·(0.5 + 0.5·0.03) ≈ 52 — "covered" can still be fragile.
        Assert.InRange(f.Quality.OverallScore, 45, 60);
    }

    [Fact]
    public void TankBucket_SeparatesFromRaid()
    {
        // Tank out of range, dps in range: raid 50%, tank 0%.
        var entities = new[] { Ent("H", "Healer"), Ent("T", "Tank"), Ent("D") };
        var pos = new[] { 0.5, 0.5, /*T*/ 0.95, 0.5, /*D*/ 0.55, 0.5 };
        var cov = CoverageTimeline.Compute(Replay(entities, new[] { pos }));

        Assert.Equal(50, cov.Frames[0].Raid.Pct);
        Assert.Equal(0, cov.Frames[0].Tank.Pct);
    }

    [Fact]
    public void Snap_QualityCollapse_WithDamageCorrelation()
    {
        // 30 frames at 200ms. Healer centered for 15 frames, then teleports far: quality
        // collapses; a damage event 1s after the collapse correlates.
        var entities = new[] { Ent("H", "Healer"), Ent("A"), Ent("B") };
        var frames = Enumerable.Range(0, 30)
            .Select(i => i < 15
                ? new[] { 0.5, 0.5, 0.45, 0.5, 0.55, 0.5 }
                : new[] { 0.95, 0.95, 0.45, 0.5, 0.55, 0.5 }).ToArray();
        var dmg = new ReplayEvent
        {
            EventId = "d1", PullId = "p1", Timestamp = "", PullTimeMs = 4000,
            EventKind = "SpellDamage",
        };
        var cov = CoverageTimeline.Compute(Replay(entities, frames, new[] { dmg }));

        Assert.NotEmpty(cov.Summary.SnappingPoints);
        var snap = cov.Summary.SnappingPoints[0];
        Assert.InRange(snap.TimeMs, 2800, 3400);          // collapse at frame 15 = 3000ms
        Assert.NotNull(snap.FollowedByDamageMs);          // the 4000ms hit, ~1s later
        Assert.InRange(snap.FollowedByDamageMs!.Value, 600, 1400);
        Assert.True(cov.Summary.MinQualityScore < 50);
    }

    [Fact]
    public void Offender_Snap_NamesTheHealerWhoMoved()
    {
        // TWO healers: H1 is the only one covering the raiders and walks off at frame 15;
        // H2 idles out of range the whole pull. Quality collapses (nobody is covered) and
        // the snap offender must be H1 — the healer whose centrality dropped — not H2.
        // (With H2 covering the raiders instead, quality would barely move and no snap
        // would fire — verified: the model correctly sees redundancy as resilience.)
        var entities = new[] { Ent("H1", "Healer"), Ent("H2", "Healer"), Ent("A"), Ent("B") };
        var frames = Enumerable.Range(0, 30)
            .Select(i => i < 15
                ? new[] { 0.5, 0.5, 0.9, 0.1, 0.45, 0.5, 0.55, 0.5 }
                : new[] { 0.95, 0.95, 0.9, 0.1, 0.45, 0.5, 0.55, 0.5 }).ToArray();
        var dmg = new ReplayEvent
        {
            EventId = "d1", PullId = "p1", Timestamp = "", PullTimeMs = 4000,
            EventKind = "SpellDamage",
        };
        var replay = Replay(entities, frames, new[] { dmg });
        var cov = CoverageTimeline.Compute(replay);

        var pattern = WipeClassifier.DetectCoveragePattern(cov, 3000);
        Assert.Equal("snap", pattern);
        var offender = WipeClassifier.DetectOffendingHealer("snap", cov, 3000, replay);
        Assert.NotNull(offender);
        Assert.Equal("H1", offender!.DisplayName);
    }

    [Fact]
    public void Pattern_EdgeOff_WhenChronicallyPoor_NoSnap()
    {
        // Raiders permanently at the rim: no snap (no drop — it starts bad), avg quality
        // low → edge-off.
        var entities = new[] { Ent("H", "Healer"), Ent("A"), Ent("B") };
        var pos = new[] { 0.5, 0.5, 0.22, 0.5, 0.78, 0.5 };
        var frames = Enumerable.Range(0, 30).Select(_ => pos).ToArray();
        var cov = CoverageTimeline.Compute(Replay(entities, frames));

        Assert.Empty(cov.Summary.SnappingPoints);
        Assert.Equal("edge-off", WipeClassifier.DetectCoveragePattern(cov, 3000));
    }

    [Fact]
    public void Fragile_AccumulatesBelow70Pct()
    {
        // One of two raiders out of range the whole pull → raid 50% < 70% every frame.
        var entities = new[] { Ent("H", "Healer"), Ent("A"), Ent("B") };
        var pos = new[] { 0.5, 0.5, 0.52, 0.5, 0.95, 0.5 };
        var frames = Enumerable.Range(0, 10).Select(_ => pos).ToArray();
        var cov = CoverageTimeline.Compute(Replay(entities, frames));

        Assert.Equal(10 * 200, cov.Summary.TimeInFragileCoverageMs);
    }
}
