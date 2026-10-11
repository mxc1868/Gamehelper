namespace Bloodybot2.Game;

using System.Numerics;
using BloodyBot.Combat;
using Bloodybot2.Navigation;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Objects.Components;

internal static class LowHealthReader
{
    public static float? Percent(Life life)
    {
        static int? Capacity(VitalStruct pool, bool required)
        {
            if (pool.Total < 0 || pool.Current < 0 || pool.ReservedFlat < 0 || pool.ReservedPercent is < 0 or > 10000) return null;
            var available = pool.Total - Math.Ceiling(pool.Total * (double)pool.ReservedPercent / 10000) - pool.ReservedFlat;
            if (available < 0 || required && available <= 0 || available == 0 && pool.Current != 0) return null;
            return (int)available;
        }
        var health = life.Health;
        var shield = life.EnergyShield;
        var lifeMax = Capacity(health, true);
        var shieldMax = Capacity(shield, false);
        return lifeMax is int hp && shieldMax is int es
            ? EscapePlanner.CombinedPercent(health.Current, hp, shield.Current, es) : null;
    }

    // null marks truncation, which must wait rather than treat unseen packs as safe.
    public static List<Vector2>? Rares(AreaInstance area, Vector2 center, float range)
    {
        var rares = new List<Vector2>();
        foreach (var entity in area.AwakeEntities.Values.DistinctBy(e => e.Address))
            if (MonsterReader.Read(entity, center, range) is { Hidden: false, Rarity: EnemyRarity.Rare } monster)
            {
                if (rares.Count == EscapePlanner.MaximumRares) return null;
                rares.Add(monster.Position);
            }
        return rares;
    }
}
