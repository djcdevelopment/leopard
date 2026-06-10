using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>One row of the two-pull diff. <see cref="L"/>/<see cref="R"/> are numbers for
/// numeric metrics and strings for categorical ones (Outcome). <see cref="Wired"/> = false
/// means the row needs replay-derived signals that one or both pulls don't have — the
/// consumer renders a placeholder rather than fabricating.</summary>
public sealed record DiffMetricRow(
    string Id, string Label, string Sev, object? L, object? R, string Unit,
    string? Delta, string Dir, string? Detail, bool Wired);

public sealed record DiffResult(
    string EncounterName, string EncounterDifficulty,
    RaidViewPull Left, RaidViewPull Right,
    string RuleHeadline, string RuleSubline,
    IReadOnlyList<DiffMetricRow> Metrics);

/// <summary>
/// Deterministic two-pull diff, ported from RaidUI's <c>buildDiff</c> (app/server.cjs — the
/// GET /api/diff backbone of the NewUI DiffLens). Nine metrics: deaths / end-HP% / duration /
/// outcome from pull metadata, plus coverage avg / coverage min / fragile seconds / snaps /
/// tightest spacing from the <see cref="SignalsArtifact"/> aggregates. Second item of the
/// 2026-06-09 unported-math audit ("this pull vs your best" is the live loop's richest
/// between-pull evidence). Rule-side only — the narrative is the model's job.
/// </summary>
public static class PullDiff
{
    /// <summary>Build the diff. Pass null aggregates when a pull has no replay-derived
    /// signals — the five diagnostic rows come back wired=false.</summary>
    public static DiffResult Build(
        string encounterName, string encounterDifficulty,
        RaidViewPull left, RaidViewPull right,
        SignalAggregatesDto? aggLeft, SignalAggregatesDto? aggRight)
    {
        var haveSignals = aggLeft is not null && aggRight is not null;

        var deathsDelta = right.Deaths - left.Deaths;
        var hpDelta = right.BossEndPctHp - left.BossEndPctHp;
        var ruleHeadline = deathsDelta > 0
            ? $"Pull #{right.N} took {deathsDelta} more death{(deathsDelta > 1 ? "s" : "")}."
            : deathsDelta < 0
                ? $"Pull #{right.N} took {-deathsDelta} fewer death{(-deathsDelta > 1 ? "s" : "")}."
                : $"Pull #{right.N} matched #{left.N} on deaths.";
        var ruleSubline = haveSignals
            ? "9 metrics - 4 from session data, 5 from per-frame replay signals."
            : "4 metrics from session data, 5 require replay frames (not available for one or both pulls).";

        var metrics = new List<DiffMetricRow>
        {
            Metric("deaths", "Deaths",
                deathsDelta > 2 ? "crit" : deathsDelta > 0 ? "warn" : "ok",
                left.Deaths, right.Deaths, "", betterLower: true,
                detail: deathsDelta != 0 ? $"d {(deathsDelta > 0 ? "+" : "")}{deathsDelta}" : null),
            Metric("end-hp", "End HP %",
                hpDelta < 0 ? "ok" : hpDelta > 0 ? "warn" : "flat",
                left.BossEndPctHp, right.BossEndPctHp, "%", betterLower: true),
            Metric("duration", "Pull duration", "info",
                left.DurationMs / 1000.0, right.DurationMs / 1000.0, "s", betterLower: false,
                detail: $"{FmtDur(left.DurationMs)} -> {FmtDur(right.DurationMs)}"),
            Outcome(left, right),
        };

        if (haveSignals)
        {
            var a = aggLeft!; var b = aggRight!;
            metrics.Add(Metric("rcov-avg", "Raid coverage avg", "ok",
                Math.Round(a.CoverageAvg * 100), Math.Round(b.CoverageAvg * 100), "%", betterLower: false));
            metrics.Add(Metric("rcov-min", "Raid coverage min", "ok",
                Math.Round(a.CoverageMin * 100), Math.Round(b.CoverageMin * 100), "%", betterLower: false));
            metrics.Add(Metric("frag", "Fragile coverage", "warn",
                Math.Round(a.FragileSec, 1), Math.Round(b.FragileSec, 1), "s", betterLower: true,
                detail: "seconds with raid coverage < 60%"));
            metrics.Add(Metric("snap", "Snapping points", "info",
                a.SnapCount, b.SnapCount, "", betterLower: true,
                detail: "sudden coverage drops >= 10pp within 3s"));
            metrics.Add(Metric("spacing", "Group spacing tightest", "ok",
                Math.Round(a.SpacingTightest, 1), Math.Round(b.SpacingTightest, 1), "yd", betterLower: false,
                detail: "min pairwise distance across frames"));
        }
        else
        {
            metrics.Add(Placeholder("rcov-avg", "Raid coverage avg", "info", "%", "replay frames not available for one or both pulls"));
            metrics.Add(Placeholder("rcov-min", "Raid coverage min", "info", "%", "requires replay frames"));
            metrics.Add(Placeholder("frag", "Fragile coverage", "warn", "s", "sustained low-coverage windows - needs frame analysis"));
            metrics.Add(Placeholder("snap", "Snapping points", "info", "", "sudden coverage drops - frame-derived"));
            metrics.Add(Placeholder("spacing", "Group spacing tightest", "ok", "yd", "min pairwise distance across frames"));
        }

        return new DiffResult(encounterName, encounterDifficulty, left, right,
            ruleHeadline, ruleSubline, metrics);
    }

    private static DiffMetricRow Metric(string id, string label, string sev,
        double l, double r, string unit, bool betterLower, string? detail = null)
    {
        var delta = r - l;
        var dir = Math.Abs(delta) <= 1e-6 ? "flat"
            : (delta < 0 && betterLower) || (delta > 0 && !betterLower) ? "better" : "worse";
        var display = $"{(delta > 0 ? "+" : "")}{delta:0.#}{unit}";
        return new DiffMetricRow(id, label, sev, l, r, unit, display, dir, detail, Wired: true);
    }

    private static DiffMetricRow Outcome(RaidViewPull left, RaidViewPull right)
    {
        var lKill = string.Equals(left.Outcome, "kill", StringComparison.OrdinalIgnoreCase);
        var rKill = string.Equals(right.Outcome, "kill", StringComparison.OrdinalIgnoreCase);
        var dir = lKill == rKill ? "flat" : rKill ? "better" : "worse";
        return new DiffMetricRow("outcome", "Outcome", lKill == rKill ? "flat" : "info",
            lKill ? "Kill" : "Wipe", rKill ? "Kill" : "Wipe", "",
            Delta: null, dir, $"{(lKill ? "Kill" : "Wipe")} -> {(rKill ? "Kill" : "Wipe")}", Wired: true);
    }

    private static DiffMetricRow Placeholder(string id, string label, string sev, string unit, string detail)
        => new(id, label, sev, null, null, unit, null, "flat", detail, Wired: false);

    private static string FmtDur(double ms)
    {
        var s = (int)Math.Round(ms / 1000);
        return $"{s / 60}:{s % 60:00}";
    }
}
