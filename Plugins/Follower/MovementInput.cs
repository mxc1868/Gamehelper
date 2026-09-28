namespace Follower;

using System.Runtime.InteropServices;

internal sealed class MovementInput : IDisposable
{
    private readonly KeyLease lease;
    private readonly Timer watchdog;
    private uint pid;
    private volatile bool disposed;
    private bool arrows;

    public MovementInput()
    {
        this.lease = new(this.Send);
        this.watchdog = new(_ => this.lease.Expire(Environment.TickCount64, this.Allowed()), null, 25, 25);
        AppDomain.CurrentDomain.ProcessExit += this.OnExit;
    }

    public MoveKeys Held => this.lease.Held;
    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool IsForeground(uint processId)
    {
        if (processId == 0) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var active);
        return active == processId;
    }

    public bool Apply(MoveKeys keys, uint processId)
    {
        if (Volatile.Read(ref this.pid) != processId) this.Stop();
        Volatile.Write(ref this.pid, processId);
        if (this.disposed || !this.Allowed()) { this.Stop(); return false; }
        return this.lease.Renew(keys, Environment.TickCount64);
    }

    public bool SetArrowMode(bool value) => this.arrows == value || this.lease.TryReset(() => this.arrows = value);
    public bool HasManualMovement(bool bothLayouts = false) => MovementBindings.HasManualMovement(this.Held, this.arrows, IsDown, bothLayouts);
    public static bool HasModifier() => IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);
    public void Stop() => this.lease.Stop();
    private bool Allowed() => !this.disposed && IsForeground(Volatile.Read(ref this.pid)) &&
        !IsDown(0x1B) && !IsDown(0x0D) && !HasModifier(); // Escape, Enter, Ctrl/Alt/Windows

    private bool Send(MoveKeys key, bool down)
    {
        // Recheck the real OS foreground immediately before every key-down.
        if (down && !this.Allowed()) return false;
        var binding = MovementBindings.Get(key, this.arrows);
        var input = new Input { Type = 1, Scan = binding.Scan, Flags = binding.Flags(down) };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private void OnExit(object? sender, EventArgs args) => this.Stop();
    public void Dispose()
    {
        this.disposed = true;
        this.Stop();
        this.watchdog.Dispose();
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
}
