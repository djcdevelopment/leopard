using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>One per-second signal series. Null entries = insufficient data that second
/// (e.g. spacing with fewer than 2 alive players) — consumers must skip, not zero.</summary>
public sealed record SignalSeriesDto(IReadOnlyList<double?> Values, SignalPeakDto Peak, string Unit);

/// <summary>The series' headline moment. Coverage/spacing peak at their MIN (worst); others MAX.</summary>
public sealed record SignalPeakDto(double Value, int AtSec);

/// <summary>A sudden coverage collapse: drop ≥ 10 percentage points within ≤ 3 s.</summary>
public sealed record SnapDto(int AtSec, int DropPct);

public sealed record SignalAggregatesDto(
    double CoverageAvg, double CoverageMin, double SpacingTightest,
    double FragileSec, int SnapCount, int DeathsTotal, int DurationSec);

public sealed record PullSignalsDto(
    string PullId, int DurationSec, double HealerRangeYd,
    IReadOnlyDictionary<string, SignalSeriesDto> Signals,
    IReadOnlyList<SnapDto> Snaps, SignalAggregatesDto Aggregates);

// Read-side shapes for the cached .signals.v1.json (BuildJson's output) — used by /api/diff
// to fetch a pull's aggregates without re-parsing the night.
public sealed record SignalsNightDto(IReadOnlyList<SignalsEncounterDto> Encounters);
public sealed record SignalsEncounterDto(
    string EncounterId, string EncounterName, string Difficulty, IReadOnlyList<SignalsPullDto> Pulls);
public sealed record SignalsPullDto(string PullId, int N, string Outcome, PullSignalsDto? Signals);

/// <summary>
/// The six-signal diagnostic pack, ported from RaidUI's <c>buildPullSignals</c>
/// (app/server.cjs — the server side of GET /api/pulls/:id/signals, the DiagStrip /
/// CoverageTimeline backbone that never crossed in the C# port). One replay walk emits
/// per-second timelines for spacing / coverage / deathsPerSec / followership / entropy /
/// hpVariance, plus snap markers and whole-pull aggregates.
/// See docs/signals-artifact-port-brief.md; first item of the 2026-06-09 unported-math audit.
/// </summary>
public static class SignalsArtifact
{
    public const double DefaultHealerRangeYd = 30;
    private const double AliveHp = 0.001;     // alive = HP fraction above this
    private const double FragileCoverage = 0.6;
    private const double SnapDrop = 0.10;     // coverage drop that registers a snap
    private const int SnapLookaheadSec = 3;
    private const int SnapDedupSec = 3;

    /// <summary>The pure port — unit-testable on a synthetic replay.</summary>
    public static PullSignalsDto BuildForReplay(PullReplay replay, double healerRangeYd = DefaultHealerRangeYd)
    {
        var arenaW = replay.ArenaYd.Width > 0 ? replay.ArenaYd.Width : 50;
        var arenaH = replay.ArenaYd.Height > 0 ? replay.ArenaYd.Height : 50;
        var frameStepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;

        // Players only; healers by role (Tempo's replay builder populates Role from specs).
        var playerIdxs = new List<int>();
        var healerIdxs = new List<int>();
        for (var i = 0; i < replay.Entities.Count; i++)
        {
            var e = replay.Entities[i];
            if (!string.Equals(e.Kind, "Player", StringComparison.Ordinal)) continue;
            playerIdxs.Add(i);
            if (e.Role?.Contains("heal", StringComparison.OrdinalIgnoreCase) == true) healerIdxs.Add(i);
        }

        var totalSec = Math.Max(1, (int)Math.Ceiling(replay.Frames.Count * frameStepMs / 1000.0));
        var spacing = new double[totalSec];
        var coverage = new double[totalSec];
        var followership = new double[totalSec];
        var entropy = new double[totalSec];
        var hpVariance = new double[totalSec];
        var counts = new int[totalSec];
        var spacingValid = new int[totalSec];
        var minSpacingTrack = new double[totalSec];
        Array.Fill(minSpacingTrack, double.PositiveInfinity);

        var prev = new double[replay.Entities.Count * 2];
        var havePrev = false;

        foreach (var (frame, f) in replay.Frames.Select((fr, i) => (fr, i)))
        {
            var sec = Math.Min(totalSec - 1, (frame.T > 0 ? frame.T : f * frameStepMs) / 1000);
            counts[sec]++;

            var alive = playerIdxs.Where(i => Hp(frame, i) > AliveHp).ToList();

            // Spacing — NaN sentinel when <2 alive, so a lone survivor never reads "0 yd, stacked".
            var sAvg = PairwiseAvgYards(frame.EntityPositions, alive, arenaW, arenaH);
            if (!double.IsNaN(sAvg)) { spacing[sec] += sAvg; spacingValid[sec]++; }
            var sMin = PairwiseMinYards(frame.EntityPositions, alive, arenaW, arenaH);
            if (!double.IsNaN(sMin) && sMin < minSpacingTrack[sec]) minSpacingTrack[sec] = sMin;

            coverage[sec] += CoverageAtFrame(frame, playerIdxs, healerIdxs, arenaW, arenaH, healerRangeYd);

            hpVariance[sec] += PopStdDev(alive.Select(i => Hp(frame, i)).ToList());

            // Followership / entropy — per-player drift vs the prior frame, normalized units.
            if (havePrev && alive.Count > 0)
            {
                double sumDx = 0, sumDy = 0;
                var mags = new List<double>(alive.Count);
                foreach (var i in alive)
                {
                    var dx = frame.EntityPositions[i * 2] - prev[i * 2];
                    var dy = frame.EntityPositions[i * 2 + 1] - prev[i * 2 + 1];
                    sumDx += dx; sumDy += dy;
                    mags.Add(Math.Sqrt(dx * dx + dy * dy));
                }
                var drift = Math.Sqrt(Math.Pow(sumDx / alive.Count, 2) + Math.Pow(sumDy / alive.Count, 2));
                followership[sec] += Math.Min(1, drift * 50);
                entropy[sec] += Math.Min(1, PopStdDev(mags) * 50);
            }
            for (var i = 0; i < replay.Entities.Count * 2 && i < frame.EntityPositions.Count; i++)
                prev[i] = frame.EntityPositions[i];
            havePrev = true;
        }

        var spacingOut = new double?[totalSec];
        var minSpacingOut = new double?[totalSec];
        var coverageOut = new double?[totalSec];
        var followershipOut = new double?[totalSec];
        var entropyOut = new double?[totalSec];
        var hpVarianceOut = new double?[totalSec];
        for (var s = 0; s < totalSec; s++)
        {
            var c = Math.Max(1, counts[s]);
            coverageOut[s] = coverage[s] / c;
            followershipOut[s] = followership[s] / c;
            entropyOut[s] = entropy[s] / c;
            hpVarianceOut[s] = hpVariance[s] / c;
            spacingOut[s] = spacingValid[s] > 0 ? spacing[s] / spacingValid[s] : null;
            minSpacingOut[s] = double.IsFinite(minSpacingTrack[s]) ? minSpacingTrack[s] : null;
        }

        var deaths = new double?[totalSec];
        Array.Fill(deaths, 0.0);
        foreach (var e in replay.Events)
        {
            if (!string.Equals(e.EventKind, "UnitDied", StringComparison.Ordinal)) continue;
            var sec = Math.Min(totalSec - 1, (int)(Math.Max(0, e.PullTimeMs) / 1000));
            deaths[sec] = (deaths[sec] ?? 0) + 1;
        }

        // Snaps — sustained coverage drops vs the prior second, deduped.
        var snaps = new List<SnapDto>();
        for (var s = 1; s < totalSec; s++)
        {
            var window = Math.Min(SnapLookaheadSec, totalSec - s - 1);
            double maxDrop = 0;
            for (var k = 1; k <= window; k++)
            {
                var drop = (coverageOut[s - 1] ?? 0) - (coverageOut[s + k - 1] ?? 0);
                if (drop > maxDrop) maxDrop = drop;
            }
            if (maxDrop >= SnapDrop && (snaps.Count == 0 || s - snaps[^1].AtSec >= SnapDedupSec))
                snaps.Add(new SnapDto(s, (int)Math.Round(maxDrop * 100)));
        }

        var aggregates = new SignalAggregatesDto(
            CoverageAvg: Avg(coverageOut),
            CoverageMin: Min(coverageOut),
            SpacingTightest: Min(minSpacingOut, v => v > 0),
            FragileSec: coverageOut.Count(v => v is not null && v < FragileCoverage),
            SnapCount: snaps.Count,
            DeathsTotal: (int)deaths.Sum(v => v ?? 0),
            DurationSec: totalSec);

        var signals = new Dictionary<string, SignalSeriesDto>(StringComparer.Ordinal)
        {
            ["spacing"] = new(spacingOut, FindPeak(minSpacingOut, max: false), "yd"),
            ["coverage"] = new(coverageOut, FindPeak(coverageOut, max: false), "%"),
            ["deathsPerSec"] = new(deaths, FindPeak(deaths, max: true), ""),
            ["followership"] = new(followershipOut, FindPeak(followershipOut, max: true), ""),
            ["entropy"] = new(entropyOut, FindPeak(entropyOut, max: true), ""),
            ["hpVariance"] = new(hpVarianceOut, FindPeak(hpVarianceOut, max: true), ""),
        };

        return new PullSignalsDto(replay.PullId, totalSec, healerRangeYd, signals, snaps, aggregates);
    }

    /// <summary>Per-night artifact: one card per boss, one signals block per pull with replay
    /// frames. Sibling to <see cref="ShapeArtifact"/>; cached as <c>.signals.v1.json</c>.</summary>
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
                PullSignalsDto? sig = null;
                if (parse.ReplaysByPullId.TryGetValue(p.Id, out var replay) && replay.Frames.Count > 0)
                {
                    try { sig = BuildForReplay(replay); } catch { /* a malformed replay must not sink the night */ }
                }
                pulls.Add(new { pullId = p.Id, n = p.N, outcome = p.Outcome, signals = sig });
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

    // ── The ported helpers (server.cjs parity) ──────────────────────────────

    private static double Hp(ReplayFrame frame, int i)
        => frame.EntityHp is { } hp && i < hp.Count ? hp[i] : 1.0;

    private static double PairwiseAvgYards(IReadOnlyList<double> pos, List<int> alive, double w, double h)
    {
        if (alive.Count < 2) return double.NaN;
        double sum = 0; var pairs = 0;
        for (var i = 0; i < alive.Count; i++)
        {
            var ax = pos[alive[i] * 2] * w; var ay = pos[alive[i] * 2 + 1] * h;
            for (var j = i + 1; j < alive.Count; j++)
            {
                var dx = ax - pos[alive[j] * 2] * w;
                var dy = ay - pos[alive[j] * 2 + 1] * h;
                sum += Math.Sqrt(dx * dx + dy * dy); pairs++;
            }
        }
        return pairs > 0 ? sum / pairs : double.NaN;
    }

    private static double PairwiseMinYards(IReadOnlyList<double> pos, List<int> alive, double w, double h)
    {
        if (alive.Count < 2) return double.NaN;
        var min = double.PositiveInfinity;
        for (var i = 0; i < alive.Count; i++)
        {
            var ax = pos[alive[i] * 2] * w; var ay = pos[alive[i] * 2 + 1] * h;
            for (var j = i + 1; j < alive.Count; j++)
            {
                var dx = ax - pos[alive[j] * 2] * w;
                var dy = ay - pos[alive[j] * 2 + 1] * h;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < min) min = d;
            }
        }
        return double.IsFinite(min) ? min : double.NaN;
    }

    private static double CoverageAtFrame(ReplayFrame frame, List<int> players, List<int> healers,
        double w, double h, double rangeYd)
    {
        var aliveHealers = healers.Where(i => Hp(frame, i) > AliveHp).ToList();
        var covered = 0; var total = 0;
        foreach (var i in players)
        {
            if (Hp(frame, i) <= AliveHp) continue;
            total++;
            if (aliveHealers.Count == 0) continue;
            var px = frame.EntityPositions[i * 2] * w;
            var py = frame.EntityPositions[i * 2 + 1] * h;
            var inRange = false;
            foreach (var hi in aliveHealers)
            {
                if (hi == i) continue; // a healer doesn't cover itself…
                var dx = px - frame.EntityPositions[hi * 2] * w;
                var dy = py - frame.EntityPositions[hi * 2 + 1] * h;
                if (Math.Sqrt(dx * dx + dy * dy) <= rangeYd) { inRange = true; break; }
            }
            // …except a solo healer with no peers, who counts as covered (no one else can reach).
            if (!inRange && aliveHealers.Count == 1 && aliveHealers[0] == i) inRange = true;
            if (inRange) covered++;
        }
        return total > 0 ? (double)covered / total : 1.0;
    }

    private static double PopStdDev(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var mean = xs.Average();
        return Math.Sqrt(xs.Sum(v => (v - mean) * (v - mean)) / xs.Count);
    }

    private static SignalPeakDto FindPeak(double?[] values, bool max)
    {
        var best = max ? double.NegativeInfinity : double.PositiveInfinity;
        var at = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not double v || !double.IsFinite(v)) continue;
            if (max ? v > best : v < best) { best = v; at = i; }
        }
        return new SignalPeakDto(double.IsFinite(best) ? best : 0, at);
    }

    private static double Avg(double?[] xs)
    {
        double s = 0; var n = 0;
        foreach (var v in xs) if (v is double d && double.IsFinite(d)) { s += d; n++; }
        return n > 0 ? s / n : 0;
    }

    private static double Min(double?[] xs, Func<double, bool>? pred = null)
    {
        var m = double.PositiveInfinity;
        foreach (var v in xs)
            if (v is double d && double.IsFinite(d) && (pred is null || pred(d)) && d < m) m = d;
        return double.IsFinite(m) ? m : 0;
    }
}
