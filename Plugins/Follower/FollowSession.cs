namespace Follower;

using System.Numerics;

internal sealed class FollowSession
{
    private bool catchingUp;
    private Vector2 anchor;
    private long progressAt;
    private bool measuring;

    public bool NeedsMovement(float distance, bool directLine, float stop, float resume)
    {
        if (!float.IsFinite(distance)) { this.Reset(); return false; }
        if (directLine && distance <= stop) this.catchingUp = false;
        else if (!directLine || distance >= resume) this.catchingUp = true;
        return this.catchingUp;
    }

    public bool IsStuck(Vector2 position, bool moving, long now, int timeout)
    {
        if (!moving) { this.measuring = false; return false; }
        if (!this.measuring || Vector2.DistanceSquared(position, this.anchor) >= 4)
        {
            this.measuring = true;
            this.anchor = position;
            this.progressAt = now;
        }
        return now - this.progressAt >= timeout;
    }

    public void Reset() { this.catchingUp = false; this.measuring = false; }
}
