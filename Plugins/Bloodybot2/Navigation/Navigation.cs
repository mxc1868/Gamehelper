namespace Bloodybot2.Navigation;

using System.Diagnostics;
using System.Numerics;

internal enum PathSearchStatus { Found, StartBlocked, GoalBlocked, Unreachable, BudgetExceeded, Cancelled, Invalid }
internal sealed record PathSearchResult(PathSearchStatus Status, List<Vector2>? Route = null);

// A blocked target can use its own recently observed walkable position, never
// an arbitrary nearest cell on the other side of a wall. Scoped to one identity.
internal sealed class NavigationTarget
{
    private Vector2? anchor;
    private Vector2? previous;
    public Vector2? Observe(NavigationGrid grid, Vector2 target)
    {
        if (!grid.Contains(target)) { this.Reset(); return null; }
        if (this.previous is Vector2 last && Vector2.DistanceSquared(last, target) > 64) this.anchor = null;
        this.previous = target;
        if (grid.Walkable(target)) this.anchor = target;
        if (this.anchor is Vector2 point && grid.CanUseObservedAnchor(point, target))
            return point;
        this.anchor = null;
        return null;
    }
    public void Reset() { this.anchor = this.previous = null; }
}

// Uses the same packed terrain format and 8-neighbour A* approach as Radar.
// Movement requires stricter edges than a display route: bounded coordinates,
// no endpoint snapping across walls and no diagonal corner cutting. Clearance is
// a route preference, never a reason to declare a physically open cell blocked.
internal sealed class NavigationGrid(byte[] data, int stride, int clearance,
    HashSet<(int, int)> openDoors, HashSet<(int, int)> closedDoors)
{
    public bool Contains(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) &&
        stride > 0 && p.X >= 0 && p.Y >= 0 && p.X < stride * 2L && p.Y < data.Length / stride;

    public bool Walkable(int x, int y)
    {
        if (stride <= 0 || x < 0 || y < 0 || x >= stride * 2L || y >= data.Length / stride) return false;
        if (closedDoors.Contains((x, y))) return false;
        return openDoors.Contains((x, y)) || ((data[y * stride + x / 2] >> ((x & 1) * 4)) & 15) != 0;
    }

    public bool Walkable(Vector2 p) => this.Contains(p) && this.Walkable((int)MathF.Round(p.X), (int)MathF.Round(p.Y));

    public bool CanUseObservedAnchor(Vector2 anchor, Vector2 goal)
    {
        if (!this.Walkable(anchor) || !this.Contains(goal) || Vector2.DistanceSquared(anchor, goal) > 16) return false;
        // A terrain mismatch may use the actor's observed history; an explicitly
        // closed doorway must not be bypassed by that history.
        for (var i = 0; i <= 16; i++)
        {
            var p = Vector2.Lerp(anchor, goal, i / 16f);
            if (closedDoors.Contains(((int)MathF.Round(p.X), (int)MathF.Round(p.Y)))) return false;
        }
        return true;
    }

    private float CellCost(int x, int y)
    {
        if (!this.Walkable(x, y)) return float.PositiveInfinity;
        // Search the nearest wall first. Penalize proximity while preserving
        // narrow corridors and allowing the player to leave a wall-adjacent cell.
        for (var radius = 1; radius <= clearance; radius++)
            for (var dx = -radius; dx <= radius; dx++)
                for (var dy = -radius; dy <= radius; dy++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && !this.Walkable(x + dx, y + dy))
                        return 1 + 3 * (clearance - radius + 1);
        return 1;
    }

    // Supercover traversal: when the segment hits a corner, both adjacent cells
    // must be clear. Sampling only Bresenham's centre cells can cut a wall corner.
    public bool Clear(Vector2 a, Vector2 b) => float.IsFinite(this.LineCost(a, b, false));

    private float LineCost(Vector2 a, Vector2 b, bool preferClearance = true)
    {
        if (!this.Contains(a) || !this.Contains(b)) return float.PositiveInfinity;
        var x = (int)MathF.Round(a.X);
        var y = (int)MathF.Round(a.Y);
        var endX = (int)MathF.Round(b.X);
        var endY = (int)MathF.Round(b.Y);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        var tx = sx == 0 ? float.PositiveInfinity : (x + sx * 0.5f - a.X) / dx;
        var ty = sy == 0 ? float.PositiveInfinity : (y + sy * 0.5f - a.Y) / dy;
        var stepX = sx == 0 ? float.PositiveInfinity : 1 / MathF.Abs(dx);
        var stepY = sy == 0 ? float.PositiveInfinity : 1 / MathF.Abs(dy);
        var limit = Math.Abs(endX - x) + Math.Abs(endY - y) + 2;
        var length = Vector2.Distance(a, b);
        var cost = 0f;
        var previousT = 0f;
        while (limit-- > 0)
        {
            if (!this.Walkable(x, y)) return float.PositiveInfinity;
            var weight = preferClearance ? this.CellCost(x, y) : 1;
            if (x == endX && y == endY) return cost + (1 - previousT) * length * weight;
            var nextT = Math.Clamp(MathF.Min(tx, ty), previousT, 1);
            cost += (nextT - previousT) * length * weight;
            previousT = nextT;
            if (MathF.Abs(tx - ty) < 0.00001f)
            {
                if (!this.Walkable(x + sx, y) || !this.Walkable(x, y + sy)) return float.PositiveInfinity;
                x += sx; y += sy; tx += stepX; ty += stepY;
            }
            else if (tx < ty) { x += sx; tx += stepX; }
            else { y += sy; ty += stepY; }
        }
        return float.PositiveInfinity;
    }

    public List<Vector2>? FindPath(Vector2 start, Vector2 goal, CancellationToken cancellation,
        int maxNodes = 40000, int maxMilliseconds = 100)
        => this.SearchPath(start, goal, cancellation, maxNodes: maxNodes, maxMilliseconds: maxMilliseconds).Route;

    public PathSearchResult SearchPath(Vector2 start, Vector2 goal, CancellationToken cancellation,
        float arrivalRadius = 0, Vector2? observedGoalAnchor = null, int maxNodes = 40000, int maxMilliseconds = 100)
    {
        if (cancellation.IsCancellationRequested) return new(PathSearchStatus.Cancelled);
        if (!this.Contains(start) || !this.Contains(goal) || Vector2.Distance(start, goal) > 600 ||
            !float.IsFinite(arrivalRadius) || arrivalRadius < 0) return new(PathSearchStatus.Invalid);
        var first = ((int)MathF.Round(start.X), (int)MathF.Round(start.Y));
        if (!this.Walkable(first.Item1, first.Item2)) return new(PathSearchStatus.StartBlocked);
        var endpoint = goal;
        if (!this.Walkable(goal))
        {
            if (observedGoalAnchor is not Vector2 anchor || !this.CanUseObservedAnchor(anchor, goal))
                return new(PathSearchStatus.GoalBlocked);
            endpoint = anchor;
        }
        if (maxNodes <= 0 || maxMilliseconds <= 0) return new(PathSearchStatus.BudgetExceeded);
        var last = ((int)MathF.Round(endpoint.X), (int)MathF.Round(endpoint.Y));
        var clock = Stopwatch.StartNew();
        // Only cells locally connected to the target's observed side are arrival
        // candidates. A radius alone would incorrectly accept a spot across a wall.
        var radius = Math.Clamp(arrivalRadius, 0, 12);
        var endpointOffset = Vector2.Distance(endpoint, goal);
        if (radius > 0 && endpointOffset > radius) return new(PathSearchStatus.GoalBlocked);
        var goals = new HashSet<(int x, int y)> { last };
        if (radius > 0)
        {
            var frontier = new PriorityQueue<(int x, int y), float>();
            var localCosts = new Dictionary<(int x, int y), float> { [last] = 0 };
            frontier.Enqueue(last, 0);
            while (frontier.TryDequeue(out var cell, out var localCost))
            {
                if (cancellation.IsCancellationRequested) return new(PathSearchStatus.Cancelled);
                if (clock.ElapsedMilliseconds > maxMilliseconds) return new(PathSearchStatus.BudgetExceeded);
                if (localCost > localCosts[cell]) continue;
                foreach (var next in this.Neighbours(cell))
                {
                    var nextCost = localCost + (next.x == cell.x || next.y == cell.y ? 1 : 1.41421356f);
                    if (nextCost + endpointOffset > radius ||
                        Vector2.DistanceSquared(new(next.x, next.y), goal) > radius * radius ||
                        localCosts.TryGetValue(next, out var oldCost) && oldCost <= nextCost) continue;
                    localCosts[next] = nextCost;
                    goals.Add(next);
                    frontier.Enqueue(next, nextCost);
                }
            }
        }
        if (goals.Contains(first)) return new(PathSearchStatus.Found, [start]);
        // A wall-adjacent straight line can be walkable yet unnecessarily hug
        // the wall. Only bypass A* when the line has no clearance penalty.
        if (this.LineCost(start, endpoint) <= Vector2.Distance(start, endpoint) + 0.001f)
            return new(PathSearchStatus.Found, [start, endpoint]);
        var queue = new PriorityQueue<(int x, int y), float>();
        var costs = new Dictionary<(int, int), float> { [first] = 0 };
        var parents = new Dictionary<(int, int), (int, int)>();
        var visited = new HashSet<(int, int)>();
        var weights = new Dictionary<(int x, int y), float>();
        float Weight((int x, int y) cell)
        {
            if (!weights.TryGetValue(cell, out var weight)) weights[cell] = weight = this.CellCost(cell.x, cell.y);
            return weight;
        }
        queue.Enqueue(first, 0);
        var count = 0;
        while (queue.TryDequeue(out var current, out _))
        {
            if (count++ >= maxNodes) return new(PathSearchStatus.BudgetExceeded);
            if ((count & 127) == 0)
            {
                if (cancellation.IsCancellationRequested) return new(PathSearchStatus.Cancelled);
                if (clock.ElapsedMilliseconds > maxMilliseconds) return new(PathSearchStatus.BudgetExceeded);
            }
            if (!visited.Add(current)) continue;
            if (goals.Contains(current))
            {
                var route = new List<Vector2> { current == last ? endpoint : new(current.x, current.y) };
                while (current != first)
                {
                    route.Add(new(current.x, current.y));
                    current = parents[current];
                }
                route.Add(start);
                route.Reverse();
                return new(PathSearchStatus.Found, route);
            }
            foreach (var next in this.Neighbours(current))
            {
                if (visited.Contains(next)) continue;
                var cost = costs[current] + (next.x == current.x || next.y == current.y ? 1 : 1.41421356f) *
                    (Weight(current) + Weight(next)) * 0.5f;
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost;
                parents[next] = current;
                queue.Enqueue(next, cost + MathF.Max(0, Vector2.Distance(new(next.x, next.y), goal) - MathF.Max(radius, Vector2.Distance(endpoint, goal))));
            }
        }
        return new(PathSearchStatus.Unreachable);
    }

    private IEnumerable<(int x, int y)> Neighbours((int x, int y) cell)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0 || !this.Walkable(cell.x + dx, cell.y + dy)) continue;
            if (dx != 0 && dy != 0 && (!this.Walkable(cell.x + dx, cell.y) || !this.Walkable(cell.x, cell.y + dy))) continue;
            yield return (cell.x + dx, cell.y + dy);
        }
    }

    // Called only when P2 is within the rejoin radius. Bound this local check so
    // rendering/input freshness never waits on an ordinary long A* search.
    public bool HasShortConnection(Vector2 from, Vector2 target, float rejoinDistance, Vector2? observedGoalAnchor = null)
    {
        if (Vector2.Distance(from, target) > rejoinDistance) return false;
        if (this.Clear(from, target)) return true;
        var result = this.SearchPath(from, target, default, observedGoalAnchor: observedGoalAnchor,
            maxNodes: 1024, maxMilliseconds: 3);
        var points = result.Route;
        if (points == null) return false;
        var length = Vector2.Distance(points[^1], target);
        for (var i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length <= rejoinDistance + 2;
    }

    public Vector2? Steer(IReadOnlyList<Vector2> route, Vector2 player)
    {
        // Keep the clearance preference when skipping path nodes. A merely
        // visible shortcut may undo A* and send the player straight along a wall.
        var nearest = 0;
        var nearestDistance = float.MaxValue;
        for (var i = 0; i < route.Count; i++)
        {
            var distance = Vector2.DistanceSquared(route[i], player);
            if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
        }
        var alongRoute = 0f;
        var previous = player;
        Vector2? aim = null;
        for (var i = nearest; i <= Math.Min(route.Count - 1, nearest + 60); i++)
        {
            alongRoute += this.LineCost(previous, route[i]);
            previous = route[i];
            var direct = this.LineCost(player, route[i]);
            // Rejoin a still-visible forward section if an old connector became
            // blocked. Every returned segment is checked against current terrain.
            if (!float.IsFinite(alongRoute)) alongRoute = direct;
            var delta = route[i] - player;
            if (delta.LengthSquared() < 0.25f || !float.IsFinite(direct) || direct > alongRoute * 1.05f + 0.05f) continue;
            aim = player + Vector2.Normalize(delta) * MathF.Min(8, delta.Length());
        }
        return aim;
    }
}

[Flags]
internal enum MoveKeys { None = 0, Up = 1, Left = 2, Down = 4, Right = 8 }

internal static class Steering
{
    // Basis vectors are projected at the player's own height; height differences
    // to the leader must not be interpreted as a horizontal movement direction.
    public static MoveKeys Choose(Vector2 targetDelta, Vector2 screenX, Vector2 screenY,
        Func<Vector2, bool> canStep)
    {
        if (!float.IsFinite(targetDelta.X + targetDelta.Y + screenX.X + screenX.Y + screenY.X + screenY.Y)) return MoveKeys.None;
        var det = screenX.X * screenY.Y - screenY.X * screenX.Y;
        if (MathF.Abs(det) < 0.0001f || targetDelta.LengthSquared() < 0.01f) return MoveKeys.None;
        var best = 0.65f;
        var result = MoveKeys.None;
        var direction = Vector2.Normalize(targetDelta);
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            if (x == 0 && y == 0) continue;
            var grid = new Vector2((screenY.Y * x - screenY.X * y) / det, (-screenX.Y * x + screenX.X * y) / det);
            grid = Vector2.Normalize(grid);
            var alignment = Vector2.Dot(grid, direction);
            if (alignment <= best || !canStep(grid * MathF.Min(2, targetDelta.Length()))) continue;
            best = alignment;
            result = (x < 0 ? MoveKeys.Left : x > 0 ? MoveKeys.Right : MoveKeys.None) |
                     (y < 0 ? MoveKeys.Up : y > 0 ? MoveKeys.Down : MoveKeys.None);
        }
        return result;
    }
}
