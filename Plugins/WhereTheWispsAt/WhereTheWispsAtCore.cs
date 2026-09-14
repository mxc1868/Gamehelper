namespace WhereTheWispsAt
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed partial class WhereTheWispsAtCore : PCore<WhereTheWispsAtSettings>
    {
        private ActiveCoroutine? areaChanged;
        private ActiveCoroutine? gameClosed;
        private ActiveCoroutine? debugTick;
        private WispObservation[] observations = [];
        private IntPtr areaAddress;
        private string areaHash = string.Empty;
        private long nextScan;
        private long nextRenderSample;
        private long nextDrawException;
        private int generation;
        private int visited;
        private double scanMilliseconds;
        private DateTime? lastScanUtc;
        private string status = string.Empty;
        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");

        public override void OnEnable(bool isGameOpened)
        {
            this.OnDisable();
            try
            {
                if (File.Exists(this.SettingsPath))
                    this.Settings = JsonConvert.DeserializeObject<WhereTheWispsAtSettings>(File.ReadAllText(this.SettingsPath)) ?? new();
            }
            catch (Exception ex)
            {
                this.Settings = new();
                this.status = ex.Message;
            }
            this.Settings.Normalize();
            this.areaChanged = CoroutineHandler.Start(this.ResetOn(RemoteEvents.AreaChanged));
            this.gameClosed = CoroutineHandler.Start(this.ResetOn(GameHelperEvents.OnClose));
            this.debugTick = CoroutineHandler.Start(this.DebugHeartbeat());
        }

        public override void OnDisable()
        {
            this.areaChanged?.Cancel();
            this.gameClosed?.Cancel();
            this.debugTick?.Cancel();
            this.areaChanged = null;
            this.gameClosed = null;
            this.debugTick = null;
            this.probeRequested = false;
            this.scanRequested = false;
            this.apiComparisonRequested = false;
            this.StopCapture("plugin_disabled");
            this.Reset();
        }

        private void Reset()
        {
            this.Remember("reset", new { PreviousAreaHash = this.areaHash, PreviousGeneration = this.generation });
            this.generation++;
            this.observations = [];
            this.areaAddress = IntPtr.Zero;
            this.areaHash = string.Empty;
            this.nextScan = 0;
            // Keep the last completed reports for post-transition diagnosis.
        }

        private IEnumerator<Wait> ResetOn(Event eventToWatch)
        {
            while (true)
            {
                yield return new Wait(eventToWatch);
                this.Reset();
            }
        }

        public override void SaveSettings()
        {
            try
            {
                this.Settings.Normalize();
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingsPath)!);
                File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
            }
            catch (Exception ex) { this.status = ex.Message; }
        }

        public override void DrawUI()
        {
            this.drawCalls++;
            try { this.DrawFrame(); }
            catch (Exception ex)
            {
                this.SetGate("draw_exception");
                this.renderReport.Values["Exception"] = ex.ToString();
                this.status = ex.Message;
                if (Environment.TickCount64 >= this.nextDrawException)
                {
                    this.Remember("draw_exception", new { AreaHash = this.areaHash, Error = ex.ToString() });
                    this.nextDrawException = Environment.TickCount64 + 2000;
                }
            }
        }

        private void DrawFrame()
        {
            var state = Core.States.GameCurrentState;
            if (state is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
                if (this.areaAddress != IntPtr.Zero) this.Reset();
                this.SetGate("game_state:" + state);
                return;
            }

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            var details = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (area.Address == IntPtr.Zero || !area.Player.IsValid || details.IsTown || details.IsHideout)
            {
                if (this.areaAddress != IntPtr.Zero) this.Reset();
                this.SetGate(area.Address == IntPtr.Zero ? "area_missing" : !area.Player.IsValid ? "player_invalid" : "town_or_hideout");
                return;
            }
            if (area.Address != this.areaAddress || area.AreaHash != this.areaHash)
            {
                this.Reset();
                this.areaAddress = area.Address;
                this.areaHash = area.AreaHash;
            }

            // Explicit diagnostics continue while inspecting settings; normal rendering keeps its gates.
            if (this.probeRequested)
            {
                this.probeRequested = false;
                this.RunSourceProbe(area);
            }
            var hidden = state != GameStateTypes.InGameState || (this.Settings.HideWhenUnfocused && !Core.Process.Foreground);
            if ((this.capture?.Active == true || this.scanRequested || !hidden) && Environment.TickCount64 >= this.nextScan)
            {
                this.scanRequested = false;
                this.Scan(area);
            }
            if (hidden) { this.SetGate(state != GameStateTypes.InGameState ? "escape_menu" : "game_unfocused"); return; }
            if (this.Settings.HideWhenPanelsOpen && Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen)
            { this.SetGate("large_panel_open"); return; }
            WispRenderReport? sampled = null;
            if (Environment.TickCount64 >= this.nextRenderSample)
            {
                sampled = new();
                this.nextRenderSample = Environment.TickCount64 + 500;
            }
            WispRenderer.Draw(this.observations, this.Settings, (key, fallback) => this.PluginText.T(key, fallback), sampled);
            if (sampled != null) this.renderReport = sampled;
        }

        private void Scan(AreaInstance area)
        {
            var expectedGeneration = this.generation;
            var expectedAddress = this.areaAddress;
            var expectedHash = this.areaHash;
            var started = Stopwatch.GetTimestamp();
            var report = new WispScanReport { AreaHash = expectedHash, Generation = expectedGeneration };
            var compare = this.apiComparisonRequested || (this.capture?.Active == true && Environment.TickCount64 >= this.nextApiComparison);
            var manualComparison = this.apiComparisonRequested;
            this.apiComparisonRequested = false;
            WispApiSnapshot[]? publicSnapshots = null;
            WispApiSnapshot? freshSnapshot = null;
            try
            {
                if (compare)
                {
                    publicSnapshots = [this.ReadPublicApi(area, false), this.ReadPublicApi(area, true)];
                    freshSnapshot = this.NewApiSnapshot("fresh_entity_scan", area, report.Stages);
                }
                var samples = new ConcurrentDictionary<uint, WispObservation>();
                area.ScanEntities(EntityScanSource.Awake, WispClassifier.IsCandidate, (_, entity) =>
                {
                    WispApiEntity? evidence = null;
                    if (freshSnapshot?.Reserve() == true)
                    {
                        evidence = ApiEntity(entity);
                        freshSnapshot.Entities[evidence.Identity] = evidence;
                    }
                    try
                    {
                        var sample = ReadObservation(entity, report.Stages, evidence);
                        if (sample != null) samples[sample.Id] = sample;
                    }
                    catch (Exception ex)
                    {
                        if (evidence != null)
                        {
                            evidence.ReadStatus = "fresh_read_exception";
                            evidence.Error = ex.ToString()[..Math.Min(ex.ToString().Length, 2048)];
                        }
                        throw;
                    }
                }, report.Stages);
                this.visited = (int)report.Stages.Counts.GetValueOrDefault("entry_seen");
                if (expectedGeneration != this.generation || area.Address != expectedAddress || area.AreaHash != expectedHash)
                {
                    report.Discarded = true;
                    report.Stages.Record("snapshot_area_changed");
                    this.Reset();
                    return;
                }

                // Replace the entire snapshot: disappearing IDs and reused slots do not leave trails.
                var updated = samples.Values.OrderBy(x => x.Id).ToArray();
                this.RecordChanges(updated);
                this.observations = updated;
                report.Observations = updated.Length;
                this.lastScanUtc = DateTime.UtcNow;
                this.status = string.Empty;
            }
            catch (Exception ex)
            {
                this.observations = [];
                report.Discarded = true;
                report.Stages.Record("scan_exception", detail: ex.ToString());
                this.status = ex.Message;
            }
            finally
            {
                this.scanMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                report.Milliseconds = this.scanMilliseconds;
                report.CompletedUtc = DateTime.UtcNow;
                this.lastReport = report;
                this.capture?.Write("scan", report);
                if (publicSnapshots != null && freshSnapshot != null)
                {
                    this.CompleteApiComparison(area, publicSnapshots, freshSnapshot, report);
                    if (manualComparison) this.ExportSamples();
                }
                // Slow reads back off automatically instead of scanning again on the next frame.
                this.nextScan = Environment.TickCount64 + Math.Max(this.Settings.ScanIntervalMs,
                    (long)Math.Min(5000, Math.Ceiling(this.scanMilliseconds * 4)));
            }
        }

        private static WispObservation? ReadObservation(Entity entity, EntityScanDiagnostics diagnostics,
            WispApiEntity? evidence = null, bool recreateComponents = false)
        {
            void Record(string stage, string detail = "")
            {
                diagnostics.Record(stage, entity.Id, entity.Address.ToInt64(), entity.Path, detail);
                if (evidence != null) evidence.ReadStatus = stage;
            }
            bool ReadComponent<T>(Func<IntPtr, T> constructor, [NotNullWhen(true)] out T? component) where T : ComponentBase
            {
                // false avoids populating the public entity's cache, but DOES NOT bypass existing cache entries.
                if (!entity.TryGetComponent(out component, shouldCache: evidence == null)) return false;
                if (recreateComponents) component = constructor(component.Address);
                if (evidence != null)
                    evidence.Components[typeof(T).Name] = new(component.Address.ToInt64(), component.IsParentValid(entity.Address));
                return true;
            }
            var animatedPath = string.Empty;
            var modelPath = string.Empty;
            if (!ReadComponent<Animated>(static address => new Animated(address), out var animated)) Record("animated_missing");
            else if (!animated.IsParentValid(entity.Address)) Record("animated_parent_invalid", $"Address=0x{animated.Address.ToInt64():X}");
            else
            {
                animatedPath = animated.Path;
                modelPath = animated.ModelPath;
                Record(string.IsNullOrEmpty(animatedPath) ? "animated_path_empty" : "animated_path_read",
                    $"Address=0x{animated.Address.ToInt64():X}; AnimatedPath={animatedPath}; ModelPath={modelPath}");
            }
            var classification = WispClassifier.Classify(entity.Path, animatedPath);
            if (evidence != null)
            {
                evidence.AnimatedPath = animatedPath;
                evidence.ModelPath = modelPath;
                evidence.Kind = classification?.Kind;
            }
            if (classification == null) Record("classification_unmatched");
            else Record(classification.Value.Kind == WispKind.UnknownResource ? "classification_unknown" : "classification_known", classification.Value.Kind.ToString());
            if (!ReadComponent<Render>(static address => new Render(address), out var render)) { Record("render_missing"); return null; }
            if (!render.IsParentValid(entity.Address)) { Record("render_parent_invalid", $"Address=0x{render.Address.ToInt64():X}"); return null; }
            var pos = render.WorldPosition;
            var world = new Vector3(pos.X, pos.Y, pos.Z);
            var grid = new Vector2(render.GridPosition.X, render.GridPosition.Y);
            var bounds = render.ModelBounds;
            if (evidence != null)
            {
                evidence.World = world; evidence.Grid = grid; evidence.TerrainHeight = render.TerrainHeight;
                evidence.Bounds = new Vector3(bounds.X, bounds.Y, bounds.Z);
            }
            Record("render_values", $"Address=0x{render.Address.ToInt64():X}; World={world}; Grid={grid}; Terrain={render.TerrainHeight}; Bounds={bounds}");
            if (!WispRenderer.IsFinite(world) || !WispRenderer.IsFinite(grid) || !float.IsFinite(render.TerrainHeight))
            { Record("position_nonfinite"); return null; }

            var consumed = false;
            var stateKnown = true;
            if (classification?.Kind == WispKind.Chest)
            {
                stateKnown = ReadComponent<Chest>(static address => new Chest(address), out var chest) && chest.IsParentValid(entity.Address);
                consumed = stateKnown && chest!.IsOpened;
                if (evidence != null) evidence.ChestOpened = stateKnown ? consumed : null;
                Record(stateKnown ? "chest_state_read" : "chest_state_unavailable", $"IsOpened={consumed}");
            }
            else if (classification == null || WispClassifier.UsesActivation(classification.Value.Kind))
            {
                stateKnown = false;
                if (ReadComponent<StateMachine>(static address => new StateMachine(address), out var machine) && machine.IsParentValid(entity.Address))
                {
                    Record("state_machine_values", $"Address=0x{machine.Address.ToInt64():X}; " + string.Join("; ", machine.States.Take(32).Select(x => $"{x.Name}={x.Value}")));
                    var activated = machine.States.FirstOrDefault(x => x.Name == "activated");
                    if (evidence != null)
                    {
                        evidence.States = machine.States.Take(32).Select(x => $"{(x.Name.Length > 128 ? x.Name[..128] : x.Name)}={x.Value}").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                        evidence.ActivatedValue = activated?.Value;
                    }
                    stateKnown = activated != null;
                    consumed = activated?.Value == 1;
                }
                if (!stateKnown) Record("activation_state_unavailable");
            }
            if (classification == null) { Record("classification_unmatched"); return null; }
            Record(consumed ? "observation_consumed" : "observation_ready", $"Kind={classification.Value.Kind}; StateKnown={stateKnown}");
            var observation = new WispObservation(entity.Id, entity.Address.ToInt64(), entity.Path, animatedPath, modelPath,
                classification.Value, grid, world, render.TerrainHeight,
                new Vector3(bounds.X, bounds.Y, bounds.Z), consumed, stateKnown);
            if (evidence != null) evidence.Observation = observation;
            return observation;
        }
    }
}
