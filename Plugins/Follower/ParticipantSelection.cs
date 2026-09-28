namespace Follower;

internal readonly record struct PlayerIdentity(uint Id, nint Address, string Name);
internal readonly record struct FollowParticipants(PlayerIdentity Primary, PlayerIdentity Leader, PlayerIdentity? Secondary = null);

internal static class ParticipantSelection
{
    public static FollowParticipants? Resolve(IEnumerable<PlayerIdentity> players, PlayerIdentity? local,
        bool localCoop, string leaderName, string primaryName, string secondaryName, out string failure)
    {
        // Area.Player and AwakeEntities can contain the same character.
        var candidates = players.Where(p => p.Address != 0 && !string.IsNullOrWhiteSpace(p.Name))
            .DistinctBy(p => (p.Address, p.Id)).ToArray();
        failure = "leader_name";
        if (string.IsNullOrWhiteSpace(leaderName)) return null;
        failure = "primary_name";
        if (localCoop && string.IsNullOrWhiteSpace(primaryName)) return null;
        failure = "follower_name";
        if (localCoop && string.IsNullOrWhiteSpace(secondaryName)) return null;
        failure = "same_player";
        if (localCoop && (string.Equals(primaryName, secondaryName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(leaderName, secondaryName, StringComparison.OrdinalIgnoreCase))) return null;

        var primary = localCoop ? FindUnique(candidates, primaryName) : local;
        failure = "player_invalid";
        if (primary == null || primary.Value.Address == 0) return null;
        var secondary = localCoop ? FindUnique(candidates, secondaryName) : null;
        failure = "secondary_invalid";
        if (localCoop && secondary == null) return null;
        // P1 may itself be the leader, allowing manual P1 + automatic P2 use too.
        // Otherwise both controlled characters follow the separately selected leader.
        var leader = FindUnique(candidates.Where(p => localCoop ||
            (p.Address != primary.Value.Address && p.Id != primary.Value.Id)), leaderName);
        failure = "target_missing";
        if (leader == null) return null;
        failure = "same_player";
        if (secondary is PlayerIdentity p2 && (p2.Address == primary.Value.Address || p2.Id == primary.Value.Id ||
            p2.Address == leader.Value.Address || p2.Id == leader.Value.Id)) return null;
        failure = string.Empty;
        return new(primary.Value, leader.Value, secondary);
    }

    private static PlayerIdentity? FindUnique(IEnumerable<PlayerIdentity> candidates, string name)
    {
        var matches = candidates.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
