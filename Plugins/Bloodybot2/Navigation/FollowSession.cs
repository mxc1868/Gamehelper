namespace Bloodybot2.Navigation;

using System.Numerics;

internal sealed class FollowSession
{
    private bool catchingUp;
    private Vector2 anchor;
    private long progressAt;
    private bool measuring;

    public bool NeedsMovement(float distance, float stop, float resume)
    {
        if (!float.IsFinite(distance)) { this.Reset(); return false; }
        // Stop distance is a radius, independent of imperfect terrain/door flags.
        // A blocked line must neither override this radius nor restart movement.
        if (distance <= stop) this.catchingUp = false;
        else if (distance >= resume) this.catchingUp = true;
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
    public void ResetProgress() => this.measuring = false;
    // Preserve accumulated lack of progress, but exclude intentional cast stillness.
    public void PauseProgress(int milliseconds)
    {
        if (this.measuring) this.progressAt += Math.Max(0, milliseconds);
    }
}
