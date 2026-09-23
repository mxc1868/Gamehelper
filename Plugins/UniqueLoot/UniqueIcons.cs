namespace UniqueLoot;

using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using GameHelper;

internal sealed class UniqueIcons
{
    internal readonly record struct Icon(IntPtr Texture, Vector2 Size);
    private readonly Dictionary<string, Icon?> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loaded = new(StringComparer.OrdinalIgnoreCase);

    internal Icon? Find(string pluginDirectory, string asset)
    {
        var key = UniqueArtCatalog.NormalizePath(asset);
        if (this.cache.TryGetValue(key, out var cached)) return cached;
        if (this.cache.Count >= 1024 || key.Length == 0) return null;
        var file = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant()))).ToLowerInvariant() + ".webp";
        var path = Path.Join(pluginDirectory, "Icons", file);
        Icon? result = null;
        try
        {
            if (File.Exists(path))
            {
                Core.Overlay.AddOrGetImagePointer(path, false, out var texture, out var width, out var height);
                if (texture != IntPtr.Zero && width > 0 && height > 0)
                {
                    result = new(texture, new(width, height));
                    this.loaded.Add(path);
                }
            }
        }
        catch { /* Optional artwork must never suppress the item name. */ }
        this.cache[key] = result;
        return result;
    }

    internal void Clear()
    {
        foreach (var path in this.loaded)
        {
            try { Core.Overlay.RemoveImage(path); }
            catch { /* Overlay teardown may already have released the texture. */ }
        }
        this.loaded.Clear();
        this.cache.Clear();
    }
}
