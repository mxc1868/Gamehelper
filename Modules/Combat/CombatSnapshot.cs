namespace BloodyBot.Combat;

using System.Numerics;

public sealed record CombatPlayer(Vector2 Position, bool IsAlive, float? LifePercent,
    float? EnergyShieldPercent, float? ManaPercent, IReadOnlySet<string>? Buffs,
    IReadOnlySet<string>? UsableSkills);

public sealed record CombatEnemy(uint Id, EnemyRarity Rarity, Vector2 Position,
    bool IsAlive, bool IsHostile, bool IsTargetable, bool IsImmune = false)
{
    public bool IsCandidate => this.IsAlive && this.IsHostile && this.IsTargetable && !this.IsImmune &&
        this.Rarity is EnemyRarity.Normal or EnemyRarity.Magic or EnemyRarity.Rare or EnemyRarity.Unique &&
        float.IsFinite(this.Position.X) && float.IsFinite(this.Position.Y);
}

public sealed record CombatSnapshot(long CapturedAt, bool CanFight, CombatPlayer Player,
    IReadOnlyList<CombatEnemy> Enemies);

// This is a proposal, not evidence that the game cast a skill. Record only accepted input.
public sealed record CombatAction(string RuleId, string Name, int Key, int PressMilliseconds,
    int PauseMilliseconds, int CooldownMilliseconds, uint? EnemyId, int MatchingEnemies);
