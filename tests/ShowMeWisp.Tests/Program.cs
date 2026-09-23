using System.Numerics;
using GameHelper.Utils;
using ShowMeWisp;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
bool Near(Vector2 actual, Vector2 expected, float epsilon = 0.0001f) => Vector2.Distance(actual, expected) < epsilon;
string ReadActiveCapture(string path)
{
    // On Windows a reader must also share write access with the active capture writer.
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
var orange = new Vector4(1, 0.5f, 0, 1);
Check("new Sacred wisp default is orange", new ShowMeWispSettings().Sacred == orange);
var legacySacred = new ShowMeWispSettings { Sacred = Vector4.One };
legacySacred.Normalize();
Check("old white Sacred configuration migrates to orange", legacySacred.Sacred == orange && legacySacred.SacredColorVersion == 1);
legacySacred.Sacred = Vector4.One;
legacySacred.Normalize();
Check("Sacred migration only runs once", legacySacred.Sacred == Vector4.One);
var customSacred = new ShowMeWispSettings { Sacred = new(0.2f, 0.3f, 0.4f, 1) };
customSacred.Normalize();
Check("Sacred migration retains existing custom colors", customSacred.Sacred == new Vector4(0.2f, 0.3f, 0.4f, 1));
const string resource = "Metadata/MiscellaneousObjects/Azmeri/AzmeriResource";
foreach (var (suffix, kind) in new[]
{
    ("_primal", WispKind.Blue), ("_warden", WispKind.Yellow),
    ("_vodoo", WispKind.Purple), ("_sacred", WispKind.Sacred),
})
    Check("shared metadata classified using " + suffix,
        WispClassifier.Classify(resource, "Metadata/Effects/wisp" + suffix)?.Kind == kind);
Check("late Animated data initially remains unknown", WispClassifier.Classify(resource, "")?.Kind == WispKind.UnknownResource);
Check("unrecognized model remains unknown", WispClassifier.Classify(resource, "new_model")?.Kind == WispKind.UnknownResource);
Check("next snapshot can resolve a late model", WispClassifier.Classify(resource, "wisp_primal")?.Kind == WispKind.Blue);
Check("ritual wisps are not Wildwood resources", WispClassifier.Classify("Metadata/Monsters/LeagueRitual/RitualWispDaemon", "wisp_primal") == null);
Check("irrelevant Azmeri objects excluded", WispClassifier.Classify("Metadata/MiscellaneousObjects/Azmeri/Unrelated", "") == null);
Check("omen chest participates in opened-state handling", WispClassifier.Classify("Metadata/Chests/LeagueAzmeri/OmenChest", "")?.Kind == WispKind.Chest);
Check("harvest has a readable event label", WispClassifier.Classify("Metadata/NPC/League/Affliction/GlyphsHarvestTree", "")?.Label == "Harvest");
Check("merchant paths supported", WispClassifier.Classify("Metadata/MiscellaneousObjects/Azmeri/UniqueDealer", "")?.Kind == WispKind.Dealer);
Check("candidate filter includes NPC merchants", WispClassifier.IsCandidate("Metadata/NPC/Azmeri/UniqueDealer"));
Check("activation belongs to wells, altars and converters", WispClassifier.UsesActivation(WispKind.Well) && WispClassifier.UsesActivation(WispKind.Altar) && WispClassifier.UsesActivation(WispKind.DustConverter) && !WispClassifier.UsesActivation(WispKind.FuelRefill));

// Model family retained in the user's saved Windows implementation. Its handoff
// records blue/yellow live samples; purple/sacred cases below remain synthetic.
const string modelDirectory = "Metadata/Effects/Spells/monsters_effects/League_Azmeri/resources/wisp_doodads/";
foreach (var (colour, expectedKind) in new[] { ("primal", WispKind.Blue), ("warden", WispKind.Yellow), ("vodoo", WispKind.Purple), ("sacred", WispKind.Sacred) })
foreach (var (suffix, expectedSize) in new[] { ("_sml", WispSize.Small), ("_med", WispSize.Medium), ("_big", WispSize.Big), ("", WispSize.Unknown) })
{
    var model = modelDirectory + "wisp_" + colour + suffix + ".ao";
    var classified = WispClassifier.Classify(resource + "Base", resource + "Base", model);
    Check("shared metadata resolves model " + colour + suffix, classified?.Kind == expectedKind && classified?.Size == expectedSize);
}
var bigModel = modelDirectory + "wisp_warden_big.ao";
Check("model classification tolerates empty Animated.Path", WispClassifier.Classify(resource, "", bigModel) is { Kind: WispKind.Yellow, Size: WispSize.Big });
Check("known Animated colour takes precedence while model supplies size", WispClassifier.Classify(resource, "wisp_primal", bigModel) is { Kind: WispKind.Blue, Size: WispSize.Big });
Check("model matching is case insensitive", WispClassifier.Classify(resource, "", bigModel.ToUpperInvariant()) is { Kind: WispKind.Yellow, Size: WispSize.Big });
foreach (var model in new[] { "Metadata/Other/wisp_warden_big.ao", modelDirectory + "wisp_warden_fake.ao", modelDirectory + "wisp_wardenfake_big.ao", modelDirectory + "wisp_warden_sml_big.ao", modelDirectory + "wisp_warden_big.dds" })
    Check("unrecognized resource model remains unknown: " + model, WispClassifier.Classify(resource, "", model) is { Kind: WispKind.UnknownResource, Size: WispSize.Unknown });
Check("size words outside the resource model do not guess size", WispClassifier.Classify(resource + "Small", "wisp_primal_big")?.Size == WispSize.Unknown);
Check("missing size stays unknown instead of becoming medium", WispClassifier.Classify(resource, "wisp_primal")?.Size == WispSize.Unknown);
Check("models cannot reclassify chests", WispClassifier.Classify("Metadata/Chests/LeagueAzmeri/ChestBig", "", bigModel) is { Kind: WispKind.Chest, Size: WispSize.Unknown });
Check("models cannot reclassify supplies", WispClassifier.Classify("Metadata/MiscellaneousObjects/Azmeri/AzmeriFuelResupply", "", bigModel) is { Kind: WispKind.FuelRefill, Size: WispSize.Unknown });
Check("models cannot turn ritual entities into Wildwood wisps", WispClassifier.Classify("Metadata/Monsters/LeagueRitual/RitualWispDaemon", "", bigModel) == null);

var boxSettings = new ShowMeWispSettings();
var smallBox = new WispClassification(WispKind.Blue, Size: WispSize.Small);
var mediumBox = new WispClassification(WispKind.Blue, Size: WispSize.Medium);
var bigBox = new WispClassification(WispKind.Blue, Size: WispSize.Big);
Check("default map boxes are 3 / 5 / 8 pixels", WispBoxSizing.MapSize(smallBox, boxSettings) == 3 && WispBoxSizing.MapSize(mediumBox, boxSettings) == 5 && WispBoxSizing.MapSize(bigBox, boxSettings) == 8);
Check("unknown wisp size preserves the configured base marker", WispBoxSizing.MapSize(new(WispKind.Blue), boxSettings) == boxSettings.MarkerSize);
Check("supplies do not inherit a wisp box scale", WispBoxSizing.MapSize(new(WispKind.FuelRefill, Size: WispSize.Big), boxSettings) == boxSettings.MarkerSize);
boxSettings.ScaleWispBoxes = false;
Check("disabling size scaling restores uniform boxes", WispBoxSizing.MapSize(smallBox, boxSettings) == 5 && WispBoxSizing.MapSize(bigBox, boxSettings) == 5);
boxSettings.ScaleWispBoxes = true;
boxSettings.BigWispScale = 2;
Check("custom Big scale changes the marker size", WispBoxSizing.MapSize(bigBox, boxSettings) == 10);
boxSettings.SmallWispScale = float.NaN; boxSettings.MediumWispScale = -1; boxSettings.BigWispScale = float.PositiveInfinity;
boxSettings.Normalize();
Check("malformed size settings are finite and bounded", boxSettings.SmallWispScale == 0.6f && boxSettings.MediumWispScale == 0.1f && boxSettings.BigWispScale == 1.6f);

WispObservation Item(uint id, WispKind kind = WispKind.Blue, float x = 0, bool consumed = false) =>
    new(id, id + 0x10000L, resource, "", "", new(kind), new(x, 0), new(x * 10.86957f, 0, 0), 0, Vector3.One, consumed, true);
Check("ground boxes use the same Small scale", WispBoxSizing.GroundSize(Item(1) with { Classification = smallBox }, boxSettings) == new Vector3(18, 18, 6));
Check("ground boxes use the same Big scale", WispBoxSizing.GroundSize(Item(1) with { Classification = bigBox }, boxSettings) == new Vector3(48, 48, 16));
Check("chest ground bounds are preserved", WispBoxSizing.GroundSize(Item(1, WispKind.Chest) with { Bounds = new(12, 23, 34) }, boxSettings) == new Vector3(12, 23, 34));
int Links(params WispObservation[] items) => WispTrails.Build(items, 30).Count();
Check("unordered input connects consecutive IDs", Links(Item(3, x: 20), Item(1), Item(2, x: 10)) == 2);
Check("ID gap does not connect", Links(Item(1), Item(3, x: 10)) == 0);
Check("colors do not cross-connect", Links(Item(1), Item(2, WispKind.Purple, 10)) == 0);
Check("distance limit is strict", Links(Item(1), Item(2, x: 30)) == 0);
Check("consumed wisps do not connect", Links(Item(1), Item(2, x: 10, consumed: true)) == 0);
Check("unknown colors do not connect", Links(Item(1, WispKind.UnknownResource), Item(2, WispKind.UnknownResource, 10)) == 0);
Check("non-resource IDs do not connect", Links(Item(1, WispKind.Chest), Item(2, WispKind.Chest, 10)) == 0);
Check("uint wraparound is not adjacency", Links(Item(uint.MaxValue), Item(0, x: 10)) == 0);
Check("nonfinite positions do not produce lines", Links(Item(1), Item(2, x: float.NaN)) == 0);
Check("nonfinite link distance is rejected", !WispTrails.Build([Item(1), Item(2)], float.PositiveInfinity).Any());

var projection = new MapProjection(2000, 0.2f);
Check("projection preserves Radar's calibrated numeric baseline", Near(projection.ProjectDelta(new(15, 8), 3), new(9.1050214f, -23.680024f)));
Check("player origin projects to map center", Near(projection.ProjectDelta(Vector2.Zero, 0), Vector2.Zero));
var xAxis = projection.ProjectDelta(Vector2.UnitX, 0);
var yAxis = projection.ProjectDelta(Vector2.UnitY, 0);
Check("map axes retain isometric orientation", xAxis.X > 0 && yAxis.X < 0 && xAxis.Y < 0 && yAxis.Y < 0);
Check("terrain height adjusts vertical position", projection.ProjectDelta(Vector2.Zero, 10).Y > 0);
Check("zoom changes scale linearly", Near(new MapProjection(2000, 0.4f).ProjectDelta(new(15, 8), 3), projection.ProjectDelta(new(15, 8), 3) * 2));
Check("invalid dimensions rejected", !new MapProjection(0, 1).IsValid && !new MapProjection(1000, float.NaN).IsValid);
Radar.Helper.DiagonalLength = 2000;
Radar.Helper.Scale = 0.2f;
var oldResult = Radar.Helper.DeltaInWorldToMapDelta(new(15, 8), 3);
Check("Radar and plugin share projection", Near(oldResult, projection.ProjectDelta(new(15, 8), 3)));
_ = new MapProjection(100, 3).ProjectDelta(new(25, 8), 12);
Check("another plugin cannot change Radar projection", Near(oldResult, Radar.Helper.DeltaInWorldToMapDelta(new(15, 8), 3)));

var settings = new ShowMeWispSettings
{
    ScanIntervalMs = -1, MarkerSize = float.NaN, GroundOpacity = 100,
    MapScaleMultiplier = float.PositiveInfinity, MapOffset = new(float.NaN, 3000),
    Blue = new(float.NaN, -1, 100, float.PositiveInfinity),
};
settings.Normalize();
Check("malformed settings cannot create a tight scan loop", settings.ScanIntervalMs == 100);
Check("malformed drawing settings have finite limits", settings.MarkerSize == 5 && settings.GroundOpacity == 1 && settings.MapScaleMultiplier == 1 && settings.MapOffset == new Vector2(0, 2000) && settings.Blue == new Vector4(1, 0, 1, 1));
var diagnostics = new GameHelper.RemoteObjects.States.InGameStateObjects.EntityScanDiagnostics(4);
Parallel.For(0, 10000, i => diagnostics.Record("read_failed", (uint)i, i, new string('p', 800), new string('d', 3000)));
Check("parallel failure counters retain all entries", diagnostics.Counts["read_failed"] == 10000);
Check("parallel evidence is bounded per stage", diagnostics.Samples.Length == 4 && diagnostics.Samples.All(x => x.Path.Length == 512 && x.Detail.Length == 2048));
diagnostics.Record("entry_seen", sample: false);
Check("counter-only stages do not retain samples", diagnostics.Counts["entry_seen"] == 1 && diagnostics.Samples.Length == 4);
var frozen = diagnostics.Counts;
diagnostics.Record("read_failed");
Check("exported counter snapshot is independent of later mutations", frozen["read_failed"] == 10000);
var temp = Path.Join(Path.GetTempPath(), "wisps-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(temp);
    var pluginRoot = Path.Join(temp, "Plugins");
    var oldPlugin = new DirectoryInfo(Path.Join(pluginRoot, "WhereTheWispsAt"));
    var newPlugin = Path.Join(pluginRoot, "ShowMeWisp");
    Directory.CreateDirectory(Path.Join(oldPlugin.FullName, "config"));
    var oldSettingsPath = Path.Join(oldPlugin.FullName, "config", "settings.txt");
    File.WriteAllText(oldSettingsPath, "legacy settings");
    Check("renamed plugin can read its legacy settings", WispSettingsFile.FindForRead(newPlugin) == oldSettingsPath);
    Check("legacy plugin is retained when renamed DLL is absent", !GameHelper.Plugin.PluginRenames.IsSuperseded(oldPlugin));
    Directory.CreateDirectory(Path.Join(newPlugin, "config"));
    File.WriteAllText(Path.Join(newPlugin, "ShowMeWisp.dll"), "discovery test placeholder");
    Check("legacy plugin is skipped when renamed DLL is installed", GameHelper.Plugin.PluginRenames.IsSuperseded(oldPlugin));
    Check("renamed plugin is not skipped", !GameHelper.Plugin.PluginRenames.IsSuperseded(new DirectoryInfo(newPlugin)));
    var newSettingsPath = Path.Join(newPlugin, "config", "settings.txt");
    File.WriteAllText(newSettingsPath, "current settings");
    Check("existing renamed settings take precedence over legacy settings", WispSettingsFile.FindForRead(newPlugin) == newSettingsPath && File.ReadAllText(oldSettingsPath) == "legacy settings");
    var oldMetadata = new GameHelper.Plugin.PluginMetadata { Enable = false };
    var savedMetadata = new Dictionary<string, GameHelper.Plugin.PluginMetadata>(StringComparer.OrdinalIgnoreCase) { ["WhereTheWispsAt"] = oldMetadata };
    var migratedMetadata = GameHelper.Plugin.PluginRenames.InitialMetadata("ShowMeWisp", savedMetadata);
    Check("rename preserves a disabled plugin", !migratedMetadata.Enable);
    oldMetadata.Enable = true;
    Check("rename copies metadata without sharing mutable state", !migratedMetadata.Enable && GameHelper.Plugin.PluginRenames.InitialMetadata("ShowMeWisp", savedMetadata).Enable);
    Check("unrelated plugins keep their normal default", GameHelper.Plugin.PluginRenames.InitialMetadata("Radar", savedMetadata).Enable);
    using var capture = new WispDebugCapture(Path.Join(temp, "capture"), 1024, 3);
    Check("capture starts without Windows or a game", capture.Start(100));
    capture.Write("nonfinite", new { Position = new Vector2(float.NaN, float.PositiveInfinity) });
    var firstLines = ReadActiveCapture(capture.CurrentFile).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    using (var json = System.Text.Json.JsonDocument.Parse(firstLines.Last()))
        Check("nonfinite coordinates remain valid JSON diagnostic values", json.RootElement.GetProperty("Data").GetProperty("Position").GetProperty("X").GetString() == "NaN");
    Check("capture stays active before its deadline", !capture.Expire(60099) && capture.Active);
    for (var i = 0; i < 100; i++) capture.Write("test", new { Index = i, Text = new string('x', 150) });
    Check("capture rotates and bounds all files", Directory.GetFiles(Path.Join(temp, "capture")).Length == 3 && Directory.GetFiles(Path.Join(temp, "capture")).All(x => new FileInfo(x).Length <= 1024));
    capture.Write("huge", new { Text = new string('x', 3000) });
    Check("oversized record is replaced by explicit omission evidence", ReadActiveCapture(capture.CurrentFile).Contains("record_omitted"));
    Check("60-second deadline stops capture", capture.Expire(60100) && !capture.Active);
    var validLines = 0;
    foreach (var file in Directory.GetFiles(Path.Join(temp, "capture")))
    foreach (var line in File.ReadLines(file))
    {
        using var json = System.Text.Json.JsonDocument.Parse(line);
        if (json.RootElement.TryGetProperty("Session", out _) && json.RootElement.TryGetProperty("Utc", out _)) validLines++;
    }
    Check("rotated output contains complete timestamped JSON lines", validLines > 2);
    capture.Write("after_stop", new { });
    Check("stopped writer remains stopped", !capture.Active && !capture.Expire(70000));
    Check("a new session can start after completion", capture.Start(80000));
    capture.Stop("test_complete");
    var blocked = Path.Join(temp, "file-not-directory");
    File.WriteAllText(blocked, "occupied");
    using var failed = new WispDebugCapture(blocked);
    Check("I/O failures are reported without crashing the caller", !failed.Start(0) && !failed.Active && failed.Error.Length > 0);
}
finally { Directory.Delete(temp, true); }
WispApiSnapshot Snapshot(string name, string area = "area-a") => new() { Source = name, AreaHash = area, AreaAddress = 100, Generation = 1 };
WispApiEntity ApiItem(uint id, long? address = null, string metadata = resource) => new()
{
    Identity = new(id, address ?? id + 0x10000L, metadata), IsValid = true,
    ReadStatus = "observation_ready", Kind = WispKind.Blue, AnimatedPath = "wisp_primal",
    World = Vector3.Zero, Grid = Vector2.Zero, TerrainHeight = 0, Bounds = Vector3.One,
    Observation = Item(id),
};
void Put(WispApiSnapshot snapshot, WispApiEntity item) => snapshot.Entities[item.Identity] = item;
var publicApi = Snapshot("public_lookup");
var freshApi = Snapshot("fresh_entity_scan");
Check("zero API targets cannot be counted as verified equality", WispApiComparer.Compare(publicApi, freshApi).EqualObservations == 0);
Put(publicApi, ApiItem(1)); Put(freshApi, ApiItem(1)); Put(freshApi, ApiItem(2));
var difference = WispApiComparer.Compare(publicApi, freshApi);
Check("API coverage comparison identifies entities missing from public collection", difference.Common == 1 && difference.OnlyRight == 1 && difference.EqualObservations == 1);
var reused = Snapshot("reused_id"); Put(reused, ApiItem(1, address: 99999));
var reuseDifference = WispApiComparer.Compare(publicApi, reused);
Check("API comparison does not match reused IDs at different addresses", reuseDifference.Common == 0 && reuseDifference.OnlyLeft == 1 && reuseDifference.OnlyRight == 1);
var renamed = Snapshot("changed_metadata"); Put(renamed, ApiItem(1, metadata: "other"));
Check("API comparison includes metadata in entity identity", WispApiComparer.Compare(publicApi, renamed).Common == 0);
var stale = Snapshot("stale_component");
var staleItem = ApiItem(1); staleItem.AnimatedPath = ""; staleItem.Kind = WispKind.UnknownResource; Put(stale, staleItem);
var staleDifference = WispApiComparer.Compare(stale, publicApi);
Check("API comparison preserves stale path and classification evidence", staleDifference.FieldDifferences["animated_path"] == 1 && staleDifference.FieldDifferences["kind"] == 1 && staleDifference.ValueSamples[0].Left!.AnimatedPath == "");
staleItem.Size = WispSize.Big;
Check("API comparison records wisp size differences", WispApiComparer.Compare(stale, publicApi).FieldDifferences.ContainsKey("wisp_size"));
staleItem.ActivatedValue = 1; staleItem.States = ["activated=1"];
Check("API comparison reports raw activation changes", WispApiComparer.Compare(stale, publicApi).FieldDifferences.ContainsKey("activated_value"));
var missingRender = ApiItem(1); missingRender.Observation = null; missingRender.ReadStatus = "render_missing";
var unusableA = Snapshot("unusable_a"); var unusableB = Snapshot("unusable_b"); Put(unusableA, missingRender); Put(unusableB, missingRender);
var unusableComparison = WispApiComparer.Compare(unusableA, unusableB);
Check("matching failures are never counted as usable API equivalence", unusableComparison.NeitherUsable == 1 && unusableComparison.EqualObservations == 0);
Check("API comparison distinguishes one-sided observation failures", WispApiComparer.Compare(unusableA, publicApi).OnlyRightUsable == 1);
var moving = ApiItem(1); moving.World = new Vector3(0.1f, 0, 0); var movingSnapshot = Snapshot("moving"); Put(movingSnapshot, moving);
Check("small sequential-read position jitter uses explicit tolerance", WispApiComparer.Compare(publicApi, movingSnapshot).EqualObservations == 1);
moving.World = new Vector3(float.NaN, 0, 0);
Check("nonfinite API coordinates cannot be declared equivalent", WispApiComparer.Compare(movingSnapshot, movingSnapshot).EqualObservations == 0);
Check("cross-area API samples are marked incomplete", !WispApiComparer.Compare(publicApi, Snapshot("other_area", "area-b")).CompleteWindow);
var bounded = Snapshot("bounded"); var reservedCount = 0;
Parallel.For(0, 5000, _ => { if (bounded.Reserve()) System.Threading.Interlocked.Increment(ref reservedCount); });
Check("parallel API sampling has a strict entity budget", reservedCount == 2000 && bounded.Truncated);
Check("truncated API reads are marked incomplete", !WispApiComparer.Compare(publicApi, bounded).CompleteWindow);
var many = Snapshot("many"); for (uint id = 100; id < 200; id++) Put(many, ApiItem(id));
Check("API discrepancy samples stay bounded while counts stay complete", WispApiComparer.Compare(publicApi, many).MissingSamples.Count == 8 && WispApiComparer.Compare(publicApi, many).OnlyRight == 100);
var serializedApi = System.Text.Json.JsonSerializer.Serialize(many, WispDebugCapture.JsonOptions);
using (var apiJson = System.Text.Json.JsonDocument.Parse(serializedApi))
    Check("API export omits the full entity dictionary but keeps bounded samples", !apiJson.RootElement.TryGetProperty("Entities", out _) && apiJson.RootElement.GetProperty("Samples").GetArrayLength() == 8);
Console.WriteLine($"{passed} regression checks passed.");
