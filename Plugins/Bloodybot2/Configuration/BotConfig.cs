namespace Bloodybot2.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using BloodyBot.Combat;
using Bloodybot2.Navigation;

public sealed class BotConfig
{
    public int SchemaVersion { get; set; } = 2;
    public string Mode { get; set; } = "";
    public int ToggleKey { get; set; } = 0x75;
    public string Name { get; set; } = "我的配置";
    public bool Preview { get; set; } = true;
    public string MonitorCharacter { get; set; } = "";
    public bool AllowControllerWithoutChat { get; set; }
    public CombatSettings Combat { get; set; } = new() { Enabled = true };
    public FollowSettings Follow { get; set; } = new();

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
        var node = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("配置不能为空。");
        if (node["schemaVersion"] is JsonValue schema && schema.TryGetValue<int>(out var version) && version == 1) node["schemaVersion"] = 2;
        var config = node.Deserialize<BotConfig>(Json) ?? throw new ArgumentException("配置不能为空。");
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
        if (this.SchemaVersion != 2) throw new ArgumentException("不支持此配置版本；请使用 Bloodybot2 配置。");
        if (this.Mode is not ("" or "Follow")) throw new ArgumentException("当前仅支持 Follow 模式。");
        if (!IsToggleKeyAllowed(this.ToggleKey)) throw new ArgumentException("启停快捷键不可使用移动键、修饰键、Enter 或 Esc。");
        if (this.Follow == null) throw new ArgumentException("缺少 Follow 配置。");
        Text(this.Follow.LeaderName, 128, "队长名称");
        Text(this.Follow.P1Name, 128, "P1 名称");
        Text(this.Follow.P2Name, 128, "P2 名称");
        Range(this.Follow.StopDistance, 3, 100, "停止距离");
        Range(this.Follow.ResumeDistance, this.Follow.StopDistance + 3, 150, "重新跟随距离");
        Range(this.Follow.P2LagDistance, 6, 150, "P2 纠偏距离");
        Range(this.Follow.P2RejoinDistance, 3, this.Follow.P2LagDistance - 3, "P2 回归距离");
        Range(this.Follow.Clearance, 0, 2, "离墙间距");
        Range(this.Follow.RepathMilliseconds, 150, 1000, "重新寻路间隔");
        Range(this.Follow.StuckMilliseconds, 1000, 10000, "脱困等待时间");
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

    public void ValidateMode()
    {
        if (this.Mode != "Follow") throw new ArgumentException("请先在 General 中选择 Follow 模式。");
    }

    public void ValidateStart()
    {
        this.ValidateMode();
        if (string.IsNullOrWhiteSpace(this.Follow.LeaderName)) throw new ArgumentException("请先选择 Follow 的队长。");
        if (this.Follow.LocalCoopFollow && (string.IsNullOrWhiteSpace(this.Follow.P1Name) || string.IsNullOrWhiteSpace(this.Follow.P2Name)))
            throw new ArgumentException("双人 Follow 需要选择 P1 和 P2。");
        if (this.Follow.LocalCoopFollow && (this.Follow.P1Name.Trim().Equals(this.Follow.P2Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
            this.Follow.P2Name.Trim().Equals(this.Follow.LeaderName.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("P2 必须与 P1 和队长是不同角色。");
    }

    public static bool IsToggleKeyAllowed(int key) => key is >= 0x08 and <= 0xFE &&
        key is not (0x0D or 0x10 or 0x11 or 0x12 or 0x1B or 0x25 or 0x26 or 0x27 or 0x28 or 0x41 or 0x44 or 0x53 or 0x57 or
                    0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
}
