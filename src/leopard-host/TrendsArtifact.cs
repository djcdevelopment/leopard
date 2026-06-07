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
    // The user-selectable recent-window sizes. The "current" window is the last N pulls,
    // compared against the N before it — so deltas are meaningful, not always "—". 6 mirrors
    // Tempo's own /api/trends default and is the UI default.
    private static readonly int[] Windows = { 4, 6, 8, 10 };
    private const int DefaultWindow = 6;

    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var encounters = EncountersProjection.ToEncounters(parse.Sessions);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<object>();

        foreach (var enc in encounters)
        {
            // A boss can appear in more than one session in a single log; one card per boss.
            if (!seen.Add(enc.Id)) continue;

            // Precompute every selectable window up front (the projection is cheap), keyed by
            // size, so the client toggles between them with no recompute and no re-parse. Same
            // proven Tempo math as before — just at four sizes instead of one.
            var windows = new Dictionary<string, object>(StringComparer.Ordinal);
            var coherences = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var w in Windows)
            {
                if (TrendsProjection.TryBuildTrendsWindow(encounters, enc.Id, w, out var window))
                    windows[w.ToString()] = window;

                coherences[w.ToString()] =
                    TrendsProjection.TryBuildCoherenceWindow(encounters, parse.ReplaysByPullId, enc.Id, w, out var coh)
                        ? coh
                        : null;
            }

            // Keep the encounter only if the default window builds — matches the prior behaviour
            // (skip bosses with no usable trend window).
            if (!windows.ContainsKey(DefaultWindow.ToString())) continue;

            cards.Add(new
            {
                encounterId = enc.Id,
                encounterName = enc.Name,
                difficulty = enc.Difficulty,
                kills = enc.Kills,
                pullCount = enc.Pulls.Count,
                inProgress = enc.Kills == 0 && enc.Pulls.Count > 0,
                windows,
                coherences,
                defaultWindow = DefaultWindow,
            });
        }

        return JsonSerializer.Serialize(new { encounters = cards }, json);
    }
}
