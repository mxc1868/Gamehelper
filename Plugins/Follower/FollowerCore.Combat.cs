namespace Follower;

using BloodyBot.Combat;
using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;

public sealed partial class FollowerCore
{
    private readonly CombatEngine combat = new();
    private long combatBusyUntil;
    private long nextCombatRead;
    private string combatStatus = "idle";
    private string combatActionName = string.Empty;
    private int combatActionKey;
    private int combatEnemyCount;
    private string combatPlayerName = string.Empty;
    private CombatSnapshot? combatSnapshot;

    private void ClearCombat()
    {
        this.combatBusyUntil = this.nextCombatRead = 0;
        this.combatSnapshot = null;
        this.combatEnemyCount = 0;
        this.combatPlayerName = string.Empty;
        this.combatStatus = "idle";
        // Cooldowns survive settings, interruptions, area events and start/stop.
        // Only plugin initialization resets the history.
    }

    // True gives this tick to the cast. No chasing, target navigation or strategy here.
    private bool TickCombat(long now, AreaInstance area, Entity primary, Entity? secondary)
    {
        var selected = this.Settings.LocalCoopFollow && this.Settings.CombatUseP2 ? secondary : primary;
        if (!this.Settings.Combat.Enabled || this.correctingP2 || this.recovery.Active || selected == null)
        {
            this.combatStatus = this.correctingP2 || this.recovery.Active ? "navigation" : "idle";
            if (this.input?.SkillHeld == true) this.input.Stop();
            this.combatBusyUntil = 0;
            return false;
        }
        if (this.Settings.PreviewOnly)
        {
            if (this.input?.SkillHeld == true) this.input.Stop();
            this.combatBusyUntil = 0;
        }
        if (this.input?.SkillHeld == true || now < this.combatBusyUntil)
        {
            this.planned = MoveKeys.None;
            this.run.Status = "combat";
            return true;
        }
        if (now < this.nextCombatRead) return false;
        this.nextCombatRead = now + 100;
        var game = Core.States.InGameStateObject;
        var details = game.CurrentWorldInstance.AreaDetails;
        this.combatSnapshot = CombatSnapshotReader.Read(area, selected, now,
            details.Address != IntPtr.Zero && !details.IsTown && !details.IsHideout);
        this.combatPlayerName = selected.TryGetComponent<Player>(out var player) ? player.Name : string.Empty;
        if (this.combatSnapshot == null || !this.combatSnapshot.CanFight)
        { this.combatStatus = "unavailable"; this.combatEnemyCount = 0; return false; }
        this.combatEnemyCount = this.combatSnapshot.Enemies.Count(e => e.IsCandidate &&
            (e.Rarity & (EnemyRarity.Rare | EnemyRarity.Unique)) != 0);
        var action = this.combat.Evaluate(this.Settings.Combat, this.combatSnapshot, now,
            key => key != this.Settings.ToggleKey && !MovementInput.IsDown(key));
        if (action == null) { this.combatStatus = "waiting"; return false; }
        this.combatActionName = action.Name;
        this.combatActionKey = action.Key;
        if (this.Settings.PreviewOnly) { this.combatStatus = "preview"; return false; }

        // Recheck host state after the scan, before touching input. Every cast uses
        // this frame's snapshot; it is never queued across a pause or area change.
        if (Core.States.GameCurrentState != GameStateTypes.InGameState || this.IsUiBlocked(game.GameUi) ||
            area.Address != this.areaAddress || area.AreaHash != this.areaHash ||
            !Alive(primary, this.followerAddress, this.followerId) ||
            secondary != null && !Alive(secondary, this.secondaryAddress, this.secondaryId) ||
            !area.AwakeEntities.Values.Prepend(area.Player).Any(e => Alive(e, this.targetAddress, this.targetId)) ||
            Environment.TickCount64 - now > 150)
        { this.Suspend("target_changed"); return true; }
        this.DiscardRoute(); // Release navigation, preserving accumulated progress evidence.
        if (this.input?.TrySkill(action.Key, action.PressMilliseconds, Core.Process.Pid) != true)
        { this.Suspend("input_failed", 500); return true; }
        var acceptedAt = Environment.TickCount64;
        this.combat.RecordAccepted(action, acceptedAt);
        this.session.PauseProgress(action.PressMilliseconds + action.PauseMilliseconds);
        this.combatBusyUntil = acceptedAt + action.PressMilliseconds + action.PauseMilliseconds;
        this.combatStatus = "pressed";
        this.run.Status = "combat";
        return true;
    }

    private static bool Alive(Entity entity, IntPtr address, uint id) => entity.IsValid &&
        entity.Address == address && entity.Id == id &&
        CombatSnapshotReader.ReadComponent(entity, out Life life) && life.IsAlive;

    private string CombatStatusText()
    {
        var detail = this.combatStatus switch
        {
            "navigation" => this.PluginText.T("combat.status.navigation", "Combat waits for P2 correction / unstuck recovery"),
            "unavailable" => this.PluginText.T("combat.status.unavailable", "Combat waits for readable player data, a combat area and no grace period"),
            "preview" => this.PluginText.F("combat.status.preview", "Would press {0} ({1})", this.combatActionName, CombatKeyName(this.combatActionKey)),
            "pressed" => this.PluginText.F("combat.status.pressed", "Key sent: {0} ({1}); resuming follow after cast pause", this.combatActionName, CombatKeyName(this.combatActionKey)),
            "waiting" => this.PluginText.T("combat.status.waiting", "Waiting for a matching rule / cooldown"),
            _ => this.PluginText.T("combat.status.idle", "Combat idle; requires active following and a selected character"),
        };
        return this.PluginText.F("combat.status", "Combat [{0}] | nearby rare/unique (150 cells): {1} | {2}",
            this.combatPlayerName, this.combatEnemyCount, detail);
    }

    private static string CombatKeyName(int key) => key == 0 ? "—" : key == 0x20 ? "Space" : ((char)key).ToString();
}
