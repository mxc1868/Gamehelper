namespace Bloodybot2.Navigation;

using System.Numerics;
using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using ImGuiNET;
using Bloodybot2.Configuration;
using Bloodybot2.Game;
using Bloodybot2.Runtime;

// The former Follower navigation, hosted by Bloodybot2. No plugin lifecycle,
// settings UI, hotkey loop or independent combat engine lives in this mode.
internal sealed class FollowMode : INavigationMode
{
    private readonly FollowSession session = new();
    private readonly CoopCoordinator coop = new();
    private readonly UnstuckRecovery recovery = new();
    private readonly NavigationTarget primaryTarget = new();
    private readonly NavigationTarget leaderTarget = new();
    private (IntPtr Area, string Hash, IntPtr Actor, uint ActorId, IntPtr Goal, uint GoalId)? recoveryIdentity;
    private readonly MovementInput input;
    private CancellationTokenSource? searchCancellation;
    private Task<PathSearchResult>? search;
    private PathSearchStatus? pathStatus;
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
    private int? dangerElites;
    private bool escaping;
    private float? escapeHealth;
    private string escapeCharacter = string.Empty;
    private EscapeRoute? escapePlan;
    private (IntPtr Address, uint Id, bool Secondary)? escapeIdentity;
    private bool correctingP2;
    private bool usingP2;
    private (bool Primary, bool Secondary)? livingPlayers;
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
        "players_dead" => "角色已死亡，等待复活后继续跟随",
        "players_changed" => "角色存活状态变化，重新寻路",
        "leader_unavailable" => "队长暂不可用，等待恢复",
        "danger" => $"躲避危险：队长 {this.Settings.DangerRange:0.#} 格内精英 {this.dangerElites} > {this.Settings.DangerEliteLimit}，暂停跟随",
        "escaping" => $"低血撤离：{this.escapeCharacter} Life+ES {this.escapeHealth:0.#}% · {(this.usingP2 ? "仅 P2 箭头" : "共享 WASD")}远离黄怪群",
        "escape_preview" => $"预览低血撤离：{this.escapeCharacter} Life+ES {this.escapeHealth:0.#}%",
        "low_health_wait" => $"低血等待恢复：{this.escapeCharacter} Life+ES {this.escapeHealth:0.#}% · 已远离黄怪群",
        "escape_no_path" => "低血撤离：没有可走的远离路线，松键重试",
        "escape_skill_wait" => "低血撤离：等待已按下的技能键释放",
        "health_unavailable" => "Life+ES 数据暂不可读，松键等待",
        "escape_scan_limit" => "黄怪扫描超过上限，松键等待",
        "recovering" => "朝目标直走脱困",
        "recovery_explore" => "换方向探索脱困",
        "recovery_route" => "验证脱困后的路线",
        "recovery_wait" => "脱困观察，准备重试",
        "search_budget" => "寻路超时，等待重试",
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

        this.ClearRoute(preserveRecovery: preserveFollowState);
        this.primaryTarget.Reset();
        this.leaderTarget.Reset();
        this.pathStatus = null;
        this.grid = null;
        this.targetAddress = this.followerAddress = this.secondaryAddress = IntPtr.Zero;
        this.areaAddress = IntPtr.Zero;
        this.nextDoorRead = 0;
        if (!preserveFollowState)
        {
            this.recoveryIdentity = null;
            this.coop.Reset();
            this.session.Reset();
            this.correctingP2 = false;
            this.usingP2 = false;
            this.livingPlayers = null;
        }
        this.distance = this.playerGap = 0;
        this.dangerElites = null;
        this.escaping = false;
        this.escapeIdentity = null;
        this.escapeHealth = null;
        this.escapePlan = null;
        this.escapeCharacter = string.Empty;
    }

    private void ClearRoute(bool release = true, bool preserveRecovery = false)
    {
        this.DiscardRoute(release);
        this.session.ResetProgress();
        if (preserveRecovery) this.recovery.Pause();
        else this.recovery.Reset();
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
            ("area_changed" or "game_state" or "follower_changed" or "target_changed" or "players_changed" or "players_dead"));
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
        if (this.Settings.EscapeLowHealth && this.TryLowHealthEscape(area, ui, players, local, now, preview, combat)) return;
        var pair = ParticipantSelection.Resolve(players.Keys, local, this.Settings.LocalCoopFollow,
            this.LeaderName, this.Settings.P1Name, this.Settings.P2Name, out var failure);
        if (pair == null) { this.Suspend(failure); return; }
        var primary = players[pair.Value.Primary];
        var secondary = pair.Value.Secondary is PlayerIdentity secondaryIdentity ? players[secondaryIdentity] : null;
        var target = players[pair.Value.Leader];
        if (!primary.TryGetComponent<Life>(out var life))
        { this.Suspend("player_invalid"); return; }
        Life? secondaryLife = null;
        if (secondary != null && !secondary.TryGetComponent<Life>(out secondaryLife))
        { this.Suspend("secondary_invalid"); return; }
        var primaryAlive = life.IsAlive;
        var secondaryAlive = secondaryLife?.IsAlive == true;
        if (!primaryAlive && !secondaryAlive) { this.Suspend("players_dead"); return; }
        Render? primaryRender = null;
        Render? secondaryRender = null;
        if (primaryAlive && !primary.TryGetComponent<Render>(out primaryRender))
        { this.Suspend("player_invalid"); return; }
        if (secondaryAlive && !secondary!.TryGetComponent<Render>(out secondaryRender))
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
        if (this.livingPlayers != (primaryAlive, secondaryAlive))
        {
            // Death/revival changes the actor and input phase. Never carry an
            // old correction route, recovery probe or held key into that phase.
            this.ClearRoute();
            this.coop.Reset();
            this.session.Reset();
            this.primaryTarget.Reset();
            this.livingPlayers = (primaryAlive, secondaryAlive);
        }
        var primaryPos = primaryRender == null ? Vector2.Zero : new Vector2(primaryRender.GridPosition.X, primaryRender.GridPosition.Y);
        var leaderPos = new Vector2(targetRender.GridPosition.X, targetRender.GridPosition.Y);
        var secondaryPos = secondaryRender == null ? Vector2.Zero : new Vector2(secondaryRender.GridPosition.X, secondaryRender.GridPosition.Y);
        var bothAlive = primaryAlive && secondaryAlive;
        this.playerGap = bothAlive ? Vector2.Distance(primaryPos, secondaryPos) : 0;
        var primaryDistance = primaryAlive ? Vector2.Distance(primaryPos, leaderPos) : 0;
        var secondaryDistance = secondaryAlive ? Vector2.Distance(secondaryPos, leaderPos) : 0;
        if (!float.IsFinite(primaryDistance + secondaryDistance + this.playerGap) ||
            primaryDistance > 600 || secondaryDistance > 600 || this.playerGap > 600 || area.GridWalkableData.Length == 0)
        { this.Suspend("terrain_missing"); return; }
        if (!preview)
        {
            this.manualKeys = this.input.ReadManualInput(this.Settings.LocalCoopFollow);
            if (this.manualKeys.Length != 0) { this.Suspend("manual"); return; }
        }
        this.dangerElites = this.Settings.AvoidDanger
            ? FollowDangerReader.CountElites(area, leaderPos, this.Settings.DangerRange) : null;
        if (Environment.TickCount64 - now > 150) { this.Suspend("frame_gap"); return; }
        if (this.dangerElites is int eliteCount && this.Settings.IsDangerous(eliteCount))
        {
            this.usingP2 = !primaryAlive;
            this.distance = primaryAlive && secondaryAlive ? Math.Min(primaryDistance, secondaryDistance)
                : primaryAlive ? primaryDistance : secondaryDistance;
            this.WaitForDanger(combat);
            return;
        }
        if (this.grid == null || now >= this.nextDoorRead)
        {
            this.grid = BuildGrid(area, this.Settings.Clearance);
            this.nextDoorRead = now + 150;
        }
        if ((primaryAlive && !this.grid.Contains(primaryPos)) || !this.grid.Contains(leaderPos) ||
            (secondaryAlive && !this.grid.Contains(secondaryPos)))
        { this.Suspend("terrain_missing"); return; }
        var primaryAnchor = primaryAlive ? this.primaryTarget.Observe(this.grid, primaryPos) : null;
        var leaderAnchor = this.leaderTarget.Observe(this.grid, leaderPos);
        var correction = bothAlive && this.coop.Update(primaryPos, secondaryPos,
            !this.coop.CorrectingP2 || this.grid.HasShortConnection(secondaryPos, primaryPos,
                this.Settings.P2RejoinDistance, primaryAnchor), this.Settings.P2LagDistance, this.Settings.P2RejoinDistance);
        var plan = FollowPlan.Resolve(primaryPos, primaryAlive, secondaryPos, secondaryAlive, leaderPos, correction)!.Value;
        if (plan.Rejoining != this.correctingP2)
        {
            // Cancel the old actor's search and release its keys before switching
            // actor, route destination, projection height, and physical mapping.
            this.ClearRoute();
            this.correctingP2 = plan.Rejoining;
        }
        this.usingP2 = plan.UseSecondary;
        if (this.input?.SetArrowMode(this.usingP2) != true) { this.Suspend("input_failed", 500); return; }
        var player = this.usingP2 ? secondaryPos : primaryPos;
        var goal = this.correctingP2 ? primaryPos : leaderPos;
        var goalAnchor = this.correctingP2 ? primaryAnchor : leaderAnchor;
        var actorEntity = this.usingP2 ? secondary! : primary;
        var goalEntity = this.correctingP2 ? primary : target;
        var identity = (area.Address, area.AreaHash, actorEntity.Address, actorEntity.Id, goalEntity.Address, goalEntity.Id);
        if (this.recoveryIdentity != identity)
        {
            this.recovery.Reset();
            this.recoveryIdentity = identity;
        }
        var playerRender = this.usingP2 ? secondaryRender! : primaryRender!;
        this.followerHeight = playerRender.TerrainHeight;
        this.distance = this.correctingP2 ? this.playerGap : plan.LeaderDistance;
        // During correction the coordinator alone decides when P2 has rejoined;
        // the ordinary leader stop distance must not end this phase early.
        var needsMovement = this.correctingP2 || this.session.NeedsMovement(this.distance,
            this.Settings.StopDistance, this.Settings.ResumeDistance);
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
            var result = this.search.GetAwaiter().GetResult();
            this.pathStatus = result.Status;
            pathFailed = result.Status is PathSearchStatus.StartBlocked or PathSearchStatus.GoalBlocked or PathSearchStatus.Unreachable;
            // A budget/cancellation result is inconclusive. Keep a usable old
            // route and retry; it must not be labelled an impassable wall.
            if (result.Status == PathSearchStatus.Found || pathFailed)
            {
                this.route = result.Route;
                this.routeAt = this.searchAt;
                this.routeGoal = this.searchGoal;
            }
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
            this.planned != MoveKeys.None && this.session.IsStuck(player, true, now, this.Settings.StuckMilliseconds)))
            this.DiscardRoute();
        if (!this.correctingP2 && !this.recovery.Active && combat()) { this.planned = MoveKeys.None; this.run.Status = "combat"; return; }
        // Recovery keeps searching from the REAL position as probes move us out
        // of a falsely blocked cell. A side-step is not cancelled by a new route.
        if (this.search == null && now >= this.nextSearch)
        {
            this.searchGoal = goal;
            this.searchAt = now;
            var snapshot = this.grid;
            this.searchCancellation = new();
            var token = this.searchCancellation.Token;
            var arrivalRadius = this.correctingP2 ? this.Settings.P2RejoinDistance : this.Settings.StopDistance;
            this.search = Task.Run(() => snapshot.SearchPath(player, goal, token, arrivalRadius, goalAnchor));
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
                    secondary.Id != this.secondaryId)) ||
                Core.States.GameCurrentState != GameStateTypes.InGameState || this.IsUiBlocked(ui))
            { this.Suspend("target_changed"); return false; }
            if (life.IsAlive != primaryAlive || (secondaryLife?.IsAlive == true) != secondaryAlive)
            { this.Suspend("players_changed", 0); return false; }
            if (!targetLife.IsAlive) { this.Suspend("leader_unavailable"); return false; }
            if (this.input?.Apply(this.planned, Core.Process.Pid, leaseMilliseconds) != true)
            { this.Suspend("input_failed", 500); return false; }
            return true;
        }

        if (this.recovery.Active)
        {
            // A false terrain block must not veto recovery too. Use the live
            // actor-to-target direction without route or walkability filtering:
            // Shared WASD: P1 -> leader; correction arrows: P2 -> P1;
            // when P1 is dead, arrows follow P2 -> leader instead.
            var towardTarget = Steering.Choose(goal - player, screenX, screenY, _ => true);
            var routeKeys = this.planned;
            var attempt = this.recovery.Advance(player, now, towardTarget, routeKeys);
            this.planned = attempt.Keys;
            if (attempt.Action == RecoveryAction.Press)
            {
                if (ApplyCurrentMovement(attempt.LeaseMilliseconds))
                    this.run.Status = this.recovery.Exploring ? "recovery_explore" : routeKeys == attempt.Keys ? "recovery_route" : "recovering";
            }
            else
            {
                this.input?.Stop();
                this.run.Status = towardTarget == MoveKeys.None ? "recovery_no_direction" : "recovery_wait";
                if (attempt.Action == RecoveryAction.Repath)
                {
                    this.session.ResetProgress();
                    this.run.Status = "following";
                }
            }
            return;
        }
        if (this.planned == MoveKeys.None)
        {
            this.input?.Stop();
            this.run.Status = this.pathStatus == PathSearchStatus.BudgetExceeded ? "search_budget" :
                steer == null ? (this.search == null ? "no_path" : "searching") : "no_direction";
            return;
        }
        if (!ApplyCurrentMovement()) return;
        this.run.Status = "following";
    }

    private bool TryLowHealthEscape(AreaInstance area, ImportantUiElements ui, Dictionary<PlayerIdentity, Entity> players,
        PlayerIdentity? local, long now, bool preview, Func<bool> combat)
    {
        var controlled = ParticipantSelection.ResolveControlled(players.Keys, local, this.Settings.LocalCoopFollow,
            this.Settings.P1Name, this.Settings.P2Name, out var failure);
        if (controlled == null) { this.Suspend(failure); return true; }
        var primary = players[controlled.Value.Primary];
        var secondary = controlled.Value.Secondary is PlayerIdentity p2 ? players[p2] : null;
        if (!CombatSnapshotReader.ReadComponent(primary, out Life life)) { this.Suspend("health_unavailable"); return true; }
        Life? secondaryLife = null;
        if (secondary != null)
        {
            if (!CombatSnapshotReader.ReadComponent<Life>(secondary, out var readSecondaryLife))
            { this.Suspend("health_unavailable"); return true; }
            secondaryLife = readSecondaryLife;
        }
        var primaryAlive = life.IsAlive;
        var secondaryAlive = secondaryLife?.IsAlive == true;
        if (!primaryAlive && !secondaryAlive) { this.Suspend("players_dead"); return true; }
        var primaryHealth = primaryAlive ? LowHealthReader.Percent(life) : null;
        var secondaryHealth = secondaryAlive ? LowHealthReader.Percent(secondaryLife!) : null;
        if (primaryAlive && !primaryHealth.HasValue || secondaryAlive && !secondaryHealth.HasValue)
        { this.Suspend("health_unavailable"); return true; }
        var useSecondary = EscapePlanner.UseSecondary(primaryHealth ?? 100, primaryAlive, secondaryHealth ?? 100, secondaryAlive);
        var health = (useSecondary ? secondaryHealth : primaryHealth)!.Value;
        if (health >= EscapePlanner.HealthThreshold)
        {
            if (this.escaping)
            {
                this.ClearNavigation();
                this.areaAddress = area.Address;
                this.areaHash = area.AreaHash;
            }
            return false;
        }
        if (!preview)
        {
            this.manualKeys = this.input.ReadManualInput(this.Settings.LocalCoopFollow);
            if (this.manualKeys.Length != 0) { this.Suspend("manual"); return true; }
        }
        var actor = useSecondary ? secondary! : primary;
        if (!CombatSnapshotReader.ReadComponent(actor, out Render render)) { this.Suspend("player_invalid"); return true; }
        var position = new Vector2(render.GridPosition.X, render.GridPosition.Y);
        if (area.GridWalkableData.Length == 0) { this.Suspend("terrain_missing"); return true; }
        if (this.livingPlayers.HasValue && this.livingPlayers != (primaryAlive, secondaryAlive))
        { this.input.StopMovement(); this.ClearRoute(release: false); this.coop.Reset(); this.session.Reset(); }
        this.livingPlayers = (primaryAlive, secondaryAlive);
        if (!this.BeginEscape(actor.Address, actor.Id, useSecondary, health,
            (useSecondary ? controlled.Value.Secondary!.Value : controlled.Value.Primary).Name)) return true;
        this.followerHeight = render.TerrainHeight;
        if (this.grid == null || now >= this.nextDoorRead)
        { this.grid = BuildGrid(area, this.Settings.Clearance); this.nextDoorRead = now + 150; }
        if (!this.grid.Contains(position)) { this.Suspend("terrain_missing"); return true; }
        var rares = LowHealthReader.Rares(area, position, this.Settings.EscapeScanRange);
        if (rares == null) { this.Suspend("escape_scan_limit"); return true; }
        this.escapePlan = EscapePlanner.Plan(this.grid, position, rares, this.Settings.EscapeClusterDistance, this.Settings.EscapeDistance);
        if (Environment.TickCount64 - now > 150) { this.Suspend("frame_gap"); return true; }
        this.route = this.escapePlan.Route;
        this.planned = MoveKeys.None;
        if (this.escapePlan.Safe)
        {
            this.input.StopMovement();
            this.run.Status = "low_health_wait";
            combat();
            return true;
        }
        var aim = this.route == null ? null : EscapePlanner.Steer(this.grid, position, this.route, rares);
        var world = Core.States.InGameStateObject.CurrentWorldInstance;
        var origin = world.WorldToScreen(position * area.WorldToGridConvertor, render.TerrainHeight);
        var screenX = world.WorldToScreen((position + Vector2.UnitX) * area.WorldToGridConvertor, render.TerrainHeight) - origin;
        var screenY = world.WorldToScreen((position + Vector2.UnitY) * area.WorldToGridConvertor, render.TerrainHeight) - origin;
        if (aim is Vector2 target)
            this.planned = Steering.Choose(target - position, screenX, screenY,
                step => EscapePlanner.SafeSegment(this.grid, position, position + step, rares));
        if (preview) { this.input.Stop(); this.run.Status = "escape_preview"; return true; }
        if (this.planned == MoveKeys.None)
        { this.input.StopMovement(); this.run.Status = "escape_no_path"; combat(); return true; }
        if (this.input.SkillHeld)
        { this.input.StopMovement(); this.planned = MoveKeys.None; this.run.Status = "escape_skill_wait"; return true; }
        // Emergency input depends on the local actors, not leader availability.
        // Repeat identity/health checks immediately before renewing the 150 ms lease.
        var latestPrimary = primaryAlive ? LowHealthReader.Percent(life) : null;
        var latestSecondary = secondaryAlive ? LowHealthReader.Percent(secondaryLife!) : null;
        if (!primary.IsValid || primary.Address != controlled.Value.Primary.Address || primary.Id != controlled.Value.Primary.Id ||
            secondary != null && (!secondary.IsValid || secondary.Address != controlled.Value.Secondary!.Value.Address ||
                secondary.Id != controlled.Value.Secondary.Value.Id) ||
            life.IsAlive != primaryAlive || (secondaryLife?.IsAlive == true) != secondaryAlive ||
            primaryAlive && !latestPrimary.HasValue || secondaryAlive && !latestSecondary.HasValue ||
            EscapePlanner.UseSecondary(latestPrimary ?? 100, primaryAlive, latestSecondary ?? 100, secondaryAlive) != useSecondary ||
            (useSecondary ? latestSecondary : latestPrimary) >= EscapePlanner.HealthThreshold)
        { this.Suspend("players_changed", 0); return true; }
        if (area.Address != this.areaAddress || area.AreaHash != this.areaHash ||
            Core.States.GameCurrentState != GameStateTypes.InGameState || this.IsUiBlocked(ui) || Environment.TickCount64 - now > 150)
        { this.Suspend("frame_gap"); return true; }
        if (!this.input.Apply(this.planned, Core.Process.Pid, 150)) { this.Suspend("input_failed", 500); return true; }
        this.run.Status = "escaping";
        return true;
    }

    private bool BeginEscape(IntPtr address, uint id, bool secondary, float health, string name)
    {
        if (!this.escaping || this.escapeIdentity != (address, id, secondary))
        {
            this.input.StopMovement();
            this.ClearRoute(release: false);
            this.coop.Reset();
            this.session.Reset();
            this.primaryTarget.Reset();
            this.leaderTarget.Reset();
            this.correctingP2 = false;
            this.pathStatus = null;
        }
        if (!this.input.SetArrowMode(secondary)) { this.Suspend("input_failed", 500); return false; }
        this.escaping = true;
        this.escapeIdentity = (address, id, secondary);
        this.escapeHealth = health;
        this.escapeCharacter = name;
        this.usingP2 = secondary;
        return true;
    }

    private void WaitForDanger(Func<bool> combat)
    {
        // Danger overrides shared movement, P2 correction and unstuck probes.
        // Keep the start command and combat independent; resume from fresh paths.
        this.input.StopMovement();
        this.ClearRoute(release: false);
        this.session.Reset();
        this.coop.Reset();
        this.primaryTarget.Reset();
        this.leaderTarget.Reset();
        this.grid = null;
        this.nextDoorRead = 0;
        this.correctingP2 = false;
        this.run.Status = "danger";
        combat();
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
            ImGui.TextUnformatted(this.escaping ? $"Escape: {this.escapeCharacter} | Life+ES: {this.escapeHealth:0.#}% | {MovementBindings.Describe(this.planned, this.usingP2)}" :
                this.correctingP2 ? $"P2: {this.Settings.P2Name} → P1: {this.Settings.P1Name}" : $"Follow → {this.LeaderName}");
            if (!this.escaping)
                ImGui.TextUnformatted($"{(this.correctingP2 ? "Rejoin" : "Nearest alive")}: {this.distance:0.0} | P1/P2: {this.playerGap:0.0} | {MovementBindings.Describe(this.planned, this.usingP2)}");
            if (this.escaping && this.escapePlan != null)
                ImGui.TextUnformatted($"Rare cluster: {this.escapePlan.ClusterSize} | Center: {this.escapePlan.Center} | Target: {this.escapePlan.Target} | Clearance: {this.Settings.EscapeDistance:0.#}");
            if (this.livingPlayers == (false, true)) ImGui.TextUnformatted("P1 dead · P2 uses arrows");
            else if (this.Settings.LocalCoopFollow && this.livingPlayers == (true, false)) ImGui.TextUnformatted("P2 dead · P1 uses WASD");
            if (this.pathStatus.HasValue) ImGui.TextUnformatted($"Path: {this.pathStatus}");
            if (!this.correctingP2 && !this.escaping)
                ImGui.TextUnformatted($"Stop: {this.Settings.StopDistance:0.0} | Resume: {this.Settings.ResumeDistance:0.0}");
            if (this.Settings.AvoidDanger)
                ImGui.TextUnformatted($"Leader elites: {this.dangerElites?.ToString() ?? "?"} | Range: {this.Settings.DangerRange:0.#} | Limit: {this.Settings.DangerEliteLimit}");
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
