using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using System.Drawing;
using System.Drawing.Imaging;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class PortableVisualLib43Tests
{
    private static string TempLibrary()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-lib43-" + Guid.NewGuid().ToString("N"), "Library");
        Directory.CreateDirectory(Path.Combine(p, "Maps"));
        Directory.CreateDirectory(Path.Combine(p, "Images", "grounds"));
        return p;
    }

    private static string WriteTempJpeg()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-lib43-" + Guid.NewGuid().ToString("N") + ".jpg");
        using var bmp = new Bitmap(40, 20, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.FromArgb(20, 120, 200));
        bmp.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static string WriteTempPngWithAlpha()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-lib43-" + Guid.NewGuid().ToString("N") + ".png");
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.OrangeRed, 4, 4, 24, 24);
        }

        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Relative_path_is_portable_by_gfx()
    {
        Assert.Equal("Visuals/Mobs/1607.png", PortableVisualStore.GetRelativePath(VisualAssetCategory.Mobs, 1607));
        Assert.Equal("Visuals/Items/196.png", PortableVisualStore.GetRelativePath(VisualAssetCategory.Items, 196));
    }

    [Fact]
    public void Import_jpg_saves_png_by_gfx_not_mob()
    {
        var lib = TempLibrary();
        var store = new PortableVisualStore();
        store.ConfigureLibraryRoot(lib);
        var jpg = WriteTempJpeg();
        try
        {
            store.ImportFromFile(VisualAssetCategory.Mobs, 1607, jpg);
            var path = store.GetPngPath(VisualAssetCategory.Mobs, 1607)!;
            Assert.True(File.Exists(path));
            Assert.EndsWith(Path.Combine("Visuals", "Mobs", "1607.png"), path, StringComparison.OrdinalIgnoreCase);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);

            // Same gfx shared — single file
            Assert.True(store.Exists(VisualAssetCategory.Mobs, 1607));
            Assert.False(store.Exists(VisualAssetCategory.Mobs, 1056)); // different gfx, no file
        }
        finally
        {
            File.Delete(jpg);
        }
    }

    [Fact]
    public async Task Manual_png_has_priority_over_failed_swf_cache()
    {
        var lib = TempLibrary();
        var svc = new ArtworkPreviewService();
        svc.Configure(clipsRoot: null, libraryRoot: lib);
        svc.Cache.MarkFailed(1563);

        var png = WriteTempPngWithAlpha();
        try
        {
            svc.ImportManualMobVisual(1563, png);
            Assert.True(svc.HasManualVisual(1563));

            var result = await svc.GetOrCreatePngAsync(1563);
            Assert.NotNull(result);
            Assert.True(result!.Length > 50);

            var changed = 0;
            svc.ManualVisualChanged += _ => changed++;
            Assert.True(svc.DeleteManualMobVisual(1563));
            Assert.Equal(1, changed);
            Assert.False(svc.HasManualVisual(1563));
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Fact]
    public void Unsupported_extension_rejected()
    {
        Assert.False(VisualImageNormalizer.IsSupportedExtension("x.bmp"));
        Assert.False(VisualImageNormalizer.IsSupportedExtension("x.gif"));
        Assert.True(VisualImageNormalizer.IsSupportedExtension("x.PNG"));
        Assert.True(VisualImageNormalizer.IsSupportedExtension("x.jpeg"));
    }

    [Fact]
    public void Items_category_directory_prepared()
    {
        var lib = TempLibrary();
        var store = new PortableVisualStore();
        store.ConfigureLibraryRoot(lib);
        Assert.True(Directory.Exists(store.GetCategoryDirectory(VisualAssetCategory.Items)));
        Assert.True(Directory.Exists(store.GetCategoryDirectory(VisualAssetCategory.Mobs)));
    }

    [Fact]
    public void Normalize_preserves_aspect_ratio()
    {
        var jpg = WriteTempJpeg(); // 40x20
        try
        {
            var bytes = VisualImageNormalizer.NormalizeToPng(jpg, maxEdge: 100);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            Assert.Equal(2.0, img.Width / (double)img.Height, precision: 1);
            Assert.True(img.Width <= 100);
            Assert.True(img.Height <= 100);
        }
        finally
        {
            File.Delete(jpg);
        }
    }

    [Fact]
    public void Import_file_api_does_not_require_ui_and_notify_is_separate()
    {
        var lib = TempLibrary();
        var svc = new ArtworkPreviewService();
        svc.Configure(clipsRoot: null, libraryRoot: lib);
        var notified = 0;
        svc.ManualVisualChanged += _ => notified++;

        var png = WriteTempPngWithAlpha();
        try
        {
            svc.ImportManualMobVisualFile(1563, png);
            Assert.Equal(0, notified); // no event from file-only API
            Assert.True(svc.HasManualVisual(1563));

            svc.NotifyManualVisualChanged(1563);
            Assert.Equal(1, notified);
        }
        finally
        {
            File.Delete(png);
        }
    }
}
