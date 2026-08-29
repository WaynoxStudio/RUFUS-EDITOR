using System.Drawing;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.Rendering;

/// <summary>
/// Builds the single placement descriptor shared by brush preview and <see cref="AstriaMapRenderer"/>.
/// </summary>
public static class GfxPlacementPipeline
{
    public static bool TryBuild(
        int mapWidth,
        int mapHeight,
        int cellId,
        GfxResource resource,
        int nativeWidth,
        int nativeHeight,
        bool flip,
        int rotation,
        bool isObject,
        out GfxPlacementDescriptor descriptor,
        int sizeCell = IsoGeometry.SizeBaseCell)
    {
        descriptor = null!;
        if (resource is null || nativeWidth <= 0 || nativeHeight <= 0)
            return false;
        if (cellId < 0)
            return false;

        var corners = IsoGeometry.BuildCellCorners(mapWidth, mapHeight, sizeCell);
        if (cellId >= corners.Length)
            return false;

        var rot = isObject && rotation is < 0 or > 3 ? 0 : Math.Clamp(rotation, 0, 3);
        if (!isObject)
            rot = Math.Clamp(rotation, 0, 3);

        var usedXml = resource.Anchor is not null;
        var (ax, ay) = GfxPlacementMath.ResolveAnchor(
            resource.Anchor?.X, resource.Anchor?.Y, nativeWidth, nativeHeight);

        var full = GfxPlacementMath.CalculateDrawPlacement(
            corners[cellId], nativeWidth, nativeHeight, ax, ay, flip, rot, isObject, sizeCell);
        var crop = IsoGeometry.ExportCrop(mapWidth, mapHeight, sizeCell);
        var hit = full.ToHitSpace(crop.X, crop.Y);

        descriptor = new GfxPlacementDescriptor
        {
            CellId = cellId,
            Category = resource.Category,
            GfxId = resource.Id,
            ResourcePath = resource.FilePath,
            NativeWidth = nativeWidth,
            NativeHeight = nativeHeight,
            AnchorX = ax,
            AnchorY = ay,
            UsedXmlAnchor = usedXml,
            Flip = flip,
            Rotation = rot,
            IsObject = isObject,
            FullCanvas = full,
            HitSpace = hit,
        };
        return true;
    }

    public static bool TryBuildFromBitmap(
        int mapWidth,
        int mapHeight,
        int cellId,
        GfxResource resource,
        Bitmap bitmap,
        bool flip,
        int rotation,
        bool isObject,
        out GfxPlacementDescriptor descriptor,
        int sizeCell = IsoGeometry.SizeBaseCell) =>
        TryBuild(
            mapWidth, mapHeight, cellId, resource,
            bitmap.Width, bitmap.Height, flip, rotation, isObject,
            out descriptor, sizeCell);
}
