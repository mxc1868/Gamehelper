namespace Bloodybot2.Navigation;

using System.Numerics;
using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using ImGuiNET;
using Bloodybot2.Configuration;
using Bloodybot2.Runtime;

// The former Follower navigation, hosted by Bloodybot2. No plugin lifecycle,
// settings UI, hotkey loop or independent combat engine lives in this mode.
internal sealed class FollowMode : INavigationMode
{
    private readonly FollowSession session = new();
    private readonly CoopCoordinator coop = new();
    private readonly UnstuckRecovery recovery = new();
    private readonly MovementInput input;
    private CancellationTokenSource? searchCancellation;
    private Task<List<Vector2>?>? search;
    private List<Vector2>? route;
    private NavigationGrid? grid;
    private readonly FollowRunState run = new();
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
    private long nextDoorRead;
    private Vector2 searchGoal;
    private Vector2 routeGoal;
    private float distance;
    private float playerGap;
    private bool correctingP2;
    private MoveKeys planned;
    private string manualKeys = string.Empty;
    private string LeaderName => this.Settings.LeaderName.Trim();


    private FollowSettings Settings = new();
    public FollowMode(MovementInput input) { this.input = input; }
    public string Name => "Follow";
    public bool IsActive => this.run.IsEnabled;
    public string Status => this.run.Status switch
    {
        "manual" => "手动按键：" + this.manualKeys,
        "target_missing" => "队长暂不可见，等待重新出现",
        "frame_gap" => "画面更新中断，等待恢复",
        "recovering" => "朝目标直走脱困",
        "recovery_wait" => "脱困观察，准备重试",
        _ => this.run.Status
    };
    public void Apply(BotConfig config) { this.Stop(); this.Settings = config.Follow; }
    public void Start() { this.ClearNavigation(); this.input.CaptureManualBaseline(); this.run.Start(); }
    public void Stop() => this.Halt("stopped");
    public void Suspend(string reason = "unfocused") => this.Suspend(reason, 250);
    public void CombatAccepted(int milliseconds) { this.DiscardRoute(release: false); this.session.PauseProgress(milliseconds); }
    public void Tick(long now, bool preview, Func<bool> combat)
    {
        if (this.run.Ready(now)) this.TickFollow(now, preview, combat);
    }
    private void ClearNavigation(bool preserveFollowState = false)
    {

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

    private void ClearRoute(bool release = true)
    {
        this.DiscardRoute(release);
        this.session.ResetProgress();
        this.recovery.Reset();
    }

    private void DiscardRoute(bool release = true)
    {
        if (release) this.input.Stop();
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

    private void Suspend(string reason, int retryMilliseconds)
    {
        this.run.Fail(reason, Environment.TickCount64, retryMilliseconds);
        this.ClearNavigation(this.run.IsEnabled && reason is not
            ("area_changed" or "game_state" or "follower_changed" or "target_changed"));
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
        // Controller UI deliberately has no ChatParent in the current core;
        // controller mode permits this automatically; keyboard UI still requires it.
        (ui.ChatParent.Address == IntPtr.Zero && !Core.GHSettings.EnableControllerMode) ||
        ui.ChatParent.IsChatActive || ui.IsAnyLargePanelOpen || MovementInput.IsDown(0x0D);


    private void TickFollow(long now, bool preview, Func<bool> combat)
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
            this.Settings.StopDistance, this.Settings.ResumeDistance);
        if (!preview)
        {
            this.manualKeys = this.input?.ReadManualInput(this.Settings.LocalCoopFollow) ?? string.Empty;
            if (this.manualKeys.Length != 0) { this.Suspend("manual"); return; }
        }
        if (!needsMovement)
        {
            // This also cancels an ongoing forced advance as soon as we enter
            // the stop radius. Never reuse its search/keys after the next resume.
            this.input?.StopMovement();
            this.ClearRoute(release: false);
            this.run.Status = combat() ? "combat" : "in_range";
            return;
        }
        var pathFailed = false;
        if (this.search?.IsCompleted == true)
        {
            this.route = this.search.GetAwaiter().GetResult();
            pathFailed = this.route == null;
            this.routeAt = this.searchAt;
            this.routeGoal = this.searchGoal;
            this.search = null;
            this.searchCancellation?.Dispose();
            this.searchCancellation = null;
        }
        var routeCurrent = this.route != null && now - this.routeAt < 1500 && Vector2.Distance(this.routeGoal, goal) < 30;
        var steer = routeCurrent ? this.grid.Steer(this.route!, player) : null;
        var world = game.CurrentWorldInstance;
        var ratio = area.WorldToGridConvertor;
        var origin = world.WorldToScreen(player * ratio, playerRender.TerrainHeight);
        var screenX = world.WorldToScreen((player + Vector2.UnitX) * ratio, playerRender.TerrainHeight) - origin;
        var screenY = world.WorldToScreen((player + Vector2.UnitY) * ratio, playerRender.TerrainHeight) - origin;
        this.planned = steer == null ? MoveKeys.None : Steering.Choose(steer.Value - player, screenX, screenY,
            step => this.grid.Clear(player, player + step));
        // A completed failed search or unusable existing route gets an immediate
        // direct advance. An initial pending search alone is not a path failure.
        // Detect stalls before combat can defer navigation again.
        if (!preview && this.recovery.BeginIfNeeded(player,
            pathFailed || (routeCurrent && this.planned == MoveKeys.None),
            this.session.IsStuck(player, true, now, this.Settings.StuckMilliseconds)))
            this.DiscardRoute();
        if (!this.correctingP2 && !this.recovery.Active && combat()) { this.planned = MoveKeys.None; this.run.Status = "combat"; return; }
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
        if (preview) { this.input?.Stop(); this.run.Status = "preview"; return; }

        bool ApplyCurrentMovement(int leaseMilliseconds = 200)
        {
            if (Environment.TickCount64 - now > 150) { this.Suspend("frame_gap"); return false; }
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


    public void DrawOverlay()
    {
        if (!MovementInput.IsForeground(Core.Process.Pid)) return;
        if (this.Settings.ShowStatus)
        {
            ImGui.SetNextWindowBgAlpha(0.75f);
            ImGui.SetNextWindowPos(new Vector2(30, 180), ImGuiCond.FirstUseEver);
            ImGui.Begin("Bloodybot2 · Follow##Bloodybot2Status", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing);
            ImGui.TextUnformatted(this.Status);
            ImGui.TextUnformatted(this.correctingP2 ? $"P2: {this.Settings.P2Name} → P1: {this.Settings.P1Name}" : $"Follow → {this.LeaderName}");
            ImGui.TextUnformatted($"Distance: {this.distance:0.0} | P1/P2: {this.playerGap:0.0} | {MovementBindings.Describe(this.planned, this.correctingP2)}");
            if (!this.correctingP2)
                ImGui.TextUnformatted($"Stop: {this.Settings.StopDistance:0.0} | Resume: {this.Settings.ResumeDistance:0.0}");
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
