namespace Leopard.Host;

public sealed record PerPullMeter(
    string PullId, string? Outcome, double TotalDistanceYd, double AvgSpeedYdPerSec,
    double PeakSpeedYdPerSec, double StationaryRatio);

public sealed record MeterRow(
    string ParticipantId, string DisplayName, string? Role,
    double TotalDistanceYd, double AvgSpeedYdPerSec, double PeakSpeedYdPerSec,
    double StationaryRatio, double MovedRatio, IReadOnlyList<PerPullMeter> PerPullMetrics);

/// <summary>Null on a side = that participant has no pulls in the partition; delta null too.</summary>
public sealed record MeterDeltaRow(
    string ParticipantId, string DisplayName,
    double? DistanceDelta, double? AvgSpeedDelta, double? PeakSpeedDelta, double? StationaryDelta,
    int WipePulls, int KillPulls);

public sealed record MetersByOutcome(
    IReadOnlyList<MeterRow> WipesOnly, IReadOnlyList<MeterRow> KillsOnly,
    IReadOnlyList<MeterDeltaRow> Delta, int WipeSamples, int KillSamples);

public sealed record GroupSummary(
    int Count, double DistanceMean, double AvgSpeedMean, double PeakSpeedMean, double StationaryMean);

public sealed record OverallMedians(
    double DistanceMedian, double AvgSpeedMedian, double PeakSpeedMedian, double StationaryMedian);

/// <summary>
/// The per-participant movement leaderboard, ported from RaidUI's <c>shape/meters.js</c> +
/// <c>shape/group-summary.js</c>: total distance, average moving speed, peak single-frame
/// speed (the blink / teleport / LOS-snap detector), stationary ratio — overall and per pull —
/// plus the wipes-vs-kills partition (what did your kills' movement have that the wipes
/// didn't, per player) and the group-vs-all summary helpers. Stationary threshold is a
/// physical 0.1 yd/s, converted per pull to normalized units (refinement over the JS single
/// shared arena, same as the affinity port).
/// </summary>
public static class ParticipantMeters
{
    private const double StillEpsYdPerSec = 0.1;
    private const double SecPerStep = 0.2; // 200ms parser contract

    public static IReadOnlyList<MeterRow> Compute(
        IReadOnlyList<ShapeParticipant> participants, ISet<string>? pullIdFilter = null)
    {
        var rows = new List<MeterRow>();
        foreach (var p in participants)
        {
            double totalDistYd = 0, movingSumYd = 0; var movingCount = 0;
            double peakYd = 0; var stillCount = 0; var totalCount = 0;
            var perPull = new List<PerPullMeter>();

            foreach (var traj in p.Trajectories)
            {
                if (pullIdFilter is not null && !pullIdFilter.Contains(traj.PullId)) continue;
                var pts = traj.Points;
                var pointCount = pts.Count / 2;
                if (pointCount < 2 || traj.ArenaYdAvg <= 0)
                {
                    perPull.Add(new PerPullMeter(traj.PullId, traj.Outcome, 0, 0, 0, 0));
                    continue;
                }

                var stillDeltaNorm = StillEpsYdPerSec * SecPerStep / traj.ArenaYdAvg;
                double distNorm = 0, movSumNorm = 0, peakNorm = 0;
                var movCnt = 0; var stillCnt = 0; var totCnt = 0;
                for (var i = 1; i < pointCount; i++)
                {
                    var dx = pts[i * 2] - pts[(i - 1) * 2];
                    var dy = pts[i * 2 + 1] - pts[(i - 1) * 2 + 1];
                    var dNorm = Math.Sqrt(dx * dx + dy * dy);
                    distNorm += dNorm; totCnt++;
                    if (dNorm < stillDeltaNorm) stillCnt++;
                    else
                    {
                        var speedNorm = dNorm / SecPerStep;
                        movSumNorm += speedNorm; movCnt++;
                        if (speedNorm > peakNorm) peakNorm = speedNorm;
                    }
                }

                var a = traj.ArenaYdAvg;
                perPull.Add(new PerPullMeter(traj.PullId, traj.Outcome,
                    distNorm * a,
                    movCnt > 0 ? movSumNorm / movCnt * a : 0,
                    peakNorm * a,
                    totCnt > 0 ? (double)stillCnt / totCnt : 0));

                totalDistYd += distNorm * a;
                movingSumYd += movSumNorm * a;
                movingCount += movCnt;
                if (peakNorm * a > peakYd) peakYd = peakNorm * a;
                stillCount += stillCnt;
                totalCount += totCnt;
            }

            var stationary = totalCount > 0 ? (double)stillCount / totalCount : 0;
            rows.Add(new MeterRow(p.ParticipantId, p.DisplayName, p.Role,
                totalDistYd,
                movingCount > 0 ? movingSumYd / movingCount : 0,
                peakYd, stationary, 1 - stationary, perPull));
        }
        return rows.OrderByDescending(r => r.TotalDistanceYd)
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal).ToList();
    }

    public static MetersByOutcome ComputeByOutcome(IReadOnlyList<ShapeParticipant> participants)
    {
        var fullRows = Compute(participants);

        var wipePulls = new HashSet<string>(StringComparer.Ordinal);
        var killPulls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in fullRows)
            foreach (var ppm in row.PerPullMetrics)
            {
                if (string.Equals(ppm.Outcome, "wipe", StringComparison.OrdinalIgnoreCase)) wipePulls.Add(ppm.PullId);
                else if (string.Equals(ppm.Outcome, "kill", StringComparison.OrdinalIgnoreCase)) killPulls.Add(ppm.PullId);
            }

        var wipesOnly = wipePulls.Count > 0 ? Compute(participants, wipePulls) : Array.Empty<MeterRow>();
        var killsOnly = killPulls.Count > 0 ? Compute(participants, killPulls) : Array.Empty<MeterRow>();

        var delta = new List<MeterDeltaRow>();
        foreach (var row in fullRows)
        {
            var w = row.PerPullMetrics.Where(x => string.Equals(x.Outcome, "wipe", StringComparison.OrdinalIgnoreCase)).ToList();
            var k = row.PerPullMetrics.Where(x => string.Equals(x.Outcome, "kill", StringComparison.OrdinalIgnoreCase)).ToList();
            double? D(Func<PerPullMeter, double> f)
                => w.Count > 0 && k.Count > 0 ? k.Average(f) - w.Average(f) : null;
            delta.Add(new MeterDeltaRow(row.ParticipantId, row.DisplayName,
                D(x => x.TotalDistanceYd), D(x => x.AvgSpeedYdPerSec),
                D(x => x.PeakSpeedYdPerSec), D(x => x.StationaryRatio),
                w.Count, k.Count));
        }
        return new MetersByOutcome(wipesOnly, killsOnly, delta, wipePulls.Count, killPulls.Count);
    }

    // ── group-summary.js helpers ────────────────────────────────────────────

    public static GroupSummary ComputeGroupSummary(
        IReadOnlyList<MeterRow> rows, ISet<string> groupMemberIds)
    {
        var members = rows.Where(r => groupMemberIds.Contains(r.ParticipantId)).ToList();
        if (members.Count == 0) return new GroupSummary(0, 0, 0, 0, 0);
        return new GroupSummary(members.Count,
            members.Average(m => m.TotalDistanceYd),
            members.Average(m => m.AvgSpeedYdPerSec),
            members.Average(m => m.PeakSpeedYdPerSec),
            members.Average(m => m.StationaryRatio));
    }

    public static OverallMedians ComputeOverallMedians(IReadOnlyList<MeterRow> rows)
    {
        static double Med(IEnumerable<double> xs)
        {
            var s = xs.Where(double.IsFinite).OrderBy(x => x).ToList();
            if (s.Count == 0) return 0;
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }
        return new OverallMedians(
            Med(rows.Select(r => r.TotalDistanceYd)),
            Med(rows.Select(r => r.AvgSpeedYdPerSec)),
            Med(rows.Select(r => r.PeakSpeedYdPerSec)),
            Med(rows.Select(r => r.StationaryRatio)));
    }
}
