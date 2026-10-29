using System.Drawing;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Swf.Hd;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Swf;

public sealed class SwfHdPipelineTests
{
    [Fact]
    public void Layout_1750_keeps_source_pixels_and_logical_bounds()
    {
        var layout = GfxHdLayout.Create(1484, 864, 742, 432);
        Assert.Equal(14840, layout.BoundsTwipsWidth);
        Assert.Equal(8640, layout.BoundsTwipsHeight);
        Assert.Equal(10, layout.MatrixScaleX, 6);
        Assert.Equal(10, layout.MatrixScaleY, 6);
        Assert.True(layout.IsHd);
    }

    [Fact]
    public void Layout_1717_uses_non_integer_scale_without_resizing_source()
    {
        var layout = GfxHdLayout.Create(1657, 949, 728, 416);
        Assert.Equal(14560, layout.BoundsTwipsWidth);
        Assert.Equal(8320, layout.BoundsTwipsHeight);
        Assert.Equal(14560 / 1657.0, layout.MatrixScaleX, 6);
        Assert.Equal(8320 / 949.0, layout.MatrixScaleY, 6);
    }

    [Fact]
    public void Encode_embeds_source_pixels_not_logical_size()
    {
        using var src = NewOpaque(1657, 949, Color.Red);
        var layout = GfxHdLayout.Create(1657, 949, 728, 416);
        var (swf, check) = GfxHdImportService.EncodeStandaloneSwf(src, layout, "1717");
        Assert.True(check.CanPublish, check.Summary);
        Assert.Equal(1657, check.EncodedBitmapWidth);
        Assert.Equal(949, check.EncodedBitmapHeight);
        Assert.Equal(14560, check.EncodedBoundsTwipsWidth);
        Assert.Equal(8320, check.EncodedBoundsTwipsHeight);
        Assert.False(check.WouldPhysicallyDownsample);
        Assert.True(swf.Length > 100);
        _ = swf;
    }

    [Fact]
    public void Validator_blocks_physical_downsample()
    {
        var layout = GfxHdLayout.Create(1657, 949, 728, 416);
        var check = SwfHdPublishValidator.Evaluate(layout, encodedBitmapWidth: 728, encodedBitmapHeight: 416);
        Assert.False(check.CanPublish);
        Assert.True(check.WouldPhysicallyDownsample);
    }

    [Fact]
    public void Validator_blocks_thumbnail_replacing_hd_source()
    {
        var layout = GfxHdLayout.Create(352, 648, 44, 81);
        var check = SwfHdPublishValidator.Evaluate(layout, 44, 81, thumbnailWidth: 44, thumbnailHeight: 81);
        Assert.True(check.ThumbnailWouldReplaceSource);
        Assert.False(check.CanPublish);
    }

    [Fact]
    public void Boomerang_sequence_matches_approved_5042_order()
    {
        var seq = SwfHdAnimationBuilder.BuildSequence(6, boomerang: true);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 4, 3, 2, 1 }, seq);
    }

    [Fact]
    public void Bitmap_shape_initial_style_change_selects_fill_without_new_styles()
    {
        var layout = GfxHdLayout.Create(352, 648, 44, 81, 22, 73);
        var encoded = SwfHdShapeEncoder.EncodeBitmapFillShape(120, 119, layout);

        // Long SWF tag header (2-byte record + 4-byte payload length), then
        // DefineShape3 character id.
        var reader = new SwfBitReader(encoded, 6 + 2);
        _ = reader.ReadRect();
        Assert.Equal(1, reader.ReadUi8()); // fill count
        Assert.Equal(0x41, reader.ReadUi8()); // clipped bitmap fill
        Assert.Equal(119, reader.ReadUi16());
        reader.ReadMatrix();
        Assert.Equal(0, reader.ReadUi8()); // line style count
        Assert.Equal(1, reader.ReadUb(4)); // NumFillBits
        Assert.Equal(0, reader.ReadUb(4)); // NumLineBits
        Assert.Equal(0, reader.ReadUb(1)); // non-edge StyleChangeRecord
        Assert.Equal(0b00011, reader.ReadUb(5)); // FillStyle0 + MoveTo
    }

    [Fact]
    public void Placement_uses_logical_size_not_hd_pixels()
    {
        var resource = new GfxResource
        {
            Id = 5057,
            Category = GfxCategory.Object,
            FilePath = "5057.png",
            Folder = "Torre",
            Extension = ".png",
            PixelWidth = 352,
            PixelHeight = 648,
            LogicalWidth = 44,
            LogicalHeight = 81,
        };
        using var hd = NewOpaque(352, 648, Color.Blue);
        var cell = IsoGeometry.BuildCellCorners(15, 17)[228];
        var dest = GfxPlacementMath.CalculateDrawPlacement(
            cell, resource.PlacementWidth, resource.PlacementHeight, 22, 80, false, 0, true);
        Assert.Equal(44, dest.Width);
        Assert.Equal(81, dest.Height);
        Assert.True(GfxPlacementPipeline.TryBuildFromBitmap(15, 17, 228, resource, hd, false, 0, true, out var d));
        Assert.Equal(44, d.DrawWidth);
        Assert.Equal(81, d.DrawHeight);
        Assert.Equal(44, d.NativeWidth);
        Assert.Equal(81, d.NativeHeight);
    }

    [Fact]
    public void Sidecar_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rufus-hd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "5057.png");
            using (var bmp = NewOpaque(352, 648, Color.Green))
                bmp.Save(png);
            var layout = GfxHdLayout.Create(352, 648, 44, 81, 22, 80);
            GfxHdLayout.SaveSidecar(png, layout);
            Assert.True(GfxHdLayout.TryLoadSidecar(png, out var loaded));
            Assert.Equal(44, loaded.LogicalWidth);
            Assert.Equal(81, loaded.LogicalHeight);
            Assert.Equal(352, loaded.SourcePixelWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static Bitmap NewOpaque(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }
}
