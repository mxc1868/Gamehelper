namespace Follower;

using System.Diagnostics;
using System.Numerics;

// Uses the same packed terrain format and 8-neighbour A* approach as Radar.
// Movement requires stricter edges than a display route: bounded coordinates,
// no endpoint snapping across walls, clearance, and no diagonal corner cutting.
internal sealed class NavigationGrid(byte[] data, int stride, int clearance,
    HashSet<(int, int)> openDoors, HashSet<(int, int)> closedDoors)
{
    public bool Contains(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) &&
        stride > 0 && p.X >= 0 && p.Y >= 0 && p.X < stride * 2L && p.Y < data.Length / stride;

    private bool Cell(int x, int y)
    {
        if (stride <= 0 || x < 0 || y < 0 || x >= stride * 2L || y >= data.Length / stride) return false;
        if (closedDoors.Contains((x, y))) return false;
        return openDoors.Contains((x, y)) || ((data[y * stride + x / 2] >> ((x & 1) * 4)) & 15) != 0;
    }

    public bool Walkable(int x, int y)
    {
        for (var dx = -clearance; dx <= clearance; dx++)
            for (var dy = -clearance; dy <= clearance; dy++)
                if (!this.Cell(x + dx, y + dy)) return false;
        return true;
    }

    // Supercover traversal: when the segment hits a corner, both adjacent cells
    // must be clear. Sampling only Bresenham's centre cells can cut a wall corner.
    public bool Clear(Vector2 a, Vector2 b)
    {
        if (!this.Contains(a) || !this.Contains(b)) return false;
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
        while (limit-- > 0)
        {
            if (!this.Walkable(x, y)) return false;
            if (x == endX && y == endY) return true;
            if (MathF.Abs(tx - ty) < 0.00001f)
            {
                if (!this.Walkable(x + sx, y) || !this.Walkable(x, y + sy)) return false;
                x += sx; y += sy; tx += stepX; ty += stepY;
            }
            else if (tx < ty) { x += sx; tx += stepX; }
            else { y += sy; ty += stepY; }
        }
        return false;
    }

    public List<Vector2>? FindPath(Vector2 start, Vector2 goal, CancellationToken cancellation,
        int maxNodes = 40000, int maxMilliseconds = 100)
    {
        if (cancellation.IsCancellationRequested || !this.Contains(start) || !this.Contains(goal) || Vector2.Distance(start, goal) > 600) return null;
        var first = ((int)MathF.Round(start.X), (int)MathF.Round(start.Y));
        var last = ((int)MathF.Round(goal.X), (int)MathF.Round(goal.Y));
        if (!this.Walkable(first.Item1, first.Item2) || !this.Walkable(last.Item1, last.Item2)) return null;
        if (this.Clear(start, goal)) return [start, goal];
        var clock = Stopwatch.StartNew();
        var queue = new PriorityQueue<(int x, int y), float>();
        var costs = new Dictionary<(int, int), float> { [first] = 0 };
        var parents = new Dictionary<(int, int), (int, int)>();
        var visited = new HashSet<(int, int)>();
        queue.Enqueue(first, 0);
        var count = 0;
        while (queue.TryDequeue(out var current, out _) && count++ < maxNodes)
        {
            if ((count & 127) == 0 && (cancellation.IsCancellationRequested || clock.ElapsedMilliseconds > maxMilliseconds)) return null;
            if (!visited.Add(current)) continue;
            if (current == last)
            {
                var route = new List<Vector2> { goal };
                while (current != first)
                {
                    route.Add(new(current.x, current.y));
                    current = parents[current];
                }
                route.Add(start);
                route.Reverse();
                return route;
            }
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var next = (x: current.x + dx, y: current.y + dy);
                if (visited.Contains(next) || !this.Walkable(next.x, next.y)) continue;
                if (dx != 0 && dy != 0 && (!this.Walkable(current.x + dx, current.y) || !this.Walkable(current.x, current.y + dy))) continue;
                var cost = costs[current] + (dx == 0 || dy == 0 ? 1 : 1.41421356f);
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost;
                parents[next] = current;
                queue.Enqueue(next, cost + Vector2.Distance(new(next.x, next.y), new(last.Item1, last.Item2)));
            }
        }
        return null;
    }

    public Vector2? Steer(IReadOnlyList<Vector2> route, Vector2 player)
    {
        // The farthest visible point within a short horizon avoids old path nodes
        // pulling the character backwards while keeping turns close to the route.
        var nearest = 0;
        var nearestDistance = float.MaxValue;
        for (var i = 0; i < route.Count; i++)
        {
            var distance = Vector2.DistanceSquared(route[i], player);
            if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
        }
        for (var i = Math.Min(route.Count - 1, nearest + 60); i >= nearest; i--)
        {
            var delta = route[i] - player;
            if (delta.LengthSquared() < 0.25f || !this.Clear(player, route[i])) continue;
            return player + Vector2.Normalize(delta) * MathF.Min(8, delta.Length());
        }
        return null;
    }
}

[Flags]
internal enum MoveKeys { None = 0, W = 1, A = 2, S = 4, D = 8 }

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
            result = (x < 0 ? MoveKeys.A : x > 0 ? MoveKeys.D : MoveKeys.None) |
                     (y < 0 ? MoveKeys.W : y > 0 ? MoveKeys.S : MoveKeys.None);
        }
        return result;
    }
}
