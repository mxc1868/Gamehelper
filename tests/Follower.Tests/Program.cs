using System.Numerics;
using Bloodybot2.Navigation;

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
Check(Grid(all, 1).Walkable(0, 1) && !Grid(all, 1).Walkable(-1, 1), "clearance preserves walkable map-edge cells without extending bounds");
Check(Grid(wall, 1).Walkable(9, 5) && !Grid(wall, 1).Walkable(10, 5), "clearance distinguishes wall proximity from a solid wall");
var nearWallGrid = Grid(split, 1);
var wallStart = new Vector2(9, 5);
var awayFromWall = new Vector2(3, 5);
var escapePath = nearWallGrid.FindPath(wallStart, awayFromWall, default);
Check(escapePath != null, "default clearance allows starting beside a wall");
Check(escapePath != null && nearWallGrid.Steer(escapePath, wallStart) is Vector2 escapeAim &&
    escapeAim.X < wallStart.X && nearWallGrid.Clear(wallStart, escapeAim), "near-wall start has a usable outward steering segment");
Check(nearWallGrid.FindPath(awayFromWall, wallStart, default) != null, "default clearance allows leader beside a wall");
Check(nearWallGrid.FindPath(wallStart, new(11, 5), default) == null, "soft clearance cannot connect opposite sides of a solid wall");
var narrowGrid = Grid(Map(20, 20, (_, y) => y == 10), 2);
var narrowPath = narrowGrid.FindPath(new(2, 10), new(17, 10), default);
Check(narrowPath != null && narrowPath.All(p => p.Y == 10), "one-cell corridor remains connected with clearance two");
Check(narrowPath != null && narrowGrid.Steer(narrowPath, new(2, 10)) is Vector2 narrowAim &&
    Steering.Choose(narrowAim - new Vector2(2, 10), Vector2.UnitX, Vector2.UnitY,
        step => narrowGrid.Clear(new(2, 10), new Vector2(2, 10) + step)) == MoveKeys.Right,
    "narrow corridor produces a valid movement key");
var alongWall = nearWallGrid.FindPath(new(9, 2), new(9, 17), default);
Check(alongWall != null && alongWall.Any(p => p.X <= 8), "clearance prefers an interior route over a long wall-hugging line");
Check(alongWall != null && nearWallGrid.Steer(alongWall, new(9, 2)) is Vector2 interiorAim && interiorAim.X < 8.75f,
    "steering shortcuts preserve the preference to move away from a wall");
var noClearanceAlongWall = Grid(split).FindPath(new(9, 2), new(9, 17), default);
Check(noClearanceAlongWall?.Count == 2, "zero clearance keeps direct wall-adjacent routes");
var bendGrid = Grid(Map(20, 20, (x, y) => (y == 10 && x <= 10) || (x == 10 && y >= 10)), 2);
var bendPath = bendGrid.FindPath(new(2, 10), new(10, 17), default);
Check(bendPath != null && bendPath.Zip(bendPath.Skip(1)).All(pair => bendGrid.Clear(pair.First, pair.Second)),
    "tight corridor bend produces only physically walkable path segments");
Check(bendPath != null && bendGrid.Steer(bendPath, new(9, 10)) is Vector2 bendAim && bendAim.Y == 10 && bendAim.X > 9,
    "tight bend steering reaches the corner before turning");
Check(bendPath != null && bendGrid.Steer(bendPath, new(10, 10)) is Vector2 turnAim && turnAim.X == 10 && turnAim.Y > 10,
    "tight bend steering advances after reaching the corner");
foreach (var preferredGap in new[] { 1, 2 })
{
    var nearWall = Grid(split, preferredGap);
    foreach (var (from, to) in new[]
    {
        (new Vector2(9.4f, 4.2f), new Vector2(3.2f, 8.4f)),
        (new Vector2(3.2f, 8.4f), new Vector2(9.4f, 4.2f)),
        (new Vector2(11, 5), new Vector2(17, 5)),
    })
    {
        var fractionalPath = nearWall.FindPath(from, to, default);
        Check(fractionalPath != null && fractionalPath[0] == from && fractionalPath[^1] == to &&
            fractionalPath.Zip(fractionalPath.Skip(1)).All(pair => nearWall.Clear(pair.First, pair.Second)),
            $"near-wall endpoints remain connected without snapping: clearance {preferredGap}, {from} -> {to}");
    }
    var opening = Grid(split, preferredGap, open: [(10, 5)]);
    var openingPath = opening.FindPath(new(9, 5), new(11, 5), default);
    Check(openingPath != null, $"narrow open doorway remains usable with clearance {preferredGap}");
    var shutDoor = Grid(split, preferredGap, open: [(10, 5)], closed: [(10, 5)]);
    Check(shutDoor.FindPath(new(9, 5), new(11, 5), default) == null &&
        openingPath != null && shutDoor.Steer(openingPath, new(9, 5)) == null,
        $"closed doorway blocks both new search and old-route steering with clearance {preferredGap}");
    var tightCorner = Grid(Map(20, 20, (x, y) => (x, y) is (2, 2) or (3, 3)), preferredGap);
    Check(tightCorner.FindPath(new(2, 2), new(3, 3), default) == null,
        $"clearance preference cannot cut diagonally through a closed corner: {preferredGap}");
}
Check(detourGrid.FindPath(new(3, 5), new(17, 5), default, maxNodes: 1) == null, "search node budget");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    Check(grid.FindPath(new(3, 5), new(17, 5), cancel.Token) == null, "canceled search discards even direct route");
}
Check(Grid(split).SearchPath(new(10, 5), new(17, 5), default).Status == PathSearchStatus.StartBlocked,
    "search reports blocked actual start separately from unreachable terrain");
Check(Grid(split).SearchPath(new(3, 5), new(10, 5), default).Status == PathSearchStatus.GoalBlocked,
    "search reports blocked goal without inventing a walkable endpoint");
Check(Grid(split).SearchPath(new(3, 5), new(17, 5), default).Status == PathSearchStatus.Unreachable,
    "an exhausted disconnected search reports no route");
Check(detourGrid.SearchPath(new(3, 5), new(17, 5), default, maxNodes: 1).Status == PathSearchStatus.BudgetExceeded,
    "node limit does not mislabel a real detour as an impassable wall");
Check(grid.SearchPath(new(3, 5), new(17, 5), default, maxMilliseconds: 0).Status == PathSearchStatus.BudgetExceeded,
    "time budget exhaustion remains distinct from path failure");
using (var cancelledSearch = new CancellationTokenSource())
{
    cancelledSearch.Cancel();
    Check(grid.SearchPath(new(3, 5), new(17, 5), cancelledSearch.Token).Status == PathSearchStatus.Cancelled,
        "cancelled search is not treated as terrain failure");
}
var regionRoute = nearWallGrid.SearchPath(new(3, 5), new(9, 5), default, arrivalRadius: 3);
Check(regionRoute.Route is { Count: > 1 } && regionRoute.Route[^1] != new Vector2(9, 5) &&
    Vector2.Distance(regionRoute.Route[^1], new(9, 5)) <= 3,
    "arrival region reaches a connected landing near a wall-adjacent target without occupying its exact cell");
Check(regionRoute.Route != null && regionRoute.Route.Zip(regionRoute.Route.Skip(1)).All(p => nearWallGrid.Clear(p.First, p.Second)),
    "arrival-region paths retain physically walkable segments");
Check(Grid(split).SearchPath(new(9, 5), new(11, 5), default, arrivalRadius: 12).Status == PathSearchStatus.Unreachable,
    "a large arrival radius cannot accept a nearby cell across a disconnected wall");
var targetHistory = new NavigationTarget();
Check(targetHistory.Observe(nearWallGrid, new(10, 5)) == null,
    "an initially blocked target has no invented anchor");
Check(targetHistory.Observe(nearWallGrid, new(9, 5)) == new Vector2(9, 5),
    "target history records the actual observed walkable position");
var observedAnchor = targetHistory.Observe(nearWallGrid, new(10, 5));
var anchoredRoute = nearWallGrid.SearchPath(new(3, 5), new(10, 5), default, arrivalRadius: 4, observedGoalAnchor: observedAnchor);
Check(observedAnchor == new Vector2(9, 5) && anchoredRoute.Route != null && anchoredRoute.Route.All(p => p.X < 10),
    "a terrain-mismatched target can use its nearby observed anchor on the same side of the wall");
Check(nearWallGrid.SearchPath(new(10, 5), new(9, 5), default, arrivalRadius: 4, observedGoalAnchor: new(9, 5)).Status == PathSearchStatus.StartBlocked,
    "goal tolerance never snaps a blocked follower start");
Check(targetHistory.Observe(nearWallGrid, new(10, 12)) == null,
    "target movement beyond the small anchor tolerance discards old history");
targetHistory.Observe(nearWallGrid, new(9, 5));
targetHistory.Reset();
Check(targetHistory.Observe(nearWallGrid, new(10, 5)) == null, "area or identity reset discards target anchors");
Check(Grid(split, closed: [(10, 5)]).SearchPath(new(3, 5), new(10, 5), default,
    arrivalRadius: 4, observedGoalAnchor: new(9, 5)).Status == PathSearchStatus.GoalBlocked,
    "an observed anchor cannot bypass an explicitly closed door");
var localCorner = Grid(Map(20, 20, (x, y) => (x, y) != (10, 10)));
Check(!localCorner.Clear(new(9, 10), new(11, 10)) && localCorner.HasShortConnection(new(9, 10), new(11, 10), 4),
    "nearby players can rejoin via a short corner detour when the straight line is blocked");
Check(!Grid(split).HasShortConnection(new(9, 10), new(11, 10), 4),
    "distance alone does not declare players on opposite sides of a wall reunited");
var longDetourGrid = Grid(Map(20, 20, (x, y) => x != 10 || y == 18));
Check(!longDetourGrid.HasShortConnection(new(9, 2), new(11, 2), 12),
    "a long wall detour is not a short reunion despite nearby coordinates");
var cornerCoop = new CoopCoordinator();
cornerCoop.Update(new(11, 10), new(1, 10), false, 6, 4);
Check(!cornerCoop.Update(new(11, 10), new(9, 10), localCorner.HasShortConnection(new(9, 10), new(11, 10), 4), 6, 4),
    "short-path evidence ends P2 correction without requiring a direct line");

foreach (var (direction, keys) in new[]
{
    (new Vector2(0,-5), MoveKeys.Up), (new Vector2(5,0), MoveKeys.Right),
    (new Vector2(0,5), MoveKeys.Down), (new Vector2(-5,0), MoveKeys.Left),
    (new Vector2(5,-5), MoveKeys.Up | MoveKeys.Right), (new Vector2(-5,5), MoveKeys.Down | MoveKeys.Left),
}) Check(Steering.Choose(direction, Vector2.UnitX, Vector2.UnitY, _ => true) == keys, "WASD " + keys);
Check(Steering.Choose(new(5, 0), new(1, 0.5f), new(-1, 0.5f), _ => true) == (MoveKeys.Down | MoveKeys.Right), "isometric world-to-screen basis");
Check(Steering.Choose(new(5, 0), Vector2.UnitX, Vector2.UnitY, _ => false) == MoveKeys.None, "blocked keyboard directions produce no key");
Check(Steering.Choose(new(5, 0), Vector2.Zero, Vector2.Zero, _ => true) == MoveKeys.None, "invalid projection produces no key");

var events = new List<(MoveKeys, bool)>();
var lease = new KeyLease((key, down) => { events.Add((key, down)); return true; });
Check(lease.Renew(MoveKeys.Up | MoveKeys.Right, 100), "diagonal key-down");
lease.Renew(MoveKeys.Up | MoveKeys.Right, 120);
Check(events.Count == 2, "renewal does not repeat key-down");
lease.Renew(MoveKeys.Down, 130);
Check(events.Skip(2).SequenceEqual(new[] { (MoveKeys.Up, false), (MoveKeys.Right, false), (MoveKeys.Down, true) }), "direction change releases old keys first");
lease.Expire(329, true);
Check(lease.Held == MoveKeys.Down, "lease alive before expiry");
lease.Expire(330, true);
Check(lease.Held == MoveKeys.None, "F9/missing frame timeout releases keys");
lease.Renew(MoveKeys.Left, 400);
lease.Expire(401, false);
Check(lease.Held == MoveKeys.None, "focus loss releases immediately");
lease.Renew(MoveKeys.Right, 500);
lease.Stop();
Check(lease.Held == MoveKeys.None, "disable/area/error cleanup releases owned keys");
var failRelease = true;
var retry = new KeyLease((_, down) => down || !failRelease);
retry.Renew(MoveKeys.Up, 0);
retry.Stop();
Check(retry.Held == MoveKeys.Up, "failed key-up retains ownership");
Check(!retry.Renew(MoveKeys.Down, 1) && retry.Held == MoveKeys.Up, "failed release prevents opposite key-down");
failRelease = false;
retry.Expire(500, false);
Check(retry.Held == MoveKeys.None, "watchdog retries failed release");
var partial = new KeyLease((key, down) => !down || key != MoveKeys.Right);
Check(!partial.Renew(MoveKeys.Up | MoveKeys.Right, 0) && partial.Held == MoveKeys.None, "partial diagonal send failure cleans up");

var session = new FollowSession();
Check(!session.NeedsMovement(20, 18, 25), "idle inside hysteresis band");
Check(session.NeedsMovement(30, 18, 25), "resume outside distance");
Check(session.NeedsMovement(20, 18, 25), "continue within hysteresis band");
Check(!session.NeedsMovement(18, 18, 25), "stop at following distance");
Check(!session.NeedsMovement(10, 18, 25), "nearby target stops even when a wall or terrain flag blocks the direct line");
session.Reset();
Check(!session.NeedsMovement(5, 18, 25),
    "wall proximity alone does not prevent stopping within following distance");
var thirtyGrid = new NavigationGrid(Map(80, 80, (x, _) => x != 20), 40, 2, [], []);
var thirtySession = new FollowSession();
var thirtyLeader = new Vector2(45, 5);
Check(thirtySession.NeedsMovement(40, 30, 33), "30-grid setting starts pursuit beyond resume radius");
var thirtyPlayer = new Vector2(15, 5);
Check(!thirtyGrid.Clear(thirtyPlayer, thirtyLeader) &&
    !thirtySession.NeedsMovement(Vector2.Distance(thirtyPlayer, thirtyLeader), 30, 33),
    "regression: exactly 30 grids stops an active pursuit even with a blocked line");
Check(!thirtySession.NeedsMovement(29, 30, 33) && !thirtySession.NeedsMovement(32.9f, 30, 33),
    "blocked terrain cannot restart movement inside stop/resume hysteresis");
Check(thirtySession.NeedsMovement(33, 30, 33) && thirtySession.NeedsMovement(30.1f, 30, 33),
    "resume boundary restarts pursuit until it reaches the stop radius again");
Check(!thirtySession.NeedsMovement(float.NaN, 30, 33) && !thirtySession.NeedsMovement(31, 30, 33),
    "invalid distance releases pursuit and clears its hysteresis");

// Stop/resume uses the nearest living follower without changing shared steering.
var formationLeader = new Vector2(100, 100);
var formationSession = new FollowSession();
var formation = FollowPlan.Resolve(new(50, 100), true, new(60, 100), true, formationLeader, false)!.Value;
Check(formation.LeaderDistance == 40 && !formation.UseSecondary && !formation.Rejoining &&
    formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "both followers beyond resume use shared WASD steered from P1");
formation = FollowPlan.Resolve(new(60, 100), true, new(70, 100), true, formationLeader, false)!.Value;
Check(formation.LeaderDistance == 30 && !formation.UseSecondary &&
    !formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "P2 reaching 30 stops shared movement while P1 is still 40 from leader");
formation = FollowPlan.Resolve(new(50, 100), true, new(67.1f, 100), true, formationLeader, false)!.Value;
Check(!formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "distant P1 cannot resume while P2 remains below resume radius");
formation = FollowPlan.Resolve(new(50, 100), true, new(67, 100), true, formationLeader, false)!.Value;
Check(formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "shared movement resumes exactly when both followers reach resume radius");
formation = FollowPlan.Resolve(new(70, 100), true, new(60, 100), true, formationLeader, false)!.Value;
Check(formation.LeaderDistance == 30 && !formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "nearest role can change from P2 to P1 and still stop immediately");
formation = FollowPlan.Resolve(new(60, 100), true, formationLeader, false, formationLeader, true)!.Value;
Check(formation.LeaderDistance == 40 && !formation.UseSecondary && !formation.Rejoining &&
    formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "dead P2 at the leader neither stops living P1 nor keeps P2 correction active");
formation = FollowPlan.Resolve(formationLeader, false, new(60, 100), true, formationLeader, true)!.Value;
Check(formation.LeaderDistance == 40 && formation.UseSecondary && !formation.Rejoining,
    "dead P1 is excluded and surviving P2 uses arrows toward leader rather than corpse");
formation = FollowPlan.Resolve(new(float.NaN, float.NaN), false, new(70, 100), true, formationLeader, false)!.Value;
Check(formation.LeaderDistance == 30 && !formationSession.NeedsMovement(formation.LeaderDistance, 30, 33),
    "missing or invalid corpse render cannot prevent living P2 stopping at leader radius");
formation = FollowPlan.Resolve(new(60, 100), true, new(float.NaN, float.NaN), false, formationLeader, false)!.Value;
Check(formation.LeaderDistance == 40 && !formation.UseSecondary,
    "single-player following and living P1 need no secondary position");
Check(FollowPlan.Resolve(formationLeader, false, formationLeader, false, formationLeader, true) == null,
    "both followers dead provide no actor or movement plan");
formation = FollowPlan.Resolve(new(60, 100), true, new(float.NaN, 100), true, formationLeader, false)!.Value;
Check(!float.IsFinite(formation.LeaderDistance), "invalid living position cannot be silently excluded from nearest distance");
formation = FollowPlan.Resolve(new(80, 100), true, new(20, 100), true, formationLeader, true)!.Value;
Check(formation.UseSecondary && formation.Rejoining && formation.LeaderDistance == 20,
    "P2 reunion remains a separate phase even with P1 already in leader radius");

// A death/revival phase releases the old mapping before issuing the new one.
var survivorEvents = new List<(ushort Key, bool Down)>();
var survivorArrows = false;
var survivorLease = new KeyLease((key, down) =>
{
    survivorEvents.Add(((ushort)MovementBindings.Get(key, survivorArrows).VirtualKey, down));
    return true;
});
var survivorRun = new FollowRunState();
survivorRun.Start();
survivorLease.Renew(MoveKeys.Up, 0, 150);
formation = FollowPlan.Resolve(formationLeader, false, new(50, 100), true, formationLeader, false)!.Value;
Check(survivorLease.TryReset(() => survivorArrows = formation.UseSecondary) && survivorRun.Ready(75),
    "P1 death switches to P2 without revoking the start command");
survivorLease.Renew(MoveKeys.Up, 75, 150);
Check(survivorEvents.SequenceEqual(new[] { ((ushort)0x57, true), ((ushort)0x57, false), ((ushort)0x26, true) }),
    "P1 death releases shared W before P2 arrow down");
survivorLease.Expire(225, true);
Check(survivorLease.Held == MoveKeys.None && survivorEvents[^1] == ((ushort)0x26, false),
    "surviving P2 still releases on the 150 ms stale-input boundary");
survivorRun.Fail("players_dead", 250);
Check(survivorRun.IsEnabled && !survivorRun.Ready(499) && survivorRun.Ready(500),
    "both dead wait and retry rather than disabling Follow");
formation = FollowPlan.Resolve(new(50, 100), true, new(70, 100), true, formationLeader, false)!.Value;
formationSession.Reset();
Check(!formationSession.NeedsMovement(formation.LeaderDistance, 30, 33) &&
    survivorLease.TryReset(() => survivorArrows = formation.UseSecondary) && !survivorArrows,
    "revival restores shared mapping and respects revived P2 at the stop radius");
survivorRun.Stop("stopped");
Check(!survivorRun.Ready(1000), "revival after an explicit stop cannot restart Follow");

Check(!session.IsStuck(new(2, 2), true, 100, 2500), "start progress monitor");
Check(session.IsStuck(new(2, 2), true, 2600, 2500), "stationary movement times out");
Check(!session.IsStuck(new(5, 2), true, 2601, 2500), "movement resets progress timer");
Check(!session.IsStuck(new(5, 2), false, 9000, 2500), "waiting for route is not stuck movement");
Check(!session.IsStuck(new(5, 2), true, 9001, 2500), "resume starts fresh progress timer");
session.Reset();
Check(!session.NeedsMovement(20, 18, 25), "target/area reset clears hysteresis");
var settings = new FollowSettings { StopDistance = float.NaN, ResumeDistance = float.PositiveInfinity, Clearance = -1, RepathMilliseconds = 0 };
settings.Normalize();
Check(settings.StopDistance == 18 && settings.ResumeDistance > 18 && settings.Clearance == 0 && settings.RepathMilliseconds == 150, "invalid settings normalize");
Check(new Bloodybot2.Configuration.BotConfig().Preview, "new install defaults to preview without input");
Check(EscapePlanner.CombinedPercent(600, 1000, 0, 2000) == 20, "Life and ES are weighted by total capacity rather than averaging percentages");
Check(EscapePlanner.CombinedPercent(500, 1000, 500, 1000) == 50 && EscapePlanner.CombinedPercent(500, 1000, 499, 1000) < 50,
    "combined pool preserves the strict 50 percent boundary");
Check(EscapePlanner.CombinedPercent(499, 1000, 0, 0) == 49.9f, "characters without ES use Life alone");
Check(EscapePlanner.CombinedPercent(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) == 100,
    "combined pool cannot overflow int when capacities are added");
Check(EscapePlanner.CombinedPercent(1, 0, 0, 0) == null && EscapePlanner.CombinedPercent(1, 100, -1, 100) == null,
    "unknown Life or invalid ES must wait instead of guessing health");
Check(EscapePlanner.UseSecondary(45, true, 30, true) && !EscapePlanner.UseSecondary(30, true, 45, true),
    "lowest living combined pool chooses P2 arrows or shared P1 WASD");
Check(!EscapePlanner.UseSecondary(30, true, 30, true), "equal low health prefers P1 consistently");
Check(!EscapePlanner.UseSecondary(60, true, 0, false) && EscapePlanner.UseSecondary(0, false, 60, true),
    "corpses cannot win emergency actor selection");
var grouped = EscapePlanner.Cluster([new(10, 10), new(13, 10), new(16, 10), new(40, 10), new(42, 10)], 3);
Check(grouped.Count == 2 && grouped[0].Members.Length == 3 && grouped[0].Center == new Vector2(13, 10) && grouped[1].Center == new Vector2(41, 10),
    "transitive nearby rares form separate clusters with individual centers");
Check(EscapePlanner.Cluster([new(13, 10), new(10, 10), new(13, 10), new(float.NaN, 10)], 3).Single().Center == new Vector2(11.5f, 10),
    "cluster geometry is deterministic and ignores duplicate/invalid positions");
var escapeBytes = Map(60, 60, (_, _) => true);
var escapeGrid = new NavigationGrid(escapeBytes, 30, 0, [], []);
var escapePlayer = new Vector2(20, 20);
Vector2[] nearbyRares = [new(24, 20), new(26, 20)];
var escape = EscapePlanner.Plan(escapeGrid, escapePlayer, nearbyRares, 15, 10, maxNodes: 20000, maxMilliseconds: 1000);
Check(escape.Center == new Vector2(25, 20) && escape.ClusterSize == 2 && escape.Target is Vector2 escapeGoal && escapeGoal.X < 20,
    "emergency goal moves away from nearest rare cluster center");
Check(escape.Route != null && EscapePlanner.NearestDistance(escape.Route[^1], nearbyRares) >= 10 &&
    escape.Route.Zip(escape.Route.Skip(1)).All(p => EscapePlanner.SafeSegment(escapeGrid, p.First, p.Second, nearbyRares)),
    "escape route reaches configured separation from every rare without crossing a pack");
var retreatAim = EscapePlanner.Steer(escapeGrid, escapePlayer, escape.Route!, nearbyRares);
Check(retreatAim.HasValue && Steering.Choose(retreatAim.Value - escapePlayer, Vector2.UnitX, Vector2.UnitY,
    step => EscapePlanner.SafeSegment(escapeGrid, escapePlayer, escapePlayer + step, nearbyRares)) == MoveKeys.Left,
    "escape path projects to movement away from the pack");
Vector2[] twoPacks = [new(10, 20), new(30, 20)];
escape = EscapePlanner.Plan(escapeGrid, escapePlayer, twoPacks, 5, 15, maxNodes: 20000, maxMilliseconds: 1000);
Check(escape.ClusterSize == 1 && escape.Target.HasValue && MathF.Abs(escape.Target.Value.Y - 20) > 5 &&
    EscapePlanner.NearestDistance(escape.Target.Value, twoPacks) >= 15,
    "separate packs choose a sideways exit instead of their misleading global midpoint");
Vector2[] centeredPack = [new(18, 20), new(22, 20)];
escape = EscapePlanner.Plan(escapeGrid, escapePlayer, centeredPack, 8, 12, maxNodes: 20000, maxMilliseconds: 1000);
Check(escape.Center == escapePlayer && escape.Target.HasValue && float.IsFinite(escape.Target.Value.X + escape.Target.Value.Y) &&
    EscapePlanner.NearestDistance(escape.Target.Value, centeredPack) >= 12,
    "standing at pack centroid still finds a finite reachable escape direction");
var escapeWall = new NavigationGrid(Map(60, 60, (x, y) => x != 17 || y < 12 || y > 28), 30, 0, [], []);
escape = EscapePlanner.Plan(escapeWall, escapePlayer, nearbyRares, 15, 10, maxNodes: 20000, maxMilliseconds: 1000);
Check(escape.Route != null && escape.Route.Zip(escape.Route.Skip(1)).All(p => escapeWall.Clear(p.First, p.Second)) &&
    EscapePlanner.NearestDistance(escape.Route[^1], nearbyRares) >= 10,
    "blocked radial escape tries a reachable side route without walking through the wall");
var enclosed = new NavigationGrid(Map(60, 60, (x, y) => x == 20 && y == 20), 30, 0, [], []);
Check(EscapePlanner.Plan(enclosed, escapePlayer, nearbyRares, 15, 10, maxMilliseconds: 1000) is { Safe: false, Route: null },
    "enclosed low-health player waits instead of blindly pushing toward monsters");
Check(!EscapePlanner.SafeSegment(escapeGrid, new(15, 20), new(25, 20), [new(20, 20)]),
    "safe endpoints cannot hide a segment crossing through the pack");
Check(EscapePlanner.Plan(escapeGrid, new(5, 20), nearbyRares, 15, 10).Safe &&
    EscapePlanner.Plan(escapeGrid, escapePlayer, [], 15, 10).Safe,
    "already separated or absent rares stop escape movement while health remains low");
Check(EscapePlanner.Plan(escapeGrid, escapePlayer, nearbyRares, 15, 10, maxNodes: 0) is { Limited: true, Route: null },
    "zero search budget waits without assuming a route exists");
var partialEscape = EscapePlanner.Plan(escapeGrid, escapePlayer, nearbyRares, 15, 30, maxNodes: 2, maxMilliseconds: 1000);
Check(partialEscape is { Limited: true, Route: not null } &&
    EscapePlanner.NearestDistance(partialEscape.Route[^1], nearbyRares) > EscapePlanner.NearestDistance(escapePlayer, nearbyRares),
    "bounded escape search can advance to a safer intermediate position and retry");
Check(EscapePlanner.Plan(escapeGrid, escapePlayer, [new(float.NaN, 20)], 15, 10).Route == null,
    "invalid threat geometry never emits an escape route");
Check(!new FollowSettings().EscapeLowHealth && new FollowSettings().EscapeDistance == 30, "old settings keep low-health retreat opt-in");

// Role selection must not assume that Area.Player always denotes P1 or P2.
var p1 = new PlayerIdentity(101, 0x1000, "Primary");
var p2 = new PlayerIdentity(102, 0x2000, "Secondary");
var leader = new PlayerIdentity(104, 0x4000, "Leader");
var stranger = new PlayerIdentity(103, 0x3000, "Other");
Check(ParticipantSelection.ResolveControlled([p1, p2], p1, true, "Primary", "Secondary", out var emergencyFailure) is
    { Primary: var emergencyPrimary, Secondary: var emergencySecondary } && emergencyPrimary == p1 && emergencySecondary == p2 && emergencyFailure == "",
    "low-health actors resolve without a visible leader");
Check(ParticipantSelection.ResolveControlled([p1, p2], p1, false, "", "", out emergencyFailure)?.Primary == p1,
    "single-player emergency uses local character without leader lookup");
Check(ParticipantSelection.ResolveControlled([p1], p1, true, "Primary", "Secondary", out emergencyFailure) == null && emergencyFailure == "secondary_invalid",
    "missing configured P2 waits instead of choosing a stranger for escape");
PlayerIdentity[] nearby = [stranger, p2, p1, p1, leader];
foreach (var local in new[] { p1, p2 })
{
    var roles = ParticipantSelection.Resolve(nearby, local, true, "Leader", "Primary", "Secondary", out var reason);
    Check(roles == new FollowParticipants(p1, leader, p2) && reason == string.Empty,
        $"explicit co-op roles survive extra players, duplicate local entry, and local ID {local.Id}");
}
Check(ParticipantSelection.Resolve(nearby, p2, false, "Leader", "", "", out _) == new FollowParticipants(p2, leader),
    "legacy mode still controls the local character and follows the selected leader");
Check(ParticipantSelection.Resolve(nearby, p1, false, "Primary", "", "", out _) == null,
    "legacy mode cannot follow itself");
Check(ParticipantSelection.Resolve(nearby, p1, true, "Leader", "Primary", "", out var selectionReason) == null && selectionReason == "follower_name",
    "co-op requires explicit P2 selection instead of choosing the first other player");
Check(ParticipantSelection.Resolve(nearby, p1, true, "Leader", "Primary", "primary", out selectionReason) == null && selectionReason == "same_player",
    "same P1/P2 name is rejected case-insensitively");
Check(ParticipantSelection.Resolve([p1, stranger, leader], p1, true, "Leader", "Primary", "Secondary", out selectionReason) == null && selectionReason == "secondary_invalid",
    "missing P2 does not fall back to the local character or a stranger");
Check(ParticipantSelection.Resolve([p2, stranger, leader], p2, true, "Leader", "Primary", "Secondary", out selectionReason) == null && selectionReason == "player_invalid",
    "missing P1 does not fall back to another player");
Check(ParticipantSelection.Resolve([p1, p2, new(105, 0x5000, "Secondary"), leader], p1, true, "Leader", "Primary", "Secondary", out _) == null,
    "ambiguous P2 name is rejected");
Check(ParticipantSelection.Resolve([p1, p2, new(105, 0x5000, "Primary"), leader], p1, true, "Leader", "Primary", "Secondary", out _) == null,
    "ambiguous P1 name is rejected");
Check(ParticipantSelection.Resolve(nearby, p1, true, "leader", "PRIMARY", "SECONDARY", out _) == new FollowParticipants(p1, leader, p2),
    "saved names match case-insensitively");
Check(ParticipantSelection.Resolve([p1, p2], p1, true, "Primary", "Primary", "Secondary", out _) == new FollowParticipants(p1, p1, p2),
    "P1 can also be the leader for two-character use");
Check(ParticipantSelection.Resolve([p1, p2], p1, true, "Leader", "Primary", "Secondary", out _) == null,
    "missing group leader does not silently become P1");

var coordinator = new CoopCoordinator();
var fixedP1 = new Vector2(100, 100);
Check(!coordinator.Update(fixedP1, new(100, 70), true, 35, 12), "shared WASD continues before the P1/P2 gap threshold");
Check(coordinator.Update(fixedP1, new(100, 65), true, 35, 12), "gap of 35 starts P2 correction at the threshold");
Check(coordinator.Update(fixedP1, new(100, 80), true, 35, 12), "correction persists in gap hysteresis band");
Check(coordinator.Update(fixedP1, new(100, 90), false, 35, 12), "wall between nearby players prevents premature reunion");
Check(!coordinator.Update(fixedP1, new(100, 88), true, 35, 12),
    "P2 rejoining within 12 ends correction independently of how far the leader is");
Check(!coordinator.Update(fixedP1, new(100, 75), true, 35, 12), "reunited players do not oscillate in the hysteresis band");
Check(coordinator.Update(fixedP1, new(100, 140), true, 35, 12), "large P1/P2 separation corrects even when P2 is ahead");
coordinator.Reset();
Check(!coordinator.CorrectingP2 && !coordinator.Update(fixedP1, new(100, 75), true, 35, 12),
    "area/stop reset clears correction phase");
session.Reset();
Check(session.NeedsMovement(40, 18, 25), "normal leader following begins before correction");
session.IsStuck(new(2, 2), true, 0, 2500);
session.ResetProgress();
Check(session.NeedsMovement(20, 18, 25) && !session.IsStuck(new(2, 2), true, 9000, 2500),
    "phase change preserves leader-distance hysteresis but resets actor progress tracking");

foreach (var (direction, vk, scan) in new[]
{
    (MoveKeys.Up, 0x26, 0x48), (MoveKeys.Left, 0x25, 0x4B),
    (MoveKeys.Down, 0x28, 0x50), (MoveKeys.Right, 0x27, 0x4D),
})
{
    var binding = MovementBindings.Get(direction, true);
    Check(binding.VirtualKey == vk && binding.Scan == scan && binding.Flags(true) == 0x09 && binding.Flags(false) == 0x0B,
        $"{direction} emits extended arrow scan code on both down and up, not a numpad key");
    var wasd = MovementBindings.Get(direction, false);
    Check(!wasd.Extended && wasd.Flags(true) == 0x08 && wasd.Flags(false) == 0x0A && wasd.Scan != binding.Scan,
        $"legacy {direction} retains non-extended WASD mapping");
}
Check(MovementBindings.Describe(MoveKeys.Up | MoveKeys.Right, true) == "↑ + →",
    "co-op preview displays arrow combination rather than WASD");
var manualMovement = new ManualMovementTracker();
Check(manualMovement.Read(MoveKeys.None, true, true, 0, key => key is 0x57 or 0x41 or 0x53 or 0x44) == "W + A + S + D",
    "manual WASD during P2 correction stops conflicting input");
Check(manualMovement.Read(MoveKeys.Up, false, true, 0, key => key == 0x26) == "↑",
    "manual arrow during shared WASD stops conflicting input");
Check(manualMovement.Read(MoveKeys.Up, false, true, 0, key => key == 0x57) == "",
    "owned shared WASD does not trigger manual takeover");
Check(manualMovement.Read(MoveKeys.Up, true, false, 0, key => key is 0x26 or 0x25) == "←",
    "additional manual arrow stops P2 while its owned arrow is ignored");
Check(manualMovement.Read(MoveKeys.Up, true, false, 0, key => key == 0x26) == "",
    "owned P2 arrow does not self-trigger manual takeover");
Check(manualMovement.Read(MoveKeys.None, false, false, 0, key => key == 0x57) == "W",
    "legacy mode still stops on manual WASD");
Check(manualMovement.Read(MoveKeys.None, false, false, 0, key => key == 0x26) == "",
    "legacy mode does not treat unrelated arrow input as manual WASD");

var startupKeys = new ManualMovementTracker();
startupKeys.CaptureBaseline(0, key => key == 0x57);
Check(startupKeys.Read(MoveKeys.None, false, true, 1, key => key == 0x57) == "",
    "stale W already down at startup does not immediately pause the newly started session");
Check(startupKeys.Read(MoveKeys.None, false, true, 3000, key => key == 0x57) == "",
    "persistent baseline W cannot create an endless manual-input wait");
Check(startupKeys.Read(MoveKeys.None, false, true, 3001, key => key is 0x57 or 0x41) == "A",
    "ignoring stale W does not hide a newly pressed A and reports the exact trigger");
startupKeys.Read(MoveKeys.None, false, true, 3002, _ => false);
Check(startupKeys.Read(MoveKeys.None, false, true, 3003, key => key == 0x57) == "W",
    "baseline W rearms after a real up observation so a new press still pauses");

var injectedKeys = new ManualMovementTracker();
injectedKeys.RecordSynthetic(0x57, 0);
injectedKeys.Read(MoveKeys.None, false, true, 50, _ => false);
Check(injectedKeys.Read(MoveKeys.None, false, true, 100, key => key == 0x57) == "",
    "early up state cannot make a delayed injected W look like a manual press");
injectedKeys.RecordSynthetic(0x57, 150); // Key-up accepted, but Windows still reports W down.
Check(injectedKeys.Read(MoveKeys.None, true, true, 160, key => key == 0x57) == "",
    "WASD-to-arrows switch keeps the old W release excluded from manual detection");
Check(injectedKeys.Read(MoveKeys.None, true, true, 900, key => key == 0x57) == "",
    "synthetic release remains excluded until Windows acknowledges up, even after the delay");
Check(injectedKeys.Read(MoveKeys.None, true, true, 901, key => key is 0x57 or 0x28) == "↓",
    "a real additional arrow remains detectable while a synthetic W release is pending");
injectedKeys.Read(MoveKeys.None, true, true, 902, _ => false);
Check(injectedKeys.Read(MoveKeys.None, true, true, 903, key => key == 0x57) == "W",
    "physical W can take over again after the injected release is acknowledged");
injectedKeys.RecordSynthetic(0x26, 1000);
injectedKeys.CaptureBaseline(1050, _ => false);
Check(injectedKeys.Read(MoveKeys.None, false, true, 1100, key => key == 0x26) == "",
    "restarting follow does not forget a pending synthetic arrow release");

var watchdogKeys = new ManualMovementTracker();
var simulatedNow = 0L;
var observedDown = new HashSet<int>();
var watchdogLease = new KeyLease((direction, down) =>
{
    var vk = MovementBindings.Get(direction, false).VirtualKey;
    watchdogKeys.RecordSynthetic(vk, simulatedNow);
    if (down) observedDown.Add(vk); // Deliberately delay the OS's key-up state update.
    return true;
});
watchdogLease.Renew(MoveKeys.Up, simulatedNow);
simulatedNow = 200;
watchdogLease.Expire(simulatedNow, true);
Check(watchdogLease.Held == MoveKeys.None &&
    watchdogKeys.Read(watchdogLease.Held, false, true, 201, observedDown.Contains) == "",
    "watchdog expiry with a delayed OS key-up does not self-trigger manual input");
observedDown.Clear();
watchdogKeys.Read(watchdogLease.Held, false, true, 500, observedDown.Contains);
observedDown.Add(0x57);
Check(watchdogKeys.Read(watchdogLease.Held, false, true, 501, observedDown.Contains) == "W",
    "manual W detection recovers after a watchdog release settles");

var arrowMode = false;
var keyEvents = new List<(ushort Scan, uint Flags)>();
var releaseBlocked = false;
var mappedLease = new KeyLease((direction, down) =>
{
    var binding = MovementBindings.Get(direction, arrowMode);
    keyEvents.Add((binding.Scan, binding.Flags(down)));
    return down || !releaseBlocked;
});
mappedLease.Renew(MoveKeys.Up, 0);
Check(mappedLease.TryReset(() => arrowMode = true) && keyEvents[^1] == ((ushort)0x11, 0x0Au),
    "changing to arrows releases the old W using its original scan code");
keyEvents.Clear();
mappedLease.Renew(MoveKeys.Up | MoveKeys.Right, 1);
mappedLease.Expire(201, true);
Check(keyEvents.SequenceEqual(new[] { ((ushort)0x48, 0x09u), ((ushort)0x4D, 0x09u), ((ushort)0x48, 0x0Bu), ((ushort)0x4D, 0x0Bu) }),
    "P2 diagonal lease expiry releases both extended arrows and never sends WASD");
mappedLease.Renew(MoveKeys.Down, 300);
releaseBlocked = true;
Check(!mappedLease.TryReset(() => arrowMode = false) && arrowMode && mappedLease.Held == MoveKeys.Down,
    "failed arrow release prevents mapping switch and retains ownership");
releaseBlocked = false;
mappedLease.Expire(301, false);
Check(mappedLease.Held == MoveKeys.None && keyEvents[^1] == ((ushort)0x50, 0x0Bu),
    "watchdog retries failed release with the original arrow mapping");
Check(mappedLease.TryReset(() => arrowMode = false) && !arrowMode,
    "mapping can change after release succeeds");

var legacySettings = new FollowSettings { LeaderName = " Leader " };
legacySettings.Normalize();
Check(!legacySettings.LocalCoopFollow && legacySettings.LeaderName == "Leader" && legacySettings.P1Name == "" && legacySettings.P2Name == "",
    "missing new configuration fields preserve legacy mode and leader");
var coopSettings = new FollowSettings { LocalCoopFollow = true, P1Name = " Leader ", P2Name = " Follower " };
coopSettings.Normalize();
Check(coopSettings.P1Name == "Leader" && coopSettings.P2Name == "Follower",
    "co-op names normalize");
Check(new[] { 0x25, 0x26, 0x27, 0x28, 0x41, 0x44, 0x53, 0x57 }.All(key => !Bloodybot2.Configuration.BotConfig.IsToggleKeyAllowed(key)),
    "all movement keys are excluded from start-stop hotkey selection");
coopSettings.P2LagDistance = 6;
coopSettings.P2RejoinDistance = float.PositiveInfinity;
coopSettings.Normalize();
Check(coopSettings.P2RejoinDistance == 3 && coopSettings.P2LagDistance == 6, "correction thresholds retain a non-overlapping gap after normalization");

foreach (var reason in new[] { "unfocused", "panel", "settings_open", "frame_gap", "manual", "area_changed",
    "game_state", "player_invalid", "secondary_invalid", "players_dead", "players_changed", "target_missing", "leader_unavailable", "follower_changed", "target_changed", "terrain_missing", "input_failed", "error" })
{
    var run = new FollowRunState();
    run.Start();
    run.Fail(reason, 100);
    Check(run.IsEnabled && run.Status == reason && !run.Ready(349) && run.Ready(350),
        reason + " releases into a timed wait without requiring a new start command");
    run.Fail(reason, 350);
    Check(run.IsEnabled && !run.Ready(599) && run.Ready(600), reason + " can wait repeatedly and automatically retry");
}
foreach (var reason in new[] { "leader_name", "primary_name", "follower_name", "same_player", "ambiguous_player" })
{
    var run = new FollowRunState();
    run.Start();
    run.Fail(reason, 0);
    Check(!run.IsEnabled && !run.Ready(long.MaxValue), reason + " still requires an explicit restart");
}
var stoppedRun = new FollowRunState();
stoppedRun.Start();
stoppedRun.Fail("unfocused", 0);
stoppedRun.Stop("stopped");
stoppedRun.Fail("area_changed", 2000);
Check(!stoppedRun.IsEnabled && stoppedRun.Status == "stopped" && !stoppedRun.Ready(9000),
    "manual stop while waiting is never undone by a later area event or timer");
stoppedRun.Start();
Check(stoppedRun.Ready(9000), "manual restart after a stop is allowed");
Check(ParticipantSelection.Resolve([p2, leader], p2, true, "Leader", "Primary", "Secondary", out selectionReason) == null &&
    selectionReason == "player_invalid" && !FollowRunState.IsTerminal(selectionReason), "missing P1 with a visible leader waits");
Check(ParticipantSelection.Resolve([p2], p2, true, "Leader", "Primary", "Secondary", out selectionReason) == null &&
    selectionReason == "target_missing" && !FollowRunState.IsTerminal(selectionReason),
    "missing leader waits even when P1 is also missing");
var lostLeaderRun = new FollowRunState();
lostLeaderRun.Start();
Check(ParticipantSelection.Resolve([p1, p2], p1, true, "Leader", "Primary", "Secondary", out selectionReason) == null,
    "a transient leader disappearance has no follow target");
lostLeaderRun.Fail(selectionReason, 1000);
Check(lostLeaderRun.IsEnabled && !lostLeaderRun.Ready(1249), "leader loss keeps start but delays the next scan");
Check(ParticipantSelection.Resolve([p1, p2, leader], p1, true, "Leader", "Primary", "Secondary", out selectionReason) != null &&
    lostLeaderRun.Ready(1250), "leader reappearance allows following without a second Start");
lostLeaderRun.Fail("target_missing", 1300);
lostLeaderRun.Stop("stopped");
lostLeaderRun.Fail("target_missing", 2000);
Check(!lostLeaderRun.IsEnabled && !lostLeaderRun.Ready(long.MaxValue) && lostLeaderRun.Status == "stopped",
    "explicit stop while the leader is missing prevents automatic resume");
Check(ParticipantSelection.Resolve([p1, p2, leader, new(105, 0x5000, "Primary")], p1, true, "Leader", "Primary", "Secondary", out selectionReason) == null &&
    selectionReason == "ambiguous_player" && FollowRunState.IsTerminal(selectionReason), "ambiguous role selection is a configuration stop");

// Recovery uses fresh synthetic 100/75 ms frames; long timestamp jumps must not
// be mistaken for two seconds of actual movement attempts.
var stuckPosition = new Vector2(5, 5);
var visibleTarget = new Vector2(15, 5);
var inaccurateTerrain = Grid(Map(20, 20, (_, _) => false));
var forcedDirection = Steering.Choose(visibleTarget - stuckPosition, Vector2.UnitX, Vector2.UnitY, _ => true);
var recovery = new UnstuckRecovery(new Random(17));
Check(!recovery.BeginIfNeeded(stuckPosition, false, false) && !recovery.Active,
    "pending or budget-exhausted search alone does not trigger blind movement");
Check(recovery.BeginIfNeeded(stuckPosition,
    inaccurateTerrain.SearchPath(stuckPosition, visibleTarget, default).Status == PathSearchStatus.StartBlocked, false),
    "blocked real start immediately begins direct recovery without snapping the actor across a wall");
var firstProbe = recovery.Advance(stuckPosition, 0, forcedDirection);
Check(firstProbe.Keys == MoveKeys.Right && firstProbe.LeaseMilliseconds == 150,
    "all terrain directions blocked still permits a short-lease direct probe");
Check(!recovery.BeginIfNeeded(stuckPosition, true, true), "repeated path failures do not restart an active recovery");
for (var at = 100; at < 2000; at += 100)
{
    var step = recovery.Advance(stuckPosition, at, forcedDirection);
    Check((step.Keys is MoveKeys.None or MoveKeys.Right) && !recovery.Exploring,
        "direct attempts persist before two seconds despite pulse rests: " + at);
}
Check(recovery.Advance(stuckPosition, 2000, forcedDirection).Action == RecoveryAction.Wait && recovery.Exploring,
    "two seconds without displacement releases forward input and enters exploration");
recovery.Advance(stuckPosition, 2100, forcedDirection);
var sideProbe = recovery.Advance(stuckPosition, 2200, forcedDirection);
Check(sideProbe.Keys is MoveKeys.Up or MoveKeys.Down,
    "the first randomized exploration tier is sideways relative to the target");

// A repath at the same spot must not spend another two seconds on the failed direction.
recovery.Begin(stuckPosition);
Check(recovery.Advance(stuckPosition, 2300, forcedDirection).Action == RecoveryAction.Wait && recovery.Exploring,
    "a remembered local failure skips the next direct attempt");
recovery.Advance(stuckPosition, 2400, forcedDirection);
Check(recovery.Advance(stuckPosition, 2500, forcedDirection).Keys is MoveKeys.Up or MoveKeys.Down,
    "remembered forward failure still allows alternate movement");
recovery.Begin(stuckPosition);
Check(recovery.Advance(stuckPosition, 13000, forcedDirection).Keys == MoveKeys.Right && !recovery.Exploring,
    "local failure memory expires instead of permanently banning a direction");

recovery.Reset();
recovery.BeginIfNeeded(stuckPosition, false, true);
var triedDirections = new List<MoveKeys>();
var previousKeys = MoveKeys.None;
for (var at = 0; at <= 6500; at += 100)
{
    var step = recovery.Advance(stuckPosition, at, MoveKeys.Right);
    if (step.Keys != MoveKeys.None && step.Keys != previousKeys) triedDirections.Add(step.Keys);
    previousKeys = step.Keys;
}
Check(triedDirections.Count == 7 && triedDirections.Distinct().Count() == 7 && !triedDirections.Contains(MoveKeys.Right),
    "stationary exploration tries all seven alternatives once, excluding the already failed forward direction");
Check(recovery.Active && recovery.Advance(stuckPosition, 6600, MoveKeys.Right).Action == RecoveryAction.Wait,
    "exhausted directions rest without disabling follow or immediately retrying a failed direction");
recovery.Begin(new(30, 5));
Check(recovery.Advance(new(30, 5), 6700, MoveKeys.Right).Keys == MoveKeys.Right && !recovery.Exploring,
    "failure memory is local and is discarded after leaving the corner");
recovery.Reset();
recovery.Begin(stuckPosition);
Check(recovery.Advance(stuckPosition, 6800, MoveKeys.Right).Keys == MoveKeys.Right,
    "actor, target, area and explicit stop reset old failure memory");

Vector2 KeyDelta(MoveKeys keys) => new(
    ((keys & MoveKeys.Right) != 0 ? 1 : 0) - ((keys & MoveKeys.Left) != 0 ? 1 : 0),
    ((keys & MoveKeys.Down) != 0 ? 1 : 0) - ((keys & MoveKeys.Up) != 0 ? 1 : 0));
recovery.Reset();
recovery.BeginIfNeeded(stuckPosition, false, true);
var escapingSide = recovery.Advance(stuckPosition, 0, MoveKeys.Right).Keys;
var escapeDelta = KeyDelta(escapingSide);
Check(escapingSide is MoveKeys.Up or MoveKeys.Down, "a known stalled route starts with an alternate direction");
for (var at = 100; at <= 1500; at += 100)
{
    var step = recovery.Advance(stuckPosition + escapeDelta * (at / 200f), at, MoveKeys.Right, escapingSide);
    Check(recovery.Active && recovery.Exploring && (step.Keys == escapingSide || step.Keys == MoveKeys.None),
        "an available route does not interrupt a productive side-step before eight grids: " + at);
}
Check(recovery.Advance(stuckPosition + escapeDelta * 8, 1600, MoveKeys.Right, escapingSide).Keys == escapingSide &&
    recovery.Active && !recovery.Exploring, "after escaping eight grids a fresh route is tried but not yet declared successful");
recovery.Advance(stuckPosition + escapeDelta * 9.5f, 1700, MoveKeys.Right, escapingSide);
recovery.Advance(stuckPosition + escapeDelta * 11, 1800, MoveKeys.Right, escapingSide);
Check(recovery.Advance(stuckPosition + escapeDelta * 12.5f, 1900, MoveKeys.Right, escapingSide).Action == RecoveryAction.Repath &&
    !recovery.Active, "a route with 300 ms and four grids of real progress ends recovery");

// Merely moving eight grids, or merely finding a route, cannot reset the recovery.
recovery.Reset();
recovery.Begin(stuckPosition);
for (var at = 0; at <= 1600; at += 100)
    recovery.Advance(stuckPosition + new Vector2(at / 100f, 0), at, MoveKeys.Right);
Check(recovery.Active, "displacement alone does not end recovery while routing remains unavailable");
recovery.Reset();
recovery.Begin(stuckPosition);
for (var at = 0; at < 2000; at += 100)
    recovery.Advance(stuckPosition, at, MoveKeys.Right, MoveKeys.Right);
Check(recovery.Active && recovery.Advance(stuckPosition, 2000, MoveKeys.Right, MoveKeys.Right).Action == RecoveryAction.Wait &&
    recovery.Exploring, "a repeatedly found route that cannot move the actor still escalates to exploration");

recovery.Reset();
recovery.Begin(stuckPosition);
for (var at = 0; at <= 1000; at += 100) recovery.Advance(stuckPosition, at, MoveKeys.Right);
recovery.Pause();
Check(recovery.Advance(stuckPosition, 10000, MoveKeys.Right).Keys == MoveKeys.Right && !recovery.Exploring,
    "a long focus/panel suspension does not count as two seconds of attempted movement");
for (var at = 10100; at < 11000; at += 100) recovery.Advance(stuckPosition, at, MoveKeys.Right);
Check(recovery.Advance(stuckPosition, 11000, MoveKeys.Right).Action == RecoveryAction.Wait && recovery.Exploring,
    "pausing excludes idle time without forgetting the earlier one second of stall evidence");

recovery.Reset();
recovery.Begin(stuckPosition);
recovery.Advance(stuckPosition, 0, MoveKeys.Right);
Check(recovery.Advance(stuckPosition, 1000, MoveKeys.Right).Action == RecoveryAction.Wait && !recovery.Exploring,
    "an unobserved frame gap releases recovery rather than counting stale time or sending a key");
Check(recovery.Advance(stuckPosition, 1100, MoveKeys.Left).Keys == MoveKeys.Left,
    "fresh input after a gap follows the current target direction");

recovery.Reset();
recovery.Begin(stuckPosition);
recovery.Advance(stuckPosition, 0, MoveKeys.Right);
Check(recovery.Advance(stuckPosition, 100, MoveKeys.Left).Action == RecoveryAction.Wait,
    "target changing sides releases the old direct direction first");
recovery.Advance(stuckPosition, 200, MoveKeys.Left);
Check(recovery.Advance(stuckPosition, 300, MoveKeys.Left).Keys == MoveKeys.Left,
    "the next direct pulse follows the updated target");
Check(recovery.Advance(new(float.NaN, 5), 400, MoveKeys.Left).Action == RecoveryAction.Wait,
    "invalid actor position never sends forced input");
var invalidDirection = Steering.Choose(Vector2.UnitX, Vector2.Zero, Vector2.Zero, _ => true);
Check(recovery.Advance(stuckPosition, 500, invalidDirection).Action == RecoveryAction.Wait,
    "invalid screen projection never falls back to a guessed screen direction");
recovery.Reset();
recovery.Begin(stuckPosition);
var isometricDirection = Steering.Choose(new(1, -1), new(1, 0.5f), new(-1, 0.5f), _ => true);
Check(recovery.Advance(stuckPosition, 0, isometricDirection).Keys == MoveKeys.Right,
    "initial target direction uses the actor's isometric projection");

var longAdvanceEvents = new List<(MoveKeys, bool)>();
var longAdvanceLease = new KeyLease((key, down) => { longAdvanceEvents.Add((key, down)); return true; });
recovery.Reset();
recovery.Begin(stuckPosition);
for (var at = 0; at <= 600; at += 75)
{
    longAdvanceLease.Expire(at, true);
    var step = recovery.Advance(stuckPosition + new Vector2(at / 100f, 0), at, MoveKeys.Right);
    if (step.Action == RecoveryAction.Press) longAdvanceLease.Renew(step.Keys, at, step.LeaseMilliseconds);
    else longAdvanceLease.Stop();
    Check(longAdvanceLease.Held == (at < 600 ? MoveKeys.Right : MoveKeys.None),
        "fresh frames sustain a bounded 600 ms pulse: " + at);
}
Check(longAdvanceEvents.SequenceEqual(new[] { (MoveKeys.Right, true), (MoveKeys.Right, false) }),
    "long advances hold continuously rather than repeating key-down events");
foreach (var useArrows in new[] { false, true })
{
    var sent = new List<MovementBinding>();
    var probeLease = new KeyLease((key, _) => { sent.Add(MovementBindings.Get(key, useArrows)); return true; });
    recovery.Reset();
    recovery.BeginIfNeeded(stuckPosition, false, true);
    var probe = recovery.Advance(stuckPosition, 0, MoveKeys.Up);
    probeLease.Renew(probe.Keys, 0, probe.LeaseMilliseconds);
    probeLease.Expire(150, true);
    Check(probeLease.Held == MoveKeys.None && sent.Count == 2 && sent.All(binding => binding.Extended == useArrows),
        useArrows ? "P2 exploratory presses use arrows only and expire at 150 ms" : "shared exploratory presses use WASD only and expire at 150 ms");
}
recovery.Reset();
Check(!recovery.Active && recovery.Advance(stuckPosition, 0, MoveKeys.Right).Action == RecoveryAction.Repath,
    "stop/range/actor reset cancels all old exploratory commands");
var combatProgress = new FollowSession();
Check(!combatProgress.IsStuck(Vector2.Zero, true, 0, 2500), "combat progress starts measuring movement");
combatProgress.PauseProgress(330);
Check(!combatProgress.IsStuck(Vector2.Zero, true, 2829, 2500) && combatProgress.IsStuck(Vector2.Zero, true, 2830, 2500),
    "intentional cast pause is excluded without forgetting earlier movement stalls");
combatProgress.ResetProgress();
combatProgress.IsStuck(Vector2.Zero, true, 0, 2500);
combatProgress.PauseProgress(330);
combatProgress.PauseProgress(330);
Check(!combatProgress.IsStuck(Vector2.Zero, true, 3159, 2500) && combatProgress.IsStuck(Vector2.Zero, true, 3160, 2500),
    "repeated casts cannot indefinitely suppress unstuck recovery");
Check(!combatProgress.IsStuck(new(2, 0), true, 3160, 2500), "real displacement after a cast resets stall evidence");
Console.WriteLine($"All {passed} Follow navigation checks passed. No Windows input or live gameplay tested.");
