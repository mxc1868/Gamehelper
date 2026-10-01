using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using BloodyBot.Combat;
using Bloodybot2.Game;
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
Entity Monster(Rarity rarity)
{
    // Sufficient for Radar's normal rarity icon branch: no enemy Life,
    // Positioned or Targetable components are required.
    var entity = Entity(2, EntityTypes.Monster);
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
Console.WriteLine($"All {checks} combat reader checks passed. Cached fixtures only; no game process or input.");
