namespace Bloodybot2.Navigation;

using System.Runtime.InteropServices;
using BloodyBot.Combat;
using Bloodybot2.Runtime;

internal sealed class MovementInput : IBotInput, IDisposable
{
    private readonly KeyLease lease;
    private readonly SkillKeyPulse skill;
    private readonly ManualMovementTracker manual = new();
    private readonly Timer watchdog;
    private uint pid;
    private volatile bool disposed;
    private bool arrows;
    private int stopRequested;

    public MovementInput()
    {
        this.lease = new(this.Send);
        this.skill = new(this.SendSkill);
        this.watchdog = new(_ =>
        {
            // Remember Escape even while DrawUI is suppressed by F9. Otherwise
            // automatic resume could undo an explicit stop during that interval.
            if (IsDown(0x1B) && IsForeground(Volatile.Read(ref this.pid))) Interlocked.Exchange(ref this.stopRequested, 1);
            this.lease.Expire(Environment.TickCount64, this.Allowed());
            this.skill.Expire(Environment.TickCount64, this.Allowed());
            if (this.disposed && this.lease.Held == MoveKeys.None && this.skill.Held == 0) this.watchdog?.Dispose();
        }, null, 25, 25);
        AppDomain.CurrentDomain.ProcessExit += this.OnExit;
    }

    public MoveKeys Held => this.lease.Held;
    public bool SkillHeld => this.skill.Held != 0;
    public bool Foreground => IsForeground(Volatile.Read(ref this.pid));
    public bool CanInput => this.Allowed();
    public bool IsKeyAvailable(int key) => !IsDown(key);
    public bool TryPress(int key, int milliseconds, long now) => this.TrySkill(key, milliseconds, Volatile.Read(ref this.pid));
    public void Release() => this.Stop();
    public void Expire(long now, bool allowed)
    {
        this.skill.Expire(now, allowed && this.Allowed());
        this.lease.Expire(now, allowed && this.Allowed());
    }
    public bool ConsumeStopRequest() => Interlocked.Exchange(ref this.stopRequested, 0) != 0;
    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool IsForeground(uint processId)
    {
        if (processId == 0) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var active);
        return active == processId;
    }

    public void ObserveProcess(uint processId)
    {
        if (Volatile.Read(ref this.pid) != processId) this.Stop();
        Volatile.Write(ref this.pid, processId);
    }

    public bool Apply(MoveKeys keys, uint processId, int leaseMilliseconds = 200)
    {
        this.ObserveProcess(processId);
        if (this.disposed || !this.Allowed()) { this.Stop(); return false; }
        if (this.SkillHeld) { this.lease.Stop(); return false; }
        return this.lease.Renew(keys, Environment.TickCount64, leaseMilliseconds);
    }

    public bool TrySkill(int key, int milliseconds, uint processId)
    {
        this.ObserveProcess(processId);
        this.lease.Stop();
        if (this.lease.Held != MoveKeys.None || !this.Allowed() || IsDown(key)) return false;
        return this.skill.TryStart(key, milliseconds, Environment.TickCount64);
    }

    public bool SetArrowMode(bool value) => this.arrows == value || this.lease.TryReset(() => this.arrows = value);
    public void CaptureManualBaseline() => this.manual.CaptureBaseline(Environment.TickCount64, IsDown);
    public string ReadManualInput(bool bothLayouts)
    {
        var keys = new List<string>();
        var movement = this.manual.Read(this.Held, this.arrows, bothLayouts, Environment.TickCount64, IsDown);
        if (movement.Length != 0) keys.Add(movement);
        foreach (var (key, label) in new[] { (0x11, "Ctrl"), (0x12, "Alt"), (0x5B, "LWin"), (0x5C, "RWin") })
            if (IsDown(key)) keys.Add(label);
        return string.Join(" + ", keys);
    }
    public static bool HasModifier() => IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);
    public void StopMovement() => this.lease.Stop();
    public void Stop() { this.lease.Stop(); this.skill.Stop(); }
    private bool Allowed() => !this.disposed && IsForeground(Volatile.Read(ref this.pid)) &&
        !IsDown(0x1B) && !IsDown(0x0D) && !HasModifier(); // Escape, Enter, Ctrl/Alt/Windows

    private bool Send(MoveKeys key, bool down)
    {
        // Recheck the real OS foreground immediately before every key-down.
        if (down && !this.Allowed()) return false;
        var binding = MovementBindings.Get(key, this.arrows);
        var input = new Input { Type = 1, Scan = binding.Scan, Flags = binding.Flags(down) };
        // Record before sending: the watchdog and DrawUI can observe Windows key
        // state before lease ownership has caught up. Include key-up events too.
        this.manual.RecordSynthetic(binding.VirtualKey, Environment.TickCount64);
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private bool SendSkill(int key, bool down)
    {
        if (down && !this.Allowed()) return false;
        var scan = MapVirtualKey((uint)key, 0);
        if (scan == 0) return false;
        var input = new Input { Type = 1, Scan = (ushort)scan, Flags = down ? 0x0008u : 0x000Au };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private void OnExit(object? sender, EventArgs args) => this.Stop();
    public void Dispose()
    {
        this.disposed = true;
        this.Stop();
        if (this.lease.Held == MoveKeys.None && !this.SkillHeld) this.watchdog.Dispose();
        AppDomain.CurrentDomain.ProcessExit -= this.OnExit;
    }

    // Windows x64 INPUT union is 40 bytes; KEYBDINPUT begins at byte 8.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
}
