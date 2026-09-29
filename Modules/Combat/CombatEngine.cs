namespace BloodyBot.Combat;

using System.Numerics;

/// <summary>Ordered rules produce one key-only action. The host owns navigation and input.</summary>
public sealed class CombatEngine
{
    private readonly Dictionary<string, long> ruleReadyAt = new(StringComparer.Ordinal);
    private readonly Dictionary<int, long> keyReadyAt = [];
    private long nextActionAt;

    public CombatAction? Evaluate(CombatSettings settings, CombatSnapshot snapshot, long now,
        Func<int, bool>? keyAllowed = null)
    {
        var player = snapshot.Player;
        if (!settings.Enabled || !snapshot.CanFight || !player.IsAlive || now < snapshot.CapturedAt ||
            now - snapshot.CapturedAt > 150 || now < this.nextActionAt ||
            !float.IsFinite(player.Position.X) || !float.IsFinite(player.Position.Y)) return null;

        foreach (var rule in settings.Rules)
        {
            if (!rule.Enabled || !CombatRule.IsKeyAllowed(rule.Key) || keyAllowed?.Invoke(rule.Key) == false ||
                (this.ruleReadyAt.TryGetValue(rule.Id, out var ruleAt) && now < ruleAt) ||
                (this.keyReadyAt.TryGetValue(rule.Key, out var keyAt) && now < keyAt)) continue;
            if (rule.UseLifeBelow && !Below(player.LifePercent, rule.LifeBelow) ||
                rule.UseEnergyShieldBelow && !Below(player.EnergyShieldPercent, rule.EnergyShieldBelow) ||
                rule.UseManaBelow && !Below(player.ManaPercent, rule.ManaBelow) ||
                rule.MinimumMana > 0 && !(player.ManaPercent is float mana && float.IsFinite(mana) && mana >= rule.MinimumMana)) continue;
            // Unknown Buffs/Actor data never means "buff missing" or "skill ready".
            if (rule.RequiredBuff.Length > 0 && (player.Buffs == null || !HasBuff(player.Buffs, rule.RequiredBuff)) ||
                rule.MissingBuff.Length > 0 && (player.Buffs == null || HasBuff(player.Buffs, rule.MissingBuff)) ||
                rule.ReadySkill.Length > 0 && (player.UsableSkills == null || !player.UsableSkills.Contains(rule.ReadySkill))) continue;

            CombatEnemy? best = null;
            var count = 0;
            var bestDistance = float.PositiveInfinity;
            if (rule.RequireEnemy)
            {
                foreach (var enemy in snapshot.Enemies)
                {
                    if (!enemy.IsCandidate || (enemy.Rarity & rule.Rarities) == 0) continue;
                    var distance = Vector2.Distance(player.Position, enemy.Position);
                    if (!float.IsFinite(distance) || distance > rule.Range) continue;
                    count++;
                    if (best == null || enemy.Rarity > best.Rarity || enemy.Rarity == best.Rarity &&
                        (distance < bestDistance || distance == bestDistance && enemy.Id < best.Id))
                    { best = enemy; bestDistance = distance; }
                }
                if (count < rule.MinimumEnemies) continue;
            }
            return new(rule.Id, rule.Name, rule.Key, rule.PressMilliseconds, rule.PauseMilliseconds,
                rule.CooldownMilliseconds, best?.Id, count);
        }
        return null;
    }

    public void RecordAccepted(CombatAction action, long now)
    {
        var readyAt = now + Math.Clamp(action.CooldownMilliseconds, 300, 600000);
        this.ruleReadyAt[action.RuleId] = readyAt;
        this.keyReadyAt[action.Key] = readyAt;
        // Leave time for the host to advance navigation between successive casts,
        // even when a rule is configured with the shortest repeat interval.
        this.nextActionAt = now + Math.Max(300, action.PressMilliseconds + action.PauseMilliseconds + 150);
    }

    public void Reset() { this.ruleReadyAt.Clear(); this.keyReadyAt.Clear(); this.nextActionAt = 0; }
    private static bool Below(float? value, int threshold) => value is float number && float.IsFinite(number) && number >= 0 && number < threshold;
    private static bool HasBuff(IReadOnlySet<string> buffs, string name) =>
        buffs.Any(b => b.Contains(name, StringComparison.OrdinalIgnoreCase));
}
