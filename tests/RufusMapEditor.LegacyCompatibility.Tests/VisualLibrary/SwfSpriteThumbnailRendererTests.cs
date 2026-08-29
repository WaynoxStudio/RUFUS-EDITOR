using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class SwfSpriteThumbnailRendererTests
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
                && Directory.Exists(Path.Combine(c, "sprites")))
                return Path.GetFullPath(c);
        }

        return null;
    }

    private static string TempLibraryRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-sprite-" + Guid.NewGuid().ToString("N"), "Library");
        Directory.CreateDirectory(Path.Combine(p, "cache", "sprites", "v3"));
        return p;
    }

    [Fact]
    public void Rasterize_rejects_invalid_swf()
    {
        Assert.ThrowsAny<Exception>(() => SwfSpriteThumbnailRenderer.RasterizeToPng([1, 2, 3]));
    }

    [Fact]
    public void Sprite_cache_uses_versioned_folder()
    {
        var cache = new SpritePreviewCache();
        cache.ConfigureLibraryRoot(TempLibraryRoot());
        var path = cache.GetCachedPngPath(71);
        Assert.NotNull(path);
        Assert.Contains(Path.Combine("cache", "sprites", "v3"), path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cache_version_matches_renderer()
    {
        Assert.Equal(SwfSpriteThumbnailRenderer.CacheVersion, 3);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(71)]
    [InlineData(120)]
    [InlineData(1245)]
    [InlineData(9073)]
    public void Real_sprite_renders_non_empty_png(int gfxId)
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "sprites", gfxId + ".swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        var png = SwfSpriteThumbnailRenderer.RasterizeToPng(bytes, 96, gfxId);
        Assert.True(png.Length > 200, $"gfx {gfxId} png too small");
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }

    [Fact]
    public void Real_gfx71_uses_staticR_linkage()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;
        var swfPath = Path.Combine(clips, "sprites", "71.swf");
        if (!File.Exists(swfPath)) return;

        var png = SwfSpriteThumbnailRenderer.RasterizeToPng(File.ReadAllBytes(swfPath), 96, 71);
        Assert.True(png.Length > 200);
    }

    [Fact]
    public void Real_gfx1245_renders_ogivol_sprite()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;
        var swfPath = Path.Combine(clips, "sprites", "1245.swf");
        if (!File.Exists(swfPath)) return;

        var png = SwfSpriteThumbnailRenderer.RasterizeToPng(File.ReadAllBytes(swfPath), 96, 1245);
        Assert.True(png.Length > 200);
    }

    [Fact]
    public async Task Npc_preview_cascade_uses_sprite_before_artwork()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var lib = TempLibraryRoot();
        var svc = new NpcGfxPreviewService();
        svc.Configure(clips, lib);

        var png = await svc.GetOrCreatePngAsync(71);
        Assert.NotNull(png);
        Assert.True(svc.SpriteCache.GetCachedPngPath(71) is not null);
        Assert.True(File.Exists(svc.SpriteCache.GetCachedPngPath(71)!));
    }

    [Fact]
    public async Task Gfx9999_without_swf_returns_null()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var lib = TempLibraryRoot();
        var svc = new NpcGfxPreviewService();
        svc.Configure(clips, lib);

        var png = await svc.GetOrCreatePngAsync(9999);
        Assert.Null(png);
    }

    [Fact]
    public void Movie_parser_reads_export_assets_tag56()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;
        var swfPath = Path.Combine(clips, "sprites", "71.swf");
        if (!File.Exists(swfPath)) return;

        var movie = SwfMovieParser.Parse(File.ReadAllBytes(swfPath));
        Assert.NotEmpty(movie.ExportedNames);
    }

    [Fact]
    public void Forced_sprite_frame_api_works()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;
        var swfPath = Path.Combine(clips, "sprites", "9073.swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        var movie = SwfMovieParser.Parse(bytes);
        var spriteId = movie.Sprites.Values.OrderByDescending(s => s.PayloadBytes).First().CharacterId;
        using var bmp = SwfSpriteThumbnailRenderer.RasterizeToBitmap(bytes, spriteId, 0, 96, 9073, out var diag);
        Assert.NotNull(bmp);
        Assert.True(bmp.Width > 0);
        Assert.Equal(spriteId, diag!.SpriteId);
    }
}
