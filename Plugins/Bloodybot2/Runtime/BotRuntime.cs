namespace Bloodybot2.Runtime;

using BloodyBot.Combat;
using Bloodybot2.Configuration;

public interface IBotInput
{
    bool IsKeyAvailable(int key);
    bool TryPress(int key, int milliseconds, long now);
    void Release();
    void Expire(long now, bool allowed);
}

// Navigation owns movement; combat owns skill selection. A future Follower mode
// can deny combat during correction/recovery without teaching Combat about paths.
public interface INavigationMode
{
    string Name { get; }
    bool AllowsCombat(CombatSnapshot snapshot, long now);
    void Stop();
}

public sealed class ManualNavigation : INavigationMode
{
    public string Name => "手动移动";
    public bool AllowsCombat(CombatSnapshot snapshot, long now) => true;
    public void Stop() { }
}

public sealed record Observation(CombatSnapshot? Combat, string Character, string Area, string[] Players, string[] Skills);
public sealed record BotEvent(long At, string Message);
public sealed record BotStatus(bool Running, bool Preview, string Navigation, string Reason, long UpdatedAt,
    string Character, string Area, float? Life, float? Shield, float? Mana,
    int Normal, int Magic, int Rare, int Unique, string[] Players, string[] Buffs, string[] Skills, string[] ReadySkills,
    CombatAction? Action, long AcceptedInputs, BotEvent[] Events);

public sealed class BotRuntime(IBotInput input, INavigationMode? navigation = null)
{
    private readonly object sync = new();
    private readonly CombatEngine engine = new();
    private readonly INavigationMode navigation = navigation ?? new ManualNavigation();
    private readonly Queue<BotEvent> events = new();
    private BotConfig config = new();
    private long revision;
    private bool running;
    private long lastTick;
    private long updatedAt;
    private long acceptedInputs;
    private string reason = "已停止";
    private Observation? observation;
    private CombatAction? action;

    public void Apply(BotConfig next, long nextRevision)
    {
        var copy = next.Copy();
        lock (this.sync)
        {
            this.StopLocked("配置已应用，请启动");
            this.config = copy;
            this.revision = nextRevision;
        }
    }

    public (BotConfig Config, long Revision) ReadConfig()
    { lock (this.sync) return (this.config.Copy(), this.revision); }

    public void Start(long now)
    {
        lock (this.sync)
        {
            if (!this.config.Combat.Enabled || !this.config.Combat.Rules.Any(r => r.Enabled && r.Key != 0))
                throw new ArgumentException("请先启用战斗，并保存至少一条已绑定按键的技能规则。");
            this.running = true;
            this.lastTick = now;
            this.reason = "等待游戏画面";
            this.Log(this.config.Preview ? "预览已启动，不发送按键" : "战斗已启动");
        }
    }

    public void Stop(string reason = "已停止") { lock (this.sync) this.StopLocked(reason); }
    private void StopLocked(string reason)
    {
        if (this.running) this.Log(reason);
        this.running = false;
        this.reason = reason;
        this.action = null;
        input.Release();
        this.navigation.Stop();
        // Cooldowns survive pauses, configuration saves and area changes.
    }

    public void Watchdog(long now, bool foreground, bool escape)
    {
        lock (this.sync)
        {
            if (escape) this.StopLocked("Esc 已停止");
            else if (this.running && now - this.lastTick > 400) this.StopLocked("画面更新中断，请重新启动（检查 F9）");
            input.Expire(now, this.running && !this.config.Preview && foreground && now - this.lastTick <= 150);
        }
    }

    // Only the host game tick creates observations. HTTP and the watchdog never read game memory.
    public void Tick(Observation? next, string blocked, long expectedRevision, long now, Func<bool> recheck)
    {
        lock (this.sync)
        {
            if (expectedRevision != this.revision) return;
            this.lastTick = now;
            this.updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            this.observation = next;
            this.action = null;
            if (!this.running) { input.Release(); return; }
            var snapshot = next?.Combat;
            if (blocked.Length != 0 || snapshot == null || !snapshot.CanFight || !snapshot.Player.IsAlive)
            {
                this.reason = blocked.Length != 0 ? blocked : "角色数据不可用、入场保护或非战斗区域";
                input.Release();
                this.navigation.Stop();
                return;
            }
            if (!this.navigation.AllowsCombat(snapshot, now)) { this.reason = "导航处理中"; input.Release(); return; }
            this.action = this.engine.Evaluate(this.config.Combat, snapshot, now, input.IsKeyAvailable);
            if (this.action == null) { this.reason = "等待条件或技能间隔"; return; }
            if (this.config.Preview) { input.Release(); this.reason = "预览：" + this.action.Name; return; }
            if (!recheck()) { input.Release(); this.action = null; this.reason = "游戏状态已改变"; return; }
            if (!input.TryPress(this.action.Key, this.action.PressMilliseconds, now))
            { this.reason = "按键未接受（检查焦点或手动按键）"; return; }
            this.engine.RecordAccepted(this.action, now);
            this.acceptedInputs++;
            this.reason = "已发送：" + this.action.Name;
            this.Log(this.reason + $" · key {this.action.Key} · 敌人 {this.action.MatchingEnemies}");
        }
    }

    public BotStatus Status()
    {
        lock (this.sync)
        {
            var player = this.observation?.Combat?.Player;
            var enemies = this.observation?.Combat?.Enemies.Where(e => e.IsCandidate).ToArray() ?? [];
            return new(this.running, this.config.Preview, this.navigation.Name, this.reason, this.updatedAt,
                this.observation?.Character ?? "", this.observation?.Area ?? "", player?.LifePercent, player?.EnergyShieldPercent, player?.ManaPercent,
                enemies.Count(e => e.Rarity == EnemyRarity.Normal), enemies.Count(e => e.Rarity == EnemyRarity.Magic),
                enemies.Count(e => e.Rarity == EnemyRarity.Rare), enemies.Count(e => e.Rarity == EnemyRarity.Unique),
                this.observation?.Players ?? [], player?.Buffs?.Order().ToArray() ?? [], this.observation?.Skills ?? [],
                player?.UsableSkills?.Order().ToArray() ?? [], this.action, this.acceptedInputs, this.events.ToArray());
        }
    }

    private void Log(string message)
    {
        this.events.Enqueue(new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), message));
        while (this.events.Count > 60) this.events.Dequeue();
    }
}
