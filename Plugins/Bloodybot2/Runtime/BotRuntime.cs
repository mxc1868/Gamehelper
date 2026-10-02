namespace Bloodybot2.Runtime;

using BloodyBot.Combat;
using Bloodybot2.Configuration;

public interface IBotInput
{
    bool SkillHeld { get; }
    bool IsKeyAvailable(int key);
    bool TryPress(int key, int milliseconds, long now);
    void Release();
    void Expire(long now, bool allowed);
}

// Navigation owns movement and gives combat an opportunity when movement can yield.
// Follow reserves correction/recovery ticks without teaching Combat about paths.
public interface INavigationMode
{
    string Name { get; }
    string Status { get; }
    bool IsActive { get; }
    void Apply(BotConfig config);
    void Start();
    void Suspend();
    void Tick(long now, bool preview, Func<bool> combat);
    void DrawOverlay();
    void CombatAccepted(int milliseconds);
    void Stop();
}

public sealed record Observation(CombatSnapshot? Combat, string Character, string Area, string[] Players, string[] Skills);
public sealed record BotEvent(long At, string Message);
public sealed record BotStatus(bool Running, bool Preview, string Navigation, string Reason, long UpdatedAt,
    string Character, string Area, float? Life, float? Shield, float? Mana,
    int Normal, int Magic, int Rare, int Unique, string[] Players, string[] Buffs, string[] Skills, string[] ReadySkills,
    CombatAction? Action, long AcceptedInputs, BotEvent[] Events);

public sealed class BotRuntime(IBotInput input, INavigationMode navigation)
{
    private readonly object sync = new();
    private readonly CombatEngine engine = new();
    private readonly INavigationMode navigation = navigation;
    private readonly Queue<BotEvent> events = new();
    private BotConfig config = new();
    private long revision;
    private bool running;
    private long lastTick;
    private long updatedAt;
    private long acceptedInputs;
    private long busyUntil;
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
            this.navigation.Apply(copy);
        }
    }

    public (BotConfig Config, long Revision) ReadConfig()
    { lock (this.sync) return (this.config.Copy(), this.revision); }

    public int ToggleKey { get { lock (this.sync) return this.config.ToggleKey; } }

    public void Start(long now)
    {
        lock (this.sync)
        {
            this.config.ValidateStart();
            this.navigation.Start();
            this.running = true;
            this.lastTick = now;
            this.reason = "等待游戏画面";
            this.Log(this.config.Preview ? "Follow 预览已启动，不发送按键" : "Follow 已启动");
        }
    }

    public void Stop(string reason = "已停止") { lock (this.sync) this.StopLocked(reason); }
    private void StopLocked(string reason)
    {
        if (this.running) this.Log(reason);
        this.running = false;
        this.reason = reason;
        this.action = null;
        this.busyUntil = 0;
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
            if (blocked.Length != 0)
            {
                this.reason = blocked;
                input.Release();
                this.navigation.Suspend();
                return;
            }
            input.Expire(now, !this.config.Preview);
            this.reason = "等待技能条件";
            this.navigation.Tick(now, this.config.Preview, () => this.TickCombat(next?.Combat, now, recheck));
            if (!this.navigation.IsActive) this.StopLocked(this.navigation.Status);
            else this.reason = this.navigation.Status + " · " + this.reason;
        }
    }

    // Host render thread only. Drawing never advances navigation, sends input or
    // refreshes the watchdog; HTTP/config changes serialize with this snapshot.
    public void DrawOverlay()
    {
        lock (this.sync) this.navigation.DrawOverlay();
    }

    private bool TickCombat(CombatSnapshot? snapshot, long now, Func<bool> recheck)
    {
        if (input.SkillHeld || now < this.busyUntil) { this.reason = "施法停顿"; return true; }
        if (snapshot == null || !snapshot.CanFight || !snapshot.Player.IsAlive) { this.reason = "暂不可战斗"; return false; }
        this.action = this.engine.Evaluate(this.config.Combat, snapshot, now,
            key => key != this.config.ToggleKey && input.IsKeyAvailable(key));
        if (this.action == null) return false;
        if (this.config.Preview) { this.reason = "预览：" + this.action.Name; return false; }
        if (!recheck()) { input.Release(); this.action = null; this.reason = "游戏状态已改变"; return true; }
        if (!input.TryPress(this.action.Key, this.action.PressMilliseconds, now))
        { this.reason = "按键未接受"; return false; }
        this.engine.RecordAccepted(this.action, now);
        var duration = this.action.PressMilliseconds + this.action.PauseMilliseconds;
        this.navigation.CombatAccepted(duration);
        this.busyUntil = now + duration;
        this.acceptedInputs++;
        this.reason = "已发送：" + this.action.Name;
        this.Log(this.reason + $" · key {this.action.Key} · 敌人 {this.action.MatchingEnemies}");
        return true;
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
