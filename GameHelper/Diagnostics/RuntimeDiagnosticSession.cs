namespace GameHelper.Diagnostics;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

internal sealed record DiagnosticOptions(
    int FileBytes = 2 * 1024 * 1024,
    int QueueCapacity = 256,
    int HeartbeatMs = 5000,
    int StallMs = 3000,
    int PollMs = 50);

internal sealed record DesktopObservation(long Window, int ProcessId, bool MenuKeyDown);

/// <summary>
/// Local, bounded diagnostics. The monitor reads published snapshots only; it never
/// traverses remote objects, touches the coroutine scheduler, or sends input.
/// All file writes take place on a separate worker, including when Render is stuck.
/// </summary>
internal sealed class RuntimeDiagnosticSession : IDisposable
{
    private sealed record Activity(string Name, long Started, int Thread);
    private sealed record Snapshot(object Data, long Captured);
    private sealed record Entry(DateTimeOffset Time, string Kind, object? Data);

    private readonly DiagnosticOptions options;
    private readonly Func<long> clock;
    private readonly Func<int, DesktopObservation>? observeDesktop;
    private readonly BlockingCollection<Entry> entries;
    private readonly Thread writer;
    private readonly Thread? monitor;
    private readonly ManualResetEventSlim stopMonitor = new();
    private Activity activity;
    private Snapshot? snapshot;
    private long framesStarted, framesCompleted, lastFrameCompleted, nextSnapshot;
    private long lastHeartbeat, lastStallReport, dropped, suppressedConsole, writeFailures;
    private long consoleWindow;
    private long plausibleReadFailures, rejectedReadAddresses, lastMonitorError;
    private int consoleLines, polling, disposed, menuKey = 123;
    private bool stalled, previousKeyDown;
    private DesktopObservation? desktop;
    private string? lastWriteError;

    internal RuntimeDiagnosticSession(string directory, DiagnosticOptions? options = null,
        Func<long>? clock = null, Func<int, DesktopObservation>? observeDesktop = null,
        bool startMonitor = true)
    {
        this.options = options ?? new();
        this.clock = clock ?? (() => Environment.TickCount64);
        this.observeDesktop = observeDesktop;
        this.activity = new("startup", this.clock(), Environment.CurrentManagedThreadId);
        this.lastFrameCompleted = this.lastHeartbeat = this.clock();
        this.entries = new(new ConcurrentQueue<Entry>(), this.options.QueueCapacity);
        // Parallel entity readers share the thread pool. Dedicated threads keep a
        // thread-pool stall from disabling the very diagnostics intended to catch it.
        this.writer = new Thread(() => this.WriteEntries(directory)) { IsBackground = true, Name = "GameHelper diagnostic writer" };
        this.writer.Start();
        this.Record("session_start", new
        {
            schema = 1,
            pid = Environment.ProcessId,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assembly = typeof(RuntimeDiagnosticSession).Assembly.GetName().Version?.ToString(),
            build = typeof(RuntimeDiagnosticSession).Module.ModuleVersionId,
        });
        if (startMonitor)
        {
            this.monitor = new Thread(() =>
            {
                while (!this.stopMonitor.Wait(this.options.PollMs)) this.Poll();
            }) { IsBackground = true, Name = "GameHelper progress monitor" };
            this.monitor.Start();
        }
    }

    internal long FramesCompleted => Interlocked.Read(ref this.framesCompleted);
    internal long WriteFailures => Interlocked.Read(ref this.writeFailures);
    internal long Dropped => Interlocked.Read(ref this.dropped);
    internal string? LastWriteError => Volatile.Read(ref this.lastWriteError);

    internal void CountMemoryFailure(bool plausibleAddress)
    {
        if (plausibleAddress) Interlocked.Increment(ref this.plausibleReadFailures);
        else Interlocked.Increment(ref this.rejectedReadAddresses);
    }

    internal IDisposable BeginFrame()
    {
        Interlocked.Increment(ref this.framesStarted);
        return new FrameScope(this, this.Measure("frame"));
    }

    internal IDisposable Measure(string name)
    {
        var current = new Activity(name, this.clock(), Environment.CurrentManagedThreadId);
        var previous = Interlocked.Exchange(ref this.activity, current);
        return new ActivityScope(this, current, previous);
    }

    internal bool SnapshotDue()
    {
        var now = this.clock();
        if (now < Interlocked.Read(ref this.nextSnapshot)) return false;
        Interlocked.Exchange(ref this.nextSnapshot, now + 1000);
        return true;
    }

    internal void PublishSnapshot(object data, int menuKey)
    {
        Volatile.Write(ref this.menuKey, menuKey);
        Volatile.Write(ref this.snapshot, new(data, this.clock()));
    }

    internal void Record(string kind, object? data)
    {
        if (Volatile.Read(ref this.disposed) != 0) return;
        try
        {
            if (!this.entries.TryAdd(new(DateTimeOffset.Now, kind, data)))
                Interlocked.Increment(ref this.dropped);
        }
        catch (InvalidOperationException) { /* Shutdown completed the queue concurrently. */ }
    }

    internal void ConsoleLine(string text, string stream)
    {
        // Keep a bad offset or a repeating exception from filling the queue/disk.
        var window = this.clock() / 1000;
        if (Interlocked.Exchange(ref this.consoleWindow, window) != window)
            Interlocked.Exchange(ref this.consoleLines, 0);
        if (Interlocked.Increment(ref this.consoleLines) > 8)
        {
            Interlocked.Increment(ref this.suppressedConsole);
            return;
        }
        this.Record("console", new { stream, text = text.Length <= 8192 ? text : text[..8192] + " [truncated]" });
    }

    internal void Poll()
    {
        if (Volatile.Read(ref this.disposed) != 0 || Interlocked.Exchange(ref this.polling, 1) != 0) return;
        try
        {
            var now = this.clock();
            try
            {
                if (this.observeDesktop != null)
                {
                    var currentDesktop = this.observeDesktop(Volatile.Read(ref this.menuKey));
                    if (currentDesktop.Window != this.desktop?.Window || currentDesktop.ProcessId != this.desktop?.ProcessId)
                        this.Record("desktop_focus", currentDesktop);
                    if (currentDesktop.MenuKeyDown && !this.previousKeyDown)
                        this.Record("menu_key_observed", new { key = Volatile.Read(ref this.menuKey), desktop = currentDesktop });
                    this.previousKeyDown = currentDesktop.MenuKeyDown;
                    this.desktop = currentDesktop;
                }
            }
            catch (Exception ex)
            {
                if (this.lastMonitorError == 0 || now - this.lastMonitorError >= 15000)
                {
                    this.Record("desktop_observation_error", ex.ToString());
                    this.lastMonitorError = now;
                }
            }

            var gap = now - Interlocked.Read(ref this.lastFrameCompleted);
            if (gap >= this.options.StallMs)
            {
                if (!this.stalled || now - this.lastStallReport >= 15000)
                {
                    this.Record("frame_progress_gap", this.Status(now));
                    this.lastStallReport = now;
                }
                this.stalled = true;
            }
            else if (this.stalled)
            {
                this.Record("frame_progress_resumed", this.Status(now));
                this.stalled = false;
            }

            if (now - this.lastHeartbeat >= this.options.HeartbeatMs)
            {
                this.Record("heartbeat", this.Status(now));
                this.lastHeartbeat = now;
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must not become a new unhandled exception on a timer thread.
            this.Record("monitor_error", ex.ToString());
        }
        finally { Volatile.Write(ref this.polling, 0); }
    }

    private object Status(long now)
    {
        var current = Volatile.Read(ref this.activity);
        var saved = Volatile.Read(ref this.snapshot);
        return new
        {
            framesStarted = Interlocked.Read(ref this.framesStarted),
            framesCompleted = this.FramesCompleted,
            frameGapMs = now - Interlocked.Read(ref this.lastFrameCompleted),
            phase = current.Name,
            phaseAgeMs = now - current.Started,
            phaseThread = current.Thread,
            desktop = this.desktop,
            snapshotAgeMs = saved == null ? (long?)null : now - saved.Captured,
            snapshot = saved?.Data,
            dropped = this.Dropped,
            suppressedConsole = Interlocked.Read(ref this.suppressedConsole),
            writeFailures = this.WriteFailures,
            lastWriteError = this.LastWriteError,
            plausibleReadFailures = Interlocked.Read(ref this.plausibleReadFailures),
            rejectedReadAddresses = Interlocked.Read(ref this.rejectedReadAddresses),
        };
    }

    private void WriteEntries(string directory)
    {
        var jsonOptions = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        long retryAfter = 0;
        foreach (var entry in this.entries.GetConsumingEnumerable())
        {
            if (this.clock() < retryAfter) continue;
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "runtime-current.log");
                var line = JsonSerializer.Serialize(entry, jsonOptions) + Environment.NewLine;
                // Also bound unusually large exception/object representations.
                if (Encoding.UTF8.GetByteCount(line) > 32768)
                    line = JsonSerializer.Serialize(entry with { Data = "[oversized diagnostic entry omitted]" }, jsonOptions) + Environment.NewLine;
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > this.options.FileBytes)
                {
                    var older = Path.Combine(directory, "runtime-previous-1.log");
                    if (File.Exists(older)) File.Move(older, Path.Combine(directory, "runtime-previous-2.log"), true);
                    File.Move(path, older, true);
                }
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                file.Write(bytes);
                Volatile.Write(ref this.lastWriteError, null);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref this.writeFailures);
                Volatile.Write(ref this.lastWriteError, ex.GetType().Name + ": " + ex.Message);
                retryAfter = this.clock() + 5000;
            }
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref this.disposed) != 0) return;
        this.stopMonitor.Set();
        if (this.monitor != null && Thread.CurrentThread != this.monitor)
            this.monitor.Join(TimeSpan.FromMilliseconds(250));
        this.Record("session_end", this.Status(this.clock()));
        Volatile.Write(ref this.disposed, 1);
        this.entries.CompleteAdding();
        // A slow/full/unavailable drive may not block normal application shutdown.
        if (Thread.CurrentThread != this.writer) this.writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class ActivityScope(RuntimeDiagnosticSession owner, Activity current, Activity previous) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0) return;
            var duration = owner.clock() - current.Started;
            if (duration >= 250) owner.Record("slow_phase", new { phase = current.Name, milliseconds = duration, thread = current.Thread });
            Interlocked.CompareExchange(ref owner.activity, previous, current);
        }
    }

    private sealed class FrameScope(RuntimeDiagnosticSession owner, IDisposable activity) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0) return;
            activity.Dispose();
            var now = owner.clock();
            Interlocked.Increment(ref owner.framesCompleted);
            Interlocked.Exchange(ref owner.lastFrameCompleted, now);
            Volatile.Write(ref owner.activity, new("between_frames (overlay host / Present / frame limiter)", now, Environment.CurrentManagedThreadId));
        }
    }
}
