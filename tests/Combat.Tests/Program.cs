using System.Numerics;
using System.Text.Json;
using BloodyBot.Combat;

// Synthetic state and fake input only: no Windows API, game attachment or key presses.
var passed = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception("FAIL " + description);
    passed++;
    Console.WriteLine("PASS " + description);
}

var engine = new CombatEngine();
var rule = new CombatRule { Name = "Elite", Key = 'Q' };
var settings = new CombatSettings { Enabled = true, Rules = [rule] };
var player = new CombatPlayer(Vector2.Zero, true, 100, 100, 100,
    new HashSet<string>(), new HashSet<string> { "Guard" });
CombatEnemy Enemy(uint id = 1, EnemyRarity rarity = EnemyRarity.Rare, float distance = 20) =>
    new(id, rarity, new(distance, 0), true, true, true);
CombatSnapshot Frame(long now = 1000, params CombatEnemy[] enemies) => new(now, true, player, enemies.Length == 0 ? [Enemy()] : enemies);
CombatAction? Evaluate(CombatSnapshot? frame = null, long now = 1000) => engine.Evaluate(settings, frame ?? Frame(now), now);

Check(!new CombatSettings().Enabled && new CombatRule().Key == 0, "old settings cannot enable unconfigured input");
Check(Evaluate()?.Name == "Elite", "rare nearby proposes configured skill");
Check(Evaluate(Frame(1000, Enemy(rarity: EnemyRarity.Unique))) != null, "unique matches default rule");
Check(Evaluate(Frame(1000, Enemy(rarity: EnemyRarity.Normal))) == null, "normal excluded by default");
Check(Evaluate(Frame(1000, Enemy(rarity: EnemyRarity.Magic))) == null, "magic excluded by default");
Check(Evaluate(Frame(1000, Enemy(rarity: EnemyRarity.None))) == null, "unknown rarity is not inferred");
Check(Evaluate(Frame() with { Enemies = [] }) == null, "empty area cannot trigger enemy rule");
Check(Evaluate(Frame(1000, Enemy(distance: 50))) != null && Evaluate(Frame(1000, Enemy(distance: 50.01f))) == null,
    "range includes boundary and excludes farther enemies");
foreach (var invalid in new[] { Enemy() with { IsAlive = false }, Enemy() with { IsHostile = false },
    Enemy() with { IsTargetable = false }, Enemy() with { IsImmune = true }, Enemy(distance: float.NaN) })
    Check(Evaluate(Frame(1000, invalid)) == null, "dead/friendly/untargetable/immune/nonfinite enemy excluded: " + invalid);
rule.MinimumEnemies = 2;
Check(Evaluate() == null && Evaluate(Frame(1000, Enemy(), Enemy(2)))?.MatchingEnemies == 2, "minimum enemy count");
Check(Evaluate(Frame(1000, Enemy(), Enemy(2, EnemyRarity.Normal))) == null, "count includes only selected rarities");
rule.MinimumEnemies = 1;
Check(Evaluate(Frame(1000, Enemy(), Enemy(2, EnemyRarity.Unique, 45)))?.EnemyId == 2, "unique wins over closer rare");
Check(Evaluate(Frame(1000, Enemy(2), Enemy(1)))?.EnemyId == 1, "equal targets break ties deterministically");
Check(Evaluate(Frame(1000, Enemy(1, distance: 30), Enemy(2, distance: 10)))?.EnemyId == 2, "same rarity uses nearest target");

settings.Enabled = false;
Check(Evaluate() == null, "disabled combat never proposes input");
settings.Enabled = true;
foreach (var frame in new[] { Frame() with { CanFight = false }, Frame() with { Player = player with { IsAlive = false } },
    Frame() with { Player = player with { Position = new(float.NaN, 0) } }, Frame(849), Frame(1001) })
    Check(Evaluate(frame) == null, "blocked/dead/invalid/stale/future snapshot rejected");
Check(Evaluate(Frame(850)) != null, "150 ms freshness boundary");
Check(engine.Evaluate(settings, Frame(), 1000, _ => false) == null, "host key veto blocks toggle or held key");

rule.UseLifeBelow = true;
Check(Evaluate() == null && Evaluate(Frame() with { Player = player with { LifePercent = 49 } }) != null, "life threshold");
Check(Evaluate(Frame() with { Player = player with { LifePercent = 50 } }) == null, "below condition is strict");
foreach (float? life in new float?[] { null, float.NaN, -1 })
    Check(Evaluate(Frame() with { Player = player with { LifePercent = life } }) == null, "unknown life never counts as low");
rule.UseLifeBelow = false;
rule.UseEnergyShieldBelow = true;
Check(Evaluate(Frame() with { Player = player with { EnergyShieldPercent = null } }) == null, "character with no ES cannot satisfy low ES");
Check(Evaluate(Frame() with { Player = player with { EnergyShieldPercent = 10 } }) != null, "depleted existing ES triggers");
rule.UseEnergyShieldBelow = false;
rule.UseManaBelow = true;
rule.MinimumMana = 10;
Check(Evaluate(Frame() with { Player = player with { ManaPercent = 20 } }) != null, "combined low mana and minimum mana");
Check(Evaluate(Frame() with { Player = player with { ManaPercent = 9 } }) == null, "minimum mana can veto low mana condition");
Check(Evaluate(Frame() with { Player = player with { ManaPercent = null } }) == null, "unknown mana blocks required mana checks");
rule.UseManaBelow = false;
rule.MinimumMana = 0;
rule.RequiredBuff = "Guard";
Check(Evaluate() == null && Evaluate(Frame() with { Player = player with { Buffs = new HashSet<string> { "guard_ABC" } } }) != null,
    "buff present matches internal substring case-insensitively");
rule.RequiredBuff = "";
rule.MissingBuff = "Guard";
Check(Evaluate() != null && Evaluate(Frame() with { Player = player with { Buffs = new HashSet<string> { "guard_ABC" } } }) == null,
    "missing buff prevents repeated casts while buff exists");
Check(Evaluate(Frame() with { Player = player with { Buffs = null } }) == null, "unreadable buffs do not mean missing");
rule.MissingBuff = "";
rule.ReadySkill = "Guard";
Check(Evaluate() != null && Evaluate(Frame() with { Player = player with { UsableSkills = null } }) == null,
    "optional readiness requires observed usable skill");
rule.ReadySkill = "MissingSkill";
Check(Evaluate() == null, "unknown/unusable skill cannot trigger");
rule.ReadySkill = "";
rule.RequireEnemy = false;
rule.UseLifeBelow = true;
Check(Evaluate(Frame() with { Enemies = [], Player = player with { LifePercent = 20 } }) != null, "self condition can run without nearby enemies");
rule.RequireEnemy = true;
Check(Evaluate(Frame() with { Enemies = [], Player = player with { LifePercent = 20 } }) == null, "enemy and player conditions are ANDed");
rule.UseLifeBelow = false;

var second = new CombatRule { Name = "Second", Key = 'E' };
settings.Rules.Add(second);
var proposed = Evaluate()!;
Check(Evaluate() == proposed, "preview/evaluation does not consume cooldown");
engine.RecordAccepted(proposed, 1000);
Check(Evaluate(now: 1299) == null, "global interval prevents skill bursts");
Check(Evaluate(now: 1330) == null, "cast completion leaves a navigation window even with other eligible rules");
Check(Evaluate(now: 1480)?.Name == "Second", "next eligible rule runs while first cools down");
second.Key = 'Q';
Check(Evaluate(now: 1500) == null, "duplicate key rule cannot bypass repeat interval");
Check(Evaluate(now: 3000)?.Name == "Elite", "first rule returns when cooldown ends");
settings.Enabled = false;
settings.Enabled = true;
Check(Evaluate(now: 1500) == null, "temporary disable does not erase cooldown");
engine.Reset();
Check(Evaluate() != null, "explicit initialization reset clears history");
rule.Enabled = false;
Check(Evaluate()?.Name == "Second", "disabled first rule does not starve later rules");

var events = new List<(int, bool)>();
var allowDown = true;
var allowUp = true;
var pulse = new SkillKeyPulse((key, down) => { events.Add((key, down)); return down ? allowDown : allowUp; });
Check(pulse.TryStart('Q', 80, 1000) && pulse.Held == 'Q', "accepted key press owns its release");
Check(!pulse.TryStart('E', 80, 1010), "pulse rejects overlapping key presses");
pulse.Expire(1079, true);
Check(pulse.Held == 'Q', "key remains down for requested pulse duration");
pulse.Expire(1080, true);
Check(pulse.Held == 0 && events.SequenceEqual(new[] { ((int)'Q', true), ((int)'Q', false) }), "timer releases exactly once without further game ticks");
pulse.TryStart('Q', 80, 2000);
pulse.Expire(2001, false);
Check(pulse.Held == 0, "focus/escape/stop gate releases immediately");
pulse.TryStart('Q', 80, 3000);
allowUp = false;
pulse.Stop();
Check(pulse.Held == 'Q' && !pulse.TryStart('E', 80, 4000), "failed key-up keeps ownership and blocks replacement");
allowUp = true;
pulse.Expire(4000, true);
Check(pulse.Held == 0 && events.Last() == ('Q', false), "watchdog retries the original key-up");
allowDown = false;
Check(!pulse.TryStart('Q', 80, 5000) && pulse.Held == 0, "failed key-down never takes ownership");
allowDown = true;
pulse.TryStart('Q', 100000, 6000);
pulse.Expire(6200, true);
Check(pulse.Held == 0, "press cannot exceed 200 ms even with malformed duration");
foreach (var key in new[] { 0, 0x1B, 0x0D, 0x11, 0x12, 0x25, 0x26, 0x27, 0x28, (int)'W', (int)'A', (int)'S', (int)'D', 0x75, 1 })
    Check(!pulse.TryStart(key, 80, 7000), "reject movement/control/mouse key " + key);

var malformed = new CombatSettings { Rules = [new() { Id = "duplicate", Range = float.NaN, Key = 'W', CooldownMilliseconds = 0 },
    new() { Id = "duplicate", PressMilliseconds = 10000, PauseMilliseconds = -1 }, null!] };
malformed.Normalize();
Check(malformed.Rules.Count == 2 && malformed.Rules.Select(r => r.Id).Distinct().Count() == 2, "null/duplicate rules normalize without cooldown aliasing");
Check(malformed.Rules[0].Key == 0 && malformed.Rules[0].Range == 50 && malformed.Rules[0].CooldownMilliseconds == 300,
    "invalid keys and timing are normalized");
Check(malformed.Rules[1].PressMilliseconds == 200 && malformed.Rules[1].PauseMilliseconds == 0, "press and pause bounds");
var options = new JsonSerializerOptions { IncludeFields = true };
var restored = JsonSerializer.Deserialize<CombatSettings>(JsonSerializer.Serialize(settings, options), options)!;
restored.Normalize();
Check(restored.Rules[0].Id == rule.Id && restored.Rules[0].Name == rule.Name && restored.Rules[1].Key == second.Key,
    "rules, order, identity and bindings survive serialization");
Console.WriteLine($"All {passed} combat checks passed. No game input was sent.");
