namespace GameHelper.Diagnostics;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class RuntimeDiagnostics
{
    private static RuntimeDiagnosticSession? session;
    private static readonly IDisposable Empty = new EmptyScope();

    internal static IDisposable Start(string baseDirectory)
    {
        var current = new RuntimeDiagnosticSession(Path.Combine(baseDirectory, "diagnostics"), observeDesktop: ObserveDesktop);
        Volatile.Write(ref session, current);
        var output = Console.Out;
        var error = Console.Error;
        var capturedOutput = new DiagnosticConsoleWriter(output, current, "stdout");
        var capturedError = new DiagnosticConsoleWriter(error, current, "stderr");
        Console.SetOut(capturedOutput);
        Console.SetError(capturedError);
        return new Lifetime(current, output, error, capturedOutput, capturedError);
    }

    internal static IDisposable BeginFrame() => Volatile.Read(ref session)?.BeginFrame() ?? Empty;
    internal static IDisposable Measure(string phase) => Volatile.Read(ref session)?.Measure(phase) ?? Empty;
    internal static bool SnapshotDue() => Volatile.Read(ref session)?.SnapshotDue() == true;
    internal static void PublishSnapshot(object snapshot, int key) => Volatile.Read(ref session)?.PublishSnapshot(snapshot, key);
    internal static void Record(string kind, object? data) => Volatile.Read(ref session)?.Record(kind, data);
    internal static void CountMemoryFailure(bool plausibleAddress) => Volatile.Read(ref session)?.CountMemoryFailure(plausibleAddress);

    private static DesktopObservation ObserveDesktop(int menuKey)
    {
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var processId);
        return new(window.ToInt64(), unchecked((int)processId), menuKey is >= 0 and <= 255 && (GetAsyncKeyState(menuKey) & 0x8000) != 0);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    private sealed class Lifetime(RuntimeDiagnosticSession current, TextWriter output, TextWriter error,
        DiagnosticConsoleWriter capturedOutput, DiagnosticConsoleWriter capturedError) : IDisposable
    {
        public void Dispose()
        {
            Console.SetOut(output);
            Console.SetError(error);
            capturedOutput.Flush();
            capturedError.Flush();
            Interlocked.CompareExchange(ref session, null, current);
            current.Dispose();
        }
    }
}
