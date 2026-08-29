using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class ArtworkPreviewLib42Tests
{
    private static string? ResolveClipsRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "RUFUS RETRO", "resources", "app", "retroclient", "clips"),
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c)
                && Directory.Exists(Path.Combine(c, "artworks", "big")))
                return Path.GetFullPath(c);
        }

        return null;
    }

    private static string TempCacheRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-lib42-" + Guid.NewGuid().ToString("N"), "Library");
        Directory.CreateDirectory(Path.Combine(p, "cache", "artworks"));
        // Satisfy TryFindRepoMasterLibrary? We configure cache directly via library root.
        Directory.CreateDirectory(Path.Combine(p, "Maps"));
        Directory.CreateDirectory(Path.Combine(p, "Images", "grounds"));
        return p;
    }

    [Fact]
    public void Rasterizer_rejects_invalid_swf()
    {
        Assert.ThrowsAny<Exception>(() => SwfArtworkRasterizer.RasterizeToPng([1, 2, 3, 4]));
    }

    [Fact]
    public void Cache_key_is_gfx_not_mob()
    {
        var lib = TempCacheRoot();
        var cache = new ArtworkPreviewCache();
        cache.ConfigureLibraryRoot(lib);
        var a = cache.GetCachedPngPath(1607);
        var b = cache.GetCachedPngPath(1607);
        Assert.Equal(a, b);
        Assert.EndsWith(Path.Combine("cache", "artworks", "1607.png"), a!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_artworks_rasterize_and_cache_when_clips_present()
    {
        var clips = ResolveClipsRoot();
        if (clips is null)
        {
            // Environment without RUFUS RETRO clips — skip silently for CI.
            return;
        }

        var lib = TempCacheRoot();
        var svc = new ArtworkPreviewService();
        svc.Configure(clips, lib);
        Assert.Contains("✓", svc.ClipsStatus);

        foreach (var gfx in new[] { 1563, 1568, 1566, 1156, 1607 })
        {
            var png = await svc.GetOrCreatePngAsync(gfx);
            Assert.NotNull(png);
            Assert.True(png!.Length > 100, $"gfx {gfx} png too small");
            Assert.Equal(0x89, png[0]);
            Assert.Equal((byte)'P', png[1]);

            var cachedPath = svc.Cache.GetCachedPngPath(gfx)!;
            Assert.True(File.Exists(cachedPath));

            // Second call hits cache (same bytes).
            var png2 = await svc.GetOrCreatePngAsync(gfx);
            Assert.Equal(png.Length, png2!.Length);
        }

        // Shared gfxID reused by multiple mobs — single cache file.
        Assert.True(File.Exists(svc.Cache.GetCachedPngPath(1607)!));

        // Missing gfx
        var missing = await svc.GetOrCreatePngAsync(999999);
        Assert.Null(missing);

        var cleared = svc.Cache.ClearCache();
        Assert.True(cleared >= 5);
        Assert.False(File.Exists(svc.Cache.GetCachedPngPath(1563)!));

        // Regeneration after clear
        var again = await svc.GetOrCreatePngAsync(1563);
        Assert.NotNull(again);
    }
}
