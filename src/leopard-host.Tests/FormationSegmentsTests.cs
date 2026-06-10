using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>Invariant tests for the formation-segment port (shape/segments.js): adaptive
/// change-point detection over pairwise-distance deltas, min-segment gating, and the
/// stacked / split / dispersed bucketing. Arena 100x100yd.</summary>
public class FormationSegmentsTests
{
    private static PullReplay Replay(double[][] positionsByFrame)
        => new()
        {
            SchemaVersion = "v1", PullId = "p1", FrameStepMs = 200,
            ArenaYd = new ArenaYd(100, 100),
            Entities = new[] { "A", "B", "C" }.Select(id => new ReplayEntity
                { EntityId = id, Kind = "Player", DisplayName = id }).ToArray(),
            Frames = positionsByFrame.Select((p, i) => new ReplayFrame
                { T = i * 200, EntityPositions = p }).ToArray(),
            Events = Array.Empty<ReplayEvent>(),
            PullParticipantIds = Array.Empty<string>(),
        };

    [Fact]
    public void ConstantFormation_OneSegment()
    {
        // Three players stacked within 3yd, never moving: one stacked segment, full span.
        var pos = new[] { 0.50, 0.50, 0.52, 0.50, 0.51, 0.52 };
        var frames = Enumerable.Range(0, 40).Select(_ => pos).ToArray();
        var result = FormationSegments.Detect(Replay(frames));

        Assert.Single(result.Segments);
        Assert.Equal("stacked", result.Segments[0].Formation);
        Assert.Equal(40, result.Segments[0].FrameCount);
    }

    [Fact]
    public void FormationChange_TwoSegments_StackedThenDispersed()
    {
        // Stacked for 25 frames, then everyone teleports 30+yd apart for 25 frames:
        // the Frobenius delta spikes at the transition → two segments, correctly bucketed.
        var stacked = new[] { 0.50, 0.50, 0.52, 0.50, 0.51, 0.52 };
        var spread = new[] { 0.10, 0.10, 0.50, 0.90, 0.90, 0.10 };
        var frames = Enumerable.Range(0, 50).Select(i => i < 25 ? stacked : spread).ToArray();
        var result = FormationSegments.Detect(Replay(frames));

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("stacked", result.Segments[0].Formation);
        Assert.Equal("dispersed", result.Segments[1].Formation);
        Assert.Equal(25, result.Segments[1].StartFrameIdx);
    }

    [Fact]
    public void SplitBucket_BetweenFiveAndFifteenYards()
    {
        // Median pairwise ≈ 10yd → "split".
        var pos = new[] { 0.50, 0.50, 0.60, 0.50, 0.55, 0.58 };
        var frames = Enumerable.Range(0, 30).Select(_ => pos).ToArray();
        var result = FormationSegments.Detect(Replay(frames));

        Assert.Single(result.Segments);
        Assert.Equal("split", result.Segments[0].Formation);
        Assert.InRange(result.Segments[0].MedianPairwiseDistanceYd, 5, 15);
    }

    [Fact]
    public void TooFewFramesOrPlayers_NoSegments()
    {
        var one = FormationSegments.Detect(Replay(new[] { new[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 } }));
        Assert.Empty(one.Segments);
    }
}
