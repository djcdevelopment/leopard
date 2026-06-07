using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>
/// Per-night Shape artifact: the density heatmap (where the raid stood) for every pull that has
/// replay/movement frames, plus the per-pull selector metadata the Shape tab needs. Mirrors
/// <see cref="TrendsArtifact"/> — computed in-process at parse time via Tempo's
/// <see cref="ShapeProjection.TryBuildDensity"/> and cached as JSON next to the box score.
///
/// <para>Density is PER PULL (the "long-exposure of a pull") and self-scaled to each pull's own
/// arena, so it caches cleanly per night. The kill-vs-wipe contrast is deliberately NOT here — it
/// is career-scoped (fanned across nights) and computed live at /api/shape/wkdelta, because on any
/// single night a boss is almost always all-kills or all-wipes and the contrast needs both.</para>
/// </summary>
public static class ShapeArtifact
{
    private const int GridW = 32;  // Tempo's default density resolution
    private const int GridH = 16;

    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var encounters = EncountersProjection.ToEncounters(parse.Sessions);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<object>();

        foreach (var enc in encounters)
        {
            // One card per boss this night (a boss can span sessions in one log).
            if (!seen.Add(enc.Id)) continue;

            var pulls = new List<object>();
            foreach (var p in enc.Pulls)
            {
                object? density = null;
                if (ShapeProjection.TryBuildDensity(encounters, parse.ReplaysByPullId, p.Id, GridW, GridH, out var d))
                {
                    density = new
                    {
                        gridW = d.GridW,
                        gridH = d.GridH,
                        cells = d.Cells,           // normalized 0..1
                        totalSamples = d.TotalSamples,
                        maxBucket = d.MaxBucket,
                        arenaW = d.ArenaYd.Width,  // per-pull extent (self-scaled)
                        arenaH = d.ArenaYd.Height,
                    };
                }

                pulls.Add(new
                {
                    pullId = p.Id,
                    n = p.N,
                    outcome = p.Outcome,
                    endHpPct = p.BossEndPctHp,
                    durationMs = p.DurationMs,
                    deaths = p.Deaths,
                    hasMovement = density is not null,
                    density,
                });
            }

            cards.Add(new
            {
                encounterId = enc.Id,
                careerId = enc.CareerId,   // ties this night's boss to its all-time career (wkdelta)
                encounterName = enc.Name,
                difficulty = enc.Difficulty,
                kills = enc.Kills,
                pullCount = enc.Pulls.Count,
                pulls,
            });
        }

        return JsonSerializer.Serialize(new { encounters = cards }, json);
    }
}
