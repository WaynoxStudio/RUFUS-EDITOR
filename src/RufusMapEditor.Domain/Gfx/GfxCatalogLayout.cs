namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// Responsive column layout for the GFX thumbnail catalog (virtualized rows).
/// Tile outer width matches MainWindow.xaml: Border 72 + Margin 3×2 = 78.
/// </summary>
public static class GfxCatalogLayout
{
    public const double TileOuterWidth = 78;
    public const double ScrollbarReserve = 18;
    public const int MinColumns = 3;
    public const int MaxColumns = 64;
    public const int DefaultColumns = 8;

    public static int ComputeColumns(double panelWidth) =>
        ComputeColumns(panelWidth, TileOuterWidth);

    public static int ComputeColumns(double panelWidth, double tileOuterWidth)
    {
        if (panelWidth <= 0 || double.IsNaN(panelWidth))
            return DefaultColumns;

        var tile = tileOuterWidth > 0 ? tileOuterWidth : TileOuterWidth;
        var usable = panelWidth - ScrollbarReserve;
        if (usable < tile)
            return MinColumns;

        return Math.Clamp((int)Math.Floor(usable / tile), MinColumns, MaxColumns);
    }
}
