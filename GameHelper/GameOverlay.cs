// <copyright file="GameOverlay.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Utils;
    using ImGuiNET;
    using Plugin;
    using Settings;
    using Ui;

    /// <inheritdoc />
    public sealed class GameOverlay : Overlay
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GameOverlay" /> class.
        /// </summary>
        internal GameOverlay(string windowTitle)
            : base(windowTitle, true, 3840, 2160)
        {
            CoroutineHandler.Start(this.UpdateOverlayBounds(), priority: int.MaxValue);
            SettingsWindow.InitializeCoroutines();
            PerformanceStats.InitializeCoroutines();
            DataVisualization.InitializeCoroutines();
            GameUiExplorer.InitializeCoroutines();
            ElementFinder.InitializeCoroutines();
            PerformanceProfiler.InitializeCoroutines();
            MemoryReadDiagnostics.InitializeCoroutines();
            OffsetHelper.InitializeCoroutines();
            OverlayKiller.InitializeCoroutines();
            NearbyVisualization.InitializeCoroutines();
            KrangledPassiveDetector.InitializeCoroutines();
        }

        /// <summary>
        ///     Gets the fonts loaded in the overlay.
        /// </summary>
        public ImFontPtr[]? Fonts { get; private set; }

        /// <inheritdoc />
        public override async Task Run()
        {
            Core.Initialize();
            Core.InitializeCororutines();
            this.VSync = Core.GHSettings.Vsync;
            await base.Run();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Core.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override Task PostInitialized()
        {
            Ui.ImGuiTheme.Apply();

            UniversalFont.ApplyFromSettings();

            PManager.InitializePlugins();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Render()
        {
            using var diagnosticFrame = Diagnostics.RuntimeDiagnostics.BeginFrame();
            PerformanceProfiler.StartFrame();

            try
            {
                using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:Tick");
                CoroutineHandler.Tick(ImGui.GetIO().DeltaTime);
            }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.Tick] {ex}"); }

            try
            {
                using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:PerFrameDataUpdate");
                CoroutineHandler.RaiseEvent(GameHelperEvents.PerFrameDataUpdate);
            }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PerFrameDataUpdate] {ex}"); }

            try
            {
                using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:PostPerFrameDataUpdate");
                CoroutineHandler.RaiseEvent(GameHelperEvents.PostPerFrameDataUpdate);
            }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PostPerFrameDataUpdate] {ex}"); }

            if (Diagnostics.RuntimeDiagnostics.SnapshotDue()) this.CaptureDiagnosticSnapshot();

            try
            {
                using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:OnRender");
                CoroutineHandler.RaiseEvent(GameHelperEvents.OnRender);
            }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnRender] {ex}"); }

            try
            {
                using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:OnPostRender");
                CoroutineHandler.RaiseEvent(GameHelperEvents.OnPostRender);
            }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnPostRender] {ex}"); }

            if (!Core.GHSettings.IsOverlayRunning)
            {
                this.Close();
            }
        }

        private void CaptureDiagnosticSnapshot()
        {
            using var phase = Diagnostics.RuntimeDiagnostics.Measure("host:cached_diagnostic_snapshot");
            try
            {
                var process = Core.Process;
                var area = Core.States.InGameStateObject.CurrentAreaInstance;
                var player = area.Player;
                player.TryGetCachedComponent<RemoteObjects.Components.Render>(out var render);
                var position = render?.GridPosition;
                Diagnostics.RuntimeDiagnostics.PublishSnapshot(new
                {
                    gamePid = process.Information?.Id,
                    gameWindow = process.Information?.MainWindowHandle.ToInt64(),
                    gameForeground = process.Foreground,
                    overlayWindow = this.window.Handle.ToInt64(),
                    overlayWidth = this.Size.Width,
                    overlayHeight = this.Size.Height,
                    state = Core.States.GameCurrentState.ToString(),
                    area = area.AreaHash,
                    level = area.CurrentAreaLevel,
                    areaAddress = $"0x{area.Address.ToInt64():X}",
                    entities = area.AwakeEntities.Count,
                    playerValid = player.IsValid,
                    playerId = player.Id,
                    playerAddress = $"0x{player.Address.ToInt64():X}",
                    playerX = position?.X,
                    playerY = position?.Y,
                    menuVisible = Core.IsSettingsMenuOpen,
                    vsync = this.VSync,
                    fpsLimit = this.FPSLimit,
                    fps = ImGui.GetIO().Framerate,
                    miscFilterRows = Core.GHSettings.SpecialMiscObjPaths.Count,
                    ignoredMonsterRows = Core.GHSettings.MonstersPathsToIgnore.Count,
                    coroutines = Core.CoroutinesRegistrar.Take(40).Select(c => new
                    {
                        name = c.Name,
                        steps = c.MoveNextCount,
                        finished = c.IsFinished,
                        lastMs = c.LastMoveNextTime.TotalMilliseconds,
                    }).ToArray(),
                }, (int)Core.GHSettings.MainMenuHotKey);
            }
            catch (Exception ex)
            {
                Diagnostics.RuntimeDiagnostics.Record("snapshot_error", ex.ToString());
            }
        }

        private IEnumerator<Wait> UpdateOverlayBounds()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                this.Position = Core.Process.WindowArea.Location;
                this.Size = Core.Process.WindowArea.Size -
                    (Core.GHSettings.FixTaskbarNotShowing ?
                        new System.Drawing.Size(0, 1) :
                        System.Drawing.Size.Empty);
            }
        }
    }
}
