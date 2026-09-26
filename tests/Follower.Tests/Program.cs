using System.Numerics;
using Follower;

// Pure synthetic checks: no game attachment, overlay or Windows input.
var passed = 0;
void Check(bool success, string name)
{
    if (!success) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

byte[] Map(int width, int height, Func<int, int, bool> walkable)
{
    var bytes = new byte[width / 2 * height];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
        if (walkable(x, y)) bytes[y * (width / 2) + x / 2] |= (byte)(1 << ((x & 1) * 4));
    return bytes;
}

NavigationGrid Grid(byte[] bytes, int clearance = 0, HashSet<(int, int)>? open = null, HashSet<(int, int)>? closed = null)
    => new(bytes, 10, clearance, open ?? [], closed ?? []);

var all = Map(20, 20, (_, _) => true);
var grid = Grid(all);
foreach (var p in new[] { new Vector2(-1, 2), new Vector2(20, 2), new Vector2(5, -1), new Vector2(5, 20), new Vector2(float.NaN, 2) })
    Check(!grid.Contains(p), "reject out-of-grid " + p);
Check(!grid.Walkable(20, 0) && !grid.Walkable(-1, 1), "row wrapping cannot create walkable cells");
Check(!new NavigationGrid(all, 0, 0, [], []).Contains(new(1, 1)), "zero stride rejected");
Check(!Grid(all, open: [(20, 1)]).Walkable(20, 1), "door override cannot escape grid bounds");
Check(grid.Walkable(2, 1) && grid.Walkable(3, 1), "both packed nibbles decode");
Check(grid.Clear(new(2, 2), new(2, 2)), "same-cell line terminates");
Check(grid.Clear(new(2, 2), new(2, 18)), "vertical line");
Check(grid.Clear(new(2, 2), new(18, 2)), "horizontal line");
Check(grid.Clear(new(2.5f, 2.5f), new(10.5f, 10.5f)), "fractional boundary line");
Check(grid.FindPath(new(2, 2), new(18, 18), default)?.Count == 2, "open terrain direct route");
var wall = Map(20, 20, (x, y) => x != 10 || y >= 15);
var detourGrid = Grid(wall);
var path = detourGrid.FindPath(new(3, 5), new(17, 5), default);
Check(path != null && path.Any(p => p.Y >= 15), "A* goes around wall end");
Check(path != null && path.Zip(path.Skip(1)).All(pair => detourGrid.Clear(pair.First, pair.Second)), "every detour edge is walkable");
Check(!detourGrid.Clear(new(3, 5), new(17, 5)), "straight steering cannot cross wall");
Check(path != null && detourGrid.Steer(path, new(3, 5)) is Vector2 aim && detourGrid.Clear(new(3, 5), aim), "steering follows reachable route");
var split = Map(20, 20, (x, _) => x != 10);
Check(Grid(split).FindPath(new(3, 5), new(17, 5), default) == null, "disconnected terrain has no route");
Check(Grid(split).FindPath(new(10, 5), new(17, 5), default) == null, "blocked start never snaps across wall");
Check(Grid(split).FindPath(new(3, 5), new(10, 5), default) == null, "blocked goal never snaps across wall");
Check(Grid(split, open: [(10, 5)]).FindPath(new(3, 5), new(17, 5), default) != null, "known open door connects regions");
Check(Grid(split, open: [(10, 5)], closed: [(10, 5)]).FindPath(new(3, 5), new(17, 5), default) == null, "closed door takes priority over open override");
var corner = Grid(Map(20, 20, (x, y) => (x, y) != (3, 2) && (x, y) != (2, 3)));
Check(!corner.Clear(new(2, 2), new(3, 3)), "supercover rejects diagonal corner cutting");
Check(!corner.Clear(new(2.4f, 2.4f), new(3.2f, 3.2f)), "fractional diagonal rejects corner cutting");
Check(Grid(all, 1).Walkable(1, 1) && !Grid(all, 1).Walkable(0, 1), "clearance includes map edge");
Check(!Grid(wall, 1).Walkable(9, 5), "clearance keeps distance from wall");
Check(detourGrid.FindPath(new(3, 5), new(17, 5), default, maxNodes: 1) == null, "search node budget");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    Check(grid.FindPath(new(3, 5), new(17, 5), cancel.Token) == null, "canceled search discards even direct route");
}
foreach (var (direction, keys) in new[]
{
    (new Vector2(0,-5), MoveKeys.W), (new Vector2(5,0), MoveKeys.D),
    (new Vector2(0,5), MoveKeys.S), (new Vector2(-5,0), MoveKeys.A),
    (new Vector2(5,-5), MoveKeys.W | MoveKeys.D), (new Vector2(-5,5), MoveKeys.S | MoveKeys.A),
}) Check(Steering.Choose(direction, Vector2.UnitX, Vector2.UnitY, _ => true) == keys, "WASD " + keys);
Check(Steering.Choose(new(5, 0), new(1, 0.5f), new(-1, 0.5f), _ => true) == (MoveKeys.S | MoveKeys.D), "isometric world-to-screen basis");
Check(Steering.Choose(new(5, 0), Vector2.UnitX, Vector2.UnitY, _ => false) == MoveKeys.None, "blocked keyboard directions produce no key");
Check(Steering.Choose(new(5, 0), Vector2.Zero, Vector2.Zero, _ => true) == MoveKeys.None, "invalid projection produces no key");

var events = new List<(MoveKeys, bool)>();
var lease = new KeyLease((key, down) => { events.Add((key, down)); return true; });
Check(lease.Renew(MoveKeys.W | MoveKeys.D, 100), "diagonal key-down");
lease.Renew(MoveKeys.W | MoveKeys.D, 120);
Check(events.Count == 2, "renewal does not repeat key-down");
lease.Renew(MoveKeys.S, 130);
Check(events.Skip(2).SequenceEqual(new[] { (MoveKeys.W, false), (MoveKeys.D, false), (MoveKeys.S, true) }), "direction change releases old keys first");
lease.Expire(329, true);
Check(lease.Held == MoveKeys.S, "lease alive before expiry");
lease.Expire(330, true);
Check(lease.Held == MoveKeys.None, "F9/missing frame timeout releases keys");
lease.Renew(MoveKeys.A, 400);
lease.Expire(401, false);
Check(lease.Held == MoveKeys.None, "focus loss releases immediately");
lease.Renew(MoveKeys.D, 500);
lease.Stop();
Check(lease.Held == MoveKeys.None, "disable/area/error cleanup releases owned keys");
var failRelease = true;
var retry = new KeyLease((_, down) => down || !failRelease);
retry.Renew(MoveKeys.W, 0);
retry.Stop();
Check(retry.Held == MoveKeys.W, "failed key-up retains ownership");
Check(!retry.Renew(MoveKeys.S, 1) && retry.Held == MoveKeys.W, "failed release prevents opposite key-down");
failRelease = false;
retry.Expire(500, false);
Check(retry.Held == MoveKeys.None, "watchdog retries failed release");
var partial = new KeyLease((key, down) => !down || key != MoveKeys.D);
Check(!partial.Renew(MoveKeys.W | MoveKeys.D, 0) && partial.Held == MoveKeys.None, "partial diagonal send failure cleans up");

var session = new FollowSession();
Check(!session.NeedsMovement(20, true, 18, 25), "idle inside hysteresis band");
Check(session.NeedsMovement(30, true, 18, 25), "resume outside distance");
Check(session.NeedsMovement(20, true, 18, 25), "continue within hysteresis band");
Check(!session.NeedsMovement(18, true, 18, 25), "stop at following distance");
Check(session.NeedsMovement(10, false, 18, 25), "nearby target behind wall still needs route");
Check(!session.IsStuck(new(2, 2), true, 100, 2500), "start progress monitor");
Check(session.IsStuck(new(2, 2), true, 2600, 2500), "stationary movement times out");
Check(!session.IsStuck(new(5, 2), true, 2601, 2500), "movement resets progress timer");
Check(!session.IsStuck(new(5, 2), false, 9000, 2500), "waiting for route is not stuck movement");
Check(!session.IsStuck(new(5, 2), true, 9001, 2500), "resume starts fresh progress timer");
session.Reset();
Check(!session.NeedsMovement(20, true, 18, 25), "target/area reset clears hysteresis");
var settings = new FollowerSettings { StopDistance = float.NaN, ResumeDistance = float.PositiveInfinity, Clearance = -1, RepathMilliseconds = 0 };
settings.Normalize();
Check(settings.StopDistance == 18 && settings.ResumeDistance > 18 && settings.Clearance == 0 && settings.RepathMilliseconds == 150, "invalid settings normalize");
Check(new FollowerSettings().PreviewOnly, "new install defaults to preview without input");
Console.WriteLine($"All {passed} Follower checks passed. No Windows input or live gameplay tested.");
