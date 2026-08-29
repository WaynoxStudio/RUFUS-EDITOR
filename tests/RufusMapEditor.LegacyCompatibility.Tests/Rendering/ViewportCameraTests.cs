using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rendering;

public sealed class ViewportCameraTests
{
    [Fact]
    public void ClampZoom_rejects_zero_and_huge_values()
    {
        Assert.Equal(ViewportCamera.MinZoom, ViewportCamera.ClampZoom(0));
        Assert.Equal(ViewportCamera.MinZoom, ViewportCamera.ClampZoom(-1));
        Assert.Equal(ViewportCamera.MaxZoom, ViewportCamera.ClampZoom(1000));
    }

    [Fact]
    public void ZoomAt_keeps_content_point_under_cursor()
    {
        var cam = new ViewportCamera();
        cam.SetContentSize(728, 416);
        cam.SetViewportSize(800, 600);
        cam.SetZoom(1.0);
        cam.SetPan(10, 20);

        const double vx = 400;
        const double vy = 300;
        var (cx, cy) = cam.ViewportToContent(vx, vy);
        cam.ZoomAt(vx, vy, 2.0);
        var (cx2, cy2) = cam.ViewportToContent(vx, vy);

        Assert.Equal(2.0, cam.Zoom);
        Assert.InRange(cx2, cx - 0.01, cx + 0.01);
        Assert.InRange(cy2, cy - 0.01, cy + 0.01);
    }

    [Fact]
    public void FitToViewport_fits_entire_content()
    {
        var cam = new ViewportCamera();
        cam.SetContentSize(728, 416);
        cam.SetViewportSize(364, 208);
        cam.FitToViewport(padding: 0);

        Assert.InRange(cam.Zoom, 0.49, 0.51);
        var (vx, vy) = cam.ContentToViewport(728, 416);
        Assert.InRange(vx, 363, 365);
        Assert.InRange(vy, 207, 209);
    }

    [Fact]
    public void PanBy_does_not_change_zoom()
    {
        var cam = new ViewportCamera();
        cam.SetZoom(1.5);
        cam.PanBy(40, -20);
        Assert.Equal(1.5, cam.Zoom);
        Assert.Equal(40, cam.OffsetX);
        Assert.Equal(-20, cam.OffsetY);
    }
}
