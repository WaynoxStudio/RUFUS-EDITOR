using System.IO;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Réplica de GFX en mapas vecinos cuando el sprite cruza la costura del mosaico.
/// Coloca el mismo GFX en la celda del vecino cuyo ancla cae en el borde compartido,
/// para que la parte alta / lateral sea visible al ver ese mapa solo.
/// </summary>
public static class SeamPaintHelper
{
    /// <summary>
    /// Devuelve celdas de mapas vecinos donde hay que replicar el GFX del trazo principal.
    /// </summary>
    public static IReadOnlyList<WorldCellRef> FindReplicaCells(
        WorldDocument world,
        string primaryDocumentKey,
        int primaryCellId,
        int gfxId,
        PaintLayer paintLayer,
        bool brushFlip,
        int brushRotation,
        IGfxCatalog catalog,
        bool mosaicMode = true)
    {
        if (!world.Documents.TryGetValue(primaryDocumentKey, out var primaryEntry))
            return Array.Empty<WorldCellRef>();

        var primaryMap = primaryEntry.Document;
        var primaryPlacement = world.Placements.FirstOrDefault(p => p.DocumentKey == primaryDocumentKey);
        if (primaryPlacement is null)
            return Array.Empty<WorldCellRef>();

        var category = paintLayer.ToGfxCategory();
        if (!catalog.TryGet(category, gfxId, out var res) || res is null)
            return Array.Empty<WorldCellRef>();

        var imgW = res.PixelWidth ?? 0;
        var imgH = res.PixelHeight ?? 0;
        if (imgW <= 0 || imgH <= 0)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(res.FilePath) && File.Exists(res.FilePath))
                {
                    using var img = System.Drawing.Image.FromFile(res.FilePath);
                    imgW = img.Width;
                    imgH = img.Height;
                }
            }
            catch
            {
                // Tamaño estimado si no se puede leer el PNG.
            }
        }

        if (imgW <= 0) imgW = 160;
        if (imgH <= 0) imgH = 220;
        var (ax, ay) = GfxPlacementMath.ResolveAnchor(res.Anchor?.X, res.Anchor?.Y, imgW, imgH);
        var isObject = paintLayer != PaintLayer.Ground;
        var rot = paintLayer == PaintLayer.Object2 ? 0 : brushRotation;

        if (!GfxPlacementMath.TryCalculateDrawPlacementInHitSpace(
                primaryMap.Width, primaryMap.Height, primaryCellId,
                imgW, imgH, ax, ay, brushFlip, rot, isObject,
                out var hitRect))
            return Array.Empty<WorldCellRef>();

        var (prx, pry, pw, ph) = WorldGeometry.GetMapRect(
            primaryPlacement.WorldX, primaryPlacement.WorldY, primaryMap, mosaicMode);

        var drawX0 = prx + hitRect.X;
        var drawY0 = pry + hitRect.Y;
        var drawX1 = drawX0 + hitRect.Width;
        var drawY1 = drawY0 + hitRect.Height;

        var primaryTester = WorldMapHitTest.CreateHitTester(primaryMap);
        if (!primaryTester.TryGetCellCornersInHitSpace(primaryCellId, out var corners))
            return Array.Empty<WorldCellRef>();

        var (pcx, pcy) = IsoGeometry.GetCellCenter(corners);
        var worldCx = prx + pcx;
        var worldCy = pry + pcy;

        var results = new List<WorldCellRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { primaryDocumentKey + ":" + primaryCellId };

        foreach (var p in world.Placements)
        {
            if (string.Equals(p.DocumentKey, primaryDocumentKey, StringComparison.Ordinal))
                continue;
            if (!world.Documents.TryGetValue(p.DocumentKey, out var entry))
                continue;

            var map = entry.Document;
            // Solo misma geometría: costura 1:1 entre mapas del combinado.
            if (map.Width != primaryMap.Width || map.Height != primaryMap.Height)
                continue;

            var (nrx, nry, nw, nh) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, map, mosaicMode);
            if (!RectsOverlap(drawX0, drawY0, drawX1, drawY1, nrx, nry, nrx + nw, nry + nh))
                continue;

            // Proyectar el centro de la celda origen sobre el rectángulo del vecino → celda de borde.
            var tx = Clamp(worldCx, nrx + 2, nrx + nw - 2);
            var ty = Clamp(worldCy, nry + 2, nry + nh - 2);
            var hit = WorldMapHitTest.HitTestCellInMap(tx, ty, p.WorldX, p.WorldY, p.DocumentKey, map, mosaicMode);
            if (hit is null)
                continue;

            var key = hit.Value.DocumentKey + ":" + hit.Value.CellId;
            if (!seen.Add(key))
                continue;

            results.Add(new WorldCellRef(hit.Value.DocumentKey, hit.Value.CellId));
        }

        return results;
    }

    private static bool RectsOverlap(
        double ax0, double ay0, double ax1, double ay1,
        double bx0, double by0, double bx1, double by1) =>
        ax0 < bx1 && ax1 > bx0 && ay0 < by1 && ay1 > by0;

    private static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;
}
