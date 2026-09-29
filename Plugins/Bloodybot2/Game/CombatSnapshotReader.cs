namespace Bloodybot2.Game;

using System.Numerics;
using BloodyBot.Combat;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Objects.Components;

// Adapter over existing public GameHelper APIs. No offsets, navigation or input here.
internal static class CombatSnapshotReader
{
    public static CombatSnapshot? Read(AreaInstance area, Entity player, long now, bool canFight)
    {
        if (!player.IsValid || !ReadComponent(player, out Life life) || !life.IsAlive ||
            !ReadComponent(player, out Render render) || !ReadComponent(player, out Buffs buffs)) return null;
        var playerAddress = player.Address;
        var playerId = player.Id;
        var position = new Vector2(render.GridPosition.X, render.GridPosition.Y);
        var buffNames = buffs.StatusEffects.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usable = ReadComponent(player, out Actor actor)
            ? actor.IsSkillUsable.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var state = new CombatPlayer(position, life.IsAlive, Percent(life.Health),
            Percent(life.EnergyShield), Percent(life.Mana), buffNames, usable);
        var enemies = new List<CombatEnemy>();
        foreach (var entity in area.AwakeEntities.Values)
        {
            if (!entity.IsValid || entity.EntityType != EntityTypes.Monster ||
                entity.EntityState is EntityStates.Useless or EntityStates.MonsterFriendly or EntityStates.PinnacleBossHidden ||
                !ReadComponent(entity, out Render enemyRender)) continue;
            var enemyPosition = new Vector2(enemyRender.GridPosition.X, enemyRender.GridPosition.Y);
            var distance = Vector2.Distance(position, enemyPosition);
            if (!float.IsFinite(distance) || distance > 150 ||
                !ReadComponent(entity, out Life enemyLife) || !enemyLife.IsAlive ||
                !ReadComponent(entity, out Positioned positioned) || positioned.IsFriendly ||
                !ReadComponent(entity, out Targetable targetable) || !targetable.IsTargetable ||
                !ReadComponent(entity, out ObjectMagicProperties magic)) continue;
            var rarity = magic.Rarity switch
            {
                Rarity.Normal => EnemyRarity.Normal, Rarity.Magic => EnemyRarity.Magic,
                Rarity.Rare => EnemyRarity.Rare, Rarity.Unique => EnemyRarity.Unique, _ => EnemyRarity.None,
            };
            var immune = ReadComponent(entity, out Stats stats) &&
                (HasImmunity(stats.StatsChangedByBuffAndActions) || HasImmunity(stats.StatsChangedByItems));
            if (ReadComponent(entity, out Buffs enemyBuffs) && enemyBuffs.StatusEffects.ContainsKey("hidden_monster")) immune = true;
            enemies.Add(new(entity.Id, rarity, enemyPosition, true, true, true, immune));
        }
        if (!player.IsValid || player.Address != playerAddress || player.Id != playerId || !life.IsAlive) return null;
        return new(now, canFight && !buffNames.Contains("grace_period"), state, enemies);
    }

    internal static bool ReadComponent<T>(Entity entity, out T component) where T : ComponentBase
    {
        if (entity.TryGetComponent<T>(out var value) && value.Address != IntPtr.Zero && value.IsParentValid(entity.Address))
        { component = value; return true; }
        component = null!;
        return false;
    }

    private static float? Percent(VitalStruct vital) => vital.Total > 0 && vital.Unreserved > 0 && vital.Current >= 0
        ? Math.Clamp(100f * vital.Current / vital.Unreserved, 0, 100) : null;

    private static bool HasImmunity(Dictionary<GameStats, int> stats)
    {
        lock (stats)
            return stats.GetValueOrDefault(GameStats.cannot_be_damaged) != 0 ||
                   stats.GetValueOrDefault(GameStats.base_cannot_be_damaged) != 0;
    }
}
