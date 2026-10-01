namespace Bloodybot2.Navigation;

using System.Numerics;

// Input phases are exclusive: shared WASD based on P1, or arrows based on P2.
// Shared movement follows the leader; correction only reunites P2 with P1.
internal sealed class CoopCoordinator
{
    public bool CorrectingP2 { get; private set; }

    public bool Update(Vector2 p1, Vector2 p2, bool playersHaveClearLine, float startGap, float rejoinGap)
    {
        var gap = Vector2.Distance(p1, p2);
        if (this.CorrectingP2 && playersHaveClearLine && gap <= rejoinGap)
            this.CorrectingP2 = false;
        else if (gap >= startGap)
            this.CorrectingP2 = true;
        return this.CorrectingP2;
    }

    public void Reset() => this.CorrectingP2 = false;
}
