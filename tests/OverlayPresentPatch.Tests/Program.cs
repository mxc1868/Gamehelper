using System.Security.Cryptography;
using GameHelper.Build;

if (args.Length != 1) throw new ArgumentException("Pass the original CTO 11.1.0 package DLL path (read only).");
var source = Path.GetFullPath(args[0]);
var original = File.ReadAllBytes(source);
var originalHash = Convert.ToHexString(SHA256.HashData(original));
var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
void Reject(string name, Action action)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or BadImageFormatException or InvalidOperationException) { rejected = true; }
    Check(name, rejected);
}
var directory = Path.Combine(Path.GetTempPath(), "GameHelper-overlay-patch-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var first = OverlayPresentPatcher.Inspect(original);
    Check("original package is recognized by its complete SHA-256", originalHash == OverlayPresentPatcher.OriginalHash && !first.Patched);
    Check("two non-VSync flag constants are identified", first.FlagFileOffsets.Length == 2 && first.FlagFileOffsets.All(offset => original[offset] == 0x16));
    var copy = Path.Combine(directory, "ClickableTransparentOverlay.dll");
    File.WriteAllBytes(copy, original);
    Check("build copy is patched", OverlayPresentPatcher.Patch(copy));
    var patched = File.ReadAllBytes(copy);
    var second = OverlayPresentPatcher.Inspect(patched);
    var changes = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToArray();
    Check("exactly two bytes change and nothing else", changes.SequenceEqual(first.FlagFileOffsets));
    Check("both flag constants become ldc.i4.8 (DO_NOT_WAIT)", changes.All(offset => patched[offset] == 0x1E));
    Check("file length and module identity are preserved", patched.Length == original.Length && first.ModuleId == second.ModuleId);
    Check("VSync, frame pacing, metadata and all other code stay byte-identical", Enumerable.Range(0, original.Length).Where(i => !changes.Contains(i)).All(i => original[i] == patched[i]));
    Check("patched binary is independently recognizable", second.Patched && second.Hash == "8C0353939A682A00A4EE12040C7407DF5E42D3CB031DF700624CCD31A56A81FD");
    var lastWrite = File.GetLastWriteTimeUtc(copy);
    Check("incremental builds do not rewrite an already-patched DLL", !OverlayPresentPatcher.Patch(copy) && File.GetLastWriteTimeUtc(copy) == lastWrite);
    Check("package cache/source copy remains untouched", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == originalHash);

    var partial = (byte[])patched.Clone(); partial[changes[0]] = 0x16;
    Reject("half-applied patches are rejected", () => OverlayPresentPatcher.Inspect(partial));
    var unrelated = (byte[])patched.Clone(); unrelated[2] ^= 1;
    Reject("unrelated binary modifications are rejected", () => OverlayPresentPatcher.Inspect(unrelated));
    var unexpected = (byte[])original.Clone(); unexpected[changes[0]] = 0x18;
    Reject("unexpected presentation flags are rejected", () => OverlayPresentPatcher.Inspect(unexpected));
    Reject("invalid PE files are rejected", () => OverlayPresentPatcher.Inspect([1, 2, 3]));
    Reject("the patcher refuses NuGet-cache destinations", () => OverlayPresentPatcher.Patch(Path.Combine(directory, ".nuget", "packages", "dummy.dll")));

    File.WriteAllBytes(copy, original);
    using (var locked = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool blocked = false;
        try { OverlayPresentPatcher.Patch(copy); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { blocked = true; }
        Check("a locked runtime DLL blocks replacement normally", blocked);
        Check("failed replacement leaves the original DLL intact", File.ReadAllBytes(copy).SequenceEqual(original));
    }
    Check("temporary patch files are cleaned after a lock failure", Directory.GetFiles(directory, "*.tmp").Length == 0);
    Console.WriteLine($"Passed {passed} presentation patch checks. No graphics device, game, or input.");
}
finally
{
    var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).StartsWith("GameHelper-overlay-patch-", StringComparison.Ordinal))
        Directory.Delete(directory, recursive: true);
}
