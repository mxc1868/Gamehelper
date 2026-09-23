namespace UniqueLoot;

using System.Diagnostics;
using System.Numerics;
using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Natives;
using ImGuiNET;
using Newtonsoft.Json;

public sealed class UniqueLootCore : PCore<UniqueLootSettings>
{
    private sealed record Drop(uint Id, long Address, long ItemAddress, string Metadata, string BaseName,
        ArtMatch Match, StdTuple3D<float> Position, float TerrainHeight, float Distance);

    private sealed record ReadSample(uint Id, string WorldMetadata, string Stage,
        long ItemAddress = 0, string ItemMetadata = "", string Rarity = "", string Asset = "");

    private sealed class ScanReport
    {
        public DateTime Utc = DateTime.UtcNow;
        public string AreaHash = string.Empty;
        public string Catalog = "RePoE2 4.5.5.2 / b818b843337cae43b090b272fd98bbc0fd3a34f3";
        public bool CustomMapping;
        public bool ProcessAllRenderableEntities;
        public int Visited, InvalidEntities, WorldItems, UnreadableItems, MissingRarity, UniqueItems;
        public int Single, Multiple, Unknown, MissingArt, MissingPosition, Errors;
        public double Milliseconds;
        public bool Truncated;
        public string LastError = string.Empty;
        public List<Drop> Samples = new();
        public List<ReadSample> ReadSamples = new();
        public bool SamplesTruncated;

        public void Record(ReadSample sample)
        {
            // Keep several examples of each failure even if many normal items precede them.
            if (this.ReadSamples.Count(x => x.Stage == sample.Stage) < 4) this.ReadSamples.Add(sample);
        }
    }

    private UniqueArtCatalog catalog = UniqueArtCatalog.Empty;
    private Drop[] drops = [];
    private ScanReport report = new();
    private ActiveCoroutine? areaChanged, gameClosed;
    private IntPtr areaAddress;
    private string areaHash = string.Empty;
    private long nextScan;
    private int generation;
    private bool customMapping;
    private string catalogStatus = string.Empty, actionStatus = string.Empty;
    private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.json");
    private string T(string key, string fallback) => this.PluginText.T(key, fallback);
    private string L(string key, string fallback) => this.PluginText.Label(key, fallback, "UniqueLoot_" + key);

    public override void OnEnable(bool isGameOpened)
    {
        this.OnDisable();
        try
        {
            if (File.Exists(this.SettingsPath))
                this.Settings = JsonConvert.DeserializeObject<UniqueLootSettings>(File.ReadAllText(this.SettingsPath)) ?? new();
        }
        catch (Exception ex) { this.Settings = new(); this.actionStatus = ex.Message; }
        this.Settings.Normalize();
        this.LoadCatalog();
        this.areaChanged = CoroutineHandler.Start(this.ResetOn(RemoteEvents.AreaChanged));
        this.gameClosed = CoroutineHandler.Start(this.ResetOn(GameHelperEvents.OnClose));
    }

    public override void OnDisable()
    {
        this.areaChanged?.Cancel();
        this.gameClosed?.Cancel();
        this.areaChanged = this.gameClosed = null;
        this.Reset();
    }

    private void Reset()
    {
        this.generation++;
        this.drops = [];
        this.report = new();
        this.areaAddress = IntPtr.Zero;
        this.areaHash = string.Empty;
        this.nextScan = 0;
    }

    private IEnumerator<Wait> ResetOn(Event evt)
    {
        while (true) { yield return new Wait(evt); this.Reset(); }
    }

    public override void SaveSettings()
    {
        try
        {
            this.Settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingsPath)!);
            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }
        catch (Exception ex) { this.actionStatus = ex.Message; }
    }

    private void LoadCatalog()
    {
        try
        {
            using var stream = typeof(UniqueLootCore).Assembly.GetManifestResourceStream("UniqueLoot.ArtMapping")
                ?? throw new InvalidOperationException("Bundled art catalog is missing.");
            using var reader = new StreamReader(stream);
            var replacement = UniqueArtCatalog.Parse(reader.ReadToEnd());
            var customPath = Path.Join(this.DllDirectory, "uniqueArtMapping.json");
            var hasCustom = File.Exists(customPath);
            if (hasCustom)
            {
                if (new FileInfo(customPath).Length > 4 * 1024 * 1024) throw new FormatException("Custom catalog exceeds 4 MiB.");
                replacement = replacement.WithOverrides(UniqueArtCatalog.Parse(File.ReadAllText(customPath)));
            }
            this.catalog = replacement;
            this.customMapping = hasCustom;
            this.catalogStatus = string.Empty;
            this.nextScan = 0;
            this.drops = [];
        }
        catch (Exception ex)
        {
            // Keep the last known-good catalog on reload. On first load retry the bundled table.
            this.catalogStatus = ex.Message;
            if (this.catalog.Count == 0)
            {
                try
                {
                    using var stream = typeof(UniqueLootCore).Assembly.GetManifestResourceStream("UniqueLoot.ArtMapping")!;
                    using var reader = new StreamReader(stream);
                    this.catalog = UniqueArtCatalog.Parse(reader.ReadToEnd());
                    this.customMapping = false;
                }
                catch { this.catalog = UniqueArtCatalog.Empty; }
            }
        }
    }

    public override void DrawUI()
    {
        try { this.DrawFrame(); }
        catch (Exception ex) { this.drops = []; this.actionStatus = ex.Message; }
    }

    private void DrawFrame()
    {
        if (Core.States.GameCurrentState != GameStateTypes.InGameState)
        {
            this.drops = [];
            this.nextScan = 0;
            return;
        }
        var area = Core.States.InGameStateObject.CurrentAreaInstance;
        var details = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
        if (area.Address == IntPtr.Zero || !area.Player.IsValid || details.IsTown || details.IsHideout)
        { this.Reset(); return; }
        if (area.Address != this.areaAddress || area.AreaHash != this.areaHash)
        {
            this.Reset();
            this.areaAddress = area.Address;
            this.areaHash = area.AreaHash;
        }
        if (this.Settings.HideWhenUnfocused && !Core.Process.Foreground) return;
        if (Environment.TickCount64 >= this.nextScan)
        {
            this.Scan(area);
            this.nextScan = Environment.TickCount64 + this.Settings.ScanIntervalMs;
        }
        if (this.Settings.HideWhenPanelsOpen && Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen) return;
        this.DrawDrops();
    }

    private void Scan(AreaInstance area)
    {
        var scanGeneration = this.generation;
        var scanAddress = area.Address;
        var watch = Stopwatch.StartNew();
        var result = new List<Drop>();
        var sample = new ScanReport { AreaHash = area.AreaHash, CustomMapping = this.customMapping,
            ProcessAllRenderableEntities = Core.GHSettings.ProcessAllRenderableEntities };
        foreach (var entity in area.AwakeEntities.Values)
        {
            if (sample.Visited >= 20000 || watch.ElapsedMilliseconds > 100)
            { sample.Truncated = true; break; }
            sample.Visited++;
            if (!entity.IsValid) { sample.InvalidEntities++; continue; }
            try
            {
                if (!entity.TryGetComponent<WorldItem>(out var cachedWorld, shouldCache: false)) continue;
                sample.WorldItems++;
                var world = new WorldItem(cachedWorld.Address);
                if (!world.IsParentValid(entity.Address) || !world.TryReadItem(out var item))
                {
                    sample.UnreadableItems++;
                    sample.Record(new(entity.Id, entity.Path, "inner_item_unreadable", world.ItemEntityAddress.ToInt64()));
                    continue;
                }
                if (!item.TryGetComponent<Mods>(out var mods, shouldCache: false) || !mods.IsParentValid(item.Address))
                {
                    sample.MissingRarity++;
                    sample.Record(new(entity.Id, entity.Path, "rarity_unreadable", item.Address.ToInt64(), item.Path));
                    continue;
                }
                if (mods.Rarity != Rarity.Unique)
                {
                    sample.Record(new(entity.Id, entity.Path, "non_unique", item.Address.ToInt64(), item.Path, mods.Rarity.ToString()));
                    continue;
                }
                sample.UniqueItems++;
                var asset = item.TryGetComponent<RenderItem>(out var art, shouldCache: false) && art.IsParentValid(item.Address)
                    ? art.ResourcePath : string.Empty;
                var match = this.catalog.Resolve(asset);
                sample.Record(new(entity.Id, entity.Path, match.Kind.ToString(), item.Address.ToInt64(), item.Path, mods.Rarity.ToString(), asset));
                switch (match.Kind)
                {
                    case ArtMatchKind.Single: sample.Single++; break;
                    case ArtMatchKind.Multiple: sample.Multiple++; break;
                    case ArtMatchKind.Unknown: sample.Unknown++; break;
                    case ArtMatchKind.MissingArt: sample.MissingArt++; break;
                }
                var baseName = item.TryGetComponent<Base>(out var baseItem, shouldCache: false) && baseItem.IsParentValid(item.Address)
                    ? baseItem.BaseItemName : string.Empty;
                var position = new StdTuple3D<float> { X = float.NaN, Y = float.NaN, Z = float.NaN };
                var height = float.NaN;
                var distance = float.PositiveInfinity;
                if (entity.TryGetComponent<Render>(out var cachedRender, shouldCache: false))
                {
                    var render = new Render(cachedRender.Address);
                    if (render.IsParentValid(entity.Address))
                    {
                        position = render.WorldPosition;
                        height = render.TerrainHeight;
                        if (area.Player.TryGetComponent<Render>(out var playerRender, shouldCache: false))
                        {
                            var p = playerRender.GridPosition;
                            var r = render.GridPosition;
                            var d = Vector2.Distance(new(p.X, p.Y), new(r.X, r.Y));
                            if (float.IsFinite(d)) distance = d;
                        }
                    }
                }
                if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(height)) sample.MissingPosition++;
                var drop = new Drop(entity.Id, entity.Address.ToInt64(), item.Address.ToInt64(), item.Path,
                    baseName, match, position, height, distance);
                result.Add(drop);
                if (sample.Samples.Count < 100) sample.Samples.Add(drop);
                else sample.SamplesTruncated = true;
            }
            catch (Exception ex)
            {
                sample.Errors++;
                sample.LastError = ex.Message;
                sample.Record(new(entity.Id, entity.Path, "exception"));
            }
        }
        sample.Milliseconds = watch.Elapsed.TotalMilliseconds;
        if (scanGeneration != this.generation || area.Address != scanAddress || area.AreaHash != sample.AreaHash) return;
        // Replace each scan: picked-up/unreadable drops cannot persist in a historical alert cache.
        this.drops = result.OrderBy(x => x.Distance).ThenBy(x => x.Id).ToArray();
        this.report = sample;
    }

    private string DropText(Drop drop) => drop.Match.Kind switch
    {
        ArtMatchKind.Single => drop.Match.Candidates[0],
        ArtMatchKind.Multiple => this.T("possible", "Possible: ") + string.Join(" / ", drop.Match.Candidates),
        ArtMatchKind.MissingArt => this.T("missing", "Unique (art unreadable)") + BaseSuffix(drop),
        _ => this.T("unknown", "Unknown unique") + BaseSuffix(drop),
    };

    private static string BaseSuffix(Drop drop) => string.IsNullOrWhiteSpace(drop.BaseName) ? string.Empty : " · " + drop.BaseName;

    private void DrawDrops()
    {
        var draw = ImGui.GetBackgroundDrawList();
        var screenSize = ImGui.GetIO().DisplaySize;
        var world = Core.States.InGameStateObject.CurrentWorldInstance;
        var listPos = new Vector2(Math.Min(this.Settings.ListX, Math.Max(0, screenSize.X - 200)),
            Math.Min(this.Settings.ListY, Math.Max(0, screenSize.Y - 60)));
        var count = 0;
        foreach (var drop in this.drops)
        {
            if (!this.Settings.ShowUnknown && drop.Match.Kind is ArtMatchKind.Unknown or ArtMatchKind.MissingArt) continue;
            if (count++ >= this.Settings.MaxLabels) break;
            var text = this.DropText(drop);
            var color = drop.Match.Kind == ArtMatchKind.Single ? 0xFF55AAFFu : 0xFF80DCFFu;
            if (this.Settings.ShowGroundNames && float.IsFinite(drop.Position.X) && float.IsFinite(drop.Position.Y) &&
                float.IsFinite(drop.Position.Z) && float.IsFinite(drop.TerrainHeight))
            {
                var point = world.WorldToScreen(drop.Position, drop.TerrainHeight);
                if (float.IsFinite(point.X) && float.IsFinite(point.Y) && point != Vector2.Zero &&
                    point.X >= 0 && point.Y >= 0 && point.X <= screenSize.X && point.Y <= screenSize.Y)
                    DrawText(draw, point + new Vector2(-ImGui.CalcTextSize(text).X / 2, this.Settings.GroundOffsetY), text, color);
            }
            if (this.Settings.ShowList)
            {
                if (count == 1)
                {
                    DrawText(draw, listPos, this.T("list.title", "Unique drops"), 0xFFFFFFFF);
                    listPos.Y += ImGui.GetTextLineHeightWithSpacing() + 4;
                }
                var distance = float.IsFinite(drop.Distance) ? $"  [{drop.Distance:0}]" : string.Empty;
                if (listPos.Y + ImGui.GetTextLineHeightWithSpacing() < screenSize.Y)
                    DrawText(draw, listPos, text + distance, color);
                listPos.Y += ImGui.GetTextLineHeightWithSpacing() + 3;
            }
        }
    }

    private static void DrawText(ImDrawListPtr draw, Vector2 position, string text, uint color)
    {
        var size = ImGui.CalcTextSize(text);
        draw.AddRectFilled(position - new Vector2(3, 2), position + size + new Vector2(3, 2), 0xBB000000, 3);
        draw.AddText(position, color, text);
    }

    public override void DrawSettings()
    {
        ImGui.TextWrapped(this.T("intro", "Reveal unique drop names from item art, without identification or price data. Shared art shows all candidates."));
        ImGui.Checkbox(this.L("ground", "Names beside ground items"), ref this.Settings.ShowGroundNames);
        ImGui.Checkbox(this.L("list", "Drop list"), ref this.Settings.ShowList);
        ImGui.Checkbox(this.L("show_unknown", "Show unknown / unreadable uniques"), ref this.Settings.ShowUnknown);
        ImGui.Checkbox(this.L("unfocused", "Hide when unfocused"), ref this.Settings.HideWhenUnfocused);
        ImGui.Checkbox(this.L("panels", "Hide while large panels are open"), ref this.Settings.HideWhenPanelsOpen);
        ImGui.SliderInt(this.L("interval", "Scan interval (ms)"), ref this.Settings.ScanIntervalMs, 200, 5000);
        ImGui.SliderInt(this.L("max", "Maximum labels"), ref this.Settings.MaxLabels, 1, 100);
        ImGui.SliderInt(this.L("list_x", "List X"), ref this.Settings.ListX, 0, 4000);
        ImGui.SliderInt(this.L("list_y", "List Y"), ref this.Settings.ListY, 0, 2000);
        ImGui.SliderInt(this.L("ground_y", "Ground text Y offset"), ref this.Settings.GroundOffsetY, -300, 300);
        this.Settings.Normalize();
        ImGui.Separator();
        ImGui.TextWrapped(this.PluginText.F("catalog", "Art paths: {0}; bundled data: PoE2 4.5.5.2; custom: {1}", this.catalog.Count, this.customMapping));
        if (ImGui.Button(this.L("reload", "Reload art mapping"))) this.LoadCatalog();
        if (!string.IsNullOrEmpty(this.catalogStatus)) ImGui.TextWrapped(this.catalogStatus);
        var r = this.report;
        ImGui.TextWrapped(this.PluginText.F("stats", "Scanned: {0}; ground: {1}; unique: {2}; single: {3}; multiple: {4}; unknown: {5}; missing art: {6}",
            r.Visited, r.WorldItems, r.UniqueItems, r.Single, r.Multiple, r.Unknown, r.MissingArt));
        ImGui.TextWrapped(this.PluginText.F("reads", "Unreadable items: {0}; missing rarity: {1}; errors: {2}; scan: {3:F1} ms; truncated: {4}",
            r.UnreadableItems, r.MissingRarity, r.Errors, r.Milliseconds, r.Truncated));
        ImGui.TextWrapped(this.T("limits", "Only ground entities exposed by GameHelper are scanned. Zero results do not prove there were no drops. Windows/game validation pending."));
        if (ImGui.Button(this.L("export", "Export latest scan"))) this.ExportReport();
        if (!string.IsNullOrEmpty(this.actionStatus)) ImGui.TextWrapped(this.actionStatus);
    }

    private void ExportReport()
    {
        try
        {
            var directory = Path.Join(this.DllDirectory, "diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Join(directory, "latest-scan.json");
            var json = JsonConvert.SerializeObject(new { PluginVersion = typeof(UniqueLootCore).Assembly.GetName().Version?.ToString(),
                CoreVersion = typeof(Core).Assembly.GetName().Version?.ToString(), this.catalogStatus, this.Settings, Scan = this.report }, Formatting.Indented);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
            this.actionStatus = path;
        }
        catch (Exception ex) { this.actionStatus = ex.Message; }
    }
}
