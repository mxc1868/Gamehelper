namespace Bloodybot2.Navigation;

using System.Numerics;

internal enum RecoveryAction { Wait, Press, Repath }
internal readonly record struct RecoveryStep(RecoveryAction Action, MoveKeys Keys = MoveKeys.None, int LeaseMilliseconds = 200);

// Force bounded advances toward the current target, independent of terrain flags.
// Meaningful displacement ends recovery; failed rounds rest and replan.
internal sealed class UnstuckRecovery
{
    internal const int PulseMilliseconds = 600;
    internal const int InputLeaseMilliseconds = 150;
    internal const float ProgressDistance = 8;
    internal const int RestMilliseconds = 120;
    internal const int RoundRestMilliseconds = 1000;
    internal const int MaxPulses = 8;
    private Vector2 anchor;
    private int pulses;
    private MoveKeys held;
    private long pulseUntil;
    private long restUntil;
    private long finishAt;
    public bool Active { get; private set; }

    public bool BeginIfNeeded(Vector2 position, bool routeUnavailable, bool stalled)
    {
        if (this.Active || (!routeUnavailable && !stalled)) return false;
        this.Begin(position);
        return true;
    }

    public void Begin(Vector2 position)
    {
        this.anchor = position;
        this.pulses = 0;
        this.held = MoveKeys.None;
        this.pulseUntil = this.restUntil = this.finishAt = 0;
        this.Active = true;
    }

    public RecoveryStep Advance(Vector2 position, long now, MoveKeys towardTarget)
    {
        if (!this.Active) return new(RecoveryAction.Repath);
        if (!float.IsFinite(position.X + position.Y)) return new(RecoveryAction.Wait);
        if (Vector2.DistanceSquared(position, this.anchor) >= ProgressDistance * ProgressDistance)
            return this.Finish(); // Real displacement: release the probe and replan immediately.
        if (this.held != MoveKeys.None)
        {
            if (now < this.pulseUntil && this.held == towardTarget)
                return new(RecoveryAction.Press, this.held, Math.Min(InputLeaseMilliseconds, (int)(this.pulseUntil - now)));
            this.held = MoveKeys.None;
            this.restUntil = now + RestMilliseconds;
            return new(RecoveryAction.Wait);
        }
        if (now < this.restUntil) return new(RecoveryAction.Wait);
        if (this.finishAt != 0) return now >= this.finishAt ? this.Finish() : new(RecoveryAction.Wait);
        if (towardTarget == MoveKeys.None) return new(RecoveryAction.Wait);
        if (this.pulses < MaxPulses)
        {
            this.pulses++;
            this.held = towardTarget;
            this.pulseUntil = now + PulseMilliseconds;
            // A longer advance still requires fresh frames to renew a short lease.
            return new(RecoveryAction.Press, towardTarget, InputLeaseMilliseconds);
        }
        this.finishAt = now + RoundRestMilliseconds;
        return new(RecoveryAction.Wait);
    }

    private RecoveryStep Finish()
    {
        this.Active = false;
        this.held = MoveKeys.None;
        return new(RecoveryAction.Repath);
    }

    public void Reset() { this.Active = false; this.held = MoveKeys.None; }
}
