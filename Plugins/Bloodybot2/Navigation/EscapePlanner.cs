namespace Bloodybot2.Navigation;

using System.Diagnostics;
using System.Numerics;

internal sealed record RareCluster(Vector2 Center, Vector2[] Members);
internal sealed record EscapeRoute(Vector2? Center, int ClusterSize, Vector2? Target,
    List<Vector2>? Route, bool Safe, bool Limited = false);

internal static class EscapePlanner
{
    internal const float HealthThreshold = 50;
    internal const int MaximumRares = 256;

    // Weight actual Life and ES capacities, never average their percentages.
    // No ES is a valid zero-capacity pool; unknown Life is not zero health.
    public static float? CombinedPercent(int life, int lifeMax, int shield, int shieldMax)
    {
        if (lifeMax <= 0 || shieldMax < 0 || life < 0 || shield < 0 || shieldMax == 0 && shield != 0) return null;
        return (float)Math.Clamp(100d * ((long)life + shield) / ((long)lifeMax + shieldMax), 0, 100);
    }

    public static bool UseSecondary(float primary, bool primaryAlive, float secondary, bool secondaryAlive) =>
        secondaryAlive && (!primaryAlive || secondary < primary);

    public static List<RareCluster> Cluster(IReadOnlyList<Vector2> points, float linkDistance)
    {
        var remaining = points.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y))
            .Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var clusters = new List<RareCluster>();
        while (remaining.Count > 0)
        {
            var members = new List<Vector2> { remaining[0] };
            remaining.RemoveAt(0);
            for (var i = 0; i < members.Count; i++)
                for (var j = remaining.Count - 1; j >= 0; j--)
                    if (Vector2.DistanceSquared(members[i], remaining[j]) <= linkDistance * linkDistance)
                    { members.Add(remaining[j]); remaining.RemoveAt(j); }
            var center = members.Aggregate(Vector2.Zero, (sum, p) => sum + p) / members.Count;
            clusters.Add(new(center, members.ToArray()));
        }
        return clusters;
    }

    public static float NearestDistance(Vector2 position, IReadOnlyList<Vector2> rares) =>
        rares.Count == 0 ? float.PositiveInfinity : MathF.Sqrt(rares.Min(p => Vector2.DistanceSquared(position, p)));

    // A short segment must neither cross terrain nor approach a known yellow
    // monster. Test intermediate points too: safe endpoints can straddle a pack.
    public static bool SafeSegment(NavigationGrid grid, Vector2 from, Vector2 to, IReadOnlyList<Vector2> rares)
    {
        if (!grid.Clear(from, to)) return false;
        var previous = NearestDistance(from, rares);
        var samples = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(from, to) * 4), 1, 40);
        for (var i = 1; i <= samples; i++)
        {
            var next = NearestDistance(Vector2.Lerp(from, to, i / (float)samples), rares);
            if (next + 0.05f < previous) return false;
            previous = next;
        }
        return true;
    }

    public static EscapeRoute Plan(NavigationGrid grid, Vector2 player, IReadOnlyList<Vector2> rares,
        float linkDistance, float safeDistance, int maxNodes = 4096, int maxMilliseconds = 8)
    {
        if (!grid.Contains(player) || !grid.Walkable(player) || rares.Count > MaximumRares ||
            rares.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return new(null, 0, null, null, false);
        if (rares.Count == 0) return new(null, 0, null, null, true);
        var clock = Stopwatch.StartNew();
        var cluster = Cluster(rares, linkDistance).OrderBy(c => NearestDistance(player, c.Members))
            .ThenByDescending(c => c.Members.Length).First();
        var initialDistance = NearestDistance(player, rares);
        if (initialDistance >= safeDistance) return new(cluster.Center, cluster.Members.Length, null, null, true);
        var away = player - cluster.Center;
        if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
        away = Vector2.Normalize(away);
        var radius = cluster.Members.Max(p => Vector2.Distance(p, cluster.Center));
        var preferred = cluster.Center + away * (radius + safeDistance + 1);
        var start = ((int)MathF.Round(player.X), (int)MathF.Round(player.Y));
        var queue = new PriorityQueue<(int X, int Y), float>();
        var costs = new Dictionary<(int X, int Y), float> { [start] = 0 };
        var parents = new Dictionary<(int X, int Y), (int X, int Y)>();
        queue.Enqueue(start, 0);
        var best = start;
        var bestDistance = initialDistance;
        var limited = false;
        var visited = 0;
        while (queue.TryDequeue(out var cell, out _))
        {
            if (++visited > maxNodes || clock.ElapsedMilliseconds >= maxMilliseconds) { limited = true; break; }
            var point = new Vector2(cell.X, cell.Y);
            var distance = NearestDistance(point, rares);
            if (distance >= safeDistance + 0.5f) { best = cell; break; }
            if (distance > bestDistance + 0.05f) { best = cell; bestDistance = distance; }
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var next = (X: cell.X + dx, Y: cell.Y + dy);
                    var nextPoint = new Vector2(next.X, next.Y);
                    if (!SafeSegment(grid, point, nextPoint, rares)) continue;
                    var nextCost = costs[cell] + (dx != 0 && dy != 0 ? 1.414214f : 1);
                    if (costs.TryGetValue(next, out var previousCost) && nextCost >= previousCost) continue;
                    costs[next] = nextCost;
                    parents[next] = cell;
                    var clearance = NearestDistance(nextPoint, rares);
                    queue.Enqueue(next, nextCost + MathF.Max(0, safeDistance - clearance) * 2 +
                        Vector2.Distance(nextPoint, preferred) * 0.2f);
                }
        }
        if (best == start || NearestDistance(new(best.Item1, best.Item2), rares) < initialDistance + 0.5f)
            return new(cluster.Center, cluster.Members.Length, null, null, false, limited);
        var route = new List<Vector2>();
        var current = best;
        while (current != start) { route.Add(new(current.Item1, current.Item2)); current = parents[current]; }
        route.Add(player); route.Reverse();
        return new(cluster.Center, cluster.Members.Length, route[^1], route, false, limited);
    }

    public static Vector2? Steer(NavigationGrid grid, Vector2 player, IReadOnlyList<Vector2> route, IReadOnlyList<Vector2> rares)
    {
        Vector2? aim = null;
        foreach (var point in route.Skip(1).Take(30))
        {
            var delta = point - player;
            if (delta.LengthSquared() < 0.25f) continue;
            var candidate = player + Vector2.Normalize(delta) * MathF.Min(8, delta.Length());
            if (SafeSegment(grid, player, candidate, rares)) aim = candidate;
        }
        return aim;
    }
}
