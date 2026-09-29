namespace BloodyBot.Combat;

[Flags]
public enum EnemyRarity { None = 0, Normal = 1, Magic = 2, Rare = 4, Unique = 8, All = 15 }

// Configuration and snapshots deliberately have no GameHelper, Follower or Win32 dependencies.
public sealed class CombatRule
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "Skill";
    public bool Enabled = true;
    public int Key; // Unbound until explicitly selected.
    public bool RequireEnemy = true;
    public EnemyRarity Rarities = EnemyRarity.Rare | EnemyRarity.Unique;
    public float Range = 50;
    public int MinimumEnemies = 1;
    public bool UseLifeBelow;
    public int LifeBelow = 50;
    public bool UseEnergyShieldBelow;
    public int EnergyShieldBelow = 50;
    public bool UseManaBelow;
    public int ManaBelow = 30;
    public int MinimumMana;
    public string RequiredBuff = string.Empty;
    public string MissingBuff = string.Empty;
    public string ReadySkill = string.Empty;
    public int CooldownMilliseconds = 2000;
    public int PressMilliseconds = 80;
    public int PauseMilliseconds = 250;

    public void Normalize()
    {
        this.Id = string.IsNullOrWhiteSpace(this.Id) ? Guid.NewGuid().ToString("N") : this.Id;
        this.Name = (this.Name ?? string.Empty).Trim();
        this.RequiredBuff = (this.RequiredBuff ?? string.Empty).Trim();
        this.MissingBuff = (this.MissingBuff ?? string.Empty).Trim();
        this.ReadySkill = (this.ReadySkill ?? string.Empty).Trim();
        if (!IsKeyAllowed(this.Key)) this.Key = 0;
        this.Rarities &= EnemyRarity.All;
        this.Range = float.IsFinite(this.Range) ? Math.Clamp(this.Range, 1, 150) : 50;
        this.MinimumEnemies = Math.Clamp(this.MinimumEnemies, 1, 100);
        this.LifeBelow = Math.Clamp(this.LifeBelow, 1, 100);
        this.EnergyShieldBelow = Math.Clamp(this.EnergyShieldBelow, 1, 100);
        this.ManaBelow = Math.Clamp(this.ManaBelow, 1, 100);
        this.MinimumMana = Math.Clamp(this.MinimumMana, 0, 100);
        this.CooldownMilliseconds = Math.Clamp(this.CooldownMilliseconds, 300, 600000);
        this.PressMilliseconds = Math.Clamp(this.PressMilliseconds, 30, 200);
        this.PauseMilliseconds = Math.Clamp(this.PauseMilliseconds, 0, 2000);
    }

    // Single keyboard keys only. Movement, chat, modifiers, mouse and function keys
    // are excluded; the host additionally excludes its own configured toggle key.
    public static bool IsKeyAllowed(int key) => key == 0x20 || key is >= 0x30 and <= 0x39 ||
        (key is >= 0x41 and <= 0x5A && key is not (0x41 or 0x44 or 0x53 or 0x57));
}

public sealed class CombatSettings
{
    public bool Enabled;
    public List<CombatRule> Rules = [];

    public void Normalize()
    {
        this.Rules ??= [];
        this.Rules = this.Rules.Where(r => r != null).Take(32).ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in this.Rules)
        {
            rule.Normalize();
            if (!ids.Add(rule.Id)) { rule.Id = Guid.NewGuid().ToString("N"); ids.Add(rule.Id); }
        }
    }
}
