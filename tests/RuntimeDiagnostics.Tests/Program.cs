using System.Text.Json;
using GameHelper.Diagnostics;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
var root = Path.Combine(Path.GetTempPath(), "GameHelper-runtime-diagnostics-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string Folder(string name) => Path.Combine(root, name);
JsonElement[] Events(string directory)
{
    if (!Directory.Exists(directory)) return [];
    return Directory.GetFiles(directory, "runtime-*.log").SelectMany(path =>
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.TrimEnd().EndsWith('}'))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
    }).ToArray();
}
bool Kind(JsonElement entry, string kind) => entry.GetProperty("Kind").GetString() == kind;
async Task Until(Func<bool> condition)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!condition())
    {
        if (DateTime.UtcNow > deadline) throw new TimeoutException("Diagnostic worker did not make progress.");
        await Task.Delay(10);
    }
}

try
{
    // A background monitor must still persist a frozen operation without completing
    // a host frame or refreshing any application/input watchdog.
    var background = Folder("background");
    bool? observerInPool = null;
    using (var session = new RuntimeDiagnosticSession(background,
               new(HeartbeatMs: 40, StallMs: 80, PollMs: 10),
               observeDesktop: _ => { observerInPool = Thread.CurrentThread.IsThreadPoolThread; return new(111, 222, false); }))
    {
        using var frame = session.BeginFrame();
        using var phase = session.Measure("plugin:Blocked.DrawUI");
        session.PublishSnapshot(new { entities = 17, playerX = 42 }, 123);
        await Until(() => Events(background).Any(entry => Kind(entry, "frame_progress_gap")));
        var gap = Events(background).First(entry => Kind(entry, "frame_progress_gap")).GetProperty("Data");
        Check("stalled render is logged from the independent monitor", gap.GetProperty("phase").GetString() == "plugin:Blocked.DrawUI");
        Check("heartbeat cannot complete a host frame", session.FramesCompleted == 0);
        Check("frozen snapshot retains data and an explicit age", gap.GetProperty("snapshot").GetProperty("entities").GetInt32() == 17 && gap.GetProperty("snapshotAgeMs").GetInt64() >= 60);
        Check("background logging continues during the stall", Events(background).Any(entry => Kind(entry, "heartbeat")));
        Check("progress monitor is isolated from the entity-reader thread pool", observerInPool == false);
        session.Record("writer_thread", new WriterThreadProbe());
    }
    Check("file writing is isolated from the entity-reader thread pool", Events(background).Any(entry => Kind(entry, "writer_thread") && !entry.GetProperty("Data").GetProperty("InThreadPool").GetBoolean()));

    long now = 1000;
    var progress = Folder("progress");
    using (var session = new RuntimeDiagnosticSession(progress, new(StallMs: 100, HeartbeatMs: 100), () => now, startMonitor: false))
    {
        var frame = session.BeginFrame();
        var host = session.Measure("host:PerFrameDataUpdate");
        var core = session.Measure("core:AreaInstance.awake_entities");
        now += 300;
        session.Poll();
        session.Poll();
        core.Dispose();
        now += 1;
        session.Poll();
        host.Dispose();
        frame.Dispose();
        frame.Dispose();
        session.Poll();
        Check("frame completion is counted once", session.FramesCompleted == 1);
        now += 101;
        session.Poll();
    }
    var progressEvents = Events(progress);
    var gaps = progressEvents.Where(entry => Kind(entry, "frame_progress_gap")).ToArray();
    Check("a stall is rate limited within one episode", gaps.Length == 2);
    Check("deep core operation identifies the initial stall", gaps[0].GetProperty("Data").GetProperty("phase").GetString() == "core:AreaInstance.awake_entities");
    Check("resume is recorded after a completed frame", progressEvents.Any(entry => Kind(entry, "frame_progress_resumed")));
    Check("outside-render gaps point to host/presentation rather than a plugin", gaps[1].GetProperty("Data").GetProperty("phase").GetString()!.StartsWith("between_frames"));
    Check("slow completed operations are recorded", progressEvents.Any(entry => Kind(entry, "slow_phase") && entry.GetProperty("Data").GetProperty("phase").GetString() == "core:AreaInstance.awake_entities"));

    now = 1000;
    bool keyDown = false;
    var desktop = Folder("desktop");
    using (var session = new RuntimeDiagnosticSession(desktop, new(HeartbeatMs: 100), () => now,
               _ => new(55, 66, keyDown), startMonitor: false))
    {
        Check("first cached snapshot is due", session.SnapshotDue());
        Check("cached snapshots are throttled", !session.SnapshotDue());
        session.Poll();
        keyDown = true;
        session.Poll();
        session.Poll();
        keyDown = false;
        session.Poll();
        keyDown = true;
        now += 1000;
        session.Poll();
        Check("next snapshot becomes due after one second", session.SnapshotDue());
        Check("desktop/key observation never advances frame progress", session.FramesCompleted == 0);
    }
    Check("raw menu key edges are captured even without rendering", Events(desktop).Count(entry => Kind(entry, "menu_key_observed")) == 2);
    Check("unchanged focus is not logged repeatedly", Events(desktop).Count(entry => Kind(entry, "desktop_focus")) == 1);

    var consolePath = Folder("console");
    now = 1000;
    using (var session = new RuntimeDiagnosticSession(consolePath, new(HeartbeatMs: 100), () => now, startMonitor: false))
    {
        var original = new StringWriter();
        var capture = new DiagnosticConsoleWriter(original, session, "stderr");
        capture.Write("first"); capture.Write(' '); capture.WriteLine("failure");
        for (var i = 0; i < 30; i++) capture.WriteLine("repeated error");
        now += 1000;
        capture.WriteLine(new string('x', 20000));
        capture.Write("partial final message");
        capture.Flush();
        session.CountMemoryFailure(true);
        session.CountMemoryFailure(false);
        session.Poll();
        Check("existing console output is preserved", original.ToString().StartsWith("first failure" + Environment.NewLine));
    }
    var consoleEvents = Events(consolePath);
    Check("split console writes become a complete diagnostic line", consoleEvents.Any(entry => Kind(entry, "console") && entry.GetProperty("Data").GetProperty("text").GetString() == "first failure"));
    Check("console floods are bounded and counted", consoleEvents.Count(entry => Kind(entry, "console")) == 10 && consoleEvents.Any(entry => Kind(entry, "heartbeat") && entry.GetProperty("Data").GetProperty("suppressedConsole").GetInt64() > 0));
    Check("long and unterminated console writes remain bounded", consoleEvents.Where(entry => Kind(entry, "console")).All(entry => entry.GetProperty("Data").GetProperty("text").GetString()!.Length <= 8192));
    var counts = consoleEvents.First(entry => Kind(entry, "heartbeat")).GetProperty("Data");
    Check("memory-read counters distinguish bad pointers and failed plausible reads", counts.GetProperty("plausibleReadFailures").GetInt64() == 1 && counts.GetProperty("rejectedReadAddresses").GetInt64() == 1);

    var restart = Folder("restart");
    using (var session = new RuntimeDiagnosticSession(restart, startMonitor: false)) session.Record("before_restart", "retain this evidence");
    using (var session = new RuntimeDiagnosticSession(restart, startMonitor: false)) session.Record("after_restart", "new session");
    Check("restart retains the previous session and evidence", Events(restart).Any(entry => Kind(entry, "before_restart")) && Events(restart).Count(entry => Kind(entry, "session_start")) == 2);

    var rotation = Folder("rotation");
    using (var session = new RuntimeDiagnosticSession(rotation, new(FileBytes: 1024), startMonitor: false))
        for (var i = 0; i < 80; i++) session.Record("rotation", new { index = i, payload = new string('x', 400) });
    Check("disk use is limited to three rotating files", Directory.GetFiles(rotation).Length == 3 && Directory.GetFiles(rotation).All(file => new FileInfo(file).Length <= 1024));
    Check("rotated files contain complete JSON lines", Events(rotation).Length > 0);

    var flood = Folder("queue");
    using (var session = new RuntimeDiagnosticSession(flood, new(FileBytes: 4096, QueueCapacity: 1), startMonitor: false))
    {
        for (var i = 0; i < 20000; i++) session.Record("queue_flood", i);
        Check("a full queue drops entries without waiting for disk", session.Dropped > 0);
    }

    var blocked = Folder("not-a-directory");
    File.WriteAllText(blocked, "owned test fixture");
    now = 1000;
    using (var session = new RuntimeDiagnosticSession(blocked, clock: () => now, startMonitor: false))
    {
        await Until(() => session.WriteFailures > 0);
        using (session.BeginFrame()) { }
        Check("unwritable log directory cannot stop application frames", session.FramesCompleted == 1 && session.LastWriteError != null);
        File.Delete(blocked);
        now += 6000;
        session.Record("disk_recovered", "retry after backoff");
        await Until(() => Events(blocked).Any(entry => Kind(entry, "disk_recovered")));
        Check("logging can recover after a filesystem error", session.LastWriteError == null);
    }

    var nativeFailure = Folder("native-failure");
    now = 1000;
    using (var session = new RuntimeDiagnosticSession(nativeFailure, new(HeartbeatMs: 100, StallMs: 100), () => now,
               _ => throw new InvalidOperationException("simulated native observation failure"), startMonitor: false))
    {
        session.Poll();
        now += 150;
        session.Poll();
    }
    Check("native observation failure cannot disable stall monitoring", Events(nativeFailure).Any(entry => Kind(entry, "frame_progress_gap")));
    Check("native observation errors are rate limited", Events(nativeFailure).Count(entry => Kind(entry, "desktop_observation_error")) == 1);

    if (args.Length == 1)
    {
        // Exercise the actual built/deployed core, including console redirection and
        // the native observation facade. Never start the overlay, plugins, or Core.
        var host = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        var facade = host.GetType("GameHelper.Diagnostics.RuntimeDiagnostics", throwOnError: true)!;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var hostLogs = Folder("actual-host");
        using ((IDisposable)facade.GetMethod("Start", flags)!.Invoke(null, [hostLogs])!)
        {
            using (var frame = (IDisposable)facade.GetMethod("BeginFrame", flags)!.Invoke(null, null)!)
            using (var phase = (IDisposable)facade.GetMethod("Measure", flags)!.Invoke(null, ["headless:simulated_blocked_render"])!)
            {
                Console.WriteLine("headless diagnostic console capture marker");
                await Until(() => Events(Path.Combine(hostLogs, "diagnostics")).Any(entry => Kind(entry, "frame_progress_gap")));
            }
            await Until(() => Events(Path.Combine(hostLogs, "diagnostics")).Any(entry => Kind(entry, "frame_progress_resumed")));
        }
        var actual = Events(Path.Combine(hostLogs, "diagnostics"));
        Check("actual core starts and shuts down its diagnostic session", actual.Any(entry => Kind(entry, "session_start")) && actual.Any(entry => Kind(entry, "session_end")));
        Check("actual core preserves console exceptions/messages", actual.Any(entry => Kind(entry, "console") && entry.GetProperty("Data").GetProperty("text").GetString() == "headless diagnostic console capture marker"));
        Check("actual core records a blocked render phase and recovery", actual.Any(entry => Kind(entry, "frame_progress_gap") && entry.GetProperty("Data").GetProperty("phase").GetString() == "headless:simulated_blocked_render") && actual.Any(entry => Kind(entry, "frame_progress_resumed")));
        Check("actual core records its unique build identity", actual.First(entry => Kind(entry, "session_start")).GetProperty("Data").GetProperty("build").GetGuid() == host.ManifestModule.ModuleVersionId);
    }
    Console.WriteLine($"Passed {passed} runtime diagnostic checks. Synthetic state, no overlay, game, or input.");
}

finally
{
    var fullRoot = Path.GetFullPath(root);
    var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullRoot).StartsWith("GameHelper-runtime-diagnostics-", StringComparison.Ordinal))
        Directory.Delete(fullRoot, recursive: true);
}

internal sealed class WriterThreadProbe
{
    public bool InThreadPool => Thread.CurrentThread.IsThreadPoolThread;
}
