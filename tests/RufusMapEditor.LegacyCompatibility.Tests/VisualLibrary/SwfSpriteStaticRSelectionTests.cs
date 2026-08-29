using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class SwfSpriteStaticRSelectionTests
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

    [Fact]
    public void Primary_linkage_is_staticR()
    {
        Assert.Equal("staticR", SwfSpriteSelection.PrimaryNpcPreviewLinkage);
    }

    [Fact]
    public void Client_fallback_starts_with_staticR_then_staticF()
    {
        Assert.Equal("staticR", SwfSpriteSelection.ClientStaticFallbackOrder[0]);
        Assert.Equal("staticF", SwfSpriteSelection.ClientStaticFallbackOrder[1]);
    }

    [Fact]
    public void Exact_lookup_does_not_match_emoteStatic21R()
    {
        var movie = new SwfMovie { Body = Array.Empty<byte>() };
        movie.ExportedNames["emoteStatic21R"] = 99;
        movie.Sprites[99] = new SwfSpriteDefinition
        {
            CharacterId = 99,
            FrameCount = 1,
            TagBuffer = Array.Empty<byte>(),
            TagStart = 0,
            TagEnd = 500,
        };

        Assert.False(SwfSpriteSelection.TryResolveExactExport(movie, "staticR", out _));
        Assert.False(SwfSpriteSelection.TryPickExactExport(movie, "staticR", out _));
    }

    [Fact]
    public void Exact_lookup_finds_staticR_by_name()
    {
        var movie = new SwfMovie { Body = Array.Empty<byte>() };
        movie.ExportedNames["staticR"] = 42;
        movie.Sprites[42] = new SwfSpriteDefinition
        {
            CharacterId = 42,
            FrameCount = 1,
            TagBuffer = Array.Empty<byte>(),
            TagStart = 0,
            TagEnd = 17,
        };

        Assert.True(SwfSpriteSelection.TryResolveExactExport(movie, "staticR", out var id));
        Assert.Equal(42, id);
        Assert.True(SwfSpriteSelection.TryPickExactExport(movie, "staticR", out var pick));
        Assert.Equal(42, pick.SpriteId);
        Assert.Equal("staticR", pick.LinkageName);
        Assert.Contains("wrapper", pick.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectThumbnail_prefers_staticR_over_heuristic()
    {
        var movie = new SwfMovie { Body = Array.Empty<byte>() };
        movie.ExportedNames["staticR"] = 10;
        movie.Sprites[10] = new SwfSpriteDefinition
        {
            CharacterId = 10,
            FrameCount = 1,
            TagBuffer = Array.Empty<byte>(),
            TagStart = 0,
            TagEnd = 16,
        };
        movie.Sprites[653] = new SwfSpriteDefinition
        {
            CharacterId = 653,
            FrameCount = 99,
            TagBuffer = Array.Empty<byte>(),
            TagStart = 0,
            TagEnd = 39225,
        };

        var pick = SwfSpriteSelection.SelectThumbnail(movie);
        Assert.Equal(10, pick.SpriteId);
        Assert.Equal("staticR", pick.LinkageName);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(71)]
    [InlineData(120)]
    [InlineData(1245)]
    [InlineData(9073)]
    public void Real_gfx_staticR_export_resolves(int gfxId)
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swf = Path.Combine(clips, "sprites", gfxId + ".swf");
        if (!File.Exists(swf)) return;

        var movie = SwfMovieParser.Parse(File.ReadAllBytes(swf));
        Assert.True(SwfSpriteSelection.TryResolveExactExport(movie, "staticR", out var charId),
            $"GFX {gfxId} missing staticR export");
        Assert.True(movie.Sprites.ContainsKey(charId));

        var pick = SwfSpriteSelection.SelectThumbnail(movie);
        Assert.Equal("staticR", pick.LinkageName);
        Assert.Equal(charId, pick.SpriteId);
    }

    [Theory]
    [InlineData(71)]
    [InlineData(1245)]
    public void Real_gfx_renderer_uses_staticR_not_walk_cycle(int gfxId)
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "sprites", gfxId + ".swf");
        if (!File.Exists(swfPath)) return;

        var bytes = File.ReadAllBytes(swfPath);
        using var bmp = SwfSpriteThumbnailRenderer.RasterizeToBitmap(bytes, 96, gfxId, out var diag);
        Assert.NotNull(diag);
        Assert.Contains("staticR", diag!.SelectionReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("walk-cycle", diag.SelectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nested_wrapper_staticR_composes_when_clips_present()
    {
        var clips = ResolveClipsRoot();
        if (clips is null) return;

        var swfPath = Path.Combine(clips, "sprites", "71.swf");
        if (!File.Exists(swfPath)) return;

        var movie = SwfMovieParser.Parse(File.ReadAllBytes(swfPath));
        Assert.True(SwfSpriteSelection.TryPickExactExport(movie, "staticR", out var pick));

        var composer = new SwfTimelineComposer(movie);
        using var bmp = composer.ComposeSprite(pick.SpriteId, pick.FrameIndex);
        Assert.NotNull(bmp);
        var analysis = composer.Analyze(pick.SpriteId, pick.FrameIndex);
        Assert.True(analysis.NestedSprites > 0 || analysis.ShapesDrawn > 0,
            "staticR should compose via nested wrappers or shapes");
    }

    [Fact]
    public void Cache_version_is_v3()
    {
        Assert.Equal(3, SwfSpriteThumbnailRenderer.CacheVersion);
    }
}
