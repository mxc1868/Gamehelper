using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Numerics;
using BloodyBot.Combat;
using Bloodybot2.Game;
using Bloodybot2.Navigation;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Objects.Components;
using GameOffsets.Objects.States.InGameState;

// Real host entities/components populated as cached fixtures. Never attach to a
// process, invoke UpdateData, initialize AreaInstance, or send input.
const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); checks++; }
void Set(object target, string property, object value) => target.GetType().GetProperty(property)!.SetValue(target, value);
void Address(RemoteObjectBase target, long address) => typeof(RemoteObjectBase).GetField("address", Hidden)!.SetValue(target, (IntPtr)address);
Entity Entity(uint id, EntityTypes type)
{
    var entity = (Entity)Activator.CreateInstance(typeof(Entity), nonPublic: true)!;
    Address(entity, id * 1000);
    entity.IsValid = true;
    Set(entity, "Id", id); Set(entity, "EntityType", type);
    return entity;
}
ConcurrentDictionary<string, ComponentBase> Cache(Entity entity) =>
    (ConcurrentDictionary<string, ComponentBase>)typeof(Entity).GetField("componentCache", Hidden)!.GetValue(entity)!;
T Add<T>(Entity entity) where T : ComponentBase
{
    var component = (T)Activator.CreateInstance(typeof(T), IntPtr.Zero)!;
    Address(component, entity.Address.ToInt64() + 100);
    typeof(ComponentBase).GetField("OwnerEntityAddress", Hidden)!.SetValue(component, entity.Address);
    Cache(entity)[typeof(T).Name] = component;
    return component;
}
void Position(Render render, float x, float y = 0)
{
    var type = typeof(Render).GetNestedType("GridPos2DSnap", BindingFlags.NonPublic)!;
    typeof(Render).GetField("gridSnap", Hidden)!.SetValue(render, Activator.CreateInstance(type, x, y));
}
Entity Monster(Rarity rarity, uint id = 2)
{
    // Sufficient for Radar's normal rarity icon branch: no enemy Life,
    // Positioned or Targetable components are required.
    var entity = Entity(id, EntityTypes.Monster);
    Position(Add<Render>(entity), 20);
    Set(Add<ObjectMagicProperties>(entity), "Rarity", rarity);
    return entity;
}
var player = Entity(1, EntityTypes.Player);
var life = Add<Life>(player);
Set(life, "IsAlive", true);
Set(life, "Health", new VitalStruct { Current = 100, Total = 100 });
Position(Add<Render>(player), 0);
Add<Buffs>(player);
var area = (AreaInstance)RuntimeHelpers.GetUninitializedObject(typeof(AreaInstance));
var awake = new ConcurrentDictionary<EntityNodeKey, Entity>();
typeof(AreaInstance).GetField("<AwakeEntities>k__BackingField", Hidden)!.SetValue(area, awake);
CombatSnapshot Read(Entity enemy)
{
    awake.Clear(); awake[default] = enemy;
    return CombatSnapshotReader.Read(area, player, 1000, true)!;
}
bool Accepted(Entity enemy) => Read(enemy).Enemies.Any(e => e.IsCandidate);

foreach (var rarity in new[] { Rarity.Normal, Rarity.Magic, Rarity.Rare, Rarity.Unique })
{
    var monster = Monster(rarity);
    var snapshot = Read(monster);
    Check(snapshot.Enemies.SingleOrDefault() is { IsCandidate: true } candidate &&
        candidate.Rarity == (EnemyRarity)(1 << (int)rarity),
        rarity + " Radar-visible monster counted without Life/Positioned/Targetable");
}
var enemy = Monster(Rarity.Rare);
Set(Add<Life>(enemy), "IsAlive", false);
Add<Stats>(enemy).StatsChangedByBuffAndActions[GameStats.is_dead] = 0;
Check(Accepted(enemy), "core alive state is not overridden by zero monster Life");
Set(Add<Targetable>(enemy), "IsTargetable", false);
Check(Accepted(enemy), "Radar monster branch does not require Targetable=true");
foreach (var state in new[] { EntityStates.Useless, EntityStates.MonsterFriendly, EntityStates.PinnacleBossHidden, EntityStates.PlayerLeader })
{
    enemy = Monster(Rarity.Rare); Set(enemy, "EntityState", state);
    Check(!Accepted(enemy), "non-hostile core state excluded: " + state);
}
enemy = Monster(Rarity.Rare);
enemy.IsValid = false;
Check(!Accepted(enemy), "invalid/out-of-bubble entity excluded from combat");
enemy = Monster(Rarity.Rare);
Set(enemy, "EntityType", EntityTypes.Chest);
Check(!Accepted(enemy), "rare chest is not a monster");
enemy = Monster(Rarity.Rare);
Set(enemy, "EntitySubtype", EntitySubtypes.POIMonster);
Check(Accepted(enemy), "POI icon override does not remove a rare monster from combat");
enemy = Monster(Rarity.Rare);
Add<Stats>(enemy).StatsChangedByBuffAndActions[GameStats.is_hidden_monster] = 1;
Check(!Accepted(enemy), "Radar hidden-monster icon is not an ordinary enemy");
foreach (var immunity in new[] { GameStats.cannot_be_damaged, GameStats.base_cannot_be_damaged })
{
    enemy = Monster(Rarity.Rare);
    Add<Stats>(enemy).StatsChangedByBuffAndActions[immunity] = 1;
    Check(!Accepted(enemy), "known immunity still blocks combat: " + immunity);
}
enemy = Monster(Rarity.Rare);
Add<Stats>(enemy).StatsChangedByItems[GameStats.cannot_be_damaged] = 1;
Check(!Accepted(enemy), "item-stat immunity blocks combat");
enemy = Monster(Rarity.Rare);
Add<Buffs>(enemy).StatusEffects["hidden_monster"] = default;
Check(!Accepted(enemy), "known hidden buff still blocks combat");
enemy = Monster(Rarity.Rare);
Position((Render)Cache(enemy)[nameof(Render)], 151);
Check(!Accepted(enemy), "outside scan range excluded");
Position((Render)Cache(enemy)[nameof(Render)], 150);
Check(Accepted(enemy), "150 grid scan boundary included");
Position((Render)Cache(enemy)[nameof(Render)], float.NaN);
Check(!Accepted(enemy), "nonfinite enemy position excluded");
enemy = Monster(Rarity.Rare);
typeof(ComponentBase).GetField("OwnerEntityAddress", Hidden)!.SetValue(Cache(enemy)[nameof(ObjectMagicProperties)], (IntPtr)99);
Check(!Accepted(enemy), "stale rarity component parent excluded");
enemy = Monster(Rarity.Rare);
Cache(enemy).Remove(nameof(Render), out _);
Check(!Accepted(enemy), "missing position excluded");
enemy = Monster(Rarity.Rare);
Cache(enemy).Remove(nameof(ObjectMagicProperties), out _);
Check(!Accepted(enemy), "missing rarity excluded");
enemy = Monster((Rarity)99);
Check(!Accepted(enemy), "unknown rarity excluded");
enemy = Monster(Rarity.Rare);
var settings = new CombatSettings { Enabled = true, Rules = [new() { Key = 'Q', Range = 50 }] };
Check(new CombatEngine().Evaluate(settings, Read(enemy), 1000)?.MatchingEnemies == 1,
    "Radar-visible rare reaches combat rule evaluation");
Position((Render)Cache(enemy)[nameof(Render)], 60);
Check(new CombatEngine().Evaluate(settings, Read(enemy), 1000) == null,
    "scan count does not bypass a narrower skill range");
Check(CombatSnapshotReader.Read(area, player, 1000, false)?.CanFight == false,
    "town/hideout combat gate preserved");
Cache(player).Remove(nameof(Buffs), out _);
Check(CombatSnapshotReader.Read(area, player, 1000, true) == null, "unreadable player still blocks snapshot");
Add<Buffs>(player).StatusEffects["grace_period"] = default;
Check(CombatSnapshotReader.Read(area, player, 1000, true)?.CanFight == false, "entry protection preserved");
Set(life, "IsAlive", false);
Check(CombatSnapshotReader.Read(area, player, 1000, true) == null, "player death still blocks snapshot");

// Invoke the actual danger reader using host entity fixtures. The center is the
// leader, far beyond the combat monitor's scan radius; combat and Life are irrelevant.
var leaderPosition = new Vector2(300, 200);
Set(life, "IsAlive", true);
int Danger(Entity monster, float range = 50)
{
    awake.Clear(); awake[new EntityNodeKey { id = monster.Id }] = monster;
    return FollowDangerReader.CountElites(area, leaderPosition, range);
}
foreach (var rarity in new[] { Rarity.Normal, Rarity.Magic, Rarity.Rare, Rarity.Unique, (Rarity)99 })
{
    enemy = Monster(rarity);
    Position((Render)Cache(enemy)[nameof(Render)], 330, 240);
    Check(Danger(enemy) == (rarity is Rarity.Rare or Rarity.Unique ? 1 : 0),
        "leader danger rarity at inclusive 50-grid diagonal boundary: " + rarity);
}
enemy = Monster(Rarity.Rare);
Position((Render)Cache(enemy)[nameof(Render)], 350.01f, 200);
Check(Danger(enemy) == 0, "elite just outside leader radius does not pause Follow");
Position((Render)Cache(enemy)[nameof(Render)], 350, 200);
Check(Danger(enemy) == 1 && Read(enemy).Enemies.Count == 0,
    "danger uses leader position even beyond combat monitor scan bubble");
Position((Render)Cache(enemy)[nameof(Render)], 20);
Check(Danger(enemy) == 0, "elite near follower but far from leader is not danger");
Position((Render)Cache(enemy)[nameof(Render)], 300, 200);
Set(Add<Life>(enemy), "IsAlive", false);
Check(Danger(enemy, 1) == 1, "danger follows Radar core state instead of monster Life");
foreach (var state in new[] { EntityStates.Useless, EntityStates.MonsterFriendly, EntityStates.PinnacleBossHidden })
{
    Set(enemy, "EntityState", state);
    Check(Danger(enemy) == 0, "leader danger excludes core state: " + state);
}
Set(enemy, "EntityState", EntityStates.None);
var enemyStats = Add<Stats>(enemy);
enemyStats.StatsChangedByBuffAndActions[GameStats.is_hidden_monster] = 1;
Check(Danger(enemy) == 0, "leader danger excludes hidden monster stats");
enemyStats.StatsChangedByBuffAndActions.Clear();
enemyStats.StatsChangedByItems[GameStats.cannot_be_damaged] = 1;
Check(Danger(enemy) == 1, "invulnerable visible elite remains dangerous");
Add<Buffs>(enemy).StatusEffects["hidden_monster"] = default;
Check(Danger(enemy) == 0, "leader danger excludes hidden monster buff");
Cache(enemy).Remove(nameof(Buffs), out _);
enemy.IsValid = false;
Check(Danger(enemy) == 0, "leader danger excludes invalid elite");
enemy.IsValid = true;
Position((Render)Cache(enemy)[nameof(Render)], float.NaN, 200);
Check(Danger(enemy) == 0, "leader danger excludes nonfinite position");
Position((Render)Cache(enemy)[nameof(Render)], 300, 200);
typeof(ComponentBase).GetField("OwnerEntityAddress", Hidden)!.SetValue(Cache(enemy)[nameof(ObjectMagicProperties)], (IntPtr)99);
Check(Danger(enemy) == 0, "leader danger excludes stale rarity component");
awake.Clear();
foreach (var (id, rarity, x) in new[] { (10u, Rarity.Rare, 300f), (11u, Rarity.Unique, 320f),
    (12u, Rarity.Rare, 350f), (13u, Rarity.Magic, 300f), (14u, Rarity.Rare, 351f) })
{
    var monster = Monster(rarity, id);
    Position((Render)Cache(monster)[nameof(Render)], x, 200);
    awake[new EntityNodeKey { id = id }] = monster;
}
Check(FollowDangerReader.CountElites(area, leaderPosition, 50) == 3,
    "leader danger counts each in-range rare and unique, ignoring magic and outside elites");
awake.Clear();
Check(FollowDangerReader.CountElites(area, leaderPosition, 50) == 0, "danger clears when elites disappear");

// Exercise Follow's actual danger transition without constructing the Win32 input
// backend or its watchdog. Only fake lease/skill delegates can run in this fixture.
foreach (var arrows in new[] { false, true })
{
    var sent = new List<(MoveKeys Key, bool Down)>();
    var releaseFails = false;
    var movement = (MovementInput)RuntimeHelpers.GetUninitializedObject(typeof(MovementInput));
    var lease = new KeyLease((key, down) => { sent.Add((key, down)); return down || !releaseFails; });
    var pulse = new SkillKeyPulse((_, _) => true);
    typeof(MovementInput).GetField("lease", Hidden)!.SetValue(movement, lease);
    typeof(MovementInput).GetField("skill", Hidden)!.SetValue(movement, pulse);
    typeof(MovementInput).GetField("arrows", Hidden)!.SetValue(movement, arrows);
    var mode = new FollowMode(movement);
    var run = (FollowRunState)typeof(FollowMode).GetField("run", Hidden)!.GetValue(mode)!;
    run.Start();
    var cancel = new CancellationTokenSource(); var cancelToken = cancel.Token;
    typeof(FollowMode).GetField("searchCancellation", Hidden)!.SetValue(mode, cancel);
    typeof(FollowMode).GetField("route", Hidden)!.SetValue(mode, new List<Vector2> { new(1, 1), new(2, 2) });
    var recovery = (UnstuckRecovery)typeof(FollowMode).GetField("recovery", Hidden)!.GetValue(mode)!;
    recovery.BeginIfNeeded(new(1, 1), true, false);
    lease.Renew(MoveKeys.Up | MoveKeys.Right, 1000);
    pulse.TryStart('Q', 80, 1000);
    var combatCalls = 0;
    void Pause() => typeof(FollowMode).GetMethod("WaitForDanger", Hidden)!.Invoke(mode, [(Func<bool>)(() => { combatCalls++; return true; })]);
    Pause();
    Check(lease.Held == MoveKeys.None && sent.Count(e => !e.Down) == 2,
        "danger immediately releases actual " + (arrows ? "P2 arrow" : "shared WASD") + " lease");
    Check(cancelToken.IsCancellationRequested && typeof(FollowMode).GetField("route", Hidden)!.GetValue(mode) == null && !recovery.Active,
        "danger cancels search, route and unstuck for " + (arrows ? "P2" : "P1"));
    Check(mode.IsActive && run.Ready(1001) && run.Status == "danger" && pulse.Held == 'Q' && combatCalls == 1,
        "danger retains start and independent active skill without sending movement");
    Pause();
    Check(sent.Count == 4 && combatCalls == 2, "repeated dangerous observations remain stationary");
    releaseFails = true; lease.Renew(MoveKeys.Up, 1100); Pause();
    Check(lease.Held == MoveKeys.Up, "danger keeps failed key-up ownership for retry");
    releaseFails = false; lease.Expire(1101, false);
    Check(lease.Held == MoveKeys.None, "watchdog can retry the danger release");
    mode.Stop();
    Check(!mode.IsActive && pulse.Held == 0 && !run.Ready(long.MaxValue),
        "explicit stop during danger remains stopped and releases skill");
}
Set(life, "Health", new VitalStruct { Current = 400, Total = 1000 });
Set(life, "EnergyShield", new VitalStruct { Current = 600, Total = 1000 });
Check(LowHealthReader.Percent(life) == 50, "actual Life reader combines current Life and ES at 50 percent");
Set(life, "EnergyShield", new VitalStruct { Current = 599, Total = 1000 });
Check(LowHealthReader.Percent(life) < 50, "actual Life reader triggers only below 50 percent");
Set(life, "Health", new VitalStruct { Current = 800, Total = 1000 });
Set(life, "EnergyShield", new VitalStruct { Current = 100, Total = 9000 });
Check(LowHealthReader.Percent(life) == 9, "large ES pool weights the low-health ratio correctly");
Set(life, "Health", new VitalStruct { Current = 250, Total = 1000, ReservedFlat = 500 });
Set(life, "EnergyShield", default(VitalStruct));
Check(LowHealthReader.Percent(life) == 50, "no ES and flat Life reservation use available Life capacity");
Set(life, "Health", new VitalStruct { Current = 100, Total = 1000, ReservedPercent = 6000 });
Check(LowHealthReader.Percent(life) == 25, "percentage Life reservation is deducted before combined ratio");
Set(life, "Health", default(VitalStruct));
Check(LowHealthReader.Percent(life) == null, "missing Life capacity is unknown rather than healthy or zero");
Set(life, "Health", new VitalStruct { Current = 100, Total = 1000 });
Set(life, "EnergyShield", new VitalStruct { Current = -1, Total = 1000 });
Check(LowHealthReader.Percent(life) == null, "negative ES reading blocks escape decision");
Set(life, "EnergyShield", new VitalStruct { Current = 1 });
Check(LowHealthReader.Percent(life) == null, "contradictory zero-capacity ES reading blocks escape decision");
Set(life, "EnergyShield", default(VitalStruct));
Set(life, "Health", new VitalStruct { Current = 1, Total = 1000, ReservedPercent = 10001 });
Check(LowHealthReader.Percent(life) == null, "invalid reservation cannot manufacture low health");

awake.Clear();
foreach (var (id, rarity) in new[] { (21u, Rarity.Rare), (22u, Rarity.Unique), (23u, Rarity.Magic), (24u, Rarity.Normal) })
{
    var monster = Monster(rarity, id);
    Position((Render)Cache(monster)[nameof(Render)], 25, 20);
    awake[new EntityNodeKey { id = id }] = monster;
}
Check(LowHealthReader.Rares(area, new(20, 20), 50) is { Count: 1 }, "emergency clusters only yellow rares, independently of leader danger rarities");
enemy = awake.Values.Single(e => e.Id == 21);
Add<Stats>(enemy).StatsChangedByItems[GameStats.cannot_be_damaged] = 1;
Check(LowHealthReader.Rares(area, new(20, 20), 50) is { Count: 1 }, "immune visible yellow monster still belongs to escape threat cluster");
Add<Buffs>(enemy).StatusEffects["hidden_monster"] = default;
Check(LowHealthReader.Rares(area, new(20, 20), 50) is { Count: 0 }, "hidden yellow monster is not an escape threat");
awake.Clear();
for (var i = 0; i <= EscapePlanner.MaximumRares; i++)
{
    var monster = Monster(Rarity.Rare, (uint)(100 + i));
    Position((Render)Cache(monster)[nameof(Render)], 25, 20);
    awake[new EntityNodeKey { id = monster.Id }] = monster;
}
Check(LowHealthReader.Rares(area, new(20, 20), 50) == null, "truncated rare scan is unknown and cannot falsely report safety");

var retreatInput = (MovementInput)RuntimeHelpers.GetUninitializedObject(typeof(MovementInput));
var physicalEvents = new List<(int Key, bool Down)>();
var retreatReleaseFails = false;
var retreatLease = new KeyLease((key, down) =>
{
    var arrows = (bool)typeof(MovementInput).GetField("arrows", Hidden)!.GetValue(retreatInput)!;
    physicalEvents.Add((MovementBindings.Get(key, arrows).VirtualKey, down));
    return down || !retreatReleaseFails;
});
var retreatPulse = new SkillKeyPulse((_, _) => true);
typeof(MovementInput).GetField("lease", Hidden)!.SetValue(retreatInput, retreatLease);
typeof(MovementInput).GetField("skill", Hidden)!.SetValue(retreatInput, retreatPulse);
var retreatMode = new FollowMode(retreatInput);
var retreatRun = (FollowRunState)typeof(FollowMode).GetField("run", Hidden)!.GetValue(retreatMode)!;
retreatRun.Start();
bool BeginRetreat(bool secondary, float health) => (bool)typeof(FollowMode).GetMethod("BeginEscape", Hidden)!
    .Invoke(retreatMode, [(IntPtr)(secondary ? 2000 : 1000), (uint)(secondary ? 2 : 1), secondary, health, secondary ? "P2" : "P1"])!;
var oldRetreatSearch = new CancellationTokenSource(); var oldRetreatToken = oldRetreatSearch.Token;
typeof(FollowMode).GetField("searchCancellation", Hidden)!.SetValue(retreatMode, oldRetreatSearch);
typeof(FollowMode).GetField("route", Hidden)!.SetValue(retreatMode, new List<Vector2> { new(20, 20), new(30, 20) });
retreatPulse.TryStart('Q', 80, 1000);
retreatLease.Renew(MoveKeys.Up, 1000);
Check(BeginRetreat(false, 40) && retreatLease.Held == MoveKeys.None && oldRetreatToken.IsCancellationRequested &&
    typeof(FollowMode).GetField("route", Hidden)!.GetValue(retreatMode) == null && retreatPulse.Held == 'Q' && retreatMode.IsActive,
    "P1 emergency cancels follow search/movement while preserving accepted skill and start");
retreatPulse.Stop();
retreatLease.Renew(MoveKeys.Up | MoveKeys.Right, 1100);
var switchAt = physicalEvents.Count;
Check(BeginRetreat(true, 25) && physicalEvents.Skip(switchAt).SequenceEqual(new[] { ((int)'W', false), ((int)'D', false) }) &&
    (bool)typeof(MovementInput).GetField("arrows", Hidden)!.GetValue(retreatInput)! && retreatLease.Held == MoveKeys.None,
    "lower-health P2 releases shared WASD before switching to arrows");
retreatLease.Renew(MoveKeys.Left, 1200);
Check(physicalEvents[^1] == (0x25, true), "P2 emergency sends arrow mapping exclusively");
switchAt = physicalEvents.Count;
Check(BeginRetreat(false, 20) && physicalEvents.Skip(switchAt).Single() == (0x25, false) &&
    !(bool)typeof(MovementInput).GetField("arrows", Hidden)!.GetValue(retreatInput)!,
    "lower-health P1 releases old P2 arrow before switching back to shared WASD");
retreatLease.Renew(MoveKeys.Down, 1300);
retreatReleaseFails = true;
Check(!BeginRetreat(true, 10) && retreatLease.Held == MoveKeys.Down &&
    !(bool)typeof(MovementInput).GetField("arrows", Hidden)!.GetValue(retreatInput)!,
    "failed emergency key-up blocks layout switch and retains original ownership");
retreatReleaseFails = false; retreatLease.Expire(1301, false);
Check(retreatLease.Held == MoveKeys.None && physicalEvents[^1] == ((int)'S', false), "emergency release retry uses original WASD mapping");
retreatMode.Stop();
Check(!retreatMode.IsActive && !(bool)typeof(FollowMode).GetField("escaping", Hidden)!.GetValue(retreatMode)! && !retreatRun.Ready(long.MaxValue),
    "explicit stop during emergency cannot be undone by health recovery");
Console.WriteLine($"All {checks} combat reader checks passed. Cached fixtures only; no game process or input.");
