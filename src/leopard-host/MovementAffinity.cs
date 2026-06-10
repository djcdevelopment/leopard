using System.Text.Json;
using Tempo.Core.Ingest;
using Tempo.Host.ViewerApi.Projections;

namespace Leopard.Host;

public sealed record ParticipantTrajectory(string PullId, double ArenaYdAvg, IReadOnlyList<double> Points);

public sealed record ShapeParticipant(
    string ParticipantId, string DisplayName, string? Role,
    IReadOnlyList<ParticipantTrajectory> Trajectories);

/// <summary>Symmetric N×N matrices, row-major; diagonal = 1 (self-affinity).</summary>
public sealed record AffinityMatrixDto(
    IReadOnlyList<string> ParticipantIds,
    double[] CoProximity, double[] CoDirection, double[] Composite);

public sealed record DendroNode(
    int Id, string Kind, string? ParticipantId, double Height, int? LeftId, int? RightId, int LeafCount);

public sealed record DendrogramDto(
    IReadOnlyList<string> ParticipantIds, IReadOnlyList<DendroNode> Nodes, string Linkage);

public sealed record MovementGroup(string GroupId, IReadOnlyList<string> Members);

public sealed record GapParticipantRef(string Id, string Name, string? Role);

/// <summary>kind: healer-dps-gap | tank-separation | isolated-ranged. The interpretation is
/// LLM-ready prose — "the LLM should name it, not shame it" (the second-opinion reviewer's
/// framing that drove this reducer).</summary>
public sealed record CoverageGap(
    string Kind, GapParticipantRef Primary, GapParticipantRef? Secondary,
    double Composite, string Interpretation);

public sealed record CoverageGapReport(
    IReadOnlyList<CoverageGap> Gaps, int GapCount,
    IReadOnlyList<string> HealersWithGaps, bool TanksWellPaired);

/// <summary>
/// The group-structure suite, ported from RaidUI's <c>shape/affinity.js</c> +
/// <c>shape/clustering.js</c>: a pairwise N×N co-travel matrix (coProximity = fraction of
/// shared frames within 10yd; coDirection = fraction where both move &gt; 0.1 yd/s in
/// directions with cos &gt; 0.7; composite = the 0.5/0.5 blend), then agglomerative
/// complete-linkage clustering (distance = 1 − composite, deterministic ties per ADR-009)
/// and the cut-to-k-groups helper (labels A.. by size descending). "Who actually moves
/// together" — the emergent social structure of the raid, from positions alone.
///
/// <para>Refinement over the JS: the proximity/movement thresholds normalize against EACH
/// PULL's own arena (Tempo replays are self-scaled per pull; the JS assumed one shared
/// arena). Frame-aligned comparison stays within a pull, exactly like the original.</para>
/// </summary>
public static class MovementAffinity
{
    public const double ProximityThresholdYd = 10;
    public const double DirectionCosThreshold = 0.7;
    public const double MovementEpsYdPerSec = 0.1;
    private const int FrameStepMs = 200;

    /// <summary>Per-participant trajectories across pulls. Identity = ParticipantId when the
    /// replay carries one (stable across pulls), else EntityId.</summary>
    public static IReadOnlyList<ShapeParticipant> BuildParticipants(IEnumerable<PullReplay> replays)
    {
        var byId = new Dictionary<string, (string Name, string? Role, List<ParticipantTrajectory> Trs)>(StringComparer.Ordinal);
        foreach (var replay in replays)
        {
            var arenaAvg = (replay.ArenaYd.Width + replay.ArenaYd.Height) / 2;
            for (var i = 0; i < replay.Entities.Count; i++)
            {
                var e = replay.Entities[i];
                if (e.Kind != "Player") continue;
                var id = e.ParticipantId ?? e.EntityId;
                var points = new double[replay.Frames.Count * 2];
                for (var f = 0; f < replay.Frames.Count; f++)
                {
                    points[f * 2] = replay.Frames[f].EntityPositions[i * 2];
                    points[f * 2 + 1] = replay.Frames[f].EntityPositions[i * 2 + 1];
                }
                if (!byId.TryGetValue(id, out var entry))
                    entry = byId[id] = (ShortName(e.DisplayName), e.Role, new List<ParticipantTrajectory>());
                entry.Trs.Add(new ParticipantTrajectory(replay.PullId, arenaAvg, points));
            }
        }
        return byId.Select(kv => new ShapeParticipant(kv.Key, kv.Value.Name, kv.Value.Role, kv.Value.Trs))
            .OrderBy(p => p.ParticipantId, StringComparer.Ordinal).ToList();
    }

    public static AffinityMatrixDto ComputeAffinity(IReadOnlyList<ShapeParticipant> participants)
    {
        var n = participants.Count;
        var coProx = new double[n * n];
        var coDir = new double[n * n];
        var composite = new double[n * n];
        var ids = participants.Select(p => p.ParticipantId).ToList();
        for (var i = 0; i < n; i++)
        {
            coProx[i * n + i] = 1; coDir[i * n + i] = 1; composite[i * n + i] = 1;
        }
        if (n < 2) return new AffinityMatrixDto(ids, coProx, coDir, composite);

        var byPull = participants
            .Select(p => p.Trajectories.ToDictionary(t => t.PullId, StringComparer.Ordinal)).ToList();
        var secPerStep = FrameStepMs / 1000.0;

        for (var a = 0; a < n; a++)
        {
            for (var b = a + 1; b < n; b++)
            {
                var proxFrames = 0; var dirFrames = 0; var total = 0;
                foreach (var (pullId, trA) in byPull[a])
                {
                    if (!byPull[b].TryGetValue(pullId, out var trB)) continue;
                    var count = Math.Min(trA.Points.Count / 2, trB.Points.Count / 2);
                    if (count < 2 || trA.ArenaYdAvg <= 0) continue;

                    // Per-pull normalized thresholds (this pull's own arena scale).
                    var proxNorm = ProximityThresholdYd / trA.ArenaYdAvg;
                    var proxSq = proxNorm * proxNorm;
                    var epsNorm = MovementEpsYdPerSec * secPerStep / trA.ArenaYdAvg;
                    var epsSq = epsNorm * epsNorm;

                    var pa = trA.Points; var pb = trB.Points;
                    for (var i = 1; i < count; i++)
                    {
                        var ax = pa[i * 2]; var ay = pa[i * 2 + 1];
                        var bx = pb[i * 2]; var by = pb[i * 2 + 1];
                        var rx = ax - bx; var ry = ay - by;
                        if (rx * rx + ry * ry < proxSq) proxFrames++;

                        var vax = ax - pa[(i - 1) * 2]; var vay = ay - pa[(i - 1) * 2 + 1];
                        var vbx = bx - pb[(i - 1) * 2]; var vby = by - pb[(i - 1) * 2 + 1];
                        var saSq = vax * vax + vay * vay;
                        var sbSq = vbx * vbx + vby * vby;
                        if (saSq > epsSq && sbSq > epsSq)
                        {
                            var denom = Math.Sqrt(saSq * sbSq);
                            if (denom > 0 && (vax * vbx + vay * vby) / denom > DirectionCosThreshold)
                                dirFrames++;
                        }
                        total++;
                    }
                }
                var cp = total > 0 ? (double)proxFrames / total : 0;
                var cd = total > 0 ? (double)dirFrames / total : 0;
                var comp = 0.5 * cp + 0.5 * cd;
                coProx[a * n + b] = cp; coProx[b * n + a] = cp;
                coDir[a * n + b] = cd; coDir[b * n + a] = cd;
                composite[a * n + b] = comp; composite[b * n + a] = comp;
            }
        }
        return new AffinityMatrixDto(ids, coProx, coDir, composite);
    }

    /// <summary>Agglomerative complete-linkage clustering; distance = 1 − composite.
    /// Deterministic: exact ties resolve to the first-found pair (lowest I, then J).</summary>
    public static DendrogramDto Cluster(AffinityMatrixDto affinity)
    {
        var ids = affinity.ParticipantIds;
        var n = ids.Count;
        var nodes = new List<DendroNode>();
        for (var i = 0; i < n; i++)
            nodes.Add(new DendroNode(i, "leaf", ids[i], 0, null, null, 1));
        if (n < 2) return new DendrogramDto(ids, nodes, "complete");

        var active = Enumerable.Range(0, n)
            .Select(i => (NodeId: i, Members: new List<int> { i })).ToList();
        double Distance(int i, int j) => 1 - affinity.Composite[i * n + j];
        var nextId = n;

        while (active.Count > 1)
        {
            var bestI = 0; var bestJ = 1; var bestD = double.PositiveInfinity;
            for (var i = 0; i < active.Count; i++)
                for (var j = i + 1; j < active.Count; j++)
                {
                    double d = 0;
                    foreach (var a in active[i].Members)
                        foreach (var b in active[j].Members)
                        {
                            var dab = Distance(a, b);
                            if (dab > d) d = dab;
                        }
                    if (d < bestD) { bestD = d; bestI = i; bestJ = j; }
                }

            var merged = active[bestI].Members.Concat(active[bestJ].Members).ToList();
            var newId = nextId++;
            nodes.Add(new DendroNode(newId, "internal", null, bestD,
                active[bestI].NodeId, active[bestJ].NodeId, merged.Count));
            var keep = active.Where((_, k) => k != bestI && k != bestJ).ToList();
            keep.Add((newId, merged));
            active = keep;
        }
        return new DendrogramDto(ids, nodes, "complete");
    }

    /// <summary>Cut to k groups by splitting the tallest internal node until k subtrees
    /// remain; labels A.. by size descending. Members are display names.</summary>
    public static IReadOnlyList<MovementGroup> CutToGroups(
        DendrogramDto dendro, int k, IReadOnlyList<ShapeParticipant> participants)
    {
        if (dendro.Nodes.Count == 0) return Array.Empty<MovementGroup>();
        var byId = dendro.Nodes.ToDictionary(x => x.Id);
        var root = dendro.Nodes.OrderByDescending(x => x.LeafCount).First();
        var target = Math.Max(1, k);

        var subtrees = new List<DendroNode> { root };
        while (subtrees.Count < target)
        {
            var split = subtrees.Where(s => s.Kind == "internal")
                .OrderByDescending(s => s.Height).FirstOrDefault();
            if (split is null) break;
            var idx = subtrees.IndexOf(split);
            if (split.LeftId is not int li || split.RightId is not int ri
                || !byId.TryGetValue(li, out var left) || !byId.TryGetValue(ri, out var right)) break;
            subtrees.RemoveAt(idx);
            subtrees.Insert(idx, left);
            subtrees.Insert(idx + 1, right);
        }

        var nameById = participants.ToDictionary(p => p.ParticipantId, p => p.DisplayName, StringComparer.Ordinal);
        List<string> Leaves(int id)
        {
            var node = byId[id];
            if (node.Kind == "leaf")
                return node.ParticipantId is null ? new List<string>()
                    : new List<string> { nameById.GetValueOrDefault(node.ParticipantId, node.ParticipantId) };
            var output = new List<string>();
            if (node.LeftId is int l) output.AddRange(Leaves(l));
            if (node.RightId is int rr) output.AddRange(Leaves(rr));
            return output;
        }

        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return subtrees.Select(s => Leaves(s.Id))
            .OrderByDescending(m => m.Count)
            .Select((m, i) => new MovementGroup(i < letters.Length ? letters[i].ToString() : $"G{i}", m))
            .ToList();
    }

    /// <summary>Per-night artifact: the night's movement-affinity structure across every pull
    /// with replay frames — participants, the composite matrix, and the k=4 group cut.</summary>
    public static string BuildJson(ParseResult parse, JsonSerializerOptions json)
    {
        var participants = BuildParticipants(parse.ReplaysByPullId.Values.Where(r => r.Frames.Count > 1));
        var affinity = ComputeAffinity(participants);
        var groups = participants.Count >= 2
            ? CutToGroups(Cluster(affinity), Math.Min(4, participants.Count), participants)
            : Array.Empty<MovementGroup>() as IReadOnlyList<MovementGroup>;
        var gapReport = CoverageGaps.Compute(affinity, participants);
        return JsonSerializer.Serialize(new
        {
            participants = participants.Select(p => new { id = p.ParticipantId, name = p.DisplayName, role = p.Role, pulls = p.Trajectories.Count }),
            proximityThresholdYd = ProximityThresholdYd,
            directionCosThreshold = DirectionCosThreshold,
            composite = affinity.Composite,
            groups,
            coverageGaps = gapReport,
        }, json);
    }

    private static string ShortName(string displayName)
    {
        var hyphen = displayName.IndexOf('-');
        return hyphen > 0 ? displayName[..hyphen] : displayName;
    }
}

/// <summary>
/// Role-aware interpretation of the affinity matrix, ported from RaidUI's
/// <c>shape/coverage-gaps.js</c> (the "4th categorically-distinct reducer"). Reads roles
/// directly — deliberately NOT the dendrogram — so coverage logic stays robust when the
/// cluster cut isn't meaningful. Three gap kinds: a healer whose mean co-travel with the
/// DPS bucket is under 0.25 (out of heal range for the duration); tank pairs co-traveling
/// under 0.15 (not swapping — check for a taunt issue); ranged whose best pair-composite
/// is under 0.20 (standing alone, out of support range).
/// </summary>
public static class CoverageGaps
{
    public const double HealerGapThreshold = 0.25;
    public const double TankSpreadThreshold = 0.15;
    public const double IsolatedRangedThreshold = 0.20;

    public static CoverageGapReport Compute(
        AffinityMatrixDto matrix, IReadOnlyList<ShapeParticipant> participants)
    {
        var n = matrix.ParticipantIds.Count;
        if (n < 2 || participants.Count != n)
            return new CoverageGapReport(Array.Empty<CoverageGap>(), 0, Array.Empty<string>(), false);

        double Composite(int a, int b) => a == b ? 1 : matrix.Composite[a * n + b];
        GapParticipantRef Ref(int i) => new(matrix.ParticipantIds[i],
            participants[i].DisplayName, participants[i].Role);
        static string NormRole(string? r) => r switch
        {
            "MeleeDps" => "Melee", "RangedDps" => "Ranged", null => "", _ => r,
        };

        var healers = new List<int>(); var tanks = new List<int>();
        var melee = new List<int>(); var ranged = new List<int>();
        for (var i = 0; i < n; i++)
        {
            switch (NormRole(participants[i].Role))
            {
                case "Healer": healers.Add(i); break;
                case "Tank": tanks.Add(i); break;
                case "Melee": melee.Add(i); break;
                case "Ranged": ranged.Add(i); break;
            }
        }
        var dps = melee.Concat(ranged).ToList();

        var gaps = new List<CoverageGap>();
        var healersWithGaps = new List<string>();

        // 1. Healer → DPS-cluster coverage gaps.
        foreach (var h in healers)
        {
            if (dps.Count == 0) continue;
            var others = dps.Where(d => d != h).ToList();
            if (others.Count == 0) continue;
            var mean = others.Average(d => Composite(h, d));
            if (mean < HealerGapThreshold)
            {
                var p = Ref(h);
                gaps.Add(new CoverageGap("healer-dps-gap", p, null, Math.Round(mean, 3),
                    $"{p.Name} co-travel with DPS cluster at {mean:0.00} (threshold " +
                    $"{HealerGapThreshold:0.00}) - healer out of range to cover the DPS group."));
                healersWithGaps.Add(p.Name);
            }
        }

        // 2. Tank separation — pairwise tank composite below threshold = not swapping.
        var tanksWellPaired = false;
        if (tanks.Count >= 2)
        {
            var pairSum = 0.0; var pairCount = 0;
            for (var i = 0; i < tanks.Count; i++)
                for (var j = i + 1; j < tanks.Count; j++) { pairSum += Composite(tanks[i], tanks[j]); pairCount++; }
            if (pairCount > 0 && pairSum / pairCount < TankSpreadThreshold)
            {
                for (var i = 0; i < tanks.Count; i++)
                    for (var j = i + 1; j < tanks.Count; j++)
                    {
                        var c = Composite(tanks[i], tanks[j]);
                        if (c >= TankSpreadThreshold) continue;
                        var a = Ref(tanks[i]); var b = Ref(tanks[j]);
                        gaps.Add(new CoverageGap("tank-separation", a, b, Math.Round(c, 3),
                            $"{a.Name} and {b.Name} co-travel at {c:0.00} - tanks not swapping; " +
                            "check for taunt issue."));
                    }
            }
            else tanksWellPaired = true;
        }

        // 3. Isolated ranged — best pair-composite with anyone under threshold.
        foreach (var r in ranged)
        {
            var maxC = 0.0;
            for (var j = 0; j < n; j++)
                if (j != r && Composite(r, j) > maxC) maxC = Composite(r, j);
            if (maxC < IsolatedRangedThreshold)
            {
                var p = Ref(r);
                gaps.Add(new CoverageGap("isolated-ranged", p, null, Math.Round(maxC, 3),
                    $"{p.Name} max pair-composite {maxC:0.00} (threshold " +
                    $"{IsolatedRangedThreshold:0.00}) - standing alone too much, out of support range."));
            }
        }

        return new CoverageGapReport(gaps, gaps.Count, healersWithGaps, tanksWellPaired);
    }
}
