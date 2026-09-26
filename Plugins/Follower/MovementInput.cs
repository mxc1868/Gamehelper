namespace Follower;

using System.Runtime.InteropServices;

internal sealed class MovementInput : IDisposable
{
    private readonly KeyLease lease;
    private readonly Timer watchdog;
    private uint pid;
    private volatile bool disposed;

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

    public bool HasManualMovement() => KeyLease.Keys.Any(key => (this.Held & key) == 0 && IsDown(VirtualKey(key)));
    public static bool HasModifier() => IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);
    public void Stop() => this.lease.Stop();
    private bool Allowed() => !this.disposed && IsForeground(Volatile.Read(ref this.pid)) &&
        !IsDown(0x1B) && !IsDown(0x0D) && !HasModifier(); // Escape, Enter, Ctrl/Alt/Windows

    private bool Send(MoveKeys key, bool down)
    {
        // Recheck the real OS foreground immediately before every key-down.
        if (down && !this.Allowed()) return false;
        var scan = key switch { MoveKeys.W => 0x11, MoveKeys.A => 0x1E, MoveKeys.S => 0x1F, MoveKeys.D => 0x20, _ => 0 };
        var input = new Input { Type = 1, Scan = (ushort)scan, Flags = 0x0008u | (down ? 0u : 0x0002u) };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    private static int VirtualKey(MoveKeys key) => key switch { MoveKeys.W => 0x57, MoveKeys.A => 0x41, MoveKeys.S => 0x53, MoveKeys.D => 0x44, _ => 0 };
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
