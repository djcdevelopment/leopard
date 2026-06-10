using Xunit;

namespace Leopard.Host.Tests;

/// <summary>Invariant tests for the meters port (shape/meters.js + group-summary.js):
/// distance/speed/stationary math, peak single-frame speed (the teleport detector), the
/// wipes-vs-kills partition with null sides, and the group summary helpers.</summary>
public class ParticipantMetersTests
{
    // 100yd arena; points normalized. One participant, hand-built trajectories.
    private static ShapeParticipant P(string id, params ParticipantTrajectory[] trs)
        => new(id, id, "RangedDps", trs);

    private static ParticipantTrajectory Walk(string pullId, int frames, double stepNorm,
        string? outcome = null)
        => new(pullId, 100,
            Enumerable.Range(0, frames).SelectMany(i => new[] { 0.1 + i * stepNorm, 0.5 }).ToArray(),
            outcome);

    [Fact]
    public void Distance_Speed_Stationary_Math()
    {
        // 11 points moving 0.01 (=1yd) per 200ms frame: 10yd total, 5 yd/s, never still.
        var rows = ParticipantMeters.Compute(new[] { P("A", Walk("p1", 11, 0.01)) });
        var r = rows[0];

        Assert.Equal(10.0, r.TotalDistanceYd, 1);
        Assert.Equal(5.0, r.AvgSpeedYdPerSec, 1);
        Assert.Equal(0.0, r.StationaryRatio, 3);
        Assert.Equal(1.0, r.MovedRatio, 3);
    }

    [Fact]
    public void PeakSpeed_CatchesTheTeleport()
    {
        // Slow walk, then one 30yd blink in a single frame: peak = 150 yd/s.
        var pts = new List<double>();
        for (var i = 0; i < 10; i++) pts.AddRange(new[] { 0.1 + i * 0.005, 0.5 });
        pts.AddRange(new[] { 0.145 + 0.30, 0.5 }); // the blink: 30yd in one 200ms frame
        var rows = ParticipantMeters.Compute(new[]
            { P("A", new ParticipantTrajectory("p1", 100, pts.ToArray())) });

        Assert.Equal(150.0, rows[0].PeakSpeedYdPerSec, 0);
    }

    [Fact]
    public void Stationary_BelowPhysicalThreshold()
    {
        // 0.0001 normalized = 0.01yd per frame = 0.05 yd/s < 0.1 yd/s → still every frame.
        var rows = ParticipantMeters.Compute(new[] { P("A", Walk("p1", 20, 0.0001)) });
        Assert.Equal(1.0, rows[0].StationaryRatio, 3);
    }

    [Fact]
    public void ByOutcome_PartitionsAndDeltas_NullWhenOneSideEmpty()
    {
        // A moved fast on the wipe, slow on the kill → kill-minus-wipe distance delta < 0.
        var a = P("A", Walk("w1", 11, 0.02, "wipe"), Walk("k1", 11, 0.005, "kill"));
        // B only has wipe pulls → kill side empty → null deltas.
        var b = P("B", Walk("w1", 11, 0.01, "wipe"));
        var result = ParticipantMeters.ComputeByOutcome(new[] { a, b });

        Assert.Equal(1, result.WipeSamples);
        Assert.Equal(1, result.KillSamples);
        var dA = result.Delta.First(d => d.ParticipantId == "A");
        Assert.NotNull(dA.DistanceDelta);
        Assert.True(dA.DistanceDelta < 0); // killed while moving less
        var dB = result.Delta.First(d => d.ParticipantId == "B");
        Assert.Null(dB.DistanceDelta);
        Assert.Equal(0, dB.KillPulls);
    }

    [Fact]
    public void GroupSummary_MeansAndMedians()
    {
        var rows = ParticipantMeters.Compute(new[]
        {
            P("A", Walk("p1", 11, 0.01)),  // 10yd
            P("B", Walk("p1", 11, 0.02)),  // 20yd
            P("C", Walk("p1", 11, 0.03)),  // 30yd
        });
        var group = ParticipantMeters.ComputeGroupSummary(rows, new HashSet<string> { "A", "B" });
        Assert.Equal(2, group.Count);
        Assert.Equal(15.0, group.DistanceMean, 1);

        var med = ParticipantMeters.ComputeOverallMedians(rows);
        Assert.Equal(20.0, med.DistanceMedian, 1);
    }
}
