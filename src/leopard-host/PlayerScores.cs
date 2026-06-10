using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

public sealed record MovementBurst(int StartMs, int EndMs, double DistanceYd, int FrameCount);

public sealed record PlayerMovementProfile(
    string EntityId, string DisplayName, string? Role,
    double TotalDistanceYd, double? StillnessRatio, double? AvgVelocityYdPerS,
    int ActiveFrames, IReadOnlyList<MovementBurst> MovementBursts);

public sealed record AbilityDamage(int SpellId, string SpellName, long TotalDamage, int HitCount);

/// <summary>Replays retain only top-decile damage events — these numbers rank players
/// WITHIN a pull; treat dps as a lower bound (the JS original's standing caveat).</summary>
public sealed record PlayerDamageProfile(
    string EntityId, string DisplayName, string? Role,
    long TotalDamage, int HitCount, int Dps, double DamageShare,
    IReadOnlyList<AbilityDamage> TopAbilities, double BossRatio, long Peak30sDamage);

public sealed record PlayerSurvivalProfile(
    string EntityId, string DisplayName, string? Role,
    int DeathCount, long? FirstDeathMs, long TimeAliveMs, double TimeAlivePct, int SurvivalScore);

public sealed record PlayerAwarenessProfile(
    string EntityId, string DisplayName, string? Role,
    int InterruptCount, int DispelCount, double? GroupCohesionPct,
    double? BossProximityPct, int HealerSeekingCount);

/// <summary>archetype: aggressor | anchor | support_pillar | mobile_threat |
/// positioning_risk | fragile_damage | default.</summary>
public sealed record PlayerScore(
    string EntityId, string DisplayName, string? Role,
    int MovementScore, int DamageScore, int SurvivalScore, int AwarenessScore,
    int CompositeScore, string Archetype);

/// <summary>
/// The per-player scoring suite, ported from RaidUI's <c>shape/player-movement.js</c> /
/// <c>player-damage.js</c> / <c>player-survival.js</c> / <c>player-awareness.js</c> /
/// <c>player-score.js</c>: four raw profiles over one replay (movement stillness + bursts,
/// retained-damage share + peak window, death timing, interrupts + cohesion + boss
/// proximity + healer-seeking) composed into role-weighted 0-100 scores with behavioral
/// archetypes. Note: Tempo's event contract has no SpellDispel kind yet, so healer utility
/// counts read 0 until dispels reach the replay (the string match is forward-compatible).
/// </summary>
public static class PlayerScores
{
    private const double StillYdPerFrame = 0.5;
    private const int BurstGapFrames = 3;
    private const int PeakWindowMs = 30_000;
    private const int TopAbilities = 5;
    private const double GroupCohesionYd = 20;
    private const double MeleeRangeYd = 8;
    private const double LowHpFrac = 0.50;
    private const double HealerSeekYdMin = 2;

    private static readonly Dictionary<string, (double Dmg, double Surv, double Mov, double Aw)> RoleWeights = new()
    {
        ["Tank"] = (0.20, 0.35, 0.15, 0.30),
        ["Healer"] = (0.15, 0.30, 0.25, 0.30),
        ["MeleeDps"] = (0.40, 0.25, 0.25, 0.10),
        ["RangedDps"] = (0.40, 0.25, 0.25, 0.10),
    };
    private static readonly (double Dmg, double Surv, double Mov, double Aw) DefaultWeights = (0.30, 0.30, 0.20, 0.20);

    // ── Movement ────────────────────────────────────────────────────────────

    public static Dictionary<string, PlayerMovementProfile> ComputeMovement(PullReplay replay)
    {
        var w = replay.ArenaYd.Width > 0 ? replay.ArenaYd.Width : 40;
        var h = replay.ArenaYd.Height > 0 ? replay.ArenaYd.Height : 40;
        var stepMs = replay.FrameStepMs > 0 ? replay.FrameStepMs : 200;
        var frames = replay.Frames;
        var durMs = frames.Count > 0 ? frames[^1].T : 0;

        var result = new Dictionary<string, PlayerMovementProfile>(StringComparer.Ordinal);
        for (var ei = 0; ei < replay.Entities.Count; ei++)
        {
            var e = replay.Entities[ei];
            if (e.Kind != "Player") continue;

            double totalDist = 0; var stillFrames = 0; var active = 0;
            var bursts = new List<MovementBurst>();
            var inBurst = false; var gap = 0; var burstStartF = 0; double burstDist = 0; var burstFrames = 0;

            for (var f = 1; f < frames.Count; f++)
            {
                var prev = frames[f - 1].EntityPositions;
                var cur = frames[f].EntityPositions;
                var dx = (cur[ei * 2] - prev[ei * 2]) * w;
                var dy = (cur[ei * 2 + 1] - prev[ei * 2 + 1]) * h;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                totalDist += dist;
                active++;
                var tMs = frames[f].T != 0 ? frames[f].T : f * stepMs;

                if (dist < StillYdPerFrame)
                {
                    stillFrames++;
                    if (inBurst && ++gap > BurstGapFrames)
                    {
                        if (burstDist > 0)
                            bursts.Add(new MovementBurst(burstStartF * stepMs, tMs,
                                Math.Round(burstDist, 1), burstFrames));
                        inBurst = false; gap = 0; burstDist = 0; burstFrames = 0;
                    }
                }
                else
                {
                    if (!inBurst) { inBurst = true; burstStartF = f; gap = 0; burstDist = 0; burstFrames = 0; }
                    else gap = 0;
                    burstDist += dist;
                    burstFrames++;
                }
            }
            if (inBurst && burstDist > 0)
                bursts.Add(new MovementBurst(burstStartF * stepMs, durMs, Math.Round(burstDist, 1), burstFrames));

            double? stillness = active > 0 ? Math.Round((double)stillFrames / active, 3) : null;
            var elapsedSec = active > 0 ? active * stepMs / 1000.0 : 0;
            result[e.EntityId] = new PlayerMovementProfile(e.EntityId, e.DisplayName, e.Role,
                Math.Round(totalDist), stillness,
                elapsedSec > 0 ? Math.Round(totalDist / elapsedSec, 1) : null, active, bursts);
        }
        return result;
    }

    // ── Damage ──────────────────────────────────────────────────────────────

    public static Dictionary<string, PlayerDamageProfile> ComputeDamage(PullReplay replay, long durationMs)
    {
        var metaById = replay.Entities.ToDictionary(e => e.EntityId, StringComparer.Ordinal);
        var acc = replay.Entities.Where(e => e.Kind == "Player").ToDictionary(
            e => e.EntityId,
            e => (Total: 0L, Hits: 0, Boss: 0L,
                Abilities: new Dictionary<int, (string Name, long Total, int Hits)>(),
                Series: new List<(long T, long Amt)>()),
            StringComparer.Ordinal);

        long raidTotal = 0;
        foreach (var ev in replay.Events)
        {
            if (ev.EventKind != "SpellDamage" && ev.EventKind != "SwingDamage") continue;
            if (ev.Amount is not int amt0 || amt0 <= 0) continue;
            if (ev.SourceEntityId is null || !acc.TryGetValue(ev.SourceEntityId, out var a)) continue;
            long amt = amt0;
            a.Total += amt; a.Hits++; raidTotal += amt;
            if (ev.TargetEntityId is not null && metaById.TryGetValue(ev.TargetEntityId, out var tgt)
                && tgt.Kind == "Boss") a.Boss += amt;
            var sid = ev.SpellId ?? 0;
            var ab = a.Abilities.TryGetValue(sid, out var prev0) ? prev0 : (ev.SpellName ?? "Unknown", 0L, 0);
            a.Abilities[sid] = (ab.Item1, ab.Item2 + amt, ab.Item3 + 1);
            a.Series.Add((ev.PullTimeMs, amt));
            acc[ev.SourceEntityId] = a;
        }

        var result = new Dictionary<string, PlayerDamageProfile>(StringComparer.Ordinal);
        foreach (var e in replay.Entities.Where(e => e.Kind == "Player"))
        {
            var a = acc[e.EntityId];
            var abilities = a.Abilities
                .OrderByDescending(kv => kv.Value.Total).Take(TopAbilities)
                .Select(kv => new AbilityDamage(kv.Key, kv.Value.Name, kv.Value.Total, kv.Value.Hits)).ToList();
            result[e.EntityId] = new PlayerDamageProfile(e.EntityId, e.DisplayName, e.Role,
                a.Total, a.Hits,
                durationMs > 0 ? (int)Math.Round(a.Total / (double)durationMs * 1000) : 0,
                raidTotal > 0 ? Math.Round(a.Total / (double)raidTotal, 3) : 0,
                abilities,
                a.Total > 0 ? Math.Round(a.Boss / (double)a.Total, 3) : 0,
                PeakWindow(a.Series));
        }
        return result;
    }

    private static long PeakWindow(List<(long T, long Amt)> series)
    {
        if (series.Count == 0) return 0;
        series.Sort((x, y) => x.T.CompareTo(y.T));
        long max = 0, sum = 0; var lo = 0;
        for (var hi = 0; hi < series.Count; hi++)
        {
            sum += series[hi].Amt;
            while (series[hi].T - series[lo].T > PeakWindowMs) { sum -= series[lo].Amt; lo++; }
            if (sum > max) max = sum;
        }
        return max;
    }

    // ── Survival ────────────────────────────────────────────────────────────

    public static Dictionary<string, PlayerSurvivalProfile> ComputeSurvival(PullReplay replay, long durationMs)
    {
        var deaths = replay.Entities.Where(e => e.Kind == "Player")
            .ToDictionary(e => e.EntityId, _ => (Count: 0, First: (long?)null), StringComparer.Ordinal);
        foreach (var ev in replay.Events)
        {
            if (ev.EventKind != "UnitDied" || ev.TargetEntityId is null) continue;
            if (!deaths.TryGetValue(ev.TargetEntityId, out var d)) continue;
            deaths[ev.TargetEntityId] = (d.Count + 1, d.First ?? ev.PullTimeMs);
        }
        var result = new Dictionary<string, PlayerSurvivalProfile>(StringComparer.Ordinal);
        foreach (var e in replay.Entities.Where(e => e.Kind == "Player"))
        {
            var (count, first) = deaths[e.EntityId];
            var alive = first ?? durationMs;
            int score;
            if (count == 0) score = 100;
            else
            {
                var timing = durationMs > 0 ? (double)first!.Value / durationMs : 0;
                score = Math.Clamp((int)Math.Round(timing * 50) - Math.Max(0, count - 1) * 5, 0, 100);
            }
            result[e.EntityId] = new PlayerSurvivalProfile(e.EntityId, e.DisplayName, e.Role,
                count, first, alive,
                durationMs > 0 ? Math.Round(alive / (double)durationMs * 1000) / 10 : 100, score);
        }
        return result;
    }

    // ── Awareness ───────────────────────────────────────────────────────────

    public static Dictionary<string, PlayerAwarenessProfile> ComputeAwareness(PullReplay replay)
    {
        var w = replay.ArenaYd.Width > 0 ? replay.ArenaYd.Width : 40;
        var h = replay.ArenaYd.Height > 0 ? replay.ArenaYd.Height : 40;
        double DistYd(double ax, double ay, double bx, double by)
            => Math.Sqrt((ax - bx) * (ax - bx) * w * w + (ay - by) * (ay - by) * h * h);

        var players = new List<int>(); var healerIdxs = new List<int>(); var bossIdx = -1;
        for (var i = 0; i < replay.Entities.Count; i++)
        {
            var e = replay.Entities[i];
            if (e.Kind == "Player")
            {
                players.Add(i);
                if (e.Role == "Healer") healerIdxs.Add(i);
            }
            else if (e.Kind == "Boss" && bossIdx < 0) bossIdx = i;
        }

        var interrupts = new Dictionary<string, int>(StringComparer.Ordinal);
        var dispels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ev in replay.Events)
        {
            if (ev.SourceEntityId is null) continue;
            if (ev.EventKind == "SpellInterrupt")
                interrupts[ev.SourceEntityId] = interrupts.GetValueOrDefault(ev.SourceEntityId) + 1;
            else if (ev.EventKind == "SpellDispel")
                dispels[ev.SourceEntityId] = dispels.GetValueOrDefault(ev.SourceEntityId) + 1;
        }

        var cohesion = new int[players.Count];
        var bossHits = new int[players.Count];
        var active = new int[players.Count];
        var seek = new int[players.Count];
        var wasLow = new bool[players.Count];
        var prevHealerDist = new double?[players.Count];
        bool MeleeOrTank(int i) => replay.Entities[i].Role is "Tank" or "MeleeDps";

        foreach (var fr in replay.Frames)
        {
            var pos = fr.EntityPositions;
            double cx = 0, cy = 0; var cn = 0;
            foreach (var i in players)
            {
                if (replay.Entities[i].Role == "Healer") continue;
                cx += pos[i * 2]; cy += pos[i * 2 + 1]; cn++;
            }

            for (var p = 0; p < players.Count; p++)
            {
                var i = players[p];
                var px = pos[i * 2]; var py = pos[i * 2 + 1];
                active[p]++;

                if (cn > 0 && DistYd(px, py, cx / cn, cy / cn) <= GroupCohesionYd) cohesion[p]++;
                if (MeleeOrTank(i) && bossIdx >= 0
                    && DistYd(px, py, pos[bossIdx * 2], pos[bossIdx * 2 + 1]) <= MeleeRangeYd) bossHits[p]++;

                if (fr.EntityHp is { } hp && healerIdxs.Count > 0 && i < hp.Count)
                {
                    var isLow = hp[i] < LowHpFrac;
                    var nearest = double.PositiveInfinity;
                    foreach (var hi in healerIdxs)
                    {
                        var d = DistYd(px, py, pos[hi * 2], pos[hi * 2 + 1]);
                        if (d < nearest) nearest = d;
                    }
                    if (isLow && wasLow[p] && prevHealerDist[p] is double prev
                        && prev - nearest >= HealerSeekYdMin) seek[p]++;
                    wasLow[p] = isLow;
                    prevHealerDist[p] = double.IsFinite(nearest) ? nearest : null;
                }
            }
        }

        var result = new Dictionary<string, PlayerAwarenessProfile>(StringComparer.Ordinal);
        for (var p = 0; p < players.Count; p++)
        {
            var e = replay.Entities[players[p]];
            result[e.EntityId] = new PlayerAwarenessProfile(e.EntityId, e.DisplayName, e.Role,
                interrupts.GetValueOrDefault(e.EntityId), dispels.GetValueOrDefault(e.EntityId),
                active[p] > 0 ? Math.Round(cohesion[p] / (double)active[p] * 1000) / 10 : null,
                MeleeOrTank(players[p]) && active[p] > 0
                    ? Math.Round(bossHits[p] / (double)active[p] * 1000) / 10 : null,
                seek[p]);
        }
        return result;
    }

    // ── Composite ───────────────────────────────────────────────────────────

    public static IReadOnlyList<PlayerScore> Compute(PullReplay replay)
    {
        var durMs = replay.Frames.Count > 0 ? replay.Frames[^1].T : 0;
        var mov = ComputeMovement(replay);
        var dmg = ComputeDamage(replay, durMs);
        var sur = ComputeSurvival(replay, durMs);
        var aw = ComputeAwareness(replay);

        var roleCounts = replay.Entities.Where(e => e.Kind == "Player")
            .GroupBy(e => e.Role ?? "Unknown").ToDictionary(g => g.Key, g => g.Count());

        var results = new List<PlayerScore>();
        foreach (var e in replay.Entities.Where(e => e.Kind == "Player"))
        {
            var role = e.Role ?? "Unknown";
            var weights = RoleWeights.GetValueOrDefault(role, DefaultWeights);
            var peers = roleCounts.GetValueOrDefault(role, 1);

            var mSc = MovementScore(mov.GetValueOrDefault(e.EntityId), role);
            var dSc = DamageScore(dmg.GetValueOrDefault(e.EntityId), peers);
            var sSc = sur.TryGetValue(e.EntityId, out var sp) ? sp.SurvivalScore : 50;
            var aSc = AwarenessScore(aw.GetValueOrDefault(e.EntityId), durMs);

            var composite = Math.Clamp((int)Math.Round(
                mSc * weights.Mov + dSc * weights.Dmg + sSc * weights.Surv + aSc * weights.Aw), 0, 100);

            results.Add(new PlayerScore(e.EntityId, e.DisplayName, e.Role,
                mSc, dSc, sSc, aSc, composite, Archetype(mSc, dSc, sSc, aSc)));
        }
        return results.OrderByDescending(r => r.CompositeScore).ToList();
    }

    private static int MovementScore(PlayerMovementProfile? mov, string role)
    {
        if (mov?.StillnessRatio is not double s) return 50;
        return role switch
        {
            "MeleeDps" or "RangedDps" => Math.Clamp((int)Math.Round(s * 115), 0, 100),
            "Tank" => Math.Clamp((int)Math.Round(100 - Math.Abs(s - 0.55) * 120), 0, 100),
            "Healer" => Math.Clamp((int)Math.Round(100 - Math.Abs(s - 0.65) * 130), 0, 100),
            _ => Math.Clamp((int)Math.Round(s * 100), 0, 100),
        };
    }

    private static int DamageScore(PlayerDamageProfile? dmg, int rolePeerCount)
    {
        if (dmg is null || dmg.TotalDamage == 0) return 0;
        if (rolePeerCount == 0) return 50;
        var ratio = dmg.DamageShare / (1.0 / rolePeerCount); // 1.0 = exactly average for the role
        return Math.Clamp((int)Math.Round(50 + 50 * (ratio - 1)), 0, 100);
    }

    private static int AwarenessScore(PlayerAwarenessProfile? aw, long durMs)
    {
        if (aw is null) return 50;
        var parts = new List<(double Score, double Weight)>();
        var util = aw.Role == "Healer" ? aw.DispelCount : aw.InterruptCount;
        var utilPerMin = durMs > 0 ? util / (double)durMs * 60_000 : 0;
        parts.Add((Math.Clamp(Math.Round(utilPerMin * 60), 0, 100), 0.30));
        if (aw.GroupCohesionPct is double coh) parts.Add((coh, 0.40));
        if (aw.BossProximityPct is double bp) parts.Add((bp, 0.30));
        var totalW = parts.Sum(p => p.Weight);
        return Math.Clamp((int)Math.Round(parts.Sum(p => p.Score * p.Weight / totalW)), 0, 100);
    }

    private static string Archetype(int m, int d, int s, int a)
    {
        const int hi = 70, lo = 35;
        if (a <= lo && s <= lo) return "positioning_risk";
        if (d >= hi && s <= lo) return "fragile_damage";
        if (a >= hi && d < hi) return "support_pillar";
        if (s >= hi && a >= hi && d < hi) return "anchor";
        if (d >= hi && m >= hi) return "mobile_threat";
        if (d >= hi) return "aggressor";
        return "default";
    }

    /// <summary>Per-night artifact: scores per pull per boss, cached as <c>.players.v1.json</c>.</summary>
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
                IReadOnlyList<PlayerScore>? scores = null;
                if (parse.ReplaysByPullId.TryGetValue(p.Id, out var replay) && replay.Frames.Count > 1)
                {
                    try { scores = Compute(replay); } catch { /* never sink the night */ }
                }
                pulls.Add(new { pullId = p.Id, n = p.N, outcome = p.Outcome, scores });
            }
            cards.Add(new { encounterId = enc.Id, encounterName = enc.Name, difficulty = enc.Difficulty, pulls });
        }
        return JsonSerializer.Serialize(new { encounters = cards }, json);
    }
}
