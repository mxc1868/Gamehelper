namespace Bloodybot2.Navigation;

using System.Numerics;

// Shared movement still steers from P1, but its stop/resume radius protects
// whichever living character is closest to the leader. A lone P2 uses arrows.
internal readonly record struct FollowPlan(bool UseSecondary, bool Rejoining, float LeaderDistance)
{
    public static FollowPlan? Resolve(Vector2 primary, bool primaryAlive, Vector2 secondary,
        bool secondaryAlive, Vector2 leader, bool correctingP2)
    {
        if (!primaryAlive && !secondaryAlive) return null;
        var rejoining = primaryAlive && secondaryAlive && correctingP2;
        var primaryDistance = primaryAlive ? Vector2.Distance(primary, leader) : float.PositiveInfinity;
        var secondaryDistance = secondaryAlive ? Vector2.Distance(secondary, leader) : float.PositiveInfinity;
        return new(!primaryAlive || rejoining, rejoining, MathF.Min(primaryDistance, secondaryDistance));
    }
}
