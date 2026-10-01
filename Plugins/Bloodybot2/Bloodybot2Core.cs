namespace Bloodybot2;

using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using ImGuiNET;
using Bloodybot2.Configuration;
using Bloodybot2.Game;
using Bloodybot2.Runtime;
using Bloodybot2.Web;
using Bloodybot2.Navigation;

public sealed class Bloodybot2Settings : IPSettings;

public sealed class Bloodybot2Core : PCore<Bloodybot2Settings>
{
    private MovementInput? input;
    private BotRuntime? runtime;
    private WebConfigServer? web;
    private Timer? watchdog;
    private ActiveCoroutine? areaChanged;
    private ActiveCoroutine? gameClosed;
    private bool toggleWasDown;
    private long nextRead;
    private string error = "";
    private string lastArea = "";
    public override string GetDescription() => "Bloodybot2 · Follow 导航与独立战斗模块，在 Chrome 配置。默认 F6 启停，Esc 停止。";
    // The loader supersedes old Follower binaries; this also protects mixed hosts.
    public override IReadOnlyCollection<string> ConflictsWith => ["Follower"];

    public override void OnEnable(bool isGameOpened)
    {
        this.OnDisable();
        try
        {
            this.error = "";
            this.lastArea = "";
            this.nextRead = 0;
            var input = this.input = new();
            var runtime = this.runtime = new(input, new FollowMode(input));
            this.web = new(new ConfigStore(Path.Combine(this.DllDirectory, "config"),
                Path.Combine(Path.GetDirectoryName(this.DllDirectory)!, "Follower", "config", "settings.txt")), runtime);
            this.web.Start();
            this.toggleWasDown = MovementInput.IsDown(runtime.ReadConfig().Config.ToggleKey);
            this.watchdog = new(_ => runtime.Watchdog(Environment.TickCount64, input.Foreground,
                input.Foreground && (MovementInput.IsDown(0x1B) || input.ConsumeStopRequest())), null, 25, 25);
            this.areaChanged = CoroutineHandler.Start(this.WaitOn(RemoteEvents.AreaChanged, "区域已改变，请重新启动"));
            this.gameClosed = CoroutineHandler.Start(this.WaitOn(GameHelperEvents.OnClose, "游戏已关闭"));
            AppDomain.CurrentDomain.ProcessExit += this.OnExit;
        }
        catch (Exception ex) { this.error = ex.Message; this.OnDisable(); }
    }

    public override void OnDisable()
    {
        this.areaChanged?.Cancel(); this.gameClosed?.Cancel();
        this.areaChanged = this.gameClosed = null;
        this.web?.Dispose(); this.web = null;
        this.runtime?.Stop("插件已禁用");
        this.watchdog?.Dispose(); this.watchdog = null;
        this.input?.Dispose(); this.input = null;
        this.runtime = null;
        AppDomain.CurrentDomain.ProcessExit -= this.OnExit;
    }
    private void OnExit(object? sender, EventArgs args) => this.runtime?.Stop("程序退出");
    private IEnumerator<Wait> WaitOn(Event signal, string message)
    { while (true) { yield return new Wait(signal); this.runtime?.Stop(message); this.lastArea = ""; } }

    public override void DrawSettings()
    {
        ImGui.TextWrapped("Configure Bloodybot2 in Chrome. Start/stop hotkey: General (default F6). Esc: stop.");
        if (this.web != null)
        {
            ImGui.TextUnformatted(this.web.Url);
            if (ImGui.Button("Open in Chrome"))
                try { ChromeBrowser.Open(this.web.Url); } catch (Exception ex) { this.error = ex.Message; }
            ImGui.SameLine();
            if (ImGui.Button("Copy URL")) ImGui.SetClipboardText(this.web.Url);
            ImGui.TextWrapped(this.runtime?.Status().Reason ?? "");
        }
        if (this.error.Length != 0) ImGui.TextWrapped(this.error);
    }

    // WebConfigServer is the sole configuration writer; never overwrite its file on host autosave.
    public override void SaveSettings() { }

    public override void DrawUI()
    {
        if (this.runtime == null || this.input == null) return;
        try
        {
            this.input.ObserveProcess(Core.Process.Pid);
            var now = Environment.TickCount64;
            var toggle = MovementInput.IsDown(this.runtime.ToggleKey);
            var togglePressed = toggle && !this.toggleWasDown;
            this.toggleWasDown = toggle;
            if (togglePressed && this.input.Foreground && !Core.IsSettingsMenuOpen)
            {
                if (this.runtime.Status().Running) this.runtime.Stop("快捷键已停止");
                else this.runtime.Start(now);
            }
            if (now < this.nextRead) return;
            this.nextRead = now + 75;
            var (config, revision) = this.runtime.ReadConfig();
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            { this.runtime.Tick(null, "等待进入游戏", revision, now, () => false); return; }
            var game = Core.States.InGameStateObject;
            var area = game.CurrentAreaInstance;
            var identity = area.Address + ":" + area.AreaHash;
            if (this.lastArea.Length != 0 && this.lastArea != identity) this.runtime.Stop("区域已改变，请重新启动");
            this.lastArea = identity;
            var players = area.AwakeEntities.Values.Prepend(area.Player).DistinctBy(e => e.Address)
                .Where(e => e.IsValid && e.EntityType == EntityTypes.Player && CombatSnapshotReader.ReadComponent(e, out Player _)).ToArray();
            static string Name(Entity entity) => CombatSnapshotReader.ReadComponent(entity, out Player p) ? p.Name : "";
            var names = players.Select(Name).Where(n => n.Length != 0).Distinct().Order().ToArray();
            var matches = config.MonitorCharacter.Trim().Length == 0 ? [area.Player] :
                players.Where(e => Name(e).Equals(config.MonitorCharacter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            var selected = matches.Length == 1 ? matches[0] : null;
            var details = game.CurrentWorldInstance.AreaDetails;
            var blocked = this.Blocked(config);
            var snapshot = selected == null ? null : CombatSnapshotReader.Read(area, selected, now,
                details.Address != IntPtr.Zero && !details.IsTown && !details.IsHideout);
            var skills = selected != null && CombatSnapshotReader.ReadComponent(selected, out Actor actor) ? actor.ActiveSkills.Keys.Order().ToArray() : [];
            var observation = new Observation(snapshot, selected == null ? "" : Name(selected), details.Name, names, skills);
            var address = selected?.Address ?? IntPtr.Zero;
            var id = selected?.Id ?? 0;
            this.runtime.Tick(observation, blocked, revision, now, () =>
                this.Blocked(config).Length == 0 && identity == area.Address + ":" + area.AreaHash &&
                selected != null && selected.Address == address && selected.Id == id && selected.IsValid &&
                CombatSnapshotReader.ReadComponent(selected, out Life life) && life.IsAlive &&
                Environment.TickCount64 - now <= 150);
        }
        catch (Exception ex) { this.error = ex.Message; this.runtime.Stop("读取失败：" + ex.Message); }
    }

    private string Blocked(BotConfig config)
    {
        if (Core.States.GameCurrentState != GameStateTypes.InGameState) return "等待进入游戏";
        if (this.input?.Foreground != true) return "等待游戏获得焦点";
        if (this.input.CanInput != true) return "手动按键暂时阻止输入";
        var ui = Core.States.InGameStateObject.GameUi;
        if (Core.IsSettingsMenuOpen || ui.Address == IntPtr.Zero || ui.IsAnyLargePanelOpen || ui.ChatParent.IsChatActive) return "面板或聊天已打开";
        if (ui.ChatParent.Address == IntPtr.Zero && !(Core.GHSettings.EnableControllerMode && config.AllowControllerWithoutChat))
            return "聊天状态不可读；手柄模式需在网页中明确启用";
        return "";
    }
}
