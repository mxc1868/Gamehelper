namespace ShowMeWisp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using GameHelper;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;

    internal static class WispRenderer
    {
        public static void Draw(IReadOnlyList<WispObservation> observations, ShowMeWispSettings settings,
            Func<string, string, string> text, WispRenderReport? report)
        {
            var state = Core.States.InGameStateObject;
            var area = state.CurrentAreaInstance;
            if (!area.Player.IsValid || !area.Player.TryGetComponent<Render>(out var player) || !player.IsParentValid(area.Player.Address))
            { if (report != null) report.Gate = "player_render_unavailable"; return; }
            var origin = new Vector2(player.GridPosition.X, player.GridPosition.Y);
            var height = player.TerrainHeight;
            if (settings.FollowCoopCenter)
            {
                var other = area.AwakeEntities.Values.FirstOrDefault(e => e.IsValid && e.EntitySubtype == EntitySubtypes.PlayerOther);
                if (other != null && other.TryGetComponent<Render>(out var otherRender) && otherRender.IsParentValid(other.Address))
                {
                    origin = (origin + new Vector2(otherRender.GridPosition.X, otherRender.GridPosition.Y)) / 2;
                    height = (height + otherRender.TerrainHeight) / 2;
                }
            }

            if (report != null) report.Values["OriginGrid"] = origin;
            if (report != null) report.Values["OriginHeight"] = height;
            if (!IsFinite(origin) || !float.IsFinite(height)) { if (report != null) report.Gate = "player_position_nonfinite"; return; }
            if (report != null) report.Values["InputCount"] = observations.Count;
            if (report != null) report.Values["ConsumedHidden"] = observations.Count(x => x.Consumed);
            if (report != null) report.Values["UnknownHidden"] = observations.Count(x => !x.Consumed && !settings.ShowUnknownResources && x.Classification.Kind == WispKind.UnknownResource);
            var visible = observations.Where(x => !x.Consumed &&
                (settings.ShowUnknownResources || x.Classification.Kind != WispKind.UnknownResource)).ToArray();
            var draw = ImGui.GetBackgroundDrawList();
            var screenSize = new Vector2(Core.Process.WindowArea.Width, Core.Process.WindowArea.Height);
            if (report != null) report.Values["EligibleCount"] = visible.Length;
            if (report != null) report.Values["ScreenSize"] = screenSize;
            if (!IsFinite(screenSize) || screenSize.X <= 0 || screenSize.Y <= 0) { if (report != null) report.Gate = "window_size_invalid"; return; }
            var map = state.GameUi.LargeMap;
            if (report != null) report.Values["LargeMap"] = new { Enabled = settings.DrawMap, map.IsVisible, map.Center, map.Size, map.Zoom, map.Shift, map.DefaultShift, WorldMapOpen = state.GameUi.WorldMapPanel.IsVisible };
            if (settings.DrawMap && map.IsVisible && !state.GameUi.WorldMapPanel.IsVisible)
            {
                DrawMap(draw, visible, settings, text, origin, height, report, "large_map",
                    map.Center + map.Shift + map.DefaultShift + new Vector2(0.6f, 0.3f) + settings.MapOffset,
                    map.Size.Y, map.Zoom * 0.187812f * settings.MapScaleMultiplier, Vector2.Zero, screenSize);
            }

            var mini = state.GameUi.MiniMap;
            if (report != null) report.Values["MiniMap"] = new { Enabled = settings.DrawMiniMap, mini.IsVisible, mini.Position, mini.Size, mini.Zoom, mini.Shift, mini.DefaultShift };
            if (settings.DrawMiniMap && mini.IsVisible)
            {
                DrawMap(draw, visible, settings, text, origin, height, report, "mini_map",
                    mini.Position + mini.Size / 2 + mini.Shift + mini.DefaultShift + new Vector2(-5, 0) + settings.MiniMapOffset,
                    mini.Size.Y, mini.Zoom * 0.748f * settings.MiniMapScaleMultiplier, mini.Position, mini.Position + mini.Size);
            }

            if (report != null) report.Values["GroundEnabled"] = settings.DrawGround;
            if (report != null) report.Values["ChestGroundEnabled"] = settings.DrawChestGround;
            if (!settings.DrawGround && !settings.DrawChestGround) return;
            draw.PushClipRect(Vector2.Zero, screenSize, true);
            try
            {
                foreach (var item in visible)
                {
                    var kind = item.Classification.Kind;
                    var isChest = kind == WispKind.Chest;
                    if (isChest)
                    {
                        if (!settings.DrawChestGround || Vector2.Distance(item.GridPosition, origin) > settings.ChestDistance)
                        { report?.Stages.Record("ground_chest_hidden", sample: false); continue; }
                    }
                    else if (!settings.DrawGround || !(WispClassifier.IsResource(kind) || kind is WispKind.LightBomb or WispKind.FuelRefill))
                    { report?.Stages.Record("ground_type_hidden", sample: false); continue; }

                    var size = WispBoxSizing.GroundSize(item, settings);
                    if (!IsFinite(size) || size.X <= 0 || size.Y <= 0 || size.Z <= 0 ||
                        size.X > 1000 || size.Y > 1000 || size.Z > 1000)
                    { report?.Stages.Record("ground_bounds_invalid", item.Id, item.Address, item.Metadata, size.ToString()); continue; }
                    var color = GetColor(kind, settings);
                    color.W *= settings.GroundOpacity;
                    var submitted = DrawBox(draw, state.CurrentWorldInstance, item.WorldPosition, item.TerrainHeight, size, screenSize,
                        ImGuiHelper.Color(color));
                    report?.Stages.Record(submitted ? "ground_box_submitted" : "ground_projection_rejected", item.Id, item.Address, item.Metadata,
                        $"WispSize={item.Classification.Size}; BoxSize={size}");
                }
            }
            finally { draw.PopClipRect(); }
            return;
        }

        private static void DrawMap(ImDrawListPtr draw, WispObservation[] items, ShowMeWispSettings settings,
            Func<string, string, string> text, Vector2 origin, float height, WispRenderReport? report, string layer, Vector2 center,
            float mapHeight, float zoom, Vector2 clipMin, Vector2 clipMax)
        {
            var baseRes = UiElementBaseFuncs.BaseResolution;
            var diagonal = Math.Sqrt(baseRes.X * baseRes.X + baseRes.Y * baseRes.Y) * mapHeight / baseRes.Y;
            var projection = new MapProjection(diagonal, zoom);
            if (report != null) report.Values[layer + "_projection"] = new { Diagonal = diagonal, Scale = zoom, Center = center, ClipMin = clipMin, ClipMax = clipMax };
            if (!projection.IsValid || !IsFinite(center) || !IsFinite(clipMin) || !IsFinite(clipMax) || clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
            { report?.Stages.Record(layer + "_parameters_invalid"); return; }
            Vector2 Project(WispObservation item) => center + projection.ProjectDelta(item.GridPosition - origin, item.TerrainHeight - height);
            bool Inside(Vector2 point) => IsFinite(point) && point.X >= clipMin.X && point.Y >= clipMin.Y &&
                point.X <= clipMax.X && point.Y <= clipMax.Y;
            draw.PushClipRect(clipMin, clipMax, true);
            try
            {
                if (settings.DrawMapLines)
                {
                    foreach (var (from, to) in WispTrails.Build(items, settings.MaxLinkGridDistance))
                    {
                        var start = Project(from);
                        var end = Project(to);
                        if (Inside(start) && Inside(end))
                        {
                            draw.AddLine(start, end, ImGuiHelper.Color(GetColor(from.Classification.Kind, settings)), settings.LineWidth);
                            report?.Stages.Record(layer + "_line_submitted", sample: false);
                        }
                    }
                }

                foreach (var item in items)
                {
                    var position = Project(item);
                    if (!Inside(position))
                    { report?.Stages.Record(layer + (IsFinite(position) ? "_outside_clip" : "_projection_nonfinite"), item.Id, item.Address, item.Metadata, position.ToString()); continue; }
                    var color = ImGuiHelper.Color(GetColor(item.Classification.Kind, settings));
                    var markerSize = WispBoxSizing.MapSize(item.Classification, settings);
                    report?.Stages.Record(layer + "_marker_submitted", item.Id, item.Address, item.Metadata,
                        $"Position={position}; WispSize={item.Classification.Size}; BoxSize={markerSize}");
                    var half = new Vector2(markerSize / 2);
                    draw.AddRectFilled(position - half, position + half, color);
                    if (!settings.DrawLabels) continue;
                    var label = item.Classification.Kind == WispKind.UnknownResource ? "?" : item.Classification.Label;
                    if (!string.IsNullOrEmpty(item.Classification.LabelKey))
                        label = text(item.Classification.LabelKey, label);
                    if (string.IsNullOrEmpty(label)) continue;
                    var labelSize = ImGui.CalcTextSize(label);
                    var labelPos = position + new Vector2(markerSize, -labelSize.Y / 2);
                    draw.AddRectFilled(labelPos - new Vector2(3, 1), labelPos + labelSize + new Vector2(3, 1), 0xD9000000);
                    draw.AddText(labelPos, color, label);
                }
            }
            finally { draw.PopClipRect(); }
        }

        private static bool DrawBox(ImDrawListPtr draw, WorldData world, Vector3 position, float ground,
            Vector3 size, Vector2 screenSize, uint color)
        {
            Span<Vector2> points = stackalloc Vector2[8];
            for (var i = 0; i < 8; i++)
            {
                var p = new StdTuple3D<float>
                {
                    X = position.X + ((i & 1) == 0 ? -size.X : size.X) / 2,
                    Y = position.Y + ((i & 2) == 0 ? -size.Y : size.Y) / 2,
                    Z = ground + ((i & 4) == 0 ? 0 : size.Z),
                };
                points[i] = world.WorldToScreen(p, p.Z);
                if (!IsFinite(points[i]) || points[i] == Vector2.Zero ||
                    points[i].X < -screenSize.X || points[i].X > screenSize.X * 2 ||
                    points[i].Y < -screenSize.Y || points[i].Y > screenSize.Y * 2) return false;
            }

            // Axis-aligned boxes: GameHelper does not expose verified model rotation here.
            draw.AddQuadFilled(points[4], points[5], points[7], points[6], color);
            draw.AddQuadFilled(points[0], points[1], points[5], points[4], color);
            draw.AddQuadFilled(points[1], points[3], points[7], points[5], color);
            draw.AddQuadFilled(points[3], points[2], points[6], points[7], color);
            draw.AddQuadFilled(points[2], points[0], points[4], points[6], color);
            return true;
        }

        public static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static Vector4 GetColor(WispKind kind, ShowMeWispSettings settings) => kind switch
        {
            WispKind.Blue => settings.Blue,
            WispKind.Yellow => settings.Yellow,
            WispKind.Purple => settings.Purple,
            WispKind.Sacred => settings.Sacred,
            WispKind.Chest => settings.Chest,
            WispKind.UnknownResource => new(0.6f, 0.6f, 0.6f, 1),
            WispKind.FuelRefill => new(0.3f, 1, 0.3f, 1),
            WispKind.Well => new(1, 0.65f, 0.15f, 1),
            WispKind.Altar => new(1, 0.25f, 0.2f, 1),
            WispKind.DustConverter or WispKind.Dealer => new(1, 0.35f, 0.75f, 1),
            _ => Vector4.One,
        };
    }
}
