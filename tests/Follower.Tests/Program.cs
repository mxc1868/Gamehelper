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
Check(!session.NeedsMovement(20, true, 18, 25), "idle inside hysteresis band");
Check(session.NeedsMovement(30, true, 18, 25), "resume outside distance");
Check(session.NeedsMovement(20, true, 18, 25), "continue within hysteresis band");
Check(!session.NeedsMovement(18, true, 18, 25), "stop at following distance");
Check(session.NeedsMovement(10, false, 18, 25), "nearby target behind wall still needs route");
session.Reset();
Check(!session.NeedsMovement(5, nearWallGrid.Clear(new(9, 2), new(9, 7)), 18, 25),
    "wall proximity alone does not prevent stopping within following distance");
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

// Role selection must not assume that Area.Player always denotes P1 or P2.
var p1 = new PlayerIdentity(101, 0x1000, "Primary");
var p2 = new PlayerIdentity(102, 0x2000, "Secondary");
var leader = new PlayerIdentity(104, 0x4000, "Leader");
var stranger = new PlayerIdentity(103, 0x3000, "Other");
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
Check(session.NeedsMovement(40, true, 18, 25), "normal leader following begins before correction");
session.IsStuck(new(2, 2), true, 0, 2500);
session.ResetProgress();
Check(session.NeedsMovement(20, true, 18, 25) && !session.IsStuck(new(2, 2), true, 9000, 2500),
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

var legacySettings = new FollowerSettings { LeaderName = " Leader " };
legacySettings.Normalize();
Check(!legacySettings.LocalCoopFollow && legacySettings.LeaderName == "Leader" && legacySettings.P1Name == "" && legacySettings.P2Name == "",
    "missing new configuration fields preserve legacy mode and leader");
var coopSettings = new FollowerSettings { LocalCoopFollow = true, P1Name = " Leader ", P2Name = " Follower ", ToggleKey = 0x26 };
coopSettings.Normalize();
Check(coopSettings.P1Name == "Leader" && coopSettings.P2Name == "Follower" && coopSettings.ToggleKey == FollowerSettings.DefaultToggleKey,
    "co-op names normalize and arrow hotkeys migrate to F6");
Check(new[] { 0x25, 0x26, 0x27, 0x28, 0x41, 0x44, 0x53, 0x57 }.All(key => !FollowerSettings.IsToggleKeyAllowed(key)),
    "all movement keys are excluded from start-stop hotkey selection");
coopSettings.P2LagDistance = 6;
coopSettings.P2RejoinDistance = float.PositiveInfinity;
coopSettings.Normalize();
Check(coopSettings.P2RejoinDistance == 3 && coopSettings.P2LagDistance == 6, "correction thresholds retain a non-overlapping gap after normalization");

foreach (var reason in new[] { "unfocused", "panel", "settings_open", "frame_gap", "manual", "area_changed",
    "game_state", "player_invalid", "secondary_invalid", "leader_unavailable", "follower_changed", "target_changed", "terrain_missing", "input_failed", "error" })
{
    var run = new FollowRunState();
    run.Start();
    run.Fail(reason, 100);
    Check(run.IsEnabled && run.Status == reason && !run.Ready(349) && run.Ready(350),
        reason + " releases into a timed wait without requiring a new start command");
    run.Fail(reason, 350);
    Check(run.IsEnabled && !run.Ready(599) && run.Ready(600), reason + " can wait repeatedly and automatically retry");
}
foreach (var reason in new[] { "target_missing", "leader_name", "primary_name", "follower_name", "same_player", "ambiguous_player" })
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
    selectionReason == "target_missing", "missing leader is not hidden by a simultaneously missing P1");
Check(ParticipantSelection.Resolve([p1, p2, leader, new(105, 0x5000, "Primary")], p1, true, "Leader", "Primary", "Secondary", out selectionReason) == null &&
    selectionReason == "ambiguous_player" && FollowRunState.IsTerminal(selectionReason), "ambiguous role selection is a configuration stop");

var noPathProgress = new FollowSession();
Check(!noPathProgress.IsStuck(new(5, 5), true, 0, 2500) &&
    !noPathProgress.IsStuck(new(5, 5), true, 2000, 2500) && noPathProgress.IsStuck(new(5, 5), true, 2500, 2500),
    "no-path and no-direction movement demand reaches recovery timeout without key output");
var recovery = new UnstuckRecovery();
recovery.Begin(new(5, 5));
var firstProbe = recovery.Advance(new(5, 5), 0, MoveKeys.Up);
Check(firstProbe.Action == RecoveryAction.Press && firstProbe.Keys == MoveKeys.Up && firstProbe.LeaseMilliseconds == 150,
    "recovery presses directly toward the target instead of choosing a side-step");
Check(recovery.Advance(new(5, 5), 100, MoveKeys.Up).LeaseMilliseconds == 50,
    "renewing a probe preserves its original deadline");
Check(recovery.Advance(new(5, 5), 150, MoveKeys.Up).Action == RecoveryAction.Wait &&
    recovery.Advance(new(5, 5), 269, MoveKeys.Up).Action == RecoveryAction.Wait,
    "probe release is followed by a rest interval");
Check(recovery.Advance(new(5, 5), 270, MoveKeys.Up).Keys == MoveKeys.Up,
    "stationary recovery retries toward the target rather than cycling away");
Check(recovery.Advance(new(6, 5), 280, MoveKeys.Up).Action == RecoveryAction.Repath && !recovery.Active,
    "one grid of actual displacement ends recovery and requests a fresh route");
recovery.Reset();
var inaccurateTerrain = Grid(Map(20, 20, (_, _) => false));
var stuckPosition = new Vector2(5, 5);
var visibleTarget = new Vector2(15, 5);
Check(inaccurateTerrain.FindPath(stuckPosition, visibleTarget, default) == null &&
    Steering.Choose(visibleTarget - stuckPosition, Vector2.UnitX, Vector2.UnitY,
        step => inaccurateTerrain.Clear(stuckPosition, stuckPosition + step)) == MoveKeys.None,
    "regression setup: blocked start makes both ordinary routing and steering refuse movement");
var forcedDirection = Steering.Choose(visibleTarget - stuckPosition, Vector2.UnitX, Vector2.UnitY, _ => true);
recovery.Begin(stuckPosition);
var forcedProbe = recovery.Advance(stuckPosition, 0, forcedDirection);
Check(forcedProbe.Action == RecoveryAction.Press && forcedProbe.Keys == MoveKeys.Right,
    "all terrain directions blocked still produces a forced press toward the visible target");
Check(recovery.Advance(stuckPosition, 20, MoveKeys.Left).Action == RecoveryAction.Wait,
    "target changing sides releases the old direction before a new press");
Check(recovery.Advance(stuckPosition, 140, MoveKeys.Left).Keys == MoveKeys.Left,
    "next press follows the target's updated direction");
recovery.Reset();
recovery.Begin(stuckPosition);
Check(recovery.Advance(new(float.NaN, 5), 0, MoveKeys.Right).Action == RecoveryAction.Wait,
    "unreadable position still prevents forced movement");
Check(recovery.Advance(stuckPosition, 0, MoveKeys.None).Action == RecoveryAction.Wait &&
    recovery.Advance(stuckPosition, 1000, MoveKeys.Right).Action == RecoveryAction.Press,
    "missing direction waits without spending attempts and resumes when direction recovers");
recovery.Reset();
recovery.Begin(new(5, 5));
var triedDirections = new List<MoveKeys>();
for (var trial = 0; trial < 8; trial++)
{
    var at = trial * 270;
    var probe = recovery.Advance(new(5, 5), at, MoveKeys.Up);
    triedDirections.Add(probe.Keys);
    Check(probe.Action == RecoveryAction.Press && recovery.Advance(new(5, 5), at + 150, MoveKeys.Up).Action == RecoveryAction.Wait,
        "stationary recovery probe " + trial + " always releases");
}
Check(triedDirections.Count == 8 && triedDirections.All(key => key == MoveKeys.Up), "one recovery round keeps all eight bounded attempts toward the target");
Check(recovery.Advance(new(5, 5), 2160, MoveKeys.Up).Action == RecoveryAction.Wait &&
    recovery.Advance(new(5, 5), 3160, MoveKeys.Up).Action == RecoveryAction.Repath && !recovery.Active,
    "failed full round rests then returns to navigation without disabling follow");
recovery.Begin(new(5, 5));
recovery.Reset();
Check(!recovery.Active && recovery.Advance(new(5, 5), 0, MoveKeys.Up).Action == RecoveryAction.Repath,
    "pause, actor switch and area reset cancel old recovery commands");
recovery.Begin(stuckPosition);
var isometricDirection = Steering.Choose(new(1, -1), new(1, 0.5f), new(-1, 0.5f), _ => true);
Check(recovery.Advance(stuckPosition, 0, isometricDirection).Keys == MoveKeys.Right,
    "forced target direction still uses the actual isometric screen projection");
var invalidDirection = Steering.Choose(Vector2.UnitX, Vector2.Zero, Vector2.Zero, _ => true);
Check(recovery.Advance(stuckPosition, 10, invalidDirection).Action == RecoveryAction.Wait,
    "invalid projection releases the old press rather than guessing a target direction");
var shortLease = new KeyLease((_, _) => true);
shortLease.Renew(MoveKeys.Up, 0, 150);
shortLease.Renew(MoveKeys.Up, 100, 50);
shortLease.Expire(149, true);
Check(shortLease.Held == MoveKeys.Up, "short probe lease is alive before its deadline");
shortLease.Expire(150, true);
Check(shortLease.Held == MoveKeys.None, "watchdog releases a 150 ms probe even if no new draw frame arrives");
foreach (var useArrows in new[] { false, true })
{
    var sent = new List<MovementBinding>();
    var probeLease = new KeyLease((key, _) => { sent.Add(MovementBindings.Get(key, useArrows)); return true; });
    recovery.Reset();
    recovery.Begin(new(5, 5));
    var probe = recovery.Advance(new(5, 5), 0, MoveKeys.Up);
    probeLease.Renew(probe.Keys, 0, probe.LeaseMilliseconds);
    probeLease.Expire(150, true);
    Check(sent.Count == 2 && sent.All(binding => binding.Extended == useArrows),
        useArrows ? "P2 recovery presses and releases only extended arrow keys" : "shared recovery presses and releases only WASD");
}
Console.WriteLine($"All {passed} Follower checks passed. No Windows input or live gameplay tested.");
