namespace Bloodybot2.Navigation;

// Logical directions are independent of the physical keys sent to Windows.
internal readonly record struct MovementBinding(int VirtualKey, ushort Scan, bool Extended, string Label)
{
    public uint Flags(bool down) => 0x0008u | (this.Extended ? 0x0001u : 0u) | (down ? 0u : 0x0002u);
}

internal static class MovementBindings
{
    public static MovementBinding Get(MoveKeys key, bool arrows) => (arrows, key) switch
    {
        (true, MoveKeys.Up) => new(0x26, 0x48, true, "↑"),
        (true, MoveKeys.Left) => new(0x25, 0x4B, true, "←"),
        (true, MoveKeys.Down) => new(0x28, 0x50, true, "↓"),
        (true, MoveKeys.Right) => new(0x27, 0x4D, true, "→"),
        (false, MoveKeys.Up) => new(0x57, 0x11, false, "W"),
        (false, MoveKeys.Left) => new(0x41, 0x1E, false, "A"),
        (false, MoveKeys.Down) => new(0x53, 0x1F, false, "S"),
        (false, MoveKeys.Right) => new(0x44, 0x20, false, "D"),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public static string Describe(MoveKeys keys, bool arrows) => keys == MoveKeys.None ? "—" :
        string.Join(" + ", KeyLease.Keys.Where(key => (keys & key) != 0).Select(key => Get(key, arrows).Label));
}
