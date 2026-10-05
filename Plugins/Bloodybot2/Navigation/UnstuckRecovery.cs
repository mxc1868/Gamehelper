namespace Bloodybot2.Navigation;

using System.Numerics;

internal enum RecoveryAction { Wait, Press, Repath }
internal readonly record struct RecoveryStep(RecoveryAction Action, MoveKeys Keys = MoveKeys.None, int LeaseMilliseconds = 150);

// Shared by P1 following and P2 correction. Only observed actor displacement is
// progress; terrain flags never veto a probe. All presses require fresh renewals.
internal sealed class UnstuckRecovery(Random? random = null)
{
    internal const int PulseMilliseconds = 600;
    internal const int InputLeaseMilliseconds = 150;
    internal const int DirectStallMilliseconds = 2000;
    internal const int RestMilliseconds = 120;
    internal const int RoundRestMilliseconds = 1000;
    internal const int MemoryMilliseconds = 10000;
    internal const float ProgressDistance = 8;
    private const float MemoryRadius = 12;
    private static readonly MoveKeys[] Directions =
    [MoveKeys.Up, MoveKeys.Up | MoveKeys.Right, MoveKeys.Right, MoveKeys.Down | MoveKeys.Right,
        MoveKeys.Down, MoveKeys.Down | MoveKeys.Left, MoveKeys.Left, MoveKeys.Up | MoveKeys.Left];
    private readonly Random random = random ?? Random.Shared;
    private readonly Dictionary<MoveKeys, (Vector2 Position, long Until)> failed = [];
    private readonly HashSet<MoveKeys> tried = [];
    private Vector2 progressAnchor;
    private Vector2 pulseAnchor;
    private Vector2 directionAnchor;
    private Vector2 routeAnchor;
    private MoveKeys held;
    private MoveKeys explorationDirection;
    private long? lastTick;
    private long pulseUntil;
    private long restUntil;
    private int noProgressMilliseconds;
    private int directionMilliseconds;
    private int routeMilliseconds;
    private bool exploring;
    private bool tryingRoute;
    private bool recordInitialFailure;
    public bool Active { get; private set; }
    public bool Exploring => this.Active && this.exploring && !this.tryingRoute;

    public bool BeginIfNeeded(Vector2 position, bool routeUnavailable, bool stalled)
    {
        if (this.Active || (!routeUnavailable && !stalled)) return false;
        this.Begin(position);
        // A normal route already spent its configured no-progress interval.
        // Do not add another two seconds of pushing into the same obstruction.
        this.exploring = stalled;
        this.recordInitialFailure = stalled;
        return true;
    }

    public void Begin(Vector2 position)
    {
        this.Active = true;
        this.progressAnchor = position;
        this.noProgressMilliseconds = 0;
        this.exploring = this.tryingRoute = false;
        this.recordInitialFailure = false;
        this.explorationDirection = MoveKeys.None;
        this.tried.Clear();
        this.Pause();
        // Failed directions survive replanning/re-entry at the same location.
    }

    public RecoveryStep Advance(Vector2 position, long now, MoveKeys towardTarget, MoveKeys routeKeys = MoveKeys.None)
    {
        if (!this.Active) return new(RecoveryAction.Repath);
        if (!float.IsFinite(position.X + position.Y) || towardTarget == MoveKeys.None)
        {
            this.Pause();
            return new(RecoveryAction.Wait);
        }
        var elapsed = this.lastTick.HasValue ? now - this.lastTick.Value : 0;
        this.lastTick = now;
        if (elapsed < 0 || elapsed > InputLeaseMilliseconds)
        {
            // Missing observations do not count as time spent attempting input.
            this.Pause();
            this.lastTick = now;
            return new(RecoveryAction.Wait);
        }
        foreach (var key in this.failed.Where(p => now >= p.Value.Until ||
            Vector2.DistanceSquared(position, p.Value.Position) > MemoryRadius * MemoryRadius).Select(p => p.Key).ToArray())
            this.failed.Remove(key);
        if (this.recordInitialFailure)
        {
            this.RememberFailure(position, now, routeKeys == MoveKeys.None ? towardTarget : routeKeys);
            this.recordInitialFailure = false;
        }

        if (Vector2.DistanceSquared(position, this.progressAnchor) >= 4)
        {
            this.progressAnchor = position;
            this.noProgressMilliseconds = 0;
        }
        else this.noProgressMilliseconds = Math.Min(DirectStallMilliseconds, this.noProgressMilliseconds + (int)elapsed);

        // At a remembered wall, skip the direct attempt even after a repath.
        if (!this.exploring && !this.tryingRoute && this.failed.ContainsKey(towardTarget))
            this.EnterExploration(position, now, towardTarget, remember: false);
        if (this.noProgressMilliseconds >= DirectStallMilliseconds && !this.Exploring)
            this.EnterExploration(position, now, this.held == MoveKeys.None ? towardTarget : this.held);

        // Let a successful side-step leave the corner before trying the target
        // route again. A route's existence alone is not evidence of recovery.
        var mayTryRoute = !this.exploring || this.tryingRoute ||
            (this.explorationDirection != MoveKeys.None &&
             Vector2.DistanceSquared(position, this.directionAnchor) >= ProgressDistance * ProgressDistance);
        if (now >= this.restUntil && routeKeys != MoveKeys.None && mayTryRoute && !this.failed.ContainsKey(routeKeys))
        {
            if (!this.tryingRoute)
            {
                this.tryingRoute = true;
                this.routeAnchor = position;
                this.routeMilliseconds = 0;
            }
            else this.routeMilliseconds += (int)elapsed;
            if (this.routeMilliseconds >= 300 && Vector2.DistanceSquared(position, this.routeAnchor) >= 16)
            {
                this.Active = false;
                this.Pause();
                return new(RecoveryAction.Repath);
            }
            this.held = routeKeys;
            return new(RecoveryAction.Press, routeKeys, InputLeaseMilliseconds);
        }
        if (this.tryingRoute)
        {
            this.tryingRoute = false;
            this.held = MoveKeys.None;
            this.restUntil = now + RestMilliseconds;
            // Preserve the escape direction and stall evidence if a route failed.
        }
        if (now < this.restUntil) return new(RecoveryAction.Wait);

        if (this.held != MoveKeys.None)
        {
            if (this.exploring)
            {
                this.directionMilliseconds += (int)Math.Clamp(this.pulseUntil - (now - elapsed), 0, elapsed);
                if (this.directionMilliseconds >= 2400 || Vector2.DistanceSquared(position, this.directionAnchor) >= 256)
                {
                    this.explorationDirection = this.held = MoveKeys.None;
                    this.restUntil = now + RestMilliseconds;
                    return new(RecoveryAction.Wait);
                }
            }
            if (!this.exploring && this.held != towardTarget)
            {
                this.held = MoveKeys.None;
                this.restUntil = now + RestMilliseconds;
                return new(RecoveryAction.Wait);
            }
            if (now < this.pulseUntil)
            {
                return new(RecoveryAction.Press, this.held, Math.Min(InputLeaseMilliseconds, (int)(this.pulseUntil - now)));
            }
            var moved = Vector2.DistanceSquared(position, this.pulseAnchor) >= 4;
            if (this.exploring && !moved)
            {
                this.RememberFailure(this.pulseAnchor, now, this.held);
                this.explorationDirection = MoveKeys.None;
            }
            this.held = MoveKeys.None;
            this.restUntil = now + RestMilliseconds;
            return new(RecoveryAction.Wait);
        }

        var next = towardTarget;
        if (this.exploring)
        {
            if (this.explorationDirection == MoveKeys.None)
            {
                next = this.ChooseDirection(towardTarget);
                if (next == MoveKeys.None)
                {
                    this.tried.Clear();
                    this.restUntil = now + RoundRestMilliseconds;
                    return new(RecoveryAction.Wait);
                }
                this.explorationDirection = next;
                this.directionAnchor = position;
                this.directionMilliseconds = 0;
                this.tried.Add(next);
            }
            else next = this.explorationDirection;
        }
        this.held = next;
        this.pulseAnchor = position;
        this.pulseUntil = now + PulseMilliseconds;
        return new(RecoveryAction.Press, next, InputLeaseMilliseconds);
    }

    private void EnterExploration(Vector2 position, long now, MoveKeys failedDirection, bool remember = true)
    {
        if (remember) this.RememberFailure(position, now, failedDirection);
        this.exploring = true;
        this.tryingRoute = false;
        this.explorationDirection = this.held = MoveKeys.None;
        this.restUntil = now + RestMilliseconds;
    }

    private void RememberFailure(Vector2 position, long now, MoveKeys direction)
    {
        if (direction != MoveKeys.None) this.failed[direction] = (position, now + MemoryMilliseconds);
    }

    private MoveKeys ChooseDirection(MoveKeys towardTarget)
    {
        var forward = Array.IndexOf(Directions, towardTarget);
        if (forward < 0) return MoveKeys.None;
        // Randomize the two sides of each tier, without replacement in a round.
        // Prefer sideways, then oblique, then backward; never veto by terrain.
        foreach (var offset in new[] { 2, 1, 3, 4, 0 })
        {
            var sign = this.random.Next(2) == 0 ? -1 : 1;
            foreach (var side in new[] { sign, -sign })
            {
                var key = Directions[(forward + side * offset + Directions.Length) % Directions.Length];
                if (!this.tried.Contains(key) && !this.failed.ContainsKey(key)) return key;
            }
        }
        return MoveKeys.None;
    }

    // Suspend input while keeping local failure memory and accumulated stall
    // time. The host resets everything when actor/target/area identity changes.
    public void Pause()
    {
        this.held = MoveKeys.None;
        this.lastTick = null;
        this.pulseUntil = this.restUntil = 0;
        this.tryingRoute = false;
    }

    public void Reset()
    {
        this.Active = false;
        this.Pause();
        this.failed.Clear();
        this.tried.Clear();
        this.explorationDirection = MoveKeys.None;
        this.exploring = false;
    }
}
