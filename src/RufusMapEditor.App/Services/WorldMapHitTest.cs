using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

public readonly record struct WorldCellHit(
    string DocumentKey,
    int CellId,
    int WorldGridX,
    int WorldGridY,
    double LocalX,
    double LocalY);

/// <summary>
/// World pixel → map document + local cell. Uses the same IsoHitTester as single-map MAPA.
/// </summary>
public static class WorldMapHitTest
{
    public static WorldCellHit? HitTestCell(
        double worldPixelX,
        double worldPixelY,
        IEnumerable<(int WorldX, int WorldY, string DocumentKey, MapDocument Map)> entries,
        bool mosaicMode,
        IReadOnlySet<string>? editableKeysOnly = null)
    {
        WorldCellHit? best = null;
        foreach (var (worldX, worldY, documentKey, map) in entries)
        {
            if (editableKeysOnly is not null && !editableKeysOnly.Contains(documentKey))
                continue;

            var hit = HitTestCellInMap(worldPixelX, worldPixelY, worldX, worldY, documentKey, map, mosaicMode);
            if (hit is not null)
                best = hit;
        }

        return best;
    }

    public static WorldCellHit? HitTestCellInMap(
        double worldPixelX,
        double worldPixelY,
        int worldX,
        int worldY,
        string documentKey,
        MapDocument map,
        bool mosaicMode)
    {
        var (rx, ry, w, h) = WorldGeometry.GetMapRect(worldX, worldY, map, mosaicMode);
        if (worldPixelX < rx || worldPixelX >= rx + w || worldPixelY < ry || worldPixelY >= ry + h)
            return null;

        var localX = worldPixelX - rx;
        var localY = worldPixelY - ry;
        var tester = CreateHitTester(map);
        var cellId = tester.HitTest(localX, localY);
        if (cellId is not int id)
            return null;

        return new WorldCellHit(documentKey, id, worldX, worldY, localX, localY);
    }

    public static IsoHitTester CreateHitTester(MapDocument map) =>
        new(map.Width, map.Height);

    public static List<WorldCellRef> CellsInWorldRect(
        double worldX0,
        double worldY0,
        double worldX1,
        double worldY1,
        IEnumerable<(int WorldX, int WorldY, string DocumentKey, MapDocument Map)> entries,
        bool mosaicMode,
        IReadOnlySet<string>? editableKeysOnly = null)
    {
        var x0 = Math.Min(worldX0, worldX1);
        var y0 = Math.Min(worldY0, worldY1);
        var x1 = Math.Max(worldX0, worldX1);
        var y1 = Math.Max(worldY0, worldY1);

        var result = new List<WorldCellRef>();
        foreach (var (worldX, worldY, documentKey, map) in entries)
        {
            if (editableKeysOnly is not null && !editableKeysOnly.Contains(documentKey))
                continue;

            var (rx, ry, w, h) = WorldGeometry.GetMapRect(worldX, worldY, map, mosaicMode);
            var ix0 = Math.Max(x0, rx);
            var iy0 = Math.Max(y0, ry);
            var ix1 = Math.Min(x1, rx + w);
            var iy1 = Math.Min(y1, ry + h);
            if (ix0 >= ix1 || iy0 >= iy1)
                continue;

            var tester = CreateHitTester(map);
            var cells = IsoSelection.CellsIntersectingRect(
                tester,
                ix0 - rx,
                iy0 - ry,
                ix1 - rx,
                iy1 - ry);
            foreach (var cellId in cells)
                result.Add(new WorldCellRef(documentKey, cellId));
        }

        return result;
    }

    public static List<WorldCellHit> CellsAlongSegment(
        double x0,
        double y0,
        double x1,
        double y1,
        IEnumerable<(int WorldX, int WorldY, string DocumentKey, MapDocument Map)> entries,
        bool mosaicMode,
        IReadOnlySet<string>? editableKeysOnly = null,
        double stepPixels = 10)
    {
        var results = new List<WorldCellHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
        {
            TryAdd(HitTestCell(x1, y1, entries, mosaicMode, editableKeysOnly));
            return results;
        }

        stepPixels = Math.Max(4, stepPixels);
        var steps = Math.Max(1, (int)Math.Ceiling(len / stepPixels));
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            TryAdd(HitTestCell(x0 + dx * t, y0 + dy * t, entries, mosaicMode, editableKeysOnly));
        }

        return results;

        void TryAdd(WorldCellHit? hit)
        {
            if (hit is not WorldCellHit h) return;
            var key = $"{h.DocumentKey}:{h.CellId}";
            if (seen.Add(key))
                results.Add(h);
        }
    }
}
