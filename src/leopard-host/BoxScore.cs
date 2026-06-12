using System.Text;
using System.Text.Json;
using Tempo.Core.Ingest;

namespace Leopard.Host;

/// <summary>
/// Distills a Tempo <see cref="ParseResult"/> into a compact, PRE-COMPUTED box score.
/// The grounding bar: the local model RESTATES these facts, it never infers them — so
/// we compute kills, the death trend, best progress, and notable pulls here, exactly.
/// </summary>
public static class BoxScore
{
    public static string Build(ParseResult parse)
    {
        var encounters = parse.Sessions.SelectMany(s => s.Encounters).ToList();
        var killed = encounters.Where(e => e.Pulls.Any(p => p.Outcome == "Kill")).ToList();
        var inProgress = encounters.Where(e => e.Pulls.Count > 0 && e.Pulls.All(p => p.Outcome != "Kill")).ToList();

        var session = parse.Sessions.FirstOrDefault();
        var zone = string.IsNullOrWhiteSpace(session?.Zone) ? "This raid night" : session!.Zone!;
        var date = (session?.StartedAt ?? "").Length >= 10 ? session!.StartedAt!.Substring(0, 10) : "";
        var diff = killed.FirstOrDefault()?.Difficulty ?? inProgress.FirstOrDefault()?.Difficulty ?? "";
        var players = session?.Participants.Count ?? 0;

        var sb = new StringBuilder();
        sb.Append($"# {zone}");
        if (!string.IsNullOrEmpty(diff)) sb.Append($" ({diff})");
        if (!string.IsNullOrEmpty(date)) sb.Append($" - {date}");
        if (players > 0) sb.Append($" - {players} players");
        sb.AppendLine();
        sb.AppendLine($"RESULT: {killed.Count} bosses KILLED, {inProgress.Count} boss(es) IN PROGRESS.");
        sb.AppendLine();

        if (killed.Count > 0)
        {
            sb.AppendLine($"## Bosses killed ({killed.Count})");
            foreach (var e in killed)
            {
                var kp = e.Pulls.First(p => p.Outcome == "Kill");
                sb.AppendLine($"- {e.Name} - killed in {Dur(kp.DurationMs)}, {kp.Deaths} deaths");
            }
            sb.AppendLine();
        }

        foreach (var e in inProgress)
        {
            var deaths = e.Pulls.Select(p => p.Deaths).ToList();
            sb.AppendLine($"## In progress: {e.Name} - {e.Pulls.Count} wipes");
            sb.AppendLine($"- Deaths per pull (1..{e.Pulls.Count}): {string.Join(",", deaths)}");
            sb.AppendLine($"- Death trend: {Trend(deaths)}");

            var executed = e.Pulls.Where(p => p.BossEndPctHp == 0).Select(p => p.Num).ToList();
            var withHp = e.Pulls.Where(p => p.BossEndPctHp >= 0 && p.BossEndPctHp <= 100).ToList();
            if (executed.Count > 0)
                sb.AppendLine($"- Best progress: reached boss 0% (execute range) on pulls {string.Join(", ", executed)}");
            else if (withHp.Count > 0)
                sb.AppendLine($"- Best progress: lowest boss HP reached was {withHp.Min(p => p.BossEndPctHp!.Value):0}%");
            else
                sb.AppendLine("- Best progress: no reliable boss-HP reading was recorded");

            var fast = e.Pulls.Where(p => p.DurationMs > 0 && p.DurationMs <= 30000).Select(p => p.Num).ToList();
            if (fast.Count > 0)
                sb.AppendLine($"- Very fast wipes (<=30s, died early): pulls {string.Join(", ", fast)}");

            var longest = e.Pulls.OrderByDescending(p => p.DurationMs).Take(3).Select(p => $"#{p.Num} ({Dur(p.DurationMs)})");
            sb.AppendLine($"- Longest attempts: {string.Join(", ", longest)}");
            sb.AppendLine();
        }

        sb.AppendLine("_(Box score computed by Leopard from Tempo's parser. Every figure above is exact - reflect on these facts, do not infer beyond them.)_");
        return sb.ToString();
    }

    /// <summary>
    /// The structured emit alongside the markdown blob — the same figures, field-addressable,
    /// so the night lens can compose them property-by-property (the gate that kept the lens
    /// composer career-only; see docs/property-inventory.md "Box Score artifact"). Cached as
    /// <c>.night.v1.json</c>. `Build` above stays byte-identical and untouched; the few small
    /// per-encounter expressions (execute/fast/longest) are deliberately mirrored here rather
    /// than refactored out from under the shipping markdown path.
    /// </summary>
    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var encounters = parse.Sessions.SelectMany(s => s.Encounters).ToList();
        var session = parse.Sessions.FirstOrDefault();
        var killed = encounters.Where(e => e.Pulls.Any(p => p.Outcome == "Kill")).ToList();
        var inProgress = encounters.Where(e => e.Pulls.Count > 0 && e.Pulls.All(p => p.Outcome != "Kill")).ToList();

        object EncounterCard(ContractEncounter e, bool wasKilled)
        {
            var deaths = e.Pulls.Select(p => p.Deaths).ToList();
            var executed = e.Pulls.Where(p => p.BossEndPctHp == 0).Select(p => p.Num).ToList();
            var withHp = e.Pulls.Where(p => p.BossEndPctHp >= 0 && p.BossEndPctHp <= 100).ToList();
            var killPull = wasKilled ? e.Pulls.First(p => p.Outcome == "Kill") : null;
            return new
            {
                name = e.Name ?? "Unknown",
                difficulty = e.Difficulty,
                killed = wasKilled,
                pullCount = e.Pulls.Count,
                killDurationMs = killPull?.DurationMs,
                killDeaths = killPull?.Deaths,
                deathsPerPull = deaths,
                deathTrend = wasKilled ? null : Trend(deaths),
                executePulls = executed,
                bestProgressPct = executed.Count > 0 ? 0.0
                    : withHp.Count > 0 ? withHp.Min(p => p.BossEndPctHp!.Value) : (double?)null,
                fastWipePulls = e.Pulls.Where(p => p.DurationMs > 0 && p.DurationMs <= 30000)
                    .Select(p => p.Num).ToList(),
                longestPulls = e.Pulls.OrderByDescending(p => p.DurationMs).Take(3)
                    .Select(p => new { n = p.Num, durationMs = p.DurationMs }).ToList(),
                pulls = e.Pulls.Select(p => new
                {
                    n = p.Num,
                    outcome = p.Outcome.ToLowerInvariant(),
                    deaths = p.Deaths,
                    durationMs = p.DurationMs,
                    bossEndPctHp = p.BossEndPctHp,
                }).ToList(),
            };
        }

        var night = new
        {
            zone = string.IsNullOrWhiteSpace(session?.Zone) ? "This raid night" : session!.Zone!,
            date = (session?.StartedAt ?? "").Length >= 10 ? session!.StartedAt!.Substring(0, 10) : "",
            difficulty = killed.FirstOrDefault()?.Difficulty ?? inProgress.FirstOrDefault()?.Difficulty ?? "",
            playerCount = session?.Participants.Count ?? 0,
            bossesKilled = killed.Count,
            bossesInProgress = inProgress.Count,
            encounters = killed.Select(e => EncounterCard(e, true))
                .Concat(inProgress.Select(e => EncounterCard(e, false))).ToList(),
        };
        return JsonSerializer.Serialize(night, json);
    }

    static string Dur(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    static string Trend(List<int> deaths)
    {
        if (deaths.Count < 4) return $"{deaths.Count} attempt(s) - too few to call a trend";
        int t = Math.Max(1, deaths.Count / 3);
        double early = deaths.Take(t).Average();
        double late = deaths.TakeLast(t).Average();
        int peak = deaths.Max();
        if (late - early > 2) return $"deaths rose then held (early ~{early:0} -> later ~{late:0} per pull; peak {peak}; did NOT decrease)";
        if (early - late > 2) return $"deaths fell over the night (early ~{early:0} -> later ~{late:0} per pull)";
        return $"deaths held roughly steady (~{deaths.Average():0} per pull; peak {peak})";
    }
}
