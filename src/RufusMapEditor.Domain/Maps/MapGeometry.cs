namespace RufusMapEditor.Domain.Maps;

/// <summary>
/// Classical isométric map dimensions and cell-count formula used by Astria / DOFUS Retro.
/// </summary>
public static class MapGeometry
{
    /// <summary>
    /// VB.NET <c>Dim Cells(N)</c> allocates indices 0..N (length N+1).
    /// Astria uses <c>N = Height * (Width * 2 - 1) - Width</c>.
    /// </summary>
    public static int CellCount(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return height * (width * 2 - 1) - width + 1;
    }

    public static int ExpectedMapDataLength(int width, int height) => CellCount(width, height) * MapDataConstants.CharsPerCell;
}

public static class MapDataConstants
{
    public const int CharsPerCell = 10;
}
