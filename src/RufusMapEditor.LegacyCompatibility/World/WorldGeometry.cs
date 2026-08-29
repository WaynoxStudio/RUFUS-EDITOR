using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;

namespace RufusMapEditor.LegacyCompatibility.World;

/// <summary>
/// Single source of truth: world grid cell → map preview rectangle in world pixel space.
/// </summary>
public static class WorldGeometry
{
    public const int InfoGapPixels = 6;
    private const int SizeBaseCell = 26;

    /// <summary>Reference map size for empty grid slot previews (classic 15×17).</summary>
    public const int DefaultSlotMapWidth = 15;
    public const int DefaultSlotMapHeight = 17;

    public static (int Width, int Height) GetMapPixelSize(MapDocument map) =>
        ExportImageSize(map.Width, map.Height);

    private static (int Width, int Height) ExportImageSize(int mapWidth, int mapHeight)
    {
        var fullW = mapWidth * SizeBaseCell * 2;
        var fullH = mapHeight * SizeBaseCell;
        return (fullW - SizeBaseCell * 2, fullH - SizeBaseCell);
    }

    public static (double X, double Y, int Width, int Height) GetSlotRect(int worldX, int worldY, bool mosaicMode)
    {
        var (pw, ph) = ExportImageSize(DefaultSlotMapWidth, DefaultSlotMapHeight);
        var gap = mosaicMode ? 0 : InfoGapPixels;
        var cellW = pw + gap;
        var cellH = ph + gap;
        return (worldX * cellW, worldY * cellH, pw, ph);
    }

    public static bool IsInGrid(WorldDocument world, int x, int y) =>
        world.HasGrid &&
        x >= world.OriginX && x < world.OriginX + world.GridWidth &&
        y >= world.OriginY && y < world.OriginY + world.GridHeight;

    public static IEnumerable<(int X, int Y)> EnumerateGridCells(WorldDocument world)
    {
        if (!world.HasGrid) yield break;
        for (var y = world.OriginY; y < world.OriginY + world.GridHeight; y++)
        for (var x = world.OriginX; x < world.OriginX + world.GridWidth; x++)
            yield return (x, y);
    }

    public static (int WorldX, int WorldY)? HitTestGridSlot(
        double worldPixelX,
        double worldPixelY,
        WorldDocument world,
        bool mosaicMode)
    {
        foreach (var (x, y) in EnumerateGridCells(world))
        {
            var (rx, ry, w, h) = GetSlotRect(x, y, mosaicMode);
            if (worldPixelX >= rx && worldPixelX < rx + w &&
                worldPixelY >= ry && worldPixelY < ry + h)
                return (x, y);
        }

        return null;
    }

    public static (double X, double Y, int Width, int Height) GetMapRect(
        int worldX,
        int worldY,
        MapDocument map,
        bool mosaicMode)
    {
        var (pw, ph) = GetMapPixelSize(map);
        var gap = mosaicMode ? 0 : InfoGapPixels;
        var cellW = pw + gap;
        var cellH = ph + gap;
        return (worldX * cellW, worldY * cellH, pw, ph);
    }

    public static (int WorldX, int WorldY)? HitTestWorldCell(
        double worldPixelX,
        double worldPixelY,
        IEnumerable<(int X, int Y, MapDocument Map)> entries,
        bool mosaicMode)
    {
        foreach (var (x, y, map) in entries)
        {
            var (rx, ry, w, h) = GetMapRect(x, y, map, mosaicMode);
            if (worldPixelX >= rx && worldPixelX < rx + w &&
                worldPixelY >= ry && worldPixelY < ry + h)
                return (x, y);
        }

        return null;
    }

    public static (int X, int Y)? FindAdjacentFree(
        int originX,
        int originY,
        IReadOnlySet<(int X, int Y)> occupied)
    {
        foreach (var (dx, dy) in new (int, int)[]
                 {
                     (1, 0), (-1, 0), (0, 1), (0, -1),
                     (1, 1), (1, -1), (-1, 1), (-1, -1),
                 })
        {
            var x = originX + dx;
            var y = originY + dy;
            if (!occupied.Contains((x, y)))
                return (x, y);
        }

        return null;
    }
}
