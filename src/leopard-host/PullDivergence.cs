using Tempo.Core.Ingest;

namespace Leopard.Host;

public sealed record EntityMatch(int LeftIdx, int RightIdx, string Kind, string Key);

public sealed record DivergencePip(int TimeMs, double PeakDelta);

/// <summary>
/// Two-pull positional divergence, ported from RaidUI's <c>compare/entity-match.js</c> +
/// <c>compare/divergence.js</c> (track-i, ADR-004 §P3-I4, amendments §I-Q3/§I-Q5):
/// match entities across pulls (players by participantId, bosses by display name, adds
/// never), then the per-frame role-weighted mean pairwise position delta (tanks 2.0 —
/// geometry anchors; healers 1.5 — range drops silently; DPS 1.0), and pip extraction —
/// contiguous above-threshold runs collapsed to prominent local maxima (threshold 0.12
/// normalized, tuned against Crown Wipe 14; 500ms debounce; 0.015 prominence). The pips
/// answer "WHEN did this pull stop looking like the reference pull."
/// </summary>
public static class PullDivergence
{
    public const double DivergenceThreshold = 0.12;
    public const int DebounceMs = 500;
    public const double PipProminence = 0.015;

    private static readonly Dictionary<string, double> RoleWeights = new(StringComparer.Ordinal)
    {
        ["Tank"] = 2.0, ["Healer"] = 1.5, ["MeleeDps"] = 1.0, ["RangedDps"] = 1.0,
    };

    /// <summary>Players match by ParticipantId; bosses by DisplayName (first-write-wins);
    /// adds are never matched (spawn counts/positions differ across pulls).</summary>
    public static IReadOnlyList<EntityMatch> Match(
        IReadOnlyList<ReplayEntity> left, IReadOnlyList<ReplayEntity> right)
    {
        var rightByPid = new Dictionary<string, int>(StringComparer.Ordinal);
        var rightBossByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var j = 0; j < right.Count; j++)
        {
            var e = right[j];
            if (e.Kind == "Player" && e.ParticipantId is not null)
                rightByPid.TryAdd(e.ParticipantId, j);
            else if (e.Kind == "Boss" && e.DisplayName.Length > 0)
                rightBossByName.TryAdd(e.DisplayName, j);
        }

        var outp = new List<EntityMatch>();
        var bossUsed = new HashSet<int>();
        for (var i = 0; i < left.Count; i++)
        {
            var e = left[i];
            if (e.Kind == "Player" && e.ParticipantId is not null
                && rightByPid.TryGetValue(e.ParticipantId, out var j))
                outp.Add(new EntityMatch(i, j, "Player", e.ParticipantId));
            else if (e.Kind == "Boss" && rightBossByName.TryGetValue(e.DisplayName, out var jb)
                && bossUsed.Add(jb))
                outp.Add(new EntityMatch(i, jb, "Boss", e.DisplayName));
        }
        return outp;
    }

    /// <summary>Role-weighted per-frame mean pairwise distance (normalized units), frame
    /// alignment index-parallel up to the shorter pull (same 200ms cadence both sides).</summary>
    public static double[] ComputeWeighted(
        PullReplay left, PullReplay right, IReadOnlyList<EntityMatch> matches)
    {
        var n = Math.Min(left.Frames.Count, right.Frames.Count);
        var series = new double[n];
        if (n == 0 || matches.Count == 0) return series;

        var weights = matches.Select(m =>
        {
            var role = left.Entities[m.LeftIdx].Role;
            return role is not null && RoleWeights.TryGetValue(role, out var w) ? w : 1.0;
        }).ToArray();

        for (var fi = 0; fi < n; fi++)
        {
            var lp = left.Frames[fi].EntityPositions;
            var rp = right.Frames[fi].EntityPositions;
            double acc = 0, wsum = 0;
            for (var mi = 0; mi < matches.Count; mi++)
            {
                var m = matches[mi];
                var dx = lp[m.LeftIdx * 2] - rp[m.RightIdx * 2];
                var dy = lp[m.LeftIdx * 2 + 1] - rp[m.RightIdx * 2 + 1];
                acc += weights[mi] * Math.Sqrt(dx * dx + dy * dy);
                wsum += weights[mi];
            }
            series[fi] = wsum > 0 ? acc / wsum : 0;
        }
        return series;
    }

    /// <summary>Collapse contiguous above-threshold runs to pips at their prominent local
    /// maxima (argmax when a run has no interior peak structure), then debounce — on
    /// collision the higher peak wins.</summary>
    public static IReadOnlyList<DivergencePip> ExtractPips(
        double[] series, int frameStepMs,
        double threshold = DivergenceThreshold, int debounceMs = DebounceMs,
        double prominence = PipProminence)
    {
        var step = frameStepMs > 0 ? frameStepMs : 200;
        var pips = new List<DivergencePip>();
        if (series.Length == 0) return pips;

        var runs = new List<(int A, int B)>();
        var runStart = -1;
        for (var i = 0; i < series.Length; i++)
        {
            var above = series[i] > threshold;
            if (above && runStart == -1) runStart = i;
            if (!above && runStart != -1) { runs.Add((runStart, i - 1)); runStart = -1; }
        }
        if (runStart != -1) runs.Add((runStart, series.Length - 1));

        foreach (var (a, b) in runs)
            foreach (var p in ProminentMaxima(series, a, b, prominence))
                pips.Add(new DivergencePip(p.Idx * step, p.Val));

        pips.Sort((x, y) => x.TimeMs.CompareTo(y.TimeMs));
        var kept = new List<DivergencePip>();
        foreach (var p in pips)
        {
            if (kept.Count == 0 || p.TimeMs - kept[^1].TimeMs >= debounceMs) kept.Add(p);
            else if (p.PeakDelta > kept[^1].PeakDelta) kept[^1] = p;
        }
        return kept;
    }

    private static List<(int Idx, double Val)> ProminentMaxima(
        double[] series, int a, int b, double prominence)
    {
        (int Idx, double Val) ArgMax()
        {
            var pi = a; var pv = series[a];
            for (var k = a + 1; k <= b; k++) if (series[k] > pv) { pv = series[k]; pi = k; }
            return (pi, pv);
        }
        if (b - a < 2) return new List<(int, double)> { ArgMax() };

        var peaks = new List<(int Idx, double Val)>();
        for (var i = a + 1; i < b; i++)
            if (series[i] > series[i - 1] && series[i] > series[i + 1])
                peaks.Add((i, series[i]));
        if (peaks.Count == 0) return new List<(int, double)> { ArgMax() };

        var kept = new List<(int Idx, double Val)>();
        for (var pi = 0; pi < peaks.Count; pi++)
        {
            var p = peaks[pi];
            var lb = pi > 0 ? peaks[pi - 1].Idx + 1 : a;
            var rb = pi < peaks.Count - 1 ? peaks[pi + 1].Idx - 1 : b;
            var lv = double.PositiveInfinity;
            for (var k = lb; k < p.Idx; k++) if (series[k] < lv) lv = series[k];
            var rv = double.PositiveInfinity;
            for (var k = p.Idx + 1; k <= rb; k++) if (series[k] < rv) rv = series[k];
            var higherValley = Math.Max(
                double.IsInfinity(lv) ? double.NegativeInfinity : lv,
                double.IsInfinity(rv) ? double.NegativeInfinity : rv);
            var prom = p.Val - (double.IsFinite(higherValley) ? higherValley : p.Val);
            if (prom >= prominence || kept.Count == 0) kept.Add(p);
        }
        if (kept.Count == 0) kept.Add(ArgMax());
        return kept;
    }
}
