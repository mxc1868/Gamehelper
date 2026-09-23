namespace UniqueLoot;

using System.Globalization;
using System.Text.Json;

public sealed record HighlightStyle(string Name, string AssetPath, uint TextColor, uint BackgroundColor, uint BorderColor, float FontScale);

/// <summary>Full asset paths only: a shared base-item metadata path cannot identify a unique.</summary>
public sealed class UniqueHighlights
{
    private sealed class Config
    {
        public List<Rule>? Rules { get; set; }
    }

    private sealed class Rule
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string AssetPath { get; set; } = string.Empty;
        public string TextColor { get; set; } = "#FFFFFF";
        public string BackgroundColor { get; set; } = "#000000EE";
        public string BorderColor { get; set; } = "#FFFFFF";
        public float FontScale { get; set; } = 1.3f;
    }

    private readonly Dictionary<string, HighlightStyle> rules;
    private UniqueHighlights(Dictionary<string, HighlightStyle> rules) => this.rules = rules;
    public static UniqueHighlights Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));
    public int Count => this.rules.Count;
    public HighlightStyle? Match(string? asset) => this.rules.GetValueOrDefault(UniqueArtCatalog.NormalizePath(asset));

    public static UniqueHighlights Parse(string json)
    {
        var config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (config?.Rules == null || config.Rules.Count > 100)
            throw new FormatException("Highlights must contain a Rules array with at most 100 entries.");
        var result = new Dictionary<string, HighlightStyle>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in config.Rules)
        {
            if (rule == null) throw new FormatException("Highlight rules cannot be null.");
            var asset = UniqueArtCatalog.NormalizePath(rule.AssetPath);
            if (!asset.StartsWith("Art/2DItems/", StringComparison.OrdinalIgnoreCase) ||
                !asset.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || asset.Length > 1024 ||
                asset.Contains("/../", StringComparison.Ordinal) || !seen.Add(asset))
                throw new FormatException("Invalid or duplicate highlight asset path: " + asset);
            if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 200 || rule.Name.Any(char.IsControl) ||
                !float.IsFinite(rule.FontScale) || rule.FontScale < 1 || rule.FontScale > 2)
                throw new FormatException("Invalid highlight name or font scale: " + asset);
            var style = new HighlightStyle(rule.Name.Trim(), asset, ParseColor(rule.TextColor),
                ParseColor(rule.BackgroundColor), ParseColor(rule.BorderColor), rule.FontScale);
            if (rule.Enabled) result.Add(asset, style);
        }
        return new(result);
    }

    private static uint ParseColor(string? hex)
    {
        if (hex == null || hex.Length is not (7 or 9) || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgba))
            throw new FormatException("Highlight colors must be #RRGGBB or #RRGGBBAA.");
        if (hex.Length == 7) rgba = (rgba << 8) | 255;
        // ImGui's packed color is ABGR, whereas the config uses familiar RGBA hex.
        return (rgba >> 24) | ((rgba >> 8) & 0xFF00) | ((rgba << 8) & 0xFF0000) | (rgba << 24);
    }
}
