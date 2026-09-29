namespace BloodyBot.Combat;

/// <summary>One bounded press with timer-driven release. Failed key-up retains ownership for retry.</summary>
public sealed class SkillKeyPulse(Func<int, bool, bool> send)
{
    private readonly object sync = new();
    private int held;
    private long releaseAt;
    public int Held { get { lock (this.sync) return this.held; } }

    public bool TryStart(int key, int milliseconds, long now)
    {
        lock (this.sync)
        {
            if (this.held != 0 || !CombatRule.IsKeyAllowed(key) || !send(key, true)) return false;
            this.held = key;
            this.releaseAt = now + Math.Clamp(milliseconds, 30, 200);
            return true;
        }
    }

    public void Expire(long now, bool allowed)
    {
        lock (this.sync)
            if (!allowed || now >= this.releaseAt) this.Release();
    }

    public void Stop() { lock (this.sync) { this.releaseAt = 0; this.Release(); } }
    private void Release() { if (this.held != 0 && send(this.held, false)) this.held = 0; }
}
