namespace UniqueLoot;

using System.Text.Json;

public enum ArtMatchKind { MissingArt, Unknown, Single, Multiple }

public sealed record ArtMatch(ArtMatchKind Kind, string AssetPath, IReadOnlyList<string> Candidates);

/// <summary>Exact full-path lookup, independent of prices, identification and base-item names.</summary>
public sealed class UniqueArtCatalog
{
    private readonly Dictionary<string, string[]> entries;
    private UniqueArtCatalog(Dictionary<string, string[]> entries) => this.entries = entries;
    public int Count => this.entries.Count;
    public static UniqueArtCatalog Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));
    public IEnumerable<ArtMatch> KnownItems => this.entries.Where(x => x.Value.Length > 0).Select(x => this.Resolve(x.Key));

    public static string NormalizePath(string? path) => (path ?? string.Empty).Trim().Replace('\\', '/');

    public static UniqueArtCatalog Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
            ?? throw new FormatException("The art mapping must be a JSON object.");
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (raw.Count > 20000) throw new FormatException("Too many art mappings.");
        foreach (var (path, names) in raw)
        {
            var key = NormalizePath(path);
            if (!key.StartsWith("Art/2DItems/", StringComparison.OrdinalIgnoreCase) ||
                !key.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || key.Length > 1024 ||
                key.Contains("/../", StringComparison.Ordinal) || names == null || names.Length > 100)
                throw new FormatException("Invalid full art path or candidate list: " + key);
            if (!merged.TryGetValue(key, out var set)) merged[key] = set = new(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl))
                    throw new FormatException("Invalid unique name for " + key);
                set.Add(name.Trim());
            }
        }
        return new(merged.ToDictionary(x => x.Key, x => x.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    // An explicit empty list suppresses an obsolete bundled mapping; other keys remain intact.
    public UniqueArtCatalog WithOverrides(UniqueArtCatalog custom)
    {
        var result = new Dictionary<string, string[]>(this.entries, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in custom.entries) result[entry.Key] = entry.Value;
        return new(result);
    }

    public ArtMatch Resolve(string? path)
    {
        var key = NormalizePath(path);
        if (key.Length == 0) return new(ArtMatchKind.MissingArt, key, Array.Empty<string>());
        if (!this.entries.TryGetValue(key, out var names) || names.Length == 0)
            return new(ArtMatchKind.Unknown, key, Array.Empty<string>());
        return new(names.Length == 1 ? ArtMatchKind.Single : ArtMatchKind.Multiple, key, Array.AsReadOnly(names));
    }
}
