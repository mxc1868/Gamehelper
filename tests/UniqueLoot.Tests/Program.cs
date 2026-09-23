using UniqueLoot;

var passed = 0;
void Check(string name, bool value)
{
    if (!value) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
bool Rejects(string json)
{
    try { UniqueArtCatalog.Parse(json); return false; }
    catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { return true; }
}

var catalog = UniqueArtCatalog.Parse("""
{
  "Art/2DItems/Rings/Shared.dds": ["Alpha", "Beta", " Alpha "],
  "art/2ditems/rings/shared.dds": ["Gamma", "Beta"],
  "Art/2DItems/Amulets/Shared.dds": ["Different item"],
  "Art/2DItems/Belts/Solo.dds": ["One item"]
}
""");
Check("same full path merges candidates without guessing", catalog.Resolve("Art/2DItems/Rings/Shared.dds").Candidates.SequenceEqual(new[] { "Alpha", "Beta", "Gamma" }));
Check("shared art is explicitly ambiguous", catalog.Resolve("Art/2DItems/Rings/Shared.dds").Kind == ArtMatchKind.Multiple);
Check("single art returns a single candidate", catalog.Resolve("Art/2DItems/Belts/Solo.dds").Kind == ArtMatchKind.Single);
Check("path matching tolerates slash and casing", catalog.Resolve(@" art\2ditems\belts\solo.DDS ").Candidates.Single() == "One item");
Check("same basename in another directory cannot collide", catalog.Resolve("Art/2DItems/Amulets/Shared.dds").Candidates.Single() == "Different item");
Check("no basename fallback", catalog.Resolve("Shared.dds").Kind == ArtMatchKind.Unknown);
Check("new art stays unknown", catalog.Resolve("Art/2DItems/New/Solo.dds").Kind == ArtMatchKind.Unknown);
Check("no fuzzy matching", catalog.Resolve("Art/2DItems/Belts/TheSolo.dds").Kind == ArtMatchKind.Unknown);
Check("missing art has a distinct reason", catalog.Resolve(null).Kind == ArtMatchKind.MissingArt && catalog.Resolve(" ").Kind == ArtMatchKind.MissingArt);
Check("empty catalog is safe", UniqueArtCatalog.Empty.Resolve("Art/2DItems/Belts/Solo.dds").Kind == ArtMatchKind.Unknown);

var overrides = UniqueArtCatalog.Parse("""
{ "Art/2DItems/Belts/Solo.dds": ["中文名称"], "Art/2DItems/Rings/Shared.dds": [] }
""");
var merged = catalog.WithOverrides(overrides);
Check("custom names can localize one asset", merged.Resolve("Art/2DItems/Belts/Solo.dds").Candidates.Single() == "中文名称");
Check("explicit empty override disables an obsolete mapping", merged.Resolve("Art/2DItems/Rings/Shared.dds").Kind == ArtMatchKind.Unknown);
Check("partial override retains other assets", merged.Resolve("Art/2DItems/Amulets/Shared.dds").Kind == ArtMatchKind.Single);
Check("overrides do not mutate original catalog", catalog.Resolve("Art/2DItems/Belts/Solo.dds").Candidates.Single() == "One item");
Check("malformed JSON rejected", Rejects("{"));
Check("null catalog rejected", Rejects("null"));
Check("basename-only data rejected", Rejects("""{"Solo.dds":["Item"]}"""));
Check("null list rejected", Rejects("""{"Art/2DItems/Belts/Solo.dds":null}"""));
Check("blank name rejected", Rejects("""{"Art/2DItems/Belts/Solo.dds":[" "]}"""));
Check("control characters rejected", Rejects("""{"Art/2DItems/Belts/Solo.dds":["Item\nFake alert"]}"""));

var bundled = UniqueArtCatalog.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "uniqueArtMapping.json")));
Check("pinned catalog has 446 full paths", bundled.Count == 446);
Check("PoE2 Bramblejack maps to its actual art directory", bundled.Resolve("Art/2DItems/Armours/BodyArmours/Uniques/Bramblejack.dds").Candidates.Single() == "Bramblejack");
Check("PoE1 Bramblejack path is not silently imported", bundled.Resolve("Art/2DItems/Armours/BodyArmours/Bramblejack.dds").Kind == ArtMatchKind.Unknown);
Check("real shared art preserves renamed variants", bundled.Resolve("Art/2DItems/Weapons/OneHandWeapons/Scepters/Uniques/GuidingPalmCold.dds").Candidates.SequenceEqual(new[] { "Guiding Palm", "Guiding Palm of the Eye" }));
var settings = new UniqueLootSettings { ScanIntervalMs = -1, MaxLabels = 999, ListX = -20, ListY = int.MaxValue, GroundOffsetY = -999 };
settings.Normalize();
Check("invalid config cannot cause unbounded drawing or tight polling", settings.ScanIntervalMs == 200 && settings.MaxLabels == 100 && settings.ListX == 0 && settings.ListY == 10000 && settings.GroundOffsetY == -300);
var highlightJson = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "highlights.default.json"));
var highlights = UniqueHighlights.Parse(highlightJson);
const string hhArt = "Art/2DItems/Belts/Uniques/Headhunter.dds";
const string mbArt = "Art/2DItems/Belts/Uniques/Mageblood.dds";
Check("priority highlights enabled for existing settings without new field", new UniqueLootSettings().HighlightPriorityDrops);
Check("default config highlights both requested belts", highlights.Count == 2 && highlights.Match(hhArt)?.Name == "Headhunter" && highlights.Match(mbArt)?.Name == "Mageblood");
Check("priority paths agree with bundled PoE2 data", bundled.Resolve(hhArt).Candidates.Single() == "Headhunter" && bundled.Resolve(mbArt).Candidates.Single() == "Mageblood");
Check("gold config is converted to ImGui ABGR", highlights.Match(hhArt)?.TextColor == 0xFF00D7FF);
Check("magenta config and font size are applied", highlights.Match(mbArt)?.TextColor == 0xFFFF70FF && highlights.Match(mbArt)?.FontScale == 1.3f);
Check("background preserves configured opacity", highlights.Match(hhArt)?.BackgroundColor == 0xEE002633);
Check("base metadata cannot highlight every belt", highlights.Match("Metadata/Items/Belts/BeltHeavy") == null);
Check("same filename in a different art directory is not highlighted", highlights.Match("Art/2DItems/Belts/Mageblood.dds") == null);
Check("highlight path normalization preserves exact asset identity", highlights.Match(@"art\2ditems\belts\uniques\headhunter.DDS") != null);
Check("other unique belts retain ordinary display", highlights.Match("Art/2DItems/Belts/Uniques/MeginordsGirdle.dds") == null);
Check("empty rules intentionally disable all highlights", UniqueHighlights.Parse("""{"Rules":[]}""").Count == 0);
var disabledJson = highlightJson.Replace("\"Enabled\": true", "\"Enabled\": false");
Check("per-rule disabling is honored", UniqueHighlights.Parse(disabledJson).Count == 0);
bool RejectsHighlights(string json)
{
    try { UniqueHighlights.Parse(json); return false; }
    catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { return true; }
}
Check("invalid color config is rejected instead of silently changing colors", RejectsHighlights(highlightJson.Replace("#FFD700", "orange")));
Check("unbounded text scale is rejected", RejectsHighlights(highlightJson.Replace("1.3", "100")));
Check("missing rule list is not treated as deliberate removal", RejectsHighlights("{}"));
var choices = HighlightChoices.Build(bundled, highlights);
Check("selection list covers every catalog asset", choices.SelectMany(x => x.AssetPaths).Distinct().Count() == bundled.Count);
var hhChoice = choices.Single(x => x.Name == "Headhunter");
Check("existing highlight rules initialize checked items", HighlightChoices.IsSelected(hhChoice, highlights));
var unselected = highlights.WithSelection(hhChoice.Name, hhChoice.AssetPaths, false);
Check("unchecking removes a highlight immediately", !HighlightChoices.IsSelected(hhChoice, unselected) && highlights.Match(hhArt) != null);
var restored = UniqueHighlights.Parse(unselected.ToJson()).WithSelection(hhChoice.Name, hhChoice.AssetPaths, true);
Check("save/reload/recheck preserves custom colors and scales", restored.Match(hhArt) == highlights.Match(hhArt));
var all = UniqueHighlights.Empty;
foreach (var choice in choices) all = all.WithSelection(choice.Name, choice.AssetPaths, true);
Check("all 446 art paths can be selected beyond old 100-rule limit", all.Count == bundled.Count && UniqueHighlights.Parse(all.ToJson()).Count == bundled.Count);
foreach (var choice in choices) all = all.WithSelection(choice.Name, choice.AssetPaths, false);
Check("clearing all choices remains empty after reload", UniqueHighlights.Parse(all.ToJson()).Count == 0);
var variants = UniqueArtCatalog.Parse("""{"Art/2DItems/Rings/One.dds":["Same unique"],"Art/2DItems/Rings/Two.dds":["Same unique"]}""");
var variantChoice = HighlightChoices.Build(variants, UniqueHighlights.Empty).Single();
Check("one item checkbox groups alternate asset paths", variantChoice.AssetPaths.Count == 2 && UniqueHighlights.Empty.WithSelection(variantChoice.Name, variantChoice.AssetPaths, true).Count == 2);
Check("ambiguous shared art keeps all candidate names in the checkbox", choices.Any(x => x.Name == "Guiding Palm / Guiding Palm of the Eye"));
Check("custom rules outside the catalog remain editable", HighlightChoices.Build(UniqueArtCatalog.Empty, highlights).Length == 2);
var displaySettings = new UniqueLootSettings();
Check("old highlight fonts are raised to the visible 1.6x default", HighlightChoices.DisplayStyle(hhArt, highlights, displaySettings)?.FontScale == 1.6f);
Check("unselected assets cannot enter the highlighted drop list", HighlightChoices.DisplayStyle("Art/2DItems/Rings/One.dds", highlights, displaySettings) == null);
displaySettings.HighlightPriorityDrops = false;
Check("master switch suppresses highlighted list eligibility", HighlightChoices.DisplayStyle(hhArt, highlights, displaySettings) == null);
displaySettings.HighlightFontScale = float.NaN;
displaySettings.Normalize();
Check("invalid highlight font size falls back to visible default", displaySettings.HighlightFontScale == 1.6f);
Console.WriteLine($"{passed} offline checks passed; memory reads and Windows rendering are NOT tested.");
