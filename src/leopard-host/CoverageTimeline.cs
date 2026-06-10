using Tempo.Core.Ingest;

namespace Leopard.Host;

public sealed record HealerFrameDto(
    string HealerId, int CoveredCount, double? CenterX, double? CenterY,
    double Centrality, double EdgeProximityOfCovered);

public sealed record CoverageBucketDto(int Covered, int Total, double Pct);

public sealed record CoverageQualityDto(
    double MeanEdgeProximity, double MeanHealerCentrality, int OverallScore);

public sealed record CoverageFrameDto(
    int FrameIdx, int TimeMs, CoverageBucketDto Raid, CoverageBucketDto Tank,
    CoverageBucketDto Flex, CoverageQualityDto Quality, IReadOnlyList<HealerFrameDto> PerHealer);

public sealed record SnappingPointDto(int TimeMs, double QualityDrop, int? FollowedByDamageMs);

public sealed record CoverageSummaryDto(
    double AvgRaidPct, double AvgTankPct, double AvgFlexPct, double AvgQualityScore,
    double MinRaidPct, double MinTankPct, int MinQualityScore,
    int TimeInFragileCoverageMs, IReadOnlyList<SnappingPointDto> SnappingPoints);

public sealed record CoverageSeriesDto(
    IReadOnlyList<CoverageFrameDto> Frames, CoverageSummaryDto Summary);

/// <summary>
/// The frame-level healing-coverage quality model, ported from RaidUI's
/// <c>shape/coverage-timeline.js</c> (the "5th categorically-distinct reducer"). Per 200ms
/// frame: raid / tank / flex coverage buckets, per-healer centroid-of-covered + centrality
/// (1 − distance-to-centroid/range) + edge-proximity-of-covered, and the composite quality
/// score <c>100 × (0.5·meanCentrality + 0.5·(1 − meanEdgeProximity))</c>. Snapping points =
/// quality drop ≥ 15 within ≤ 10 frames (2s), with a 3s damage-event lookahead correlation.
/// This is the model the wipe classifier's coverage-pattern tags and named-healer offender
/// attribution read — the v2c port that unlocked those deferred classify.js sections.
///
/// <para>v1 scope: every healer uses the default 30yd range (the JS HealerRangeConfig
/// per-healer overrides and the exclude-healer "what if" analysis ride with the heal-diff
/// port). Range is normalized by arenaYdAvg = (width + height) / 2, matching the overlay.</para>
/// </summary>
public static class CoverageTimeline
{
    public const double SnapQualityDrop = 15;
    public const int SnapWindowFrames = 10;       // 2.0s at 200ms
    public const int SnapDamageLookaheadMs = 3000;
    public const double FragileRaidPct = 70;
    public const double DefaultRangeYd = 30;

    public static CoverageSeriesDto Compute(PullReplay replay, double rangeYd = DefaultRangeYd)
    {
        var frames = replay.Frames;
        var entities = replay.Entities;
        var frameStepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        var f = frames.Count;

        var arenaW = replay.ArenaYd.Width;
        var arenaH = replay.ArenaYd.Height;
        var arenaYdAvg = arenaW > 0 && arenaH > 0 ? (arenaW + arenaH) / 2
            : arenaW > 0 ? arenaW : arenaH > 0 ? arenaH : 0;

        var healerIdx = new List<int>();
        var tankIdx = new List<int>();
        var raiderIdx = new List<int>(); // non-healer players = the "raid" bucket
        for (var i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            if (e.Kind != "Player") continue;
            if (e.Role?.Contains("heal", StringComparison.OrdinalIgnoreCase) == true) healerIdx.Add(i);
            else
            {
                raiderIdx.Add(i);
                if (e.Role?.Contains("tank", StringComparison.OrdinalIgnoreCase) == true) tankIdx.Add(i);
            }
        }
        var h = healerIdx.Count;
        var r = raiderIdx.Count;
        var t = tankIdx.Count;

        var rangeNorm = arenaYdAvg > 0 ? rangeYd / arenaYdAvg : 0;
        var healerIds = healerIdx.Select(i => entities[i].EntityId).ToArray();

        var framesOut = new List<CoverageFrameDto>(f);
        var qualityByFrame = new double[f];
        double sumRaid = 0, sumTank = 0, sumFlex = 0, sumQuality = 0;
        double minRaid = double.PositiveInfinity, minTank = double.PositiveInfinity;
        var minQuality = double.PositiveInfinity;
        var fragileMs = 0;

        var coveredBy = new List<int>[h];
        for (var k = 0; k < h; k++) coveredBy[k] = new List<int>();
        var coveredCount = new int[r];
        var minDistRatio = new double[r];

        for (var fi = 0; fi < f; fi++)
        {
            var fr = frames[fi];
            var pos = fr.EntityPositions;
            var timeMs = fr.T != 0 || fi == 0 ? fr.T : fi * frameStepMs;

            foreach (var list in coveredBy) list.Clear();
            Array.Fill(coveredCount, 0);
            Array.Fill(minDistRatio, double.PositiveInfinity);

            // Pass 1 — per (healer, raider) coverage.
            for (var hk = 0; hk < h; hk++)
            {
                if (rangeNorm <= 0) continue;
                var hx = pos[healerIdx[hk] * 2];
                var hy = pos[healerIdx[hk] * 2 + 1];
                for (var rk = 0; rk < r; rk++)
                {
                    var dx = pos[raiderIdx[rk] * 2] - hx;
                    var dy = pos[raiderIdx[rk] * 2 + 1] - hy;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= rangeNorm)
                    {
                        coveredBy[hk].Add(rk);
                        coveredCount[rk]++;
                        var ratio = d / rangeNorm;
                        if (ratio < minDistRatio[rk]) minDistRatio[rk] = ratio;
                    }
                }
            }

            var raidCovered = 0; var flexCovered = 0;
            for (var rk = 0; rk < r; rk++)
            {
                if (coveredCount[rk] >= 1) raidCovered++;
                if (coveredCount[rk] >= 2) flexCovered++;
            }
            var tankCovered = 0;
            foreach (var tEnt in tankIdx)
            {
                var slot = raiderIdx.IndexOf(tEnt);
                if (slot >= 0 && coveredCount[slot] >= 1) tankCovered++;
            }

            var raidPct = r > 0 ? 100.0 * raidCovered / r : 0;
            var tankPct = t > 0 ? 100.0 * tankCovered / t : 0;
            var flexPct = r > 0 ? 100.0 * flexCovered / r : 0;

            // Per-healer centroid / centrality / edge-proximity.
            var perHealer = new List<HealerFrameDto>(h);
            double centralitySum = 0; var centralityCount = 0;
            for (var hk = 0; hk < h; hk++)
            {
                var covered = coveredBy[hk];
                double? cxOut = null, cyOut = null;
                double centrality = 0, edgeProx = 1;
                if (covered.Count > 0)
                {
                    var hx = pos[healerIdx[hk] * 2];
                    var hy = pos[healerIdx[hk] * 2 + 1];
                    double cx = 0, cy = 0, edgeSum = 0;
                    foreach (var rk in covered)
                    {
                        var px = pos[raiderIdx[rk] * 2];
                        var py = pos[raiderIdx[rk] * 2 + 1];
                        cx += px; cy += py;
                        if (rangeNorm > 0)
                            edgeSum += Math.Sqrt((px - hx) * (px - hx) + (py - hy) * (py - hy)) / rangeNorm;
                    }
                    cxOut = cx / covered.Count;
                    cyOut = cy / covered.Count;
                    edgeProx = rangeNorm > 0 ? edgeSum / covered.Count : 1;
                    if (rangeNorm > 0)
                    {
                        var dCentroid = Math.Sqrt((hx - cxOut.Value) * (hx - cxOut.Value)
                            + (hy - cyOut.Value) * (hy - cyOut.Value));
                        centrality = 1 - Math.Min(1, dCentroid / rangeNorm);
                    }
                    centralitySum += centrality;
                    centralityCount++;
                }
                perHealer.Add(new HealerFrameDto(healerIds[hk], covered.Count, cxOut, cyOut,
                    Math.Round(centrality, 4), Math.Round(edgeProx, 4)));
            }
            var meanCentrality = centralityCount > 0 ? centralitySum / centralityCount : 0;

            double edgeSumAll = 0; var edgeCnt = 0;
            for (var rk = 0; rk < r; rk++)
            {
                if (coveredCount[rk] >= 1 && double.IsFinite(minDistRatio[rk]))
                {
                    edgeSumAll += minDistRatio[rk];
                    edgeCnt++;
                }
            }
            var meanEdge = edgeCnt > 0 ? edgeSumAll / edgeCnt : 1;
            var overallScore = (int)Math.Round(100 * (0.5 * meanCentrality + 0.5 * (1 - meanEdge)));
            qualityByFrame[fi] = overallScore;

            framesOut.Add(new CoverageFrameDto(fi, timeMs,
                new CoverageBucketDto(raidCovered, r, Math.Round(raidPct, 2)),
                new CoverageBucketDto(tankCovered, t, Math.Round(tankPct, 2)),
                new CoverageBucketDto(flexCovered, r, Math.Round(flexPct, 2)),
                new CoverageQualityDto(Math.Round(meanEdge, 4), Math.Round(meanCentrality, 4), overallScore),
                perHealer));

            sumRaid += raidPct; sumTank += tankPct; sumFlex += flexPct; sumQuality += overallScore;
            if (raidPct < minRaid) minRaid = raidPct;
            if (tankPct < minTank) minTank = tankPct;
            if (overallScore < minQuality) minQuality = overallScore;
            if (raidPct < FragileRaidPct) fragileMs += frameStepMs;
        }

        // Snapping points — rolling window max minus current, dedup within the window.
        var snaps = new List<SnappingPointDto>();
        if (f > 1)
        {
            var damageTimes = replay.Events
                .Where(e => e.EventKind.EndsWith("Damage", StringComparison.Ordinal))
                .Select(e => e.PullTimeMs).OrderBy(x => x).ToList();
            var lastEmit = int.MinValue / 2;
            for (var fi = 1; fi < f; fi++)
            {
                if (fi - lastEmit <= SnapWindowFrames) continue;
                var from = Math.Max(0, fi - SnapWindowFrames);
                var windowMax = qualityByFrame[from];
                for (var k = from + 1; k < fi; k++)
                    if (qualityByFrame[k] > windowMax) windowMax = qualityByFrame[k];
                var drop = windowMax - qualityByFrame[fi];
                if (drop >= SnapQualityDrop)
                {
                    var timeMs = framesOut[fi].TimeMs;
                    int? followedBy = null;
                    foreach (var et in damageTimes)
                    {
                        var delta = et - timeMs;
                        if (delta >= 0 && delta <= SnapDamageLookaheadMs) { followedBy = (int)delta; break; }
                        if (delta > SnapDamageLookaheadMs) break;
                    }
                    snaps.Add(new SnappingPointDto(timeMs, drop, followedBy));
                    lastEmit = fi;
                }
            }
        }

        var eff = Math.Max(1, f);
        return new CoverageSeriesDto(framesOut, new CoverageSummaryDto(
            Math.Round(sumRaid / eff, 2), Math.Round(sumTank / eff, 2),
            Math.Round(sumFlex / eff, 2), Math.Round(sumQuality / eff, 2),
            f > 0 ? Math.Round(minRaid, 2) : 0,
            f > 0 ? Math.Round(minTank, 2) : 0,
            f > 0 ? (int)Math.Round(minQuality) : 0,
            fragileMs, snaps));
    }
}
