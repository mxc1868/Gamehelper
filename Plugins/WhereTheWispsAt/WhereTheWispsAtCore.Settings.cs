namespace WhereTheWispsAt
{
    using System.Linq;
    using ImGuiNET;

    public sealed partial class WhereTheWispsAtCore
    {
        private string Label(string key, string fallback) => this.PluginText.Label(key, fallback, "Wisps." + key);

        public override void DrawSettings()
        {
            ImGui.Checkbox(this.Label("settings.map", "Draw on large map"), ref this.Settings.DrawMap);
            ImGui.Checkbox(this.Label("settings.minimap", "Draw on minimap"), ref this.Settings.DrawMiniMap);
            ImGui.Checkbox(this.Label("settings.lines", "Connect adjacent wisps"), ref this.Settings.DrawMapLines);
            ImGui.Checkbox(this.Label("settings.labels", "Show encounter labels"), ref this.Settings.DrawLabels);
            ImGui.Checkbox(this.Label("settings.unknown", "Show unidentified wisps in grey"), ref this.Settings.ShowUnknownResources);
            ImGui.Checkbox(this.Label("settings.ground", "Draw wisps on the ground"), ref this.Settings.DrawGround);
            ImGui.Checkbox(this.Label("settings.chests", "Draw nearby chest bounds"), ref this.Settings.DrawChestGround);
            ImGui.Checkbox(this.Label("settings.foreground", "Hide while game is unfocused"), ref this.Settings.HideWhenUnfocused);
            ImGui.Checkbox(this.Label("settings.panels", "Hide while game panels are open"), ref this.Settings.HideWhenPanelsOpen);
            ImGui.Checkbox(this.Label("settings.coop", "Center map on both local co-op players"), ref this.Settings.FollowCoopCenter);
            ImGui.SliderInt(this.Label("settings.interval", "Scan interval (ms)"), ref this.Settings.ScanIntervalMs, 100, 5000);
            ImGui.SliderFloat(this.Label("settings.size", "Map marker size"), ref this.Settings.MarkerSize, 1, 30);
            ImGui.SliderFloat(this.Label("settings.line_width", "Line width"), ref this.Settings.LineWidth, 0.5f, 10);
            ImGui.SliderFloat(this.Label("settings.link_distance", "Maximum link distance (grid units)"), ref this.Settings.MaxLinkGridDistance, 1, 100);
            ImGui.SliderFloat(this.Label("settings.ground_width", "Ground marker width"), ref this.Settings.GroundWidth, 1, 200);
            ImGui.SliderFloat(this.Label("settings.ground_height", "Ground marker height"), ref this.Settings.GroundHeight, 1, 200);
            ImGui.SliderFloat(this.Label("settings.opacity", "Ground opacity"), ref this.Settings.GroundOpacity, 0, 1);
            ImGui.SliderFloat(this.Label("settings.chest_distance", "Chest range (grid units)"), ref this.Settings.ChestDistance, 1, 500);
            ImGui.SliderFloat(this.Label("settings.map_scale", "Large map scale"), ref this.Settings.MapScaleMultiplier, 0.1f, 3);
            ImGui.SliderFloat(this.Label("settings.minimap_scale", "Minimap scale"), ref this.Settings.MiniMapScaleMultiplier, 0.1f, 3);
            ImGui.DragFloat2(this.Label("settings.map_offset", "Large map offset"), ref this.Settings.MapOffset, 0.5f, -2000, 2000);
            ImGui.DragFloat2(this.Label("settings.minimap_offset", "Minimap offset"), ref this.Settings.MiniMapOffset, 0.5f, -2000, 2000);
            ImGui.ColorEdit4(this.Label("settings.blue", "Blue wisps"), ref this.Settings.Blue);
            ImGui.ColorEdit4(this.Label("settings.yellow", "Yellow wisps"), ref this.Settings.Yellow);
            ImGui.ColorEdit4(this.Label("settings.purple", "Purple wisps"), ref this.Settings.Purple);
            ImGui.ColorEdit4(this.Label("settings.sacred", "Sacred wisps"), ref this.Settings.Sacred);
            ImGui.ColorEdit4(this.Label("settings.chest_color", "Chests"), ref this.Settings.Chest);
            this.Settings.Normalize();

            ImGui.Separator();
            ImGui.TextWrapped(this.PluginText.T("settings.fuel_pending", "Fuel percentage is not available yet; its current game data needs validation."));
            ImGui.Text(this.PluginText.F("diagnostics.summary", "Matched: {0}; unidentified: {1}; state unavailable: {2}; scan: {3:F1} ms",
                this.observations.Length, this.observations.Count(x => x.Classification.Kind == WispKind.UnknownResource),
                this.observations.Count(x => !x.StateKnown), this.scanMilliseconds));
            if (ImGui.Button(this.Label("diagnostics.export", "Export current samples"))) this.ExportSamples();
            ImGui.TextWrapped(this.PluginText.F("diagnostics.gate", "Render gate: {0}; DrawUI calls: {1}", this.renderReport.Gate, this.drawCalls));
            ImGui.TextWrapped(this.PluginText.F("diagnostics.last_scan", "Last completed scan (UTC): {0}", this.lastReport?.CompletedUtc?.ToString("O") ?? "none"));
            ImGui.InputText(this.Label("diagnostics.client_build", "Game build / scene note"), ref this.clientBuildNote, 160);
            if (this.capture?.Active == true)
            {
                ImGui.Text(this.PluginText.F("diagnostics.recording", "Recording: {0}s remaining", this.capture.SecondsRemaining));
                if (ImGui.Button(this.Label("diagnostics.stop", "Stop and export"))) this.StopCapture("user_stopped");
            }
            else if (ImGui.Button(this.Label("diagnostics.start", "Record 60 seconds"))) this.StartCapture();
            if (ImGui.Button(this.Label("diagnostics.scan", "Scan awake now")))
            { this.scanRequested = true; this.nextScan = 0; }
            if (ImGui.Button(this.Label("diagnostics.api_compare", "Compare existing APIs / new scan once")))
            { this.apiComparisonRequested = true; this.scanRequested = true; this.nextScan = 0; }
            ImGui.TextWrapped(this.PluginText.T("diagnostics.api_note", "60-second recording also compares three API routes every 2 seconds; it does not change the framework's entity-processing settings."));
            ImGui.Text($"ProcessAllRenderableEntities = {GameHelper.Core.GHSettings.ProcessAllRenderableEntities}");
            if (ImGui.TreeNode(this.Label("diagnostics.api_results", "Last API comparison")))
            {
                if (this.lastApiComparison != null)
                {
                    ImGui.Text($"UTC: {this.lastApiComparison.Utc:O}; ProcessAllRenderableEntities={this.lastApiComparison.ProcessAllRenderableEntities}");
                    foreach (var snapshot in this.lastApiComparison.Snapshots)
                        ImGui.Text($"{snapshot.Source}: candidates={snapshot.CandidateCount}; observations={snapshot.ObservationCount}; unknown-kind={snapshot.UnknownKindCount}; unknown-state={snapshot.UnknownStateCount}; {snapshot.Milliseconds:F1} ms; area={snapshot.AreaHash}");
                    foreach (var pair in this.lastApiComparison.Pairs)
                    {
                        ImGui.Text($"{pair.Left} / {pair.Right}: complete={pair.CompleteWindow}; common={pair.Common}; equal={pair.EqualObservations}; only-left={pair.OnlyLeft}; only-right={pair.OnlyRight}; neither-usable={pair.NeitherUsable}");
                        foreach (var field in pair.FieldDifferences) ImGui.Text($"  {field.Key}: {field.Value}");
                    }
                }
                ImGui.TreePop();
            }
            ImGui.InputText(this.Label("diagnostics.filter", "Probe metadata contains"), ref this.probeFilter, 96);
            if (ImGui.Button(this.Label("diagnostics.probe", "Compare awake / sleeping once"))) this.probeRequested = true;
            ImGui.TextWrapped(this.PluginText.T("diagnostics.probe_note", "Probe runs once in a valid combat area, may pause a frame, and does not add sleeping entities to the overlay."));
            if (this.probeRequested || this.scanRequested)
                ImGui.TextWrapped(this.PluginText.T("diagnostics.queued", "Diagnostic scan queued; waiting for a valid area and player."));
            if (ImGui.TreeNode(this.Label("diagnostics.stages", "Last scan stages")))
            {
                if (this.lastReport != null)
                {
                    ImGui.Text($"{this.lastReport.Stages.Source}; declared={this.lastReport.Stages.DeclaredCount}; discarded={this.lastReport.Discarded}");
                    foreach (var stage in this.lastReport.Stages.Counts.OrderBy(x => x.Key)) ImGui.Text($"{stage.Key}: {stage.Value}");
                }
                ImGui.TreePop();
            }
            if (ImGui.TreeNode(this.Label("diagnostics.render", "Projection and draw submission counts")))
            {
                foreach (var stage in this.renderReport.Stages.Counts.OrderBy(x => x.Key)) ImGui.Text($"{stage.Key}: {stage.Value}");
                ImGui.TreePop();
            }
            if (ImGui.TreeNode(this.Label("diagnostics.probe_results", "Last source comparison")))
            {
                foreach (var report in this.probeReports)
                {
                    ImGui.Text($"{report.Stages.Source} ({report.CompletedUtc:O}); area={report.AreaHash}; filter={report.Filter}; discarded={report.Discarded}");
                    foreach (var stage in report.Stages.Counts.OrderBy(x => x.Key)) ImGui.Text($"{stage.Key}: {stage.Value}");
                }
                ImGui.TreePop();
            }
            if (!string.IsNullOrEmpty(this.status)) ImGui.TextWrapped(this.status);
        }
    }
}
