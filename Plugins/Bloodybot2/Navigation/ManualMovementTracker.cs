namespace Bloodybot2.Navigation;

// GetAsyncKeyState includes synthetic input and may lag behind SendInput. Keep
// per-key send history across key-up and WASD/arrow mapping changes; current
// lease ownership alone is not enough to identify a human takeover.
internal sealed class ManualMovementTracker
{
    internal const int SettleMilliseconds = 200;
    private readonly object sync = new();
    private readonly Dictionary<int, long> ignoreUntilReleased = new();

    public void CaptureBaseline(long now, Func<int, bool> isDown)
    {
        lock (this.sync)
            foreach (var arrows in new[] { false, true })
                foreach (var direction in KeyLease.Keys)
                {
                    var key = MovementBindings.Get(direction, arrows).VirtualKey;
                    // A start command must not immediately pause on stale key
                    // state from before the session. Do not erase pending sends.
                    if (isDown(key)) this.ignoreUntilReleased.TryAdd(key, now);
                }
    }

    public void RecordSynthetic(int key, long now)
    {
        lock (this.sync) this.ignoreUntilReleased[key] = now + SettleMilliseconds;
    }

    public string Read(MoveKeys held, bool arrows, bool bothLayouts, long now, Func<int, bool> isDown)
    {
        lock (this.sync)
        {
            var manual = new List<string>();
            foreach (var layout in new[] { false, true })
                foreach (var direction in KeyLease.Keys)
                {
                    var binding = MovementBindings.Get(direction, layout);
                    var down = isDown(binding.VirtualKey);
                    if (this.ignoreUntilReleased.TryGetValue(binding.VirtualKey, out var settleUntil))
                    {
                        // An early "up" can precede queued injected input. After
                        // settling, wait for an observed up before rearming.
                        if (now >= settleUntil && !down) this.ignoreUntilReleased.Remove(binding.VirtualKey);
                        continue;
                    }
                    if (!down || (layout == arrows && (held & direction) != 0)) continue;
                    if (layout == arrows || bothLayouts) manual.Add(binding.Label);
                }
            return string.Join(" + ", manual);
        }
    }
}
