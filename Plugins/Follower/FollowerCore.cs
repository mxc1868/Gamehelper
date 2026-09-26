namespace Follower;

using System.Numerics;
using ClickableTransparentOverlay.Win32;
using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using ImGuiNET;
using Newtonsoft.Json;

public sealed class FollowerCore : PCore<FollowerSettings>
{
    private readonly FollowSession session = new();
    private MovementInput? input;
    private ActiveCoroutine? areaChanged;
    private ActiveCoroutine? gameClosed;
    private CancellationTokenSource? searchCancellation;
    private Task<List<Vector2>?>? search;
    private List<Vector2>? route;
    private NavigationGrid? grid;
    private bool running;
    private bool toggleWasDown;
    private IntPtr areaAddress;
    private string areaHash = string.Empty;
    private IntPtr targetAddress;
    private uint targetId;
    private long nextSearch;
    private long routeAt;
    private long searchAt;
    private long lastFrame;
    private long nextDoorRead;
    private Vector2 searchGoal;
    private Vector2 routeGoal;
    private float distance;
    private MoveKeys planned;
    private string status = "stopped";
    private string error = string.Empty;
    private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");
    private string LeaderName => this.Settings.LeaderName.Trim();
    private string ToggleKeyName => ((VK)this.Settings.ToggleKey).ToString();

    public override void OnEnable(bool isGameOpened)
    {
        this.OnDisable();
        try
        {
            if (File.Exists(this.SettingsPath))
                this.Settings = JsonConvert.DeserializeObject<FollowerSettings>(File.ReadAllText(this.SettingsPath)) ?? new();
        }
        catch (Exception ex) { this.Settings = new(); this.error = ex.Message; }
        this.Settings.Normalize();
        this.input = new();
        this.areaChanged = CoroutineHandler.Start(this.StopOn(RemoteEvents.AreaChanged));
        this.gameClosed = CoroutineHandler.Start(this.StopOn(GameHelperEvents.OnClose));
        this.lastFrame = 0;
        this.toggleWasDown = MovementInput.IsDown(this.Settings.ToggleKey);
    }

    public override void OnDisable()
    {
        this.Halt("stopped");
        this.areaChanged?.Cancel();
        this.gameClosed?.Cancel();
        this.areaChanged = this.gameClosed = null;
        this.input?.Dispose();
        this.input = null;
    }

    private IEnumerator<Wait> StopOn(Event signal)
    {
        while (true) { yield return new Wait(signal); this.Halt("area_changed"); }
    }

    private void ClearNavigation()
    {
        this.input?.Stop();
        this.searchCancellation?.Cancel();
        this.searchCancellation?.Dispose();
        this.searchCancellation = null;
        this.search = null;
        this.route = null;
        this.grid = null;
        this.targetAddress = IntPtr.Zero;
        this.areaAddress = IntPtr.Zero;
        this.nextSearch = this.nextDoorRead = 0;
        this.session.Reset();
        this.planned = MoveKeys.None;
    }

    private void Halt(string reason)
    {
        this.running = false;
        this.ClearNavigation();
        this.status = reason;
    }

    public override void SaveSettings()
    {
        this.Settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(this.SettingsPath)!);
        File.WriteAllText(this.SettingsPath + ".tmp", JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        File.Move(this.SettingsPath + ".tmp", this.SettingsPath, true);
    }

    public override void DrawSettings()
    {
        // Changing settings never leaves a previous movement command held.
        if (this.running) this.Halt("settings_open");
        ImGui.TextWrapped(this.PluginText.F("hint", "Select WASD movement in PoE2. {0} starts/stops; Escape stops. Foreground game only. Start in preview mode and inspect the route.", this.ToggleKeyName));
        if (ImGui.BeginCombo(this.PluginText.Label("toggle_key", "Start/stop hotkey", "ToggleKey"), this.ToggleKeyName))
        {
            foreach (var key in Enum.GetValues<VK>().Distinct())
                if (FollowerSettings.IsToggleKeyAllowed((int)key) && ImGui.Selectable(key.ToString(), (int)key == this.Settings.ToggleKey))
                {
                    this.Settings.ToggleKey = (int)key;
                    this.toggleWasDown = MovementInput.IsDown(this.Settings.ToggleKey);
                    this.SaveSettings();
                }
            ImGui.EndCombo();
        }
        var selected = string.IsNullOrEmpty(this.LeaderName) ? this.PluginText.T("choose_leader", "Select the leader from nearby players") : this.LeaderName;
        if (ImGui.BeginCombo(this.PluginText.Label("nearby", "Choose a nearby player", "Nearby"), selected))
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Core.States.GameCurrentState == GameStateTypes.InGameState)
            {
                var area = Core.States.InGameStateObject.CurrentAreaInstance;
                foreach (var entity in area.AwakeEntities.Values)
                    if (entity.IsValid && entity.Address != area.Player.Address && entity.EntityType == EntityTypes.Player &&
                        entity.TryGetComponent<Player>(out var player) && !string.IsNullOrWhiteSpace(player.Name))
                        names.Add(player.Name);
            }
            foreach (var name in names)
                if (ImGui.Selectable(name, string.Equals(name, this.LeaderName, StringComparison.OrdinalIgnoreCase)))
                {
                    this.Settings.LeaderName = name;
                    this.SaveSettings();
                }
            if (names.Count == 0) ImGui.TextDisabled(this.PluginText.T("no_players", "No nearby players detected. Move into the same area as the leader."));
            ImGui.EndCombo();
        }
        ImGui.Checkbox(this.PluginText.Label("preview", "Preview only (no keyboard input)", "Preview"), ref this.Settings.PreviewOnly);
        ImGui.SliderFloat(this.PluginText.Label("stop", "Stop distance (grid cells)", "Stop"), ref this.Settings.StopDistance, 3, 100);
        ImGui.SliderFloat(this.PluginText.Label("resume", "Resume distance", "Resume"), ref this.Settings.ResumeDistance, this.Settings.StopDistance + 3, 150);
        ImGui.SliderInt(this.PluginText.Label("clearance", "Wall clearance (grid cells)", "Clearance"), ref this.Settings.Clearance, 0, 2);
        ImGui.SliderInt(this.PluginText.Label("repath", "Recalculate route (ms)", "Repath"), ref this.Settings.RepathMilliseconds, 150, 1000);
        ImGui.SliderInt(this.PluginText.Label("stuck", "Stop if stuck for (ms)", "Stuck"), ref this.Settings.StuckMilliseconds, 1000, 10000);
        ImGui.Checkbox(this.PluginText.Label("show_status", "Show status", "Status"), ref this.Settings.ShowStatus);
        ImGui.Checkbox(this.PluginText.Label("show_route", "Show route", "Route"), ref this.Settings.ShowRoute);
        this.Settings.Normalize();
        ImGui.TextWrapped(this.PluginText.F("limits", "Follows a named visible player in the same area. Closed doors need manual opening. No portals or background dual-client input. Losing the target, focus or area stops following; press {0} to resume.", this.ToggleKeyName));
        ImGui.TextWrapped(this.StatusText());
        if (!string.IsNullOrEmpty(this.error)) ImGui.TextWrapped(this.error);
    }

    private string StatusText() => this.PluginText.F("status." + this.status, this.status, this.ToggleKeyName);

    public override void DrawUI()
    {
        try
        {
            var now = Environment.TickCount64;
            if (this.running && this.lastFrame != 0 && now - this.lastFrame > 300) this.Halt("frame_gap");
            this.lastFrame = now;
            var toggleDown = MovementInput.IsDown(this.Settings.ToggleKey);
            if (toggleDown && !this.toggleWasDown && MovementInput.IsForeground(Core.Process.Pid) && !Core.IsSettingsMenuOpen)
            {
                if (this.running) this.Halt("stopped");
                else { this.ClearNavigation(); this.running = true; this.error = string.Empty; }
            }
            this.toggleWasDown = toggleDown;
            if (MovementInput.IsDown(0x1B)) this.Halt("stopped");
            if (this.running) this.Tick(now);
            this.DrawOverlay();
        }
        catch (Exception ex) { this.Halt("error"); this.error = ex.Message; }
    }

    private void Tick(long now)
    {
        if (!MovementInput.IsForeground(Core.Process.Pid)) { this.Halt("unfocused"); return; }
        if (Core.States.GameCurrentState != GameStateTypes.InGameState || Core.GHSettings.EnableControllerMode) { this.Halt("game_state"); return; }
        var game = Core.States.InGameStateObject;
        var area = game.CurrentAreaInstance;
        var ui = game.GameUi;
        if (Core.IsSettingsMenuOpen || ui.Address == IntPtr.Zero || ui.ChatParent.Address == IntPtr.Zero ||
            ui.ChatParent.IsChatActive || ui.IsAnyLargePanelOpen || MovementInput.IsDown(0x0D))
        { this.Halt("panel"); return; }
        if (area.Address == IntPtr.Zero || !area.Player.IsValid ||
            !area.Player.TryGetComponent<Life>(out var life) || !life.IsAlive ||
            !area.Player.TryGetComponent<Render>(out var playerRender))
        { this.Halt("player_invalid"); return; }
        if (this.areaAddress != IntPtr.Zero && (this.areaAddress != area.Address || this.areaHash != area.AreaHash))
        { this.Halt("area_changed"); return; }
        this.areaAddress = area.Address;
        this.areaHash = area.AreaHash;
        if (string.IsNullOrEmpty(this.LeaderName)) { this.Halt("leader_name"); return; }
        var targets = area.AwakeEntities.Values.Where(e => e.IsValid && e.Address != area.Player.Address &&
            e.EntityType == EntityTypes.Player && e.TryGetComponent<Player>(out var p) &&
            string.Equals(p.Name, this.LeaderName, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (targets.Length != 1 || !targets[0].TryGetComponent<Render>(out var targetRender) ||
            !targets[0].TryGetComponent<Life>(out var targetLife) || !targetLife.IsAlive)
        { this.Halt("target_missing"); return; }
        var target = targets[0];
        if (this.targetAddress != IntPtr.Zero && (this.targetAddress != target.Address || this.targetId != target.Id))
        { this.Halt("target_changed"); return; }
        this.targetAddress = target.Address;
        this.targetId = target.Id;
        var p = playerRender.GridPosition;
        var t = targetRender.GridPosition;
        var player = new Vector2(p.X, p.Y);
        var goal = new Vector2(t.X, t.Y);
        this.distance = Vector2.Distance(player, goal);
        if (!float.IsFinite(this.distance) || this.distance > 600 || area.GridWalkableData.Length == 0)
        { this.Halt("terrain_missing"); return; }
        if (this.grid == null || now >= this.nextDoorRead)
        {
            this.grid = BuildGrid(area, this.Settings.Clearance);
            this.nextDoorRead = now + 150;
        }
        if (!this.grid.Contains(player) || !this.grid.Contains(goal)) { this.Halt("terrain_missing"); return; }
        if (!this.session.NeedsMovement(this.distance, this.grid.Clear(player, goal), this.Settings.StopDistance, this.Settings.ResumeDistance))
        {
            this.input?.Stop();
            this.planned = MoveKeys.None;
            this.session.IsStuck(player, false, now, this.Settings.StuckMilliseconds);
            this.status = "in_range";
            return;
        }
        if (this.search?.IsCompleted == true)
        {
            this.route = this.search.GetAwaiter().GetResult();
            this.routeAt = this.searchAt;
            this.routeGoal = this.searchGoal;
            this.search = null;
            this.searchCancellation?.Dispose();
            this.searchCancellation = null;
        }
        if (this.search == null && now >= this.nextSearch)
        {
            this.searchGoal = goal;
            this.searchAt = now;
            var snapshot = this.grid;
            this.searchCancellation = new();
            var token = this.searchCancellation.Token;
            this.search = Task.Run(() => snapshot.FindPath(player, goal, token));
            this.nextSearch = now + this.Settings.RepathMilliseconds;
        }
        var steer = this.route != null && now - this.routeAt < 1500 && Vector2.Distance(this.routeGoal, goal) < 30
            ? this.grid.Steer(this.route, player) : null;
        if (steer == null)
        {
            this.input?.Stop();
            this.planned = MoveKeys.None;
            this.session.IsStuck(player, false, now, this.Settings.StuckMilliseconds);
            this.status = this.search == null ? "no_path" : "searching";
            return;
        }
        var world = game.CurrentWorldInstance;
        var ratio = area.WorldToGridConvertor;
        var origin = world.WorldToScreen(player * ratio, playerRender.TerrainHeight);
        var screenX = world.WorldToScreen((player + Vector2.UnitX) * ratio, playerRender.TerrainHeight) - origin;
        var screenY = world.WorldToScreen((player + Vector2.UnitY) * ratio, playerRender.TerrainHeight) - origin;
        this.planned = Steering.Choose(steer.Value - player, screenX, screenY, step => this.grid.Clear(player, player + step));
        if (this.Settings.PreviewOnly) { this.input?.Stop(); this.status = "preview"; return; }
        if (this.input?.HasManualMovement() == true || MovementInput.HasModifier()) { this.Halt("manual"); return; }
        if (this.session.IsStuck(player, this.planned != MoveKeys.None, now, this.Settings.StuckMilliseconds))
        { this.Halt("stuck"); return; }
        if (this.planned == MoveKeys.None) { this.input?.Stop(); this.status = "no_direction"; return; }
        if (area.Address != this.areaAddress || area.AreaHash != this.areaHash || !target.IsValid ||
            target.Address != this.targetAddress || target.Id != this.targetId ||
            Core.States.GameCurrentState != GameStateTypes.InGameState || ui.ChatParent.IsChatActive)
        { this.Halt("target_changed"); return; }
        if (this.input?.Apply(this.planned, Core.Process.Pid) != true) { this.Halt("input_failed"); return; }
        this.status = "following";
    }

    private static NavigationGrid BuildGrid(AreaInstance area, int clearance)
    {
        var opened = new HashSet<(int, int)>();
        var closed = new HashSet<(int, int)>();
        foreach (var entity in area.AwakeEntities.Values)
        {
            if (!entity.IsValid) continue;
            var hasBlockage = entity.TryGetComponent<TriggerableBlockage>(out var blockage);
            if (!hasBlockage && !entity.Path.Contains("Door", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entity.TryGetComponent<Render>(out var render)) continue;
            var p = render.GridPosition;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) continue;
            // Unknown door types remain blocked. Radar's unconditional override is
            // suitable for suggested routes, but cannot establish an open doorway.
            var cells = hasBlockage && !blockage!.IsBlocked ? opened : closed;
            for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++) cells.Add(((int)MathF.Round(p.X) + x, (int)MathF.Round(p.Y) + y));
        }
        return new(area.GridWalkableData, area.TerrainMetadata.BytesPerRow, clearance, opened, closed);
    }

    private void DrawOverlay()
    {
        if (!MovementInput.IsForeground(Core.Process.Pid)) return;
        if (this.Settings.ShowStatus)
        {
            ImGui.SetNextWindowBgAlpha(0.75f);
            ImGui.SetNextWindowPos(new Vector2(30, 180), ImGuiCond.FirstUseEver);
            ImGui.Begin("Follower##FollowerStatus", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing);
            ImGui.TextUnformatted(this.StatusText());
            ImGui.TextUnformatted($"{this.LeaderName} | {this.distance:0.0} | {this.planned}");
            ImGui.End();
        }
        if (!this.running || !this.Settings.ShowRoute || this.route == null || Core.States.GameCurrentState != GameStateTypes.InGameState) return;
        var game = Core.States.InGameStateObject;
        var area = game.CurrentAreaInstance;
        if (!area.Player.TryGetComponent<Render>(out var render)) return;
        var origin = new Vector2(Core.Process.WindowArea.X, Core.Process.WindowArea.Y);
        var points = this.route.Take(1000).Select(p => origin + game.CurrentWorldInstance.WorldToScreen(p * area.WorldToGridConvertor, render.TerrainHeight)).ToArray();
        var draw = ImGui.GetBackgroundDrawList();
        for (var i = 1; i < points.Length; i++)
            if (float.IsFinite(points[i - 1].X + points[i - 1].Y + points[i].X + points[i].Y))
                draw.AddLine(points[i - 1], points[i], 0xFF00D7FF, 2);
    }
}
