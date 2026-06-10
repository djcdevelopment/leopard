using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>
/// Invariant tests for the affinity + clustering port (shape/affinity.js + clustering.js):
/// the co-proximity and co-direction components, the 0.5/0.5 composite, complete-linkage
/// clustering, and the k-group cut. Arena 100x100yd; 10yd proximity = 0.1 normalized.
/// </summary>
public class MovementAffinityTests
{
    private static PullReplay Replay(string pullId, string[] ids, double[][] positionsByFrame)
        => new()
        {
            SchemaVersion = "v1", PullId = pullId, FrameStepMs = 200,
            ArenaYd = new ArenaYd(100, 100),
            Entities = ids.Select(id => new ReplayEntity
                { EntityId = id, Kind = "Player", DisplayName = $"{id}-Realm", ParticipantId = id }).ToArray(),
            Frames = positionsByFrame.Select((p, i) => new ReplayFrame
                { T = i * 200, EntityPositions = p }).ToArray(),
            Events = Array.Empty<ReplayEvent>(),
            PullParticipantIds = Array.Empty<string>(),
        };

    private static double At(AffinityMatrixDto m, int a, int b) => m.Composite[a * m.ParticipantIds.Count + b];

    [Fact]
    public void TravelTogether_HighComposite_BothComponents()
    {
        // A and B walk the same line 3yd apart: every frame proximate, every velocity parallel.
        var frames = Enumerable.Range(0, 20)
            .Select(i => new[] { 0.1 + i * 0.01, 0.5, 0.1 + i * 0.01, 0.53 }).ToArray();
        var parts = MovementAffinity.BuildParticipants(new[] { Replay("p1", new[] { "A", "B" }, frames) });
        var m = MovementAffinity.ComputeAffinity(parts);

        Assert.Equal(1.0, At(m, 0, 1), 2); // coProx 1 and coDir 1 → composite 1
        Assert.Equal(At(m, 0, 1), At(m, 1, 0), 6); // symmetric
        Assert.Equal(1.0, At(m, 0, 0), 6);         // diagonal
    }

    [Fact]
    public void ParallelButFar_DirectionOnly_HalfComposite()
    {
        // Same direction, 40yd apart: coProx 0, coDir 1 → composite 0.5.
        var frames = Enumerable.Range(0, 20)
            .Select(i => new[] { 0.1 + i * 0.01, 0.3, 0.1 + i * 0.01, 0.7 }).ToArray();
        var m = MovementAffinity.ComputeAffinity(
            MovementAffinity.BuildParticipants(new[] { Replay("p1", new[] { "A", "B" }, frames) }));

        Assert.Equal(0.5, At(m, 0, 1), 2);
    }

    [Fact]
    public void StationaryAndFar_ZeroComposite()
    {
        // Far apart and never moving: neither component fires.
        var frames = Enumerable.Range(0, 20).Select(_ => new[] { 0.1, 0.1, 0.9, 0.9 }).ToArray();
        var m = MovementAffinity.ComputeAffinity(
            MovementAffinity.BuildParticipants(new[] { Replay("p1", new[] { "A", "B" }, frames) }));

        Assert.Equal(0.0, At(m, 0, 1), 3);
    }

    [Fact]
    public void Clustering_TwoTightPairs_CutToTwoGroups()
    {
        // A+B travel together on the left; C+D travel together on the right, opposite
        // direction. Cut at k=2 must recover the pairs.
        var frames = Enumerable.Range(0, 20).Select(i => new[]
        {
            0.1 + i * 0.01, 0.2,   // A →
            0.1 + i * 0.01, 0.23,  // B → (with A)
            0.9 - i * 0.01, 0.8,   // C ←
            0.9 - i * 0.01, 0.83,  // D ← (with C)
        }).ToArray();
        var parts = MovementAffinity.BuildParticipants(
            new[] { Replay("p1", new[] { "A", "B", "C", "D" }, frames) });
        var groups = MovementAffinity.CutToGroups(
            MovementAffinity.Cluster(MovementAffinity.ComputeAffinity(parts)), 2, parts);

        Assert.Equal(2, groups.Count);
        var sets = groups.Select(g => g.Members.OrderBy(x => x).ToArray()).ToList();
        Assert.Contains(sets, s => s.SequenceEqual(new[] { "A", "B" }));
        Assert.Contains(sets, s => s.SequenceEqual(new[] { "C", "D" }));
    }

    [Fact]
    public void CrossPull_TrajectoriesJoinByParticipantId()
    {
        // Two pulls: together in pull 1, far-and-stationary in pull 2 → composite averages
        // across both pulls' frames (≈ 0.5 of the together-pull's 1.0).
        var together = Enumerable.Range(0, 10)
            .Select(i => new[] { 0.1 + i * 0.01, 0.5, 0.1 + i * 0.01, 0.53 }).ToArray();
        var apart = Enumerable.Range(0, 10).Select(_ => new[] { 0.1, 0.1, 0.9, 0.9 }).ToArray();
        var m = MovementAffinity.ComputeAffinity(MovementAffinity.BuildParticipants(new[]
        {
            Replay("p1", new[] { "A", "B" }, together),
            Replay("p2", new[] { "A", "B" }, apart),
        }));

        Assert.InRange(At(m, 0, 1), 0.4, 0.6);
    }
}
