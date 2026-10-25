using GameHelper.Build;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[0] != "--verify"))
{
    Console.Error.WriteLine("Usage: OverlayPresentPatch [--verify] <build-output/ClickableTransparentOverlay.dll>");
    return 2;
}
try
{
    var path = args[^1];
    if (args.Length == 2)
    {
        if (!OverlayPresentPatcher.Inspect(File.ReadAllBytes(path)).Patched)
            throw new InvalidDataException("Overlay still uses blocking non-VSync Present.");
        Console.WriteLine("Verified: CTO non-VSync Present uses DO_NOT_WAIT; VSync and FPS limit unchanged.");
    }
    else
    {
        var changed = OverlayPresentPatcher.Patch(path);
        Console.WriteLine($"Overlay nonblocking Present: {(changed ? "patched two flag constants" : "already verified")}.");
        Console.WriteLine("Overlay SHA256: " + OverlayPresentPatcher.Inspect(File.ReadAllBytes(path)).Hash);
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Overlay presentation patch failed: " + ex.Message);
    return 1;
}
