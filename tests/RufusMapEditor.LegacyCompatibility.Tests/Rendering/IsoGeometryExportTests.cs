using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

public sealed class IsoGeometryExportTests
{
    [Fact]
    public void Standard_15x17_full_and_crop()
    {
        var (fullW, fullH) = IsoGeometry.FullCanvasSize(15, 17);
        Assert.Equal(780, fullW);
        Assert.Equal(442, fullH);

        var crop = IsoGeometry.ExportCrop(15, 17);
        Assert.Equal(26, crop.X);
        Assert.Equal(13, crop.Y);
        Assert.Equal(728, crop.Width);
        Assert.Equal(416, crop.Height);

        var (exportW, exportH) = IsoGeometry.ExportImageSize(15, 17);
        Assert.Equal(728, exportW);
        Assert.Equal(416, exportH);
    }

    [Fact]
    public void Custom_19x22_full_and_crop()
    {
        var (fullW, fullH) = IsoGeometry.FullCanvasSize(19, 22);
        Assert.Equal(988, fullW);
        Assert.Equal(572, fullH);

        var crop = IsoGeometry.ExportCrop(19, 22);
        Assert.Equal(26, crop.X);
        Assert.Equal(13, crop.Y);
        Assert.Equal(936, crop.Width);
        Assert.Equal(546, crop.Height);
    }
}
