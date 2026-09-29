namespace Bloodybot2.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using BloodyBot.Combat;

public sealed class BotConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "我的配置";
    public bool Preview { get; set; } = true;
    public string MonitorCharacter { get; set; } = "";
    public bool AllowControllerWithoutChat { get; set; }
    public CombatSettings Combat { get; set; } = new() { Enabled = true };

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 20,
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);
    public BotConfig Copy() => Parse(this.Serialize());
    public static BotConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<BotConfig>(json, Json) ?? throw new ArgumentException("配置不能为空。");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        static void Text(string? value, int max, string field, bool required = false)
        {
            if (value == null || value.Length > max || required && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{field}：请输入{(required ? "非空且" : "")}不超过 {max} 字的内容。");
        }
        static void Range(double value, double min, double max, string field)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"{field}：范围为 {min}–{max}。");
        }
        if (this.SchemaVersion != 1) throw new ArgumentException("不支持此配置版本；请使用 Bloodybot2 v1 配置。");
        Text(this.Name, 80, "配置名称", true);
        Text(this.MonitorCharacter, 128, "监测角色");
        if (this.Combat?.Rules == null || this.Combat.Rules.Count > 32) throw new ArgumentException("最多可配置 32 条技能规则。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in this.Combat.Rules)
        {
            if (r == null) throw new ArgumentException("技能规则不能为空。");
            Text(r.Id, 80, "规则 ID", true);
            if (!ids.Add(r.Id)) throw new ArgumentException("技能规则 ID 重复。");
            Text(r.Name, 80, "技能名称", true);
            if (r.Key != 0 && !CombatRule.IsKeyAllowed(r.Key)) throw new ArgumentException($"{r.Name}：按键仅支持字母（除 WASD）、数字及空格。");
            if ((r.Rarities & ~EnemyRarity.All) != 0 || r.RequireEnemy && r.Rarities == EnemyRarity.None)
                throw new ArgumentException($"{r.Name}：请选择有效的敌人稀有度。");
            Range(r.Range, 1, 150, "敌人距离");
            Range(r.MinimumEnemies, 1, 100, "敌人数量");
            Range(r.LifeBelow, 1, 100, "生命阈值");
            Range(r.EnergyShieldBelow, 1, 100, "护盾阈值");
            Range(r.ManaBelow, 1, 100, "魔力阈值");
            Range(r.MinimumMana, 0, 100, "最低魔力");
            Range(r.CooldownMilliseconds, 300, 600000, "重复间隔 ms");
            Range(r.PressMilliseconds, 30, 200, "按住时长 ms");
            Range(r.PauseMilliseconds, 0, 2000, "施法停顿 ms");
            Text(r.RequiredBuff, 160, "存在 Buff");
            Text(r.MissingBuff, 160, "缺少 Buff");
            Text(r.ReadySkill, 160, "就绪技能");
        }
    }
}
