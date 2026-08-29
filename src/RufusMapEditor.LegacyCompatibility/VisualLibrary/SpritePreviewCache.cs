using System.Collections.Concurrent;
using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// Versioned PNG cache for NPC sprite thumbnails under
/// <c>Library/cache/sprites/v{CacheVersion}/</c>.
/// </summary>
public sealed class SpritePreviewCache
{
    public const string CacheFolderName = "cache";
    public const string SpritesFolderName = "sprites";

    private readonly ConcurrentDictionary<int, byte> _negative = new();
    private string? _cacheRoot;

    public string? CacheRoot => _cacheRoot;

    public void ConfigureLibraryRoot(string? libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            _cacheRoot = null;
            return;
        }

        _cacheRoot = Path.Combine(
            Path.GetFullPath(libraryRoot),
            CacheFolderName,
            SpritesFolderName,
            "v" + SwfSpriteThumbnailRenderer.CacheVersion.ToString(CultureInfo.InvariantCulture));
    }

    public void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_cacheRoot))
            return;
        if (RufusLibraryPaths.TryResolveEffectiveLibrary(out _) is { } lib)
            ConfigureLibraryRoot(lib);
    }

    public string? GetCachedPngPath(int gfxId)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_cacheRoot))
            return null;
        return Path.Combine(_cacheRoot, gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
    }

    public bool TryReadCachedPng(int gfxId, out byte[]? png)
    {
        png = null;
        var path = GetCachedPngPath(gfxId);
        if (path is null || !File.Exists(path))
            return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
                return false;
            png = bytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void WriteCachedPng(int gfxId, byte[] png)
    {
        var path = GetCachedPngPath(gfxId);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
        _negative.TryRemove(gfxId, out _);
    }

    public void MarkFailed(int gfxId) => _negative[gfxId] = 1;
    public bool WasFailed(int gfxId) => _negative.ContainsKey(gfxId);
    public void ClearFailed(int gfxId) => _negative.TryRemove(gfxId, out _);

    public int ClearCache()
    {
        EnsureConfigured();
        _negative.Clear();
        if (string.IsNullOrWhiteSpace(_cacheRoot) || !Directory.Exists(_cacheRoot))
            return 0;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(_cacheRoot, "*.png"))
        {
            try
            {
                File.Delete(f);
                n++;
            }
            catch
            {
                // ignore locked files
            }
        }

        return n;
    }
}
