namespace Bloodybot2.Game;

using System.Runtime.InteropServices;
using BloodyBot.Combat;
using Bloodybot2.Runtime;

internal sealed class KeyboardInput : IBotInput, IDisposable
{
    private readonly SkillKeyPulse pulse;
    private uint pid;
    private volatile bool disposed;
    private Timer? releaseRetry;
    public KeyboardInput() { this.pulse = new(this.Send); }
    public void Observe(uint processId)
    {
        if (Volatile.Read(ref this.pid) != processId) this.Release();
        Volatile.Write(ref this.pid, processId);
    }
    public bool Foreground => IsForeground(Volatile.Read(ref this.pid));
    public bool Allowed => !this.disposed && this.Foreground && !IsDown(0x1B) && !IsDown(0x0D) &&
        !IsDown(0x11) && !IsDown(0x12) && !IsDown(0x5B) && !IsDown(0x5C);
    public bool TryPress(int key, int milliseconds, long now) => this.Allowed && !IsDown(key) && this.pulse.TryStart(key, milliseconds, now);
    public bool IsKeyAvailable(int key) => !IsDown(key);
    public void Release() => this.pulse.Stop();
    public void Expire(long now, bool allowed) => this.pulse.Expire(now, allowed && this.Allowed);
    public void Dispose()
    {
        this.disposed = true;
        this.Release();
        if (this.pulse.Held == 0) return;
        // Disabling the plugin must not discard ownership after a failed key-up.
        // Keep only a release retry alive; disposed forbids any further key-down.
        this.releaseRetry = new Timer(_ =>
        {
            this.Release();
            if (this.pulse.Held == 0) this.releaseRetry?.Dispose();
        }, null, Timeout.Infinite, Timeout.Infinite);
        this.releaseRetry.Change(25, 25);
    }
    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool IsForeground(uint processId)
    {
        if (processId == 0) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var active);
        return active == processId;
    }
    private bool Send(int key, bool down)
    {
        if (down && !this.Allowed) return false;
        var scan = MapVirtualKey((uint)key, 0);
        if (scan == 0) return false;
        var input = new Input { Type = 1, Scan = (ushort)scan, Flags = down ? 0x8u : 0xAu };
        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }
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
