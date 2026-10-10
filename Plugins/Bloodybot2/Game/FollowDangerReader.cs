namespace Bloodybot2.Game;

using System.Numerics;
using BloodyBot.Combat;
using GameHelper.RemoteObjects.States.InGameStateObjects;

internal static class FollowDangerReader
{
    // Scan around the leader directly, independently of the combat monitor's
    // character, 150-grid scan radius, enabled state, and skill rule filters.
    public static int CountElites(AreaInstance area, Vector2 leader, float range)
    {
        var count = 0;
        foreach (var entity in area.AwakeEntities.Values)
            if (MonsterReader.Read(entity, leader, range) is { Hidden: false } monster &&
                monster.Rarity is EnemyRarity.Rare or EnemyRarity.Unique)
                count++;
        return count;
    }
}
