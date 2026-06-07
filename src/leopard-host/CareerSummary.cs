using System.Text;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>
/// The career-arc grounding artifact: one boss's ALL-TIME story across every parsed night, rendered
/// as exact pre-computed figures in the same "restate, never infer" envelope as <see cref="BoxScore"/>.
/// This is the zoom above the per-night box score — the substrate that lets Ask answer "are we getting
/// better at this boss?" from real history (the wipes trending toward the kill), instead of being
/// trapped in a single night. Built from the fanned-in career inputs via Tempo's
/// <see cref="CareerProjection"/> — same data the Roster shows; no new engine math.
/// </summary>
public static class CareerSummary
{
    private const int MinPullsForDirection = 6;

    /// <param name="allEncounters">Every parsed night's career-input encounters, fanned in.</param>
    public static string Build(IReadOnlyList<RaidViewEncounter> allEncounters, string careerId)
    {
        var anyId = allEncounters.FirstOrDefault(e => string.Equals(e.CareerId, careerId, StringComparison.Ordinal))?.Id;
        if (anyId is null || !CareerProjection.TryResolveCareerEncounter(allEncounters, anyId, out var career))
            return "(no career found for this boss)";

        var pulls = career.Pulls;
        int attempts = pulls.Count;
        int kills = pulls.Count(IsKill);
        bool killed = kills > 0;
        int nights = pulls.Select(p => DateOf(p.StartedAt)).Where(d => d.Length > 0).Distinct().Count();

        var sb = new StringBuilder();
        sb.Append($"# {career.Name}");
        if (!string.IsNullOrEmpty(career.Difficulty)) sb.Append($" ({career.Difficulty})");
        sb.AppendLine(" - all-time career");

        sb.Append($"RESULT: {attempts} attempt(s)");
        if (nights > 0) sb.Append($" across {nights} night(s)");
        sb.AppendLine(killed ? $", {kills} kill(s)." : ", not yet killed.");
        sb.AppendLine();

        sb.AppendLine("## The arc");
        var first = DateOf(pulls.FirstOrDefault()?.StartedAt);
        var last = DateOf(pulls.LastOrDefault()?.StartedAt);
        if (first.Length > 0) sb.AppendLine($"- First pulled {first}{(last.Length > 0 && last != first ? $", most recently {last}" : "")}.");

        if (killed)
        {
            var firstKill = pulls.Select((p, i) => (p, i)).First(x => IsKill(x.p));
            sb.AppendLine($"- First kill on attempt {firstKill.i + 1} of {attempts} ({firstKill.i} wipe(s) before the first kill).");
        }
        else
        {
            var best = pulls.Min(Progress);
            sb.AppendLine($"- Best progress: lowest boss HP reached was {best:0}% (not yet killed).");
        }

        sb.AppendLine($"- Direction: {Direction(pulls)}.");

        var deaths = pulls.Select(p => p.Deaths).ToList();
        if (deaths.Count > 0)
            sb.AppendLine($"- Deaths: ~{deaths.Average():0} per pull (peak {deaths.Max()}).");

        // The progress arc: boss %HP remaining at the end of each attempt, oldest -> newest. A kill
        // is 0%. Lower = closer to a kill; a falling line is progress. Capped so the text stays small.
        var arc = pulls.Select(p => (int)Math.Round(Progress(p))).ToList();
        sb.AppendLine($"- Progress over time (boss %HP at end, oldest->newest, 0 = kill): {Join(arc)}");

        sb.AppendLine();
        sb.AppendLine("_(Career summary computed by Leopard from Tempo's parser across every parsed night. Every figure above is exact - reflect on these facts, do not infer beyond them.)_");
        return sb.ToString();
    }

    private static bool IsKill(RaidViewPull p) => string.Equals(p.Outcome, "kill", StringComparison.OrdinalIgnoreCase);

    // Progress = boss %HP remaining at the end; a kill is 0%. Lower is closer to a kill.
    private static double Progress(RaidViewPull p) => IsKill(p) ? 0.0 : p.BossEndPctHp;

    private static string Direction(IReadOnlyList<RaidViewPull> pulls)
    {
        if (pulls.Count < MinPullsForDirection) return $"{pulls.Count} attempt(s) - too few to call a direction";
        int third = Math.Max(1, pulls.Count / 3);
        var earlyBest = pulls.Take(third).Min(Progress);
        var lateBest = pulls.Skip(pulls.Count - third).Min(Progress);
        var delta = lateBest - earlyBest;
        if (Math.Abs(delta) < 0.5) return $"holding steady (best ~{earlyBest:0}% early vs ~{lateBest:0}% recently)";
        return delta < 0
            ? $"improving (best progress early ~{earlyBest:0}% -> recently ~{lateBest:0}%, closer to a kill)"
            : $"slipping (best progress early ~{earlyBest:0}% -> recently ~{lateBest:0}%)";
    }

    // Keep the arc compact for long careers: if very long, sample to the most recent ~40 with a note.
    private static string Join(List<int> arc)
    {
        const int Cap = 40;
        if (arc.Count <= Cap) return string.Join(",", arc);
        var recent = arc.Skip(arc.Count - Cap).ToList();
        return $"(first {arc.Count - Cap} omitted) " + string.Join(",", recent);
    }

    private static string DateOf(string? iso) => (iso ?? "").Length >= 10 ? iso!.Substring(0, 10) : "";
}
