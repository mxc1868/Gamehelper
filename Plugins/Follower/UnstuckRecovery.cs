namespace Follower;

using System.Numerics;

internal enum RecoveryAction { Wait, Press, Repath }
internal readonly record struct RecoveryStep(RecoveryAction Action, MoveKeys Keys = MoveKeys.None, int LeaseMilliseconds = 200);

// A bounded sequence of short probes. Every probe must still pass the current
// terrain/projection checks; failed rounds rest and replan instead of disabling.
internal sealed class UnstuckRecovery
{
    internal const int PulseMilliseconds = 150;
    internal const int RestMilliseconds = 120;
    internal const int RoundRestMilliseconds = 1000;
    private static readonly MoveKeys[] Directions =
    [MoveKeys.Up, MoveKeys.Up | MoveKeys.Right, MoveKeys.Right, MoveKeys.Down | MoveKeys.Right,
     MoveKeys.Down, MoveKeys.Down | MoveKeys.Left, MoveKeys.Left, MoveKeys.Up | MoveKeys.Left];
    private static readonly int[] TurnOrder = [2, -2, 4, 1, -1, 3, -3, 0];
    private Vector2 anchor;
    private int preferredIndex;
    private int nextDirection;
    private int round;
    private MoveKeys held;
    private long pulseUntil;
    private long restUntil;
    private long finishAt;
    public bool Active { get; private set; }

    public void Begin(Vector2 position, MoveKeys preferred)
    {
        this.anchor = position;
        this.preferredIndex = Math.Max(0, Array.IndexOf(Directions, preferred));
        this.nextDirection = 0;
        this.held = MoveKeys.None;
        this.pulseUntil = this.restUntil = this.finishAt = 0;
        this.Active = true;
    }

    public RecoveryStep Advance(Vector2 position, long now, Func<MoveKeys, bool> canProbe)
    {
        if (!this.Active) return new(RecoveryAction.Repath);
        if (!float.IsFinite(position.X + position.Y)) return new(RecoveryAction.Wait);
        if (Vector2.DistanceSquared(position, this.anchor) >= 1)
            return this.Finish(); // Real displacement: release the probe and replan immediately.
        if (this.held != MoveKeys.None)
        {
            if (now < this.pulseUntil && canProbe(this.held)) return new(RecoveryAction.Press, this.held, (int)(this.pulseUntil - now));
            this.held = MoveKeys.None;
            this.restUntil = now + RestMilliseconds;
            return new(RecoveryAction.Wait);
        }
        if (now < this.restUntil) return new(RecoveryAction.Wait);
        if (this.finishAt != 0) return now >= this.finishAt ? this.Finish() : new(RecoveryAction.Wait);
        while (this.nextDirection < TurnOrder.Length)
        {
            var index = (this.preferredIndex + TurnOrder[this.nextDirection++] + this.round + 16) % Directions.Length;
            var key = Directions[index];
            if (!canProbe(key)) continue;
            this.held = key;
            this.pulseUntil = now + PulseMilliseconds;
            return new(RecoveryAction.Press, key, PulseMilliseconds);
        }
        this.finishAt = now + RoundRestMilliseconds;
        return new(RecoveryAction.Wait);
    }

    private RecoveryStep Finish()
    {
        this.Active = false;
        this.held = MoveKeys.None;
        this.round = (this.round + 1) % Directions.Length;
        return new(RecoveryAction.Repath);
    }

    public void Reset() { this.Active = false; this.held = MoveKeys.None; this.round = 0; }
}
