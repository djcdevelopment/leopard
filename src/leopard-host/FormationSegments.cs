using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>formation: stacked (&lt;5yd median pairwise) | split (5-15yd) | dispersed (≥15yd).</summary>
public sealed record FormationSegment(
    int StartFrameIdx, int EndFrameIdx, int StartMs, int EndMs,
    string Formation, double MedianPairwiseDistanceYd, int FrameCount);

public sealed record SegmentDetectionResult(string PullId, IReadOnlyList<FormationSegment> Segments);

/// <summary>
/// Per-pull formation-segment detection, ported from RaidUI's <c>shape/segments.js</c>:
/// the upper-triangle pairwise-distance vector per frame, a Frobenius-style norm of the
/// frame-over-frame difference, an adaptive change-point threshold (median + 2×MAD of the
/// deltas), boundaries at exceedances ≥ 10 frames apart, and per-segment formation
/// bucketing by median pairwise distance in yards. The pull's movement phases —
/// "stacked for 40s, then split, then dispersed" — from positions alone.
/// </summary>
public static class FormationSegments
{
    public const int SegmentMinFrames = 10;

    public static SegmentDetectionResult Detect(PullReplay replay)
    {
        var frames = replay.Frames;
        var f = frames.Count;
        var stepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        var arenaYdAvg = (replay.ArenaYd.Width + replay.ArenaYd.Height) / 2;

        var playerIdx = new List<int>();
        for (var i = 0; i < replay.Entities.Count; i++)
            if (replay.Entities[i].Kind == "Player") playerIdx.Add(i);
        var p = playerIdx.Count;
        if (f < 2 || p < 2)
            return new SegmentDetectionResult(replay.PullId, Array.Empty<FormationSegment>());

        var pairCount = p * (p - 1) / 2;
        var pdByFrame = new double[f * pairCount];
        for (var fi = 0; fi < f; fi++)
        {
            var ep = frames[fi].EntityPositions;
            var k = 0;
            for (var i = 0; i < p; i++)
            {
                var xi = ep[playerIdx[i] * 2]; var yi = ep[playerIdx[i] * 2 + 1];
                for (var j = i + 1; j < p; j++)
                {
                    var dx = xi - ep[playerIdx[j] * 2];
                    var dy = yi - ep[playerIdx[j] * 2 + 1];
                    pdByFrame[fi * pairCount + k] = Math.Sqrt(dx * dx + dy * dy);
                    k++;
                }
            }
        }

        // Frobenius-style norm of the inter-frame difference vector.
        var frobDeltas = new double[f - 1];
        for (var fi = 1; fi < f; fi++)
        {
            double sumSq = 0;
            for (var k = 0; k < pairCount; k++)
            {
                var diff = pdByFrame[fi * pairCount + k] - pdByFrame[(fi - 1) * pairCount + k];
                sumSq += diff * diff;
            }
            frobDeltas[fi - 1] = Math.Sqrt(sumSq);
        }

        // Adaptive threshold: median + 2×MAD (JS floor-index median convention).
        var sorted = frobDeltas.OrderBy(x => x).ToArray();
        var median = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0;
        var mad = sorted.Length > 0
            ? sorted.Select(v => Math.Abs(v - median)).OrderBy(x => x).ToArray()[sorted.Length / 2]
            : 0;
        var threshold = median + 2 * mad;

        var boundaries = new List<int> { 0 };
        var lastBoundary = 0;
        for (var i = 0; i < frobDeltas.Length; i++)
        {
            var frameIdx = i + 1; // transition i → i+1; the new segment starts at i+1
            if (frobDeltas[i] > threshold && frameIdx - lastBoundary >= SegmentMinFrames)
            {
                boundaries.Add(frameIdx);
                lastBoundary = frameIdx;
            }
        }
        if (boundaries[^1] != f) boundaries.Add(f);

        var segments = new List<FormationSegment>();
        for (var k = 0; k < boundaries.Count - 1; k++)
        {
            var start = boundaries[k];
            var end = boundaries[k + 1];
            if (end - start < SegmentMinFrames) continue;

            var values = new double[(end - start) * pairCount];
            var vi = 0;
            for (var fi = start; fi < end; fi++)
                for (var kk = 0; kk < pairCount; kk++)
                    values[vi++] = pdByFrame[fi * pairCount + kk];
            Array.Sort(values);
            var medYd = (values.Length > 0 ? values[values.Length / 2] : 0) * arenaYdAvg;

            var formation = medYd < 5 ? "stacked" : medYd < 15 ? "split" : "dispersed";
            segments.Add(new FormationSegment(start, end, start * stepMs, end * stepMs,
                formation, medYd, end - start));
        }
        return new SegmentDetectionResult(replay.PullId, segments);
    }

    /// <summary>One-line phase story for evidence text: "stacked 0-38s, split 38-71s, …".</summary>
    public static string? Describe(SegmentDetectionResult result)
    {
        if (result.Segments.Count == 0) return null;
        return string.Join(", ", result.Segments.Select(s =>
            $"{s.Formation} {s.StartMs / 1000}-{s.EndMs / 1000}s ({s.MedianPairwiseDistanceYd:0}yd)"));
    }

    /// <summary>Per-night artifact: one card per boss, the detected movement phases per pull
    /// with replay frames (segments + the one-line phase story). Sibling to
    /// <see cref="SignalsArtifact.BuildJson"/>; cached as <c>.segments.v1.json</c>.</summary>
    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var encounters = EncountersProjection.ToEncounters(parse.Sessions);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<object>();

        foreach (var enc in encounters)
        {
            if (!seen.Add(enc.Id)) continue;
            var pulls = new List<object>();
            foreach (var p in enc.Pulls)
            {
                IReadOnlyList<FormationSegment>? segments = null;
                string? phases = null;
                if (parse.ReplaysByPullId.TryGetValue(p.Id, out var replay) && replay.Frames.Count > 1)
                {
                    try
                    {
                        var result = Detect(replay);
                        segments = result.Segments;
                        phases = Describe(result);
                    }
                    catch { /* a malformed replay must not sink the night */ }
                }
                pulls.Add(new { pullId = p.Id, n = p.N, outcome = p.Outcome, segments, phases });
            }
            cards.Add(new
            {
                encounterId = enc.Id,
                encounterName = enc.Name,
                difficulty = enc.Difficulty,
                pulls,
            });
        }
        return JsonSerializer.Serialize(new { encounters = cards }, json);
    }
}
