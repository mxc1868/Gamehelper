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

public sealed partial class FollowerCore : PCore<FollowerSettings>
{
    private readonly FollowSession session = new();
    private readonly CoopCoordinator coop = new();
    private readonly UnstuckRecovery recovery = new();
    private MovementInput? input;
    private ActiveCoroutine? areaChanged;
    private ActiveCoroutine? gameClosed;
    private CancellationTokenSource? searchCancellation;
    private Task<List<Vector2>?>? search;
    private List<Vector2>? route;
    private NavigationGrid? grid;
    private readonly FollowRunState run = new();
    private bool toggleWasDown;
    private IntPtr areaAddress;
    private string areaHash = string.Empty;
    private IntPtr targetAddress;
    private uint targetId;
    private IntPtr followerAddress;
    private uint followerId;
    private IntPtr secondaryAddress;
    private uint secondaryId;
    private float followerHeight;
    private long nextSearch;
    private long routeAt;
    private long searchAt;
    private long lastFrame;
    private long nextDoorRead;
    private Vector2 searchGoal;
    private Vector2 routeGoal;
    private float distance;
    private float playerGap;
    private bool correctingP2;
    private MoveKeys planned;
    private string error = string.Empty;
    private string manualKeys = string.Empty;
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
        this.combat.Reset();
        this.input = new();
        this.areaChanged = CoroutineHandler.Start(this.WaitOn(RemoteEvents.AreaChanged));
        this.gameClosed = CoroutineHandler.Start(this.WaitOn(GameHelperEvents.OnClose));
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

    private IEnumerator<Wait> WaitOn(Event signal)
    {
        while (true) { yield return new Wait(signal); this.Suspend("area_changed"); }
    }

    private void ClearNavigation(bool preserveFollowState = false)
    {
        this.ClearCombat();
        this.ClearRoute();
        this.grid = null;
        this.targetAddress = this.followerAddress = this.secondaryAddress = IntPtr.Zero;
        this.areaAddress = IntPtr.Zero;
        this.nextDoorRead = 0;
        if (!preserveFollowState)
        {
            this.coop.Reset();
            this.session.Reset();
            this.correctingP2 = false;
        }
        this.distance = this.playerGap = 0;
    }

    private void ClearRoute()
    {
        this.DiscardRoute();
        this.session.ResetProgress();
        this.recovery.Reset();
    }

    private void DiscardRoute()
    {
        this.input?.Stop();
        this.searchCancellation?.Cancel();
        this.searchCancellation?.Dispose();
        this.searchCancellation = null;
        this.search = null;
        this.route = null;
        this.nextSearch = 0;
        this.planned = MoveKeys.None;
    }

    private void Halt(string reason)
    {
        this.run.Stop(reason);
        this.ClearNavigation();
    }

    private void Suspend(string reason, int retryMilliseconds = 250)
    {
        this.run.Fail(reason, Environment.TickCount64, retryMilliseconds);
        this.ClearNavigation(this.run.IsEnabled && reason is not
            ("area_changed" or "game_state" or "follower_changed" or "target_changed"));
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
        if (this.run.IsEnabled) this.Suspend("settings_open");
        if (ImGui.Checkbox(this.PluginText.Label("local_coop", "Local co-op: shared WASD with P2 arrow correction", "LocalCoop"), ref this.Settings.LocalCoopFollow))
        {
            this.ClearNavigation();
            this.Settings.PreviewOnly = true;
            this.SaveSettings();
        }
        ImGui.TextWrapped(this.Settings.LocalCoopFollow
            ? this.PluginText.F("coop_hint", "Select the leader, P1 and P2. WASD moves both players toward the leader; arrow keys must control only P2 through your mapping. {0} starts/stops; Escape stops. Check both phases in preview first.", this.ToggleKeyName)
            : this.PluginText.F("hint", "Select WASD movement in PoE2. {0} starts/stops; Escape stops. Foreground game only. Start in preview mode and inspect the route.", this.ToggleKeyName));
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
        this.DrawPlayerChoice("nearby", "Leader to follow", ref this.Settings.LeaderName, includeLocal: this.Settings.LocalCoopFollow);
        if (this.Settings.LocalCoopFollow)
        {
            this.DrawPlayerChoice("p1", "P1: shared WASD navigation", ref this.Settings.P1Name, includeLocal: true);
            this.DrawPlayerChoice("p2", "P2: controlled by arrows", ref this.Settings.P2Name, includeLocal: true);
            ImGui.SliderFloat(this.PluginText.Label("p2_lag", "P1/P2 gap to start correction", "P2Lag"), ref this.Settings.P2LagDistance, 6, 150);
            ImGui.SliderFloat(this.PluginText.Label("p2_rejoin", "P1/P2 gap to resume WASD", "P2Rejoin"), ref this.Settings.P2RejoinDistance, 3, this.Settings.P2LagDistance - 3);
            ImGui.TextWrapped(this.PluginText.T("coop_limits", "Correction releases WASD, pauses P1, and routes P2 to P1. Once reunited, arrows are released and shared WASD resumes toward the leader. Distances are in grid cells. Stop before opening controller chat; its state is unavailable."));
        }
        ImGui.Checkbox(this.PluginText.Label("preview", "Preview only (no keyboard input)", "Preview"), ref this.Settings.PreviewOnly);
        ImGui.SliderFloat(this.PluginText.Label("stop", "Stop distance (grid cells)", "Stop"), ref this.Settings.StopDistance, 3, 100);
        ImGui.SliderFloat(this.PluginText.Label("resume", "Resume distance", "Resume"), ref this.Settings.ResumeDistance, this.Settings.StopDistance + 3, 150);
        ImGui.SliderInt(this.PluginText.Label("clearance", "Preferred wall clearance (grid cells)", "Clearance"), ref this.Settings.Clearance, 0, 2);
        ImGui.TextWrapped(this.PluginText.T("clearance_hint", "Prefer routes away from walls, but allow closer movement when starting beside a wall or passing through a narrow corridor."));
        ImGui.SliderInt(this.PluginText.Label("repath", "Recalculate route (ms)", "Repath"), ref this.Settings.RepathMilliseconds, 150, 1000);
        ImGui.SliderInt(this.PluginText.Label("stuck", "Start recovery after no progress (ms)", "Stuck"), ref this.Settings.StuckMilliseconds, 1000, 10000);
        ImGui.Checkbox(this.PluginText.Label("show_status", "Show status", "Status"), ref this.Settings.ShowStatus);
        ImGui.Checkbox(this.PluginText.Label("show_route", "Show route", "Route"), ref this.Settings.ShowRoute);
        this.DrawCombatSettings();
        this.Settings.Normalize();
        ImGui.TextWrapped(this.PluginText.F("limits", "Temporary interruptions release movement and resume automatically. Stalls force short presses toward the current target without terrain filtering. A missing leader or invalid selection stops following; {0} or Escape stops manually. Closed doors still require manual opening; no portal or background input.", this.ToggleKeyName));
        ImGui.TextWrapped(this.StatusText());
        if (!string.IsNullOrEmpty(this.error)) ImGui.TextWrapped(this.error);
    }

    private string StatusText() => this.run.Status == "manual"
        ? this.PluginText.F("status.manual", "Manual input ({0}) — resumes when the keys are released", this.manualKeys)
        : this.PluginText.F("status." + this.run.Status, this.run.Status, this.ToggleKeyName);

    private void DrawPlayerChoice(string key, string label, ref string selected, bool includeLocal)
    {
        if (!ImGui.BeginCombo(this.PluginText.Label(key, label, key), string.IsNullOrEmpty(selected)
                ? this.PluginText.T("choose_player", "Select a character") : selected)) return;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Core.States.GameCurrentState == GameStateTypes.InGameState)
        {
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            foreach (var player in ReadPlayers(area).Keys)
                if (includeLocal || (player.Address != area.Player.Address && player.Id != area.Player.Id)) names.Add(player.Name);
        }
        foreach (var name in names)
            if (ImGui.Selectable(name, string.Equals(name, selected, StringComparison.OrdinalIgnoreCase)))
            {
                selected = name;
                this.ClearNavigation();
                this.SaveSettings();
            }
        if (names.Count == 0) ImGui.TextDisabled(this.PluginText.T("no_players", "No nearby players detected. Move into the same area as the leader."));
        ImGui.EndCombo();
    }

    private static Dictionary<PlayerIdentity, Entity> ReadPlayers(AreaInstance area)
    {
        var result = new Dictionary<PlayerIdentity, Entity>();
        foreach (var entity in area.AwakeEntities.Values.Prepend(area.Player).DistinctBy(e => e.Address))
            if (entity.IsValid && entity.EntityType == EntityTypes.Player &&
                entity.TryGetComponent<Player>(out var player) && !string.IsNullOrWhiteSpace(player.Name))
                result.TryAdd(new(entity.Id, entity.Address, player.Name.Trim()), entity);
        return result;
    }

    private bool IsUiBlocked(ImportantUiElements ui) => Core.IsSettingsMenuOpen || ui.Address == IntPtr.Zero ||
        // Controller UI deliberately has no ChatParent in the current core. Only
        // explicit co-op mode permits this; the keyboard UI still requires it.
        (ui.ChatParent.Address == IntPtr.Zero && !(this.Settings.LocalCoopFollow && Core.GHSettings.EnableControllerMode)) ||
        ui.ChatParent.IsChatActive || ui.IsAnyLargePanelOpen || MovementInput.IsDown(0x0D);

    public override void DrawUI()
    {
        try
        {
            this.input?.ObserveProcess(Core.Process.Pid);
            var now = Environment.TickCount64;
            if (this.run.IsEnabled && this.lastFrame != 0 && now - this.lastFrame > 300) this.Suspend("frame_gap");
            this.lastFrame = now;
            var toggleDown = MovementInput.IsDown(this.Settings.ToggleKey);
            if (toggleDown && !this.toggleWasDown && MovementInput.IsForeground(Core.Process.Pid) && !Core.IsSettingsMenuOpen)
            {
                if (this.run.IsEnabled) this.Halt("stopped");
                else
                {
                    this.ClearNavigation();
                    this.input?.CaptureManualBaseline();
                    this.run.Start();
                    this.error = string.Empty;
                }
            }
            this.toggleWasDown = toggleDown;
            if (MovementInput.IsDown(0x1B) || this.input?.ConsumeStopRequest() == true) this.Halt("stopped");
            if (this.run.Ready(now)) this.Tick(now);
            this.DrawOverlay();
        }
        catch (Exception ex) { this.Suspend("error", 1000); this.error = ex.Message; }
    }

    private void Tick(long now)
    {
        if (!MovementInput.IsForeground(Core.Process.Pid)) { this.Suspend("unfocused"); return; }
        if (Core.States.GameCurrentState != GameStateTypes.InGameState ||
            (Core.GHSettings.EnableControllerMode && !this.Settings.LocalCoopFollow)) { this.Suspend("game_state"); return; }
        var game = Core.States.InGameStateObject;
        var area = game.CurrentAreaInstance;
        var ui = game.GameUi;
        if (this.IsUiBlocked(ui)) { this.Suspend("panel"); return; }
        if (area.Address == IntPtr.Zero) { this.Suspend("player_invalid"); return; }
        if (this.areaAddress != IntPtr.Zero && (this.areaAddress != area.Address || this.areaHash != area.AreaHash))
        { this.Suspend("area_changed"); return; }
        this.areaAddress = area.Address;
        this.areaHash = area.AreaHash;
        var players = ReadPlayers(area);
        var local = players.Keys.Where(p => p.Address == area.Player.Address && p.Id == area.Player.Id)
            .Select(p => (PlayerIdentity?)p).FirstOrDefault();
        var pair = ParticipantSelection.Resolve(players.Keys, local, this.Settings.LocalCoopFollow,
            this.LeaderName, this.Settings.P1Name, this.Settings.P2Name, out var failure);
        if (pair == null) { this.Suspend(failure); return; }
        var primary = players[pair.Value.Primary];
        var secondary = pair.Value.Secondary is PlayerIdentity secondaryIdentity ? players[secondaryIdentity] : null;
        var target = players[pair.Value.Leader];
        if (!primary.TryGetComponent<Life>(out var life) || !life.IsAlive ||
            !primary.TryGetComponent<Render>(out var primaryRender))
        { this.Suspend("player_invalid"); return; }
        Render? secondaryRender = null;
        Life? secondaryLife = null;
        if (secondary != null && (!secondary.TryGetComponent<Render>(out secondaryRender) ||
            !secondary.TryGetComponent<Life>(out secondaryLife) || !secondaryLife.IsAlive))
        { this.Suspend("secondary_invalid"); return; }
        if (!target.TryGetComponent<Render>(out var targetRender) ||
            !target.TryGetComponent<Life>(out var targetLife) || !targetLife.IsAlive)
        { this.Suspend("leader_unavailable"); return; }
        if (this.followerAddress != IntPtr.Zero && (this.followerAddress != primary.Address || this.followerId != primary.Id))
        { this.Suspend("follower_changed"); return; }
        if (secondary != null && this.secondaryAddress != IntPtr.Zero &&
            (this.secondaryAddress != secondary.Address || this.secondaryId != secondary.Id))
        { this.Suspend("follower_changed"); return; }
        this.followerAddress = primary.Address;
        this.followerId = primary.Id;
        this.secondaryAddress = secondary?.Address ?? IntPtr.Zero;
        this.secondaryId = secondary?.Id ?? 0;
        if (this.targetAddress != IntPtr.Zero && (this.targetAddress != target.Address || this.targetId != target.Id))
        { this.Suspend("target_changed"); return; }
        this.targetAddress = target.Address;
        this.targetId = target.Id;
        var primaryPos = new Vector2(primaryRender.GridPosition.X, primaryRender.GridPosition.Y);
        var leaderPos = new Vector2(targetRender.GridPosition.X, targetRender.GridPosition.Y);
        var secondaryPos = secondaryRender == null ? primaryPos : new Vector2(secondaryRender.GridPosition.X, secondaryRender.GridPosition.Y);
        var leaderDistance = Vector2.Distance(primaryPos, leaderPos);
        this.playerGap = Vector2.Distance(primaryPos, secondaryPos);
        if (!float.IsFinite(leaderDistance + this.playerGap) || leaderDistance > 600 || this.playerGap > 600 || area.GridWalkableData.Length == 0)
        { this.Suspend("terrain_missing"); return; }
        if (this.grid == null || now >= this.nextDoorRead)
        {
            this.grid = BuildGrid(area, this.Settings.Clearance);
            this.nextDoorRead = now + 150;
        }
        if (!this.grid.Contains(primaryPos) || !this.grid.Contains(leaderPos) || !this.grid.Contains(secondaryPos))
        { this.Suspend("terrain_missing"); return; }
        var correction = secondary != null && this.coop.Update(primaryPos, secondaryPos,
            this.grid.Clear(primaryPos, secondaryPos), this.Settings.P2LagDistance, this.Settings.P2RejoinDistance);
        if (correction != this.correctingP2)
        {
            // Cancel the old actor's search and release its keys before switching
            // actor, route destination, projection height, and physical mapping.
            this.ClearRoute();
            this.correctingP2 = correction;
        }
        if (this.input?.SetArrowMode(this.correctingP2) != true) { this.Suspend("input_failed", 500); return; }
        var player = this.correctingP2 ? secondaryPos : primaryPos;
        var goal = this.correctingP2 ? primaryPos : leaderPos;
        var playerRender = this.correctingP2 ? secondaryRender! : primaryRender;
        this.followerHeight = playerRender.TerrainHeight;
        this.distance = Vector2.Distance(player, goal);
        // During correction the coordinator alone decides when P2 has rejoined;
        // the ordinary leader stop distance must not end this phase early.
        var needsMovement = this.correctingP2 || this.session.NeedsMovement(this.distance,
            this.grid.Clear(player, goal), this.Settings.StopDistance, this.Settings.ResumeDistance);
        if (!this.Settings.PreviewOnly)
        {
            this.manualKeys = this.input?.ReadManualInput(this.Settings.LocalCoopFollow) ?? string.Empty;
            if (this.manualKeys.Length != 0) { this.Suspend("manual"); return; }
        }
        if (this.TickCombat(now, area, primary, secondary)) return;
        if (!needsMovement)
        {
            this.input?.Stop();
            this.planned = MoveKeys.None;
            this.session.IsStuck(player, false, now, this.Settings.StuckMilliseconds);
            this.recovery.Reset();
            this.run.Status = "in_range";
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
        if (!this.recovery.Active && this.search == null && now >= this.nextSearch)
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
        var world = game.CurrentWorldInstance;
        var ratio = area.WorldToGridConvertor;
        var origin = world.WorldToScreen(player * ratio, playerRender.TerrainHeight);
        var screenX = world.WorldToScreen((player + Vector2.UnitX) * ratio, playerRender.TerrainHeight) - origin;
        var screenY = world.WorldToScreen((player + Vector2.UnitY) * ratio, playerRender.TerrainHeight) - origin;
        this.planned = steer == null ? MoveKeys.None : Steering.Choose(steer.Value - player, screenX, screenY,
            step => this.grid.Clear(player, player + step));
        if (this.Settings.PreviewOnly) { this.input?.Stop(); this.run.Status = "preview"; return; }

        bool ApplyCurrentMovement(int leaseMilliseconds = 200)
        {
            if (area.Address != this.areaAddress || area.AreaHash != this.areaHash || !target.IsValid ||
                target.Address != this.targetAddress || target.Id != this.targetId ||
                !primary.IsValid || primary.Address != this.followerAddress || primary.Id != this.followerId ||
                (secondary != null && (!secondary.IsValid || secondary.Address != this.secondaryAddress ||
                    secondary.Id != this.secondaryId || secondaryLife?.IsAlive != true)) ||
                !life.IsAlive || !targetLife.IsAlive || Core.States.GameCurrentState != GameStateTypes.InGameState || this.IsUiBlocked(ui))
            { this.Suspend("target_changed"); return false; }
            if (this.input?.Apply(this.planned, Core.Process.Pid, leaseMilliseconds) != true)
            { this.Suspend("input_failed", 500); return false; }
            return true;
        }

        // Count the entire interval without displacement, including no-path,
        // searching and no-direction frames. Replanning must not reset this timer.
        if (!this.recovery.Active && this.session.IsStuck(player, true, now, this.Settings.StuckMilliseconds))
        {
            this.recovery.Begin(player);
            this.DiscardRoute();
        }
        if (this.recovery.Active)
        {
            // A false terrain block must not veto recovery too. Use the live
            // actor-to-target direction without route or walkability filtering:
            // shared WASD: P1 -> leader; correction arrows: P2 -> P1.
            var towardTarget = Steering.Choose(goal - player, screenX, screenY, _ => true);
            var attempt = this.recovery.Advance(player, now, towardTarget);
            this.planned = attempt.Keys;
            if (attempt.Action == RecoveryAction.Press)
            {
                if (ApplyCurrentMovement(attempt.LeaseMilliseconds)) this.run.Status = "recovering";
            }
            else
            {
                this.input?.Stop();
                this.run.Status = towardTarget == MoveKeys.None ? "recovery_no_direction" : "recovery_wait";
                if (attempt.Action == RecoveryAction.Repath)
                {
                    this.DiscardRoute();
                    this.session.ResetProgress();
                    this.run.Status = "searching";
                }
            }
            return;
        }
        if (this.planned == MoveKeys.None)
        {
            this.input?.Stop();
            this.run.Status = steer == null ? (this.search == null ? "no_path" : "searching") : "no_direction";
            return;
        }
        if (!ApplyCurrentMovement()) return;
        this.run.Status = "following";
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
            var roles = this.Settings.LocalCoopFollow
                ? (this.correctingP2 ? $"P2: {this.Settings.P2Name} → P1: {this.Settings.P1Name}" : $"P1: {this.Settings.P1Name} → {this.LeaderName}")
                : this.LeaderName;
            if (this.Settings.LocalCoopFollow)
                ImGui.TextUnformatted(this.PluginText.T(this.correctingP2 ? "phase.correction" : "phase.shared",
                    this.correctingP2 ? "P2 correction: P1 paused" : "Shared WASD follow") +
                    this.PluginText.F("gap", " | P1/P2 gap: {0:0.0}", this.playerGap));
            ImGui.TextUnformatted($"{roles} | {this.distance:0.0} | {MovementBindings.Describe(this.planned, this.correctingP2)}");
            if (this.Settings.Combat.Enabled) ImGui.TextWrapped(this.CombatStatusText());
            ImGui.End();
        }
        if (!this.run.IsEnabled || !this.Settings.ShowRoute || this.route == null || Core.States.GameCurrentState != GameStateTypes.InGameState) return;
        var game = Core.States.InGameStateObject;
        var area = game.CurrentAreaInstance;
        var origin = new Vector2(Core.Process.WindowArea.X, Core.Process.WindowArea.Y);
        var points = this.route.Take(1000).Select(p => origin + game.CurrentWorldInstance.WorldToScreen(p * area.WorldToGridConvertor, this.followerHeight)).ToArray();
        var draw = ImGui.GetBackgroundDrawList();
        for (var i = 1; i < points.Length; i++)
            if (float.IsFinite(points[i - 1].X + points[i - 1].Y + points[i].X + points[i].Y))
                draw.AddLine(points[i - 1], points[i], 0xFF00D7FF, 2);
    }
}
