namespace WhereTheWispsAt
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using Coroutine;
    using GameHelper;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    public sealed partial class WhereTheWispsAtCore
    {
        private WispScanReport? lastReport;
        private WispScanReport[] probeReports = [];
        private WispRenderReport renderReport = new() { Gate = "waiting_for_DrawUI" };
        private readonly Queue<object> recentEvents = new();
        private WispDebugCapture? capture;
        private long nextHeartbeat;
        private long drawCalls;
        private bool scanRequested;
        private bool probeRequested;
        private string probeFilter = "Azmeri";
        private string clientBuildNote = "";
        private string DiagnosticsDirectory => Path.Join(this.DllDirectory, "diagnostics");

        private void SetGate(string gate)
        {
            if (this.renderReport.Gate != gate)
                this.Remember("render_gate", new { Previous = this.renderReport.Gate, Current = gate });
            this.renderReport = new() { Gate = gate };
        }

        private void Remember(string type, object data)
        {
            this.recentEvents.Enqueue(new { Utc = DateTime.UtcNow, Type = type, Data = data });
            while (this.recentEvents.Count > 64) this.recentEvents.Dequeue();
            this.capture?.Write(type, data);
        }

        private void RecordChanges(WispObservation[] updated)
        {
            var before = this.observations.ToDictionary(x => x.Id);
            var after = updated.ToDictionary(x => x.Id);
            var added = updated.Where(x => !before.TryGetValue(x.Id, out var old) || old.Address != x.Address || old.Metadata != x.Metadata).ToArray();
            var missing = this.observations.Where(x => !after.TryGetValue(x.Id, out var now) || now.Address != x.Address || now.Metadata != x.Metadata).ToArray();
            var changed = updated.Where(x => before.TryGetValue(x.Id, out var old) && old.Address == x.Address && old.Metadata == x.Metadata &&
                (old.Classification.Kind != x.Classification.Kind || old.Consumed != x.Consumed || old.StateKnown != x.StateKnown)).ToArray();
            if (added.Length + missing.Length + changed.Length == 0) return;
            this.Remember("observation_changes", new
            {
                AreaHash = this.areaHash, Generation = this.generation,
                AddedCount = added.Length, MissingCount = missing.Length, ChangedCount = changed.Length,
                Added = added.Take(16).ToArray(), Missing = missing.Take(16).ToArray(), Changed = changed.Take(16).ToArray(),
                Meaning = "Missing only means absent from the next snapshot, not confirmed collection.",
            });
        }

        private void StartCapture()
        {
            this.capture ??= new(this.DiagnosticsDirectory);
            if (!this.capture.Start(Environment.TickCount64)) { this.status = this.capture.Error; return; }
            this.capture.Write("environment", this.RuntimeContext());
            this.capture.Write("settings", this.Settings);
            this.nextHeartbeat = 0;
            this.nextApiComparison = 0;
            this.nextScan = 0;
            this.status = this.capture.CurrentFile;
        }

        private void StopCapture(string reason)
        {
            if (this.capture?.Active != true) return;
            this.capture.Stop(reason);
            this.ExportSamples();
        }

        private void TickCapture()
        {
            var now = Environment.TickCount64;
            if (this.capture?.Active == true && now >= this.nextHeartbeat)
            {
                this.capture.Write("heartbeat", new
                {
                    DrawUICalls = this.drawCalls, State = Core.States.GameCurrentState.ToString(),
                    Core.Process.Foreground, AreaHash = this.areaHash, Generation = this.generation,
                    LastScanUtc = this.lastScanUtc, Render = this.renderReport,
                });
                this.nextHeartbeat = now + 2000;
            }
            if (this.capture?.Expire(now) == true) this.ExportSamples();
            if (!string.IsNullOrEmpty(this.capture?.Error)) this.status = this.capture.Error;
        }

        private IEnumerator<Wait> DebugHeartbeat()
        {
            while (true)
            {
                // The host ticks timed coroutines before PManager's F9 gate, independently of DrawUI.
                yield return new Wait(0.25);
                try { this.TickCapture(); }
                catch (Exception ex) { this.status = ex.ToString(); }
            }
        }

        private void RunSourceProbe(AreaInstance area)
        {
            var reports = new List<WispScanReport>();
            var filter = string.IsNullOrWhiteSpace(this.probeFilter) ? "Azmeri" : this.probeFilter.Trim();
            foreach (var source in new[] { EntityScanSource.Awake, EntityScanSource.Sleeping })
            {
                var report = new WispScanReport { AreaHash = area.AreaHash, Generation = this.generation, Filter = filter };
                var expectedAddress = area.Address;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var observationsRead = 0;
                    area.ScanEntities(source, path => path.Contains(filter, StringComparison.OrdinalIgnoreCase), (_, entity) =>
                    {
                        if (ReadObservation(entity, report.Stages) != null) System.Threading.Interlocked.Increment(ref observationsRead);
                    }, report.Stages);
                    report.Observations = observationsRead;
                    report.Discarded = expectedAddress != area.Address || report.AreaHash != area.AreaHash || report.Generation != this.generation;
                }
                catch (Exception ex)
                {
                    report.Discarded = true;
                    report.Stages.Record("probe_exception", detail: ex.ToString());
                }
                report.CompletedUtc = DateTime.UtcNow;
                report.Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                reports.Add(report);
                this.capture?.Write("source_probe", report);
                if (report.Discarded) break;
            }
            this.probeReports = reports.ToArray();
            this.ExportSamples();
        }

        private object RuntimeContext()
        {
            static object AssemblyInfo(Assembly assembly) => new
            {
                assembly.GetName().Name, Version = assembly.GetName().Version?.ToString(),
                Mvid = assembly.ManifestModule.ModuleVersionId,
                InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            };
            object game;
            try
            {
                using var process = Process.GetProcessById((int)Core.Process.Pid);
                game = new { Pid = process.Id, process.ProcessName, ModuleName = process.MainModule?.ModuleName, Version = process.MainModule?.FileVersionInfo.FileVersion };
            }
            catch (Exception ex) { game = new { Error = ex.Message }; }
            return new
            {
                Host = AssemblyInfo(typeof(Core).Assembly), Plugin = AssemblyInfo(typeof(WhereTheWispsAtCore).Assembly),
                Offsets = AssemblyInfo(typeof(GameOffsets.Objects.States.InGameState.EntityOffsets).Assembly),
                Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), Game = game,
                ClientBuildNote = this.clientBuildNote, Core.GHSettings.ProcessAllRenderableEntities,
            };
        }

        private void ExportSamples()
        {
            try
            {
                Directory.CreateDirectory(this.DiagnosticsDirectory);
                var path = Path.Join(this.DiagnosticsDirectory, "latest-report.json");
                var payload = JsonSerializer.Serialize(new
                {
                    SchemaVersion = 2, ExportedUtc = DateTime.UtcNow, LastScanUtc = this.lastScanUtc,
                    Environment = this.RuntimeContext(), Settings = this.Settings,
                    Current = new { AreaHash = this.areaHash, Generation = this.generation, DrawUICalls = this.drawCalls,
                        State = Core.States.GameCurrentState.ToString(), Core.Process.Foreground, Render = this.renderReport },
                    LastScan = this.lastReport, SourceProbes = this.probeReports, ApiComparison = this.lastApiComparison, RecentEvents = this.recentEvents.ToArray(),
                    TotalMatched = this.observations.Length, Samples = this.observations.Take(2000).ToArray(),
                    CaptureError = this.capture?.Error, LastStatus = this.status,
                    Limits = new { SamplesPerStage = 4, ExportedObservations = 2000, RecentEvents = 64, CaptureFiles = 3, CaptureBytesPerFile = 2097152 },
                    Interpretation = "Counts and readback samples are diagnostic evidence, not proof that offsets or semantic mappings are correct. Render submissions do not confirm visible pixels. Source probes run sequentially.",
                }, new JsonSerializerOptions(WispDebugCapture.JsonOptions) { WriteIndented = true });
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, payload);
                if (File.Exists(path)) File.Copy(path, Path.Join(this.DiagnosticsDirectory, "previous-report.json"), true);
                File.Move(temporary, path, true);
                this.status = path;
            }
            catch (Exception ex) { this.status = ex.ToString(); }
        }
    }
}
