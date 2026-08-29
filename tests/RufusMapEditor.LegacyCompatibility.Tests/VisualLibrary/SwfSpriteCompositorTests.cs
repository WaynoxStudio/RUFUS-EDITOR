using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class SwfSpriteCompositorTests
{
    [Fact]
    public void Matrix_multiply_applies_translation()
    {
        var m = new SwfMatrix { Tx = 10, Ty = 20 };
        var p = m.Transform(5, 5);
        Assert.Equal(15, p.X, 1);
        Assert.Equal(25, p.Y, 1);
    }

    [Fact]
    public void Matrix_multiply_composes_scale()
    {
        var a = new SwfMatrix { A = 2, D = 2 };
        var b = new SwfMatrix { Tx = 1, Ty = 1 };
        var c = a.Multiply(b);
        var p = c.Transform(3, 4);
        Assert.Equal(8, p.X, 1);
        Assert.Equal(10, p.Y, 1);
    }

    [Fact]
    public void ColorTransform_identity_preserves_color()
    {
        var cx = SwfColorTransform.Identity;
        var c = cx.Apply(System.Drawing.Color.FromArgb(200, 10, 20, 30));
        Assert.Equal(200, c.A);
        Assert.Equal(10, c.R);
    }

    [Fact]
    public void ColorTransform_multiply_and_add()
    {
        var cx = new SwfColorTransform { MulG = 0.5f, AddG = 10 };
        var c = cx.Apply(System.Drawing.Color.FromArgb(255, 100, 50, 0));
        Assert.Equal(100, c.R);
        Assert.Equal(35, c.G);
    }

    [Fact]
    public void Rasterize_rejects_too_short_swf()
    {
        Assert.ThrowsAny<Exception>(() => SwfMovieParser.Parse([1, 2, 3]));
    }

    [Fact]
    public void Recursion_guard_limits_nested_compose()
    {
        Assert.True(SwfSpriteLimits.MaxRecursionDepth >= 16);
    }

    [Fact]
    public void Cache_clear_invalidates_versioned_folder()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-spr-" + Guid.NewGuid().ToString("N"), "Library");
        var cache = new SpritePreviewCache();
        cache.ConfigureLibraryRoot(lib);
        var path = cache.GetCachedPngPath(71)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0]);
        Assert.Equal(1, cache.ClearCache());
        Assert.False(File.Exists(path));
    }
}
