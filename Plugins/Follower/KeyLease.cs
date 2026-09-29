namespace Follower;

// The lease is renewed only by a valid DrawUI frame. A separate timer expires it
// if F9 suppresses DrawUI, the render loop stalls, or the foreground window changes.
// Failed key-up events retain ownership so the watchdog can retry them.
internal sealed class KeyLease(Func<MoveKeys, bool, bool> send)
{
    private readonly object sync = new();
    private MoveKeys held;
    private long expires;
    public MoveKeys Held { get { lock (this.sync) return this.held; } }

    public bool Renew(MoveKeys wanted, long now, int leaseMilliseconds = 200)
    {
        lock (this.sync)
        {
            if (!this.Release(this.held & ~wanted)) return false;
            foreach (var key in Keys)
            {
                if ((wanted & key) == 0 || (this.held & key) != 0) continue;
                if (!send(key, true)) { this.Release(this.held); return false; }
                this.held |= key;
            }
            this.expires = now + Math.Clamp(leaseMilliseconds, 1, 200);
            return true;
        }
    }

    public void Expire(long now, bool allowed)
    {
        lock (this.sync)
            if (!allowed || now >= this.expires) this.Release(this.held);
    }

    public void Stop() { lock (this.sync) { this.expires = 0; this.Release(this.held); } }

    // Changing the physical mapping must release using the OLD mapping, under the
    // same lock as watchdog releases. Failed key-ups keep both ownership and mapping.
    public bool TryReset(Action change)
    {
        lock (this.sync)
        {
            this.expires = 0;
            if (!this.Release(this.held)) return false;
            change();
            return true;
        }
    }

    private bool Release(MoveKeys keys)
    {
        var success = true;
        foreach (var key in Keys)
        {
            if ((keys & key) == 0) continue;
            if (send(key, false)) this.held &= ~key;
            else success = false;
        }
        return success;
    }

    internal static readonly MoveKeys[] Keys = [MoveKeys.Up, MoveKeys.Left, MoveKeys.Down, MoveKeys.Right];
}
