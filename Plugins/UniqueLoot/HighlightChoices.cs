namespace UniqueLoot;

public sealed record HighlightChoice(string Name, IReadOnlyList<string> AssetPaths);

public static class HighlightChoices
{
    public static HighlightChoice[] Build(UniqueArtCatalog catalog, UniqueHighlights highlights)
    {
        var known = catalog.KnownItems.ToDictionary(x => x.AssetPath, x => string.Join(" / ", x.Candidates), StringComparer.OrdinalIgnoreCase);
        foreach (var item in highlights.ConfiguredItems) known.TryAdd(item.AssetPath, item.Name);
        return known.GroupBy(x => x.Value, StringComparer.Ordinal)
            .Select(group => new HighlightChoice(group.Key, group.Select(x => x.Key).ToArray()))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsSelected(HighlightChoice choice, UniqueHighlights highlights) => choice.AssetPaths.Any(x => highlights.Match(x) != null);

    public static HighlightStyle? DisplayStyle(string asset, UniqueHighlights highlights, UniqueLootSettings settings)
    {
        var style = settings.HighlightPriorityDrops ? highlights.Match(asset) : null;
        return style == null ? null : style with { FontScale = Math.Max(settings.HighlightFontScale, style.FontScale) };
    }
}
