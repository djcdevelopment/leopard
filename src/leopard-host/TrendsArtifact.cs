using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>
/// Distills a Tempo <see cref="ParseResult"/> into the JSON the Trends tab reads — one card
/// per encounter, each holding the rule-row window (kills / avg deaths / best progress /
/// duration, with current-vs-previous deltas) and a per-pull coherence series (followership /
/// entropy / peak speed). The math is Tempo's proven <see cref="TrendsProjection"/>, called
/// in-process — no running engine. Sibling to <see cref="BoxScore"/>; cached the same way.
/// </summary>
public static class TrendsArtifact
{
    // Mirror Tempo's own /api/trends default (n ?? 6): the "current" window is the last 6
    // pulls, compared against the 6 before it — so deltas are meaningful, not always "—".
    private const int Window = 6;

    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var encounters = EncountersProjection.ToEncounters(parse.Sessions);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<object>();

        foreach (var enc in encounters)
        {
            // A boss can appear in more than one session in a single log; one card per boss.
            if (!seen.Add(enc.Id)) continue;
            if (!TrendsProjection.TryBuildTrendsWindow(encounters, enc.Id, Window, out var window))
                continue;

            object? coherence =
                TrendsProjection.TryBuildCoherenceWindow(encounters, parse.ReplaysByPullId, enc.Id, Window, out var coh)
                    ? coh
                    : null;

            cards.Add(new
            {
                encounterId = enc.Id,
                encounterName = enc.Name,
                difficulty = enc.Difficulty,
                kills = enc.Kills,
                pullCount = enc.Pulls.Count,
                inProgress = enc.Kills == 0 && enc.Pulls.Count > 0,
                window,
                coherence,
            });
        }

        return JsonSerializer.Serialize(new { encounters = cards }, json);
    }
}
