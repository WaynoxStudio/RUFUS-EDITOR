using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.Rendering;

/// <summary>
/// Classical DOFUS / Astria isometric cell geometry and export canvas sizing.
/// Ported from <c>MapEditor.GenerateGrid</c> / <c>Save_Img</c> / <c>RogneImage</c>.
/// </summary>
public static class IsoGeometry
{
    /// <summary>Astria <c>MapEditor.SizeBaseCell</c> — half-cell size in pixels at export scale.</summary>
    public const int SizeBaseCell = 26;

    public readonly struct CellCorners
    {
        public required Point A { get; init; }
        public required Point B { get; init; }
        public required Point C { get; init; }
        public required Point D { get; init; }
    }

    public readonly struct Point
    {
        public Point(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    public static (int Width, int Height) FullCanvasSize(int mapWidth, int mapHeight, int sizeCell = SizeBaseCell) =>
        (mapWidth * sizeCell * 2, mapHeight * sizeCell);

    /// <summary>
    /// Crop used by Astria <c>Save_Img</c>. For 15×17 @ 26 → 728×416.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ExportCrop(int mapWidth, int mapHeight, int sizeCell = SizeBaseCell)
    {
        var (fullW, fullH) = FullCanvasSize(mapWidth, mapHeight, sizeCell);
        return (sizeCell, sizeCell / 2, fullW - sizeCell * 2, fullH - sizeCell);
    }

    public static (int Width, int Height) ExportImageSize(int mapWidth, int mapHeight, int sizeCell = SizeBaseCell)
    {
        var crop = ExportCrop(mapWidth, mapHeight, sizeCell);
        return (crop.Width, crop.Height);
    }

    /// <summary>Diamond center in the same coordinate space as <see cref="BuildCellCorners"/>.</summary>
    public static (double X, double Y) GetCellCenter(CellCorners corners) =>
        ((corners.A.X + corners.C.X) / 2.0, (corners.A.Y + corners.C.Y) / 2.0);

    public static CellCorners[] BuildCellCorners(int mapWidth, int mapHeight, int sizeCell = SizeBaseCell)
    {
        var cellCount = MapGeometry.CellCount(mapWidth, mapHeight);
        var cells = new CellCorners[cellCount];

        for (var n = 0; n < mapHeight; n++)
        {
            for (var i = 0; i <= mapWidth; i++)
            {
                var ecartHeight = n * sizeCell;
                var ecartWidth = i * sizeCell * 2;
                var id = i + (n * mapWidth * 2) - n;
                if (id < 0 || id >= cellCount)
                    continue;
                cells[id] = MakeDiamond(sizeCell, ecartWidth, ecartHeight);
            }
        }

        for (var n = 0; n <= mapHeight - 2; n++)
        {
            for (var i = 0; i <= mapWidth - 2; i++)
            {
                var ecartHeight = (n * sizeCell) + (sizeCell / 2);
                var ecartWidth = (i * sizeCell * 2) + sizeCell;
                var id = i + (n * (mapWidth * 2) + mapWidth) - n;
                if (id < 0 || id >= cellCount)
                    continue;
                cells[id] = MakeDiamond(sizeCell, ecartWidth, ecartHeight);
            }
        }

        return cells;
    }

    private static CellCorners MakeDiamond(int sizeCell, int ecartWidth, int ecartHeight) =>
        new()
        {
            A = new Point(sizeCell + ecartWidth, 0 + ecartHeight),
            B = new Point(sizeCell * 2 + ecartWidth, sizeCell / 2 + ecartHeight),
            C = new Point(sizeCell + ecartWidth, sizeCell + ecartHeight),
            D = new Point(0 + ecartWidth, sizeCell / 2 + ecartHeight),
        };
}
