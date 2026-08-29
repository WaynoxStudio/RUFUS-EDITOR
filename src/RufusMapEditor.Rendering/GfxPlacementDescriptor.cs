using System;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.Rendering;

/// <summary>
/// Single logical placement for preview, bounds, and final <c>Draw_Tile</c>.
/// Built only via <see cref="GfxPlacementPipeline"/> — no parallel math.
/// </summary>
public sealed class GfxPlacementDescriptor
{
    public required int CellId { get; init; }
    public required GfxCategory Category { get; init; }
    public required int GfxId { get; init; }
    public required string ResourcePath { get; init; }
    public required int NativeWidth { get; init; }
    public required int NativeHeight { get; init; }
    public required int AnchorX { get; init; }
    public required int AnchorY { get; init; }
    public required bool UsedXmlAnchor { get; init; }
    public required bool Flip { get; init; }
    public required int Rotation { get; init; }
    public required bool IsObject { get; init; }
    public required GfxPlacementMath.PlacementRect FullCanvas { get; init; }
    public required GfxPlacementMath.PlacementRect HitSpace { get; init; }

    public int DrawXFull => FullCanvas.X;
    public int DrawYFull => FullCanvas.Y;
    public int DrawXHit => HitSpace.X;
    public int DrawYHit => HitSpace.Y;
    public int DrawWidth => FullCanvas.Width;
    public int DrawHeight => FullCanvas.Height;

    public bool GeometryEquals(GfxPlacementDescriptor other) =>
        CellId == other.CellId
        && Category == other.Category
        && GfxId == other.GfxId
        && string.Equals(ResourcePath, other.ResourcePath, StringComparison.OrdinalIgnoreCase)
        && NativeWidth == other.NativeWidth
        && NativeHeight == other.NativeHeight
        && AnchorX == other.AnchorX
        && AnchorY == other.AnchorY
        && Flip == other.Flip
        && Rotation == other.Rotation
        && IsObject == other.IsObject
        && FullCanvas.X == other.FullCanvas.X
        && FullCanvas.Y == other.FullCanvas.Y
        && FullCanvas.Width == other.FullCanvas.Width
        && FullCanvas.Height == other.FullCanvas.Height
        && HitSpace.X == other.HitSpace.X
        && HitSpace.Y == other.HitSpace.Y
        && HitSpace.Width == other.HitSpace.Width
        && HitSpace.Height == other.HitSpace.Height;
}
