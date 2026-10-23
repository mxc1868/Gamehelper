namespace Bloodybot2.Game;

using System.Numerics;
using BloodyBot.Combat;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;

internal readonly record struct MonsterInfo(uint Id, EnemyRarity Rarity, Vector2 Position, bool Immune, bool Hidden);

// Shared Radar-compatible monster classification for combat and low-health retreat.
// Invulnerable monsters can still threaten the leader, even when combat skips them.
internal static class MonsterReader
{
    public static MonsterInfo? Read(Entity entity, Vector2 center, float range)
    {
        if (!entity.IsValid || entity.EntityType != EntityTypes.Monster || entity.EntityState != EntityStates.None ||
            !CombatSnapshotReader.ReadComponent(entity, out Render render)) return null;
        var address = entity.Address;
        var id = entity.Id;
        var position = new Vector2(render.GridPosition.X, render.GridPosition.Y);
        var distance = Vector2.DistanceSquared(center, position);
        if (!float.IsFinite(distance) || distance > range * range ||
            !CombatSnapshotReader.ReadComponent(entity, out ObjectMagicProperties magic)) return null;
        var rarity = magic.Rarity switch
        {
            Rarity.Normal => EnemyRarity.Normal, Rarity.Magic => EnemyRarity.Magic,
            Rarity.Rare => EnemyRarity.Rare, Rarity.Unique => EnemyRarity.Unique, _ => EnemyRarity.None,
        };
        var immune = false;
        if (CombatSnapshotReader.ReadComponent(entity, out Stats stats))
        {
            lock (stats.StatsChangedByBuffAndActions)
            {
                if (stats.StatsChangedByBuffAndActions.GetValueOrDefault(GameStats.is_hidden_monster) == 1) return null;
                immune = HasImmunity(stats.StatsChangedByBuffAndActions);
            }
            lock (stats.StatsChangedByItems) immune |= HasImmunity(stats.StatsChangedByItems);
        }
        var hidden = CombatSnapshotReader.ReadComponent(entity, out Buffs buffs) && buffs.StatusEffects.ContainsKey("hidden_monster");
        if (!entity.IsValid || entity.Address != address || entity.Id != id || entity.EntityState != EntityStates.None) return null;
        return new(id, rarity, position, immune, hidden);
    }

    private static bool HasImmunity(Dictionary<GameStats, int> stats) =>
        stats.GetValueOrDefault(GameStats.cannot_be_damaged) != 0 ||
        stats.GetValueOrDefault(GameStats.base_cannot_be_damaged) != 0;
}
