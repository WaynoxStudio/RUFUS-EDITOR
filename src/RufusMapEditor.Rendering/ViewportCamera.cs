namespace RufusMapEditor.Rendering;

/// <summary>
/// Pure viewport camera: maps content (map image) space ↔ viewport (control) space.
/// Zoom/pan only — never re-renders the map.
/// </summary>
public sealed class ViewportCamera
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8.0;

    /// <summary>Scale factor (1.0 = 100%).</summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>Offset of content origin relative to viewport origin, in viewport pixels.</summary>
    public double OffsetX { get; private set; }

    public double OffsetY { get; private set; }

    public double ContentWidth { get; private set; }
    public double ContentHeight { get; private set; }
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }

    public void SetContentSize(double width, double height)
    {
        ContentWidth = Math.Max(0, width);
        ContentHeight = Math.Max(0, height);
    }

    public void SetViewportSize(double width, double height)
    {
        ViewportWidth = Math.Max(0, width);
        ViewportHeight = Math.Max(0, height);
    }

    public void SetZoom(double zoom)
    {
        Zoom = ClampZoom(zoom);
    }

    public void ResetPan()
    {
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetPan(double offsetX, double offsetY)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>
    /// Zoom centered on a viewport point (typically cursor position).
    /// </summary>
    public void ZoomAt(double viewportX, double viewportY, double newZoom)
    {
        newZoom = ClampZoom(newZoom);
        if (Math.Abs(newZoom - Zoom) < 1e-12)
            return;

        var contentX = (viewportX - OffsetX) / Zoom;
        var contentY = (viewportY - OffsetY) / Zoom;
        Zoom = newZoom;
        OffsetX = viewportX - contentX * Zoom;
        OffsetY = viewportY - contentY * Zoom;
    }

    public void ZoomByFactorAt(double viewportX, double viewportY, double factor)
    {
        ZoomAt(viewportX, viewportY, Zoom * factor);
    }

    /// <summary>Fit entire content into the viewport (letterbox).</summary>
    public void FitToViewport(double padding = 8)
    {
        if (ContentWidth <= 0 || ContentHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
            return;

        var availW = Math.Max(1, ViewportWidth - padding * 2);
        var availH = Math.Max(1, ViewportHeight - padding * 2);
        var zx = availW / ContentWidth;
        var zy = availH / ContentHeight;
        Zoom = ClampZoom(Math.Min(zx, zy));
        OffsetX = (ViewportWidth - ContentWidth * Zoom) / 2.0;
        OffsetY = (ViewportHeight - ContentHeight * Zoom) / 2.0;
    }

    public void SetActualSizeCentered()
    {
        Zoom = 1.0;
        OffsetX = (ViewportWidth - ContentWidth * Zoom) / 2.0;
        OffsetY = (ViewportHeight - ContentHeight * Zoom) / 2.0;
    }

    public (double X, double Y) ViewportToContent(double viewportX, double viewportY) =>
        ((viewportX - OffsetX) / Zoom, (viewportY - OffsetY) / Zoom);

    public (double X, double Y) ContentToViewport(double contentX, double contentY) =>
        (contentX * Zoom + OffsetX, contentY * Zoom + OffsetY);

    public static double ClampZoom(double zoom) =>
        Math.Clamp(zoom, MinZoom, MaxZoom);

    public int ZoomPercent => (int)Math.Round(Zoom * 100.0);
}
