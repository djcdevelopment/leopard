using System.Text.Json;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>
/// The Roster: every boss this account has pulled, as one all-time career row. A fan-in
/// across nights — Tempo's <see cref="CareerProjection"/> already merges a boss across
/// sessions (grouped by careerId = name+difficulty, pulls re-numbered 1..N chronologically);
/// this aggregates that merged timeline into the roster stats Leopard owns.
///
/// <para>Deliberately NOT <see cref="TrendsProjection"/>: its recent-window delta model goes
/// flat once the window spans a whole career. The Roster is all-time — totals + best-ever +
/// an early-vs-late direction + the full-career arc. See docs/career-roster.md.</para>
/// </summary>
public static class CareerRoster
{
    private const int MinPullsForDirection = 6;

    public static string BuildJson(IReadOnlyList<RaidViewEncounter> allEncounters, JsonSerializerOptions json)
    {
        var rows = new List<RosterRow>();
        foreach (var group in allEncounters.GroupBy(e => e.CareerId, StringComparer.Ordinal))
        {
            // CareerProjection resolves by any encounterId in the career, then re-gathers
            // every member sharing its careerId (across all nights we fed in).
            if (!CareerProjection.TryResolveCareerEncounter(allEncounters, group.First().Id, out var career))
                continue;
            rows.Add(BuildRow(career));
        }

        var sorted = rows
            .OrderBy(r => r.Killed)                 // in-progress bosses first — what needs attention
            .ThenBy(r => r.BestPct ?? 101.0)        // then closest to a kill
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        return JsonSerializer.Serialize(new { bosses = sorted }, json);
    }

    private static RosterRow BuildRow(CareerEncounterProjection career)
    {
        var pulls = career.Pulls;
        int kills = pulls.Count(IsKill);
        bool killed = kills > 0;

        return new RosterRow(
            CareerId: career.CareerId,
            Name: career.Name,
            Difficulty: career.Difficulty,
            Attempts: pulls.Count,
            Kills: kills,
            Killed: killed,
            BestPct: pulls.Count == 0 ? null : BestProgress(pulls),
            TotalTimeMs: pulls.Sum(p => p.DurationMs),
            FirstSeen: pulls.FirstOrDefault()?.StartedAt,
            LastSeen: pulls.LastOrDefault()?.StartedAt,
            Direction: Direction(pulls),
            Arc: pulls.Select(p => Math.Round(Progress(p), 1)).ToList());
    }

    private static bool IsKill(RaidViewPull p) => string.Equals(p.Outcome, "kill", StringComparison.OrdinalIgnoreCase);

    // Progress = how far into the fight; a kill is 0% boss HP remaining. Lower is better.
    private static double Progress(RaidViewPull p) => IsKill(p) ? 0.0 : p.BossEndPctHp;

    private static double BestProgress(IReadOnlyList<RaidViewPull> pulls) => pulls.Min(Progress);

    // Early-third vs late-third best progress. Lower-later = improving (closer to a kill).
    private static string Direction(IReadOnlyList<RaidViewPull> pulls)
    {
        if (pulls.Count < MinPullsForDirection) return "new";
        int third = pulls.Count / 3;
        var earlyBest = pulls.Take(third).Min(Progress);
        var lateBest = pulls.Skip(pulls.Count - third).Min(Progress);
        var delta = lateBest - earlyBest;
        if (Math.Abs(delta) < 0.5) return "steady";
        return delta < 0 ? "improving" : "slipping";
    }

    private sealed record RosterRow(
        string CareerId,
        string Name,
        string Difficulty,
        int Attempts,
        int Kills,
        bool Killed,
        double? BestPct,
        long TotalTimeMs,
        string? FirstSeen,
        string? LastSeen,
        string Direction,
        IReadOnlyList<double> Arc);
}
