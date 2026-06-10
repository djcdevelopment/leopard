using Tempo.Core.Ingest;
using Xunit;

namespace Leopard.Host.Tests;

/// <summary>Invariant tests for the divergence port (compare/entity-match.js +
/// divergence.js): match rules (players by participantId, bosses by name, adds never),
/// role weighting, and pip extraction (threshold runs, prominence, debounce).</summary>
public class PullDivergenceTests
{
    private static ReplayEntity Ent(string id, string kind, string? role = null, string? pid = null)
        => new() { EntityId = id, Kind = kind, DisplayName = id, Role = role, ParticipantId = pid };

    private static PullReplay Replay(ReplayEntity[] entities, double[][] frames)
        => new()
        {
            SchemaVersion = "v1", PullId = "p", FrameStepMs = 200,
            ArenaYd = new ArenaYd(100, 100), Entities = entities,
            Frames = frames.Select((p, i) => new ReplayFrame { T = i * 200, EntityPositions = p }).ToArray(),
            Events = Array.Empty<ReplayEvent>(), PullParticipantIds = Array.Empty<string>(),
        };

    [Fact]
    public void Match_PlayersByParticipantId_BossesByName_AddsNever()
    {
        var left = new[]
        {
            Ent("e1", "Player", "Tank", "P-A"), Ent("e2", "Player", "Healer", "P-B"),
            Ent("e3", "Boss"), Ent("e4", "Add"),
        };
        var right = new[]
        {
            Ent("x9", "Player", "Healer", "P-B"), Ent("x3", "Boss"),
            Ent("x4", "Add"), Ent("x1", "Player", "Tank", "P-A"),
        };
        // Boss display names must match: rename both to the same.
        left[2] = left[2] with { DisplayName = "Lothraxion" };
        right[1] = right[1] with { DisplayName = "Lothraxion" };

        var matches = PullDivergence.Match(left, right);
        Assert.Equal(3, matches.Count); // two players + the boss; the add never matches
        Assert.Contains(matches, m => m.Kind == "Player" && m.Key == "P-A" && m.RightIdx == 3);
        Assert.Contains(matches, m => m.Kind == "Boss" && m.Key == "Lothraxion");
    }

    [Fact]
    public void Weighted_TankDominatesTheMean()
    {
        // Tank diverges by 0.30; dps identical. Uniform mean = 0.15; tank-weighted
        // (2.0 vs 1.0) = 0.30·2/3 = 0.20.
        var entities = new[] { Ent("t", "Player", "Tank", "P-T"), Ent("d", "Player", "RangedDps", "P-D") };
        var left = Replay(entities, new[] { new[] { 0.2, 0.5, 0.7, 0.5 } });
        var right = Replay(entities, new[] { new[] { 0.5, 0.5, 0.7, 0.5 } });
        var series = PullDivergence.ComputeWeighted(left, right, PullDivergence.Match(entities, entities));

        Assert.Equal(0.20, series[0], 2);
    }

    [Fact]
    public void Pips_RunsCollapseToPeaks_WithDebounce()
    {
        // Series: quiet, one 5-frame run peaking at idx 12, quiet, a second run at idx 30.
        var series = new double[40];
        series[10] = 0.13; series[11] = 0.14; series[12] = 0.18; series[13] = 0.14; series[14] = 0.13;
        series[30] = 0.16; series[31] = 0.15;
        var pips = PullDivergence.ExtractPips(series, 200);

        Assert.Equal(2, pips.Count);
        Assert.Equal(12 * 200, pips[0].TimeMs);
        Assert.Equal(0.18, pips[0].PeakDelta, 3);
        Assert.Equal(30 * 200, pips[1].TimeMs);
    }

    [Fact]
    public void Pips_ProminentDoublePeak_EmitsBoth_TinyWiggleDoesNot()
    {
        // One long run with two prominent peaks separated by a deep valley → 2 pips
        // (they are 10 frames = 2s apart, past the 500ms debounce).
        var series = new double[40];
        for (var i = 5; i <= 25; i++) series[i] = 0.13;
        series[10] = 0.20; series[15] = 0.14; series[20] = 0.19; // valley 0.13 between peaks
        var pips = PullDivergence.ExtractPips(series, 200);
        Assert.Equal(2, pips.Count);

        // Same run but the second bump is a 0.005 wiggle (< 0.015 prominence) → 1 pip.
        var series2 = new double[40];
        for (var i = 5; i <= 25; i++) series2[i] = 0.13;
        series2[10] = 0.20; series2[20] = 0.135;
        var pips2 = PullDivergence.ExtractPips(series2, 200);
        Assert.Single(pips2);
    }
}
