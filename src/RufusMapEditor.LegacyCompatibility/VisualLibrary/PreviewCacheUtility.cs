using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public enum NpcGfxPreviewSource
{
    None = 0,
    CacheSprite = 1,
    SpriteRenderer = 2,
    CacheArtwork = 3,
    ArtworkFallback = 4,
    ManualMobVisual = 5,
    Placeholder = 6,
}

public sealed class NpcGfxPreviewResolveInfo
{
    public int GfxId { get; set; }
    public NpcGfxPreviewSource Source { get; set; }
    public string? LibraryRoot { get; set; }
    public string? CachePath { get; set; }
    public string? Renderer { get; set; }
    public int? SpriteId { get; set; }
    public int? FrameIndex { get; set; }
    public string? SelectionReason { get; set; }
    public bool UsedArtworkFallback { get; set; }
}

public static class PreviewCacheUtility
{
    public sealed class PreviewCachePaths
    {
        public string? LibraryRoot { get; init; }
        public LibrarySource LibrarySource { get; init; }
        public string? SpriteCacheRoot { get; init; }
        public string? ArtworkCacheRoot { get; init; }
    }

    public static PreviewCachePaths ResolvePaths(string? configuredLibraryRoot = null)
    {
        string? lib = null;
        var source = LibrarySource.None;
        if (string.IsNullOrWhiteSpace(configuredLibraryRoot) || !Directory.Exists(configuredLibraryRoot))
        {
            if (RufusLibraryPaths.TryResolveEffectiveLibrary(out var resolvedSource) is { } resolved)
            {
                lib = resolved;
                source = resolvedSource;
            }
        }
        else
        {
            lib = Path.GetFullPath(configuredLibraryRoot);
            source = LibrarySource.UserSettings;
        }

        if (lib is null)
            return new PreviewCachePaths { LibrarySource = LibrarySource.None };

        return new PreviewCachePaths
        {
            LibraryRoot = lib,
            LibrarySource = source,
            SpriteCacheRoot = Path.Combine(lib, SpritePreviewCache.CacheFolderName, SpritePreviewCache.SpritesFolderName,
                "v" + SwfSpriteThumbnailRenderer.CacheVersion),
            ArtworkCacheRoot = Path.Combine(
                lib,
                ArtworkPreviewCache.CacheFolderName,
                ArtworkPreviewCache.ArtworksFolderName,
                "v" + SwfArtworkThumbnailRenderer.CacheVersion.ToString(CultureInfo.InvariantCulture)),
        };
    }

    public static (int SpriteFiles, int ArtworkFiles) ClearPreviewCaches(string? configuredLibraryRoot = null)
    {
        var paths = ResolvePaths(configuredLibraryRoot);
        var sprite = 0;
        var art = 0;

        var spritesRoot = string.IsNullOrWhiteSpace(paths.LibraryRoot)
            ? null
            : Path.Combine(paths.LibraryRoot, SpritePreviewCache.CacheFolderName, SpritePreviewCache.SpritesFolderName);
        if (!string.IsNullOrWhiteSpace(spritesRoot) && Directory.Exists(spritesRoot))
        {
            foreach (var versionDir in Directory.EnumerateDirectories(spritesRoot))
            {
                foreach (var f in Directory.EnumerateFiles(versionDir, "*.png"))
                {
                    try { File.Delete(f); sprite++; } catch { }
                }
            }

            foreach (var f in Directory.EnumerateFiles(spritesRoot, "*.png"))
            {
                try { File.Delete(f); sprite++; } catch { }
            }
        }
        else if (!string.IsNullOrWhiteSpace(paths.SpriteCacheRoot) && Directory.Exists(paths.SpriteCacheRoot))
        {
            foreach (var f in Directory.EnumerateFiles(paths.SpriteCacheRoot, "*.png"))
            {
                try { File.Delete(f); sprite++; } catch { }
            }
        }

        if (!string.IsNullOrWhiteSpace(paths.LibraryRoot))
        {
            var artworksRoot = Path.Combine(
                paths.LibraryRoot,
                ArtworkPreviewCache.CacheFolderName,
                ArtworkPreviewCache.ArtworksFolderName);
            if (Directory.Exists(artworksRoot))
            {
                foreach (var versionDir in Directory.EnumerateDirectories(artworksRoot))
                {
                    foreach (var f in Directory.EnumerateFiles(versionDir, "*.png"))
                    {
                        try { File.Delete(f); art++; } catch { }
                    }
                }

                foreach (var f in Directory.EnumerateFiles(artworksRoot, "*.png"))
                {
                    try { File.Delete(f); art++; } catch { }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(paths.ArtworkCacheRoot) && Directory.Exists(paths.ArtworkCacheRoot))
        {
            foreach (var f in Directory.EnumerateFiles(paths.ArtworkCacheRoot, "*.png"))
            {
                try { File.Delete(f); art++; } catch { }
            }
        }

        ArtworkPreviewService.Shared.Cache.ClearCache();
        NpcGfxPreviewService.Shared.SpriteCache.ClearCache();
        return (sprite, art);
    }
}
