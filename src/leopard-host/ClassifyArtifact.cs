using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

/// <summary>
/// Per-night wipe-classification artifact: the <see cref="WipeClassifier"/> rule tree run at
/// parse time over every pull with replay frames, with the full v2c inputs (the adapted
/// six-signal pack + the coverage quality model), cached as <c>.classify.v1.json</c>.
/// One card per boss, one verdict per pull; pulls the classifier declines carry an explicit
/// <c>reason</c> instead of a fabricated verdict — kills aren't classified, phantom pulls
/// aren't classified, and the artifact says so rather than going silent.
/// </summary>
public static class ClassifyArtifact
{
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
                Classification? cls = null;
                string? reason;
                if (string.Equals(p.Outcome, "kill", StringComparison.OrdinalIgnoreCase))
                    reason = "kill - kills are not classified";
                else if (!parse.ReplaysByPullId.TryGetValue(p.Id, out var replay) || replay.Frames.Count == 0)
                    reason = "no replay frames";
                else
                {
                    try
                    {
                        var sig = SignalsArtifact.BuildForReplay(replay);
                        var coverage = CoverageTimeline.Compute(replay);
                        cls = WipeClassifier.Classify(replay, WipeClassifier.AdaptSignals(sig),
                            p.Outcome, p.BossEndPctHp, coverage);
                        // Classify's own gates returned null: under-10s phantom or deathless.
                        reason = cls is null ? "below the classifier's gates (under 10 s or no player deaths)" : null;
                    }
                    catch
                    {
                        reason = "classification failed on this replay";
                    }
                }
                pulls.Add(new { pullId = p.Id, n = p.N, outcome = p.Outcome, classification = cls, reason });
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
}
