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
Console.WriteLine($"{passed} offline checks passed; memory reads and Windows rendering are NOT tested.");
