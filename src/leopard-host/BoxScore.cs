using System.Text;
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
