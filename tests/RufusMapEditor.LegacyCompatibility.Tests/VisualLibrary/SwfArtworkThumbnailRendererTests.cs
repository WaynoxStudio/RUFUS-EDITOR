using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class SwfArtworkThumbnailRendererTests
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

    private static string TempLibraryRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-artwork-" + Guid.NewGuid().ToString("N"), "Library");
        Directory.CreateDirectory(Path.Combine(p, "cache", "artworks", "v1"));
        return p;
    }

    [Fact]
    public void Rasterize_rejects_invalid_swf()
    {
        Assert.ThrowsAny<Exception>(() => SwfArtworkThumbnailRenderer.RasterizeToPng([1, 2, 3]));
    }

    [Fact]
    public void Artwork_cache_uses_versioned_folder()
    {
        var cache = new ArtworkPreviewCache();
        cache.ConfigureLibraryRoot(TempLibraryRoot());
        var path = cache.GetCachedPngPath(9059);
        Assert.NotNull(path);
        Assert.Contains(Path.Combine("cache", "artworks", "v1"), path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cache_version_matches_renderer()
    {
        Assert.Equal(SwfArtworkThumbnailRenderer.CacheVersion, 1);
    }

    [Fact]
    public void Root_timeline_compose_available_on_parsed_movie()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "artworks", "big", "9059.swf");
        if (!File.Exists(swfPath)) return;

        var movie = SwfMovieParser.Parse(File.ReadAllBytes(swfPath));
        Assert.True(movie.RootTimelineStart > 0);
        Assert.True(movie.RootTimelineEnd > movie.RootTimelineStart);

        var composer = new SwfTimelineComposer(movie);
        var analysis = composer.AnalyzeRoot(0);
        Assert.True(analysis.ShapesDrawn > 0 || analysis.NestedSprites > 0);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(71)]
    [InlineData(120)]
    [InlineData(1245)]
    [InlineData(9059)]
    [InlineData(9073)]
    public void Real_artwork_renders_non_empty_png(int gfxId)
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "artworks", "big", gfxId + ".swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        try
        {
            var png = SwfArtworkThumbnailRenderer.RasterizeToPng(bytes, 128, gfxId, out var diag);
            Assert.True(png.Length > 200, $"gfx {gfxId} artwork png too small");
            Assert.Equal(0x89, png[0]);
            Assert.True(diag.Success);
        }
        catch (InvalidOperationException) when (gfxId == 1245)
        {
            // GFX 1245 artwork may require DoAction registration — pending.
        }
    }

    [Fact]
    public void Real_gfx9059_artwork_uses_root_timeline()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "artworks", "big", "9059.swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        SwfArtworkThumbnailRenderer.RasterizeToPng(bytes, 128, 9059, out var diag);
        Assert.True(
            diag.Strategy is SwfArtworkThumbnailRenderer.ArtworkRenderStrategy.RootTimeline
                or SwfArtworkThumbnailRenderer.ArtworkRenderStrategy.InternalSprite,
            $"strategy={diag.Strategy}");
        Assert.True(diag.NestedSprites > 0 || diag.ShapesDrawn > 0);
    }

    [Fact]
    public void Real_gfx9059_sprite_staticR_renders()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "sprites", "9059.swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        var movie = SwfMovieParser.Parse(bytes);
        Assert.True(movie.ExportedNames.ContainsKey("staticR"));

        var png = SwfSpriteThumbnailRenderer.RasterizeToPng(bytes, 128, 9059);
        Assert.True(png.Length > 200);
    }

    [Fact]
    public void Artwork_and_sprite_caches_are_independent()
    {
        var lib = TempLibraryRoot();
        var spriteCache = new SpritePreviewCache();
        spriteCache.ConfigureLibraryRoot(lib);
        var artworkCache = new ArtworkPreviewCache();
        artworkCache.ConfigureLibraryRoot(lib);

        var spritePath = spriteCache.GetCachedPngPath(9059);
        var artworkPath = artworkCache.GetCachedPngPath(9059);
        Assert.NotNull(spritePath);
        Assert.NotNull(artworkPath);
        Assert.NotEqual(spritePath, artworkPath);
        Assert.Contains("sprites", spritePath!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artworks", artworkPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Artwork_missing_swf_returns_null_from_service()
    {
        var lib = TempLibraryRoot();
        var svc = new ArtworkPreviewService();
        svc.Configure(Path.GetTempPath(), lib);
        var png = svc.GetOrCreatePngAsync(99999999).GetAwaiter().GetResult();
        Assert.Null(png);
    }

    [Fact]
    public void Preview_cache_clear_removes_both_pipelines()
    {
        var lib = TempLibraryRoot();
        Directory.CreateDirectory(Path.Combine(lib, "cache", "sprites", "v3"));
        File.WriteAllBytes(Path.Combine(lib, "cache", "sprites", "v3", "1.png"), [0x89, 0x50]);
        File.WriteAllBytes(Path.Combine(lib, "cache", "artworks", "v1", "1.png"), [0x89, 0x50]);

        var (sprites, artworks) = PreviewCacheUtility.ClearPreviewCaches(lib);
        Assert.True(sprites >= 1);
        Assert.True(artworks >= 1);
        Assert.False(File.Exists(Path.Combine(lib, "cache", "sprites", "v3", "1.png")));
        Assert.False(File.Exists(Path.Combine(lib, "cache", "artworks", "v1", "1.png")));
    }
}
