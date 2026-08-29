using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.MapData;

/// <summary>
/// In-memory cell mutations for Phase 5. Encodes/decodes via <see cref="MapDataCodec"/>;
/// never writes Astria files. Does not use Astria <c>Cell.Type()</c>.
/// </summary>
public static class MapCellEditor
{
    public enum Layer
    {
        Ground = 0,
        Object1 = 1,
        Object2 = 2,
    }

    /// <summary>Deep-copy of MapData-backed fields (no shared references).</summary>
    public static CellData Clone(CellData source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CellData
        {
            Active = source.Active,
            LineOfSight = source.LineOfSight,
            Movement = source.Movement,
            GroundGfxId = source.GroundGfxId,
            Object1GfxId = source.Object1GfxId,
            Object2GfxId = source.Object2GfxId,
            FlipGround = source.FlipGround,
            FlipObject1 = source.FlipObject1,
            FlipObject2 = source.FlipObject2,
            GroundRotation = source.GroundRotation,
            Object1Rotation = source.Object1Rotation,
            GroundLevel = source.GroundLevel,
            GroundSlope = source.GroundSlope,
            InteractiveObject = source.InteractiveObject,
            FightCell = source.FightCell,
        };
    }

    public static bool CellEquals(CellData a, CellData b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Active == b.Active
            && a.LineOfSight == b.LineOfSight
            && a.Movement == b.Movement
            && a.GroundGfxId == b.GroundGfxId
            && a.Object1GfxId == b.Object1GfxId
            && a.Object2GfxId == b.Object2GfxId
            && a.FlipGround == b.FlipGround
            && a.FlipObject1 == b.FlipObject1
            && a.FlipObject2 == b.FlipObject2
            && a.GroundRotation == b.GroundRotation
            && a.Object1Rotation == b.Object1Rotation
            && a.GroundLevel == b.GroundLevel
            && a.GroundSlope == b.GroundSlope
            && a.InteractiveObject == b.InteractiveObject
            && a.FightCell == b.FightCell;
    }

    public static void SetLayerGfx(CellData cell, Layer layer, int gfxId, bool? flip = null, int? rotation = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (gfxId < 0)
            throw new ArgumentOutOfRangeException(nameof(gfxId));

        switch (layer)
        {
            case Layer.Ground:
                cell.GroundGfxId = gfxId;
                if (flip.HasValue) cell.FlipGround = flip.Value;
                if (rotation.HasValue) cell.GroundRotation = ClampRotation(rotation.Value);
                break;
            case Layer.Object1:
                cell.Object1GfxId = gfxId;
                if (flip.HasValue) cell.FlipObject1 = flip.Value;
                if (rotation.HasValue) cell.Object1Rotation = ClampRotation(rotation.Value);
                break;
            case Layer.Object2:
                cell.Object2GfxId = gfxId;
                if (flip.HasValue) cell.FlipObject2 = flip.Value;
                // MapData has no Object2 rotation (Astria RotaGfx3 does not exist).
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    public static void ClearLayer(CellData cell, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(cell);
        switch (layer)
        {
            case Layer.Ground:
                cell.GroundGfxId = 0;
                break;
            case Layer.Object1:
                cell.Object1GfxId = 0;
                break;
            case Layer.Object2:
                cell.Object2GfxId = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    public static void SetMovement(CellData cell, MovementType movement)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var v = (int)movement;
        if (v is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(movement));
        cell.Movement = (MovementType)(v & 7);
        if (cell.Movement == MovementType.Unwalkable)
            cell.FightCell = 0;
    }

    public static void SetLineOfSight(CellData cell, bool lineOfSight)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cell.LineOfSight = lineOfSight;
    }

    public static void SetFightCell(CellData cell, int fightCell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (fightCell is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(fightCell));
        cell.FightCell = fightCell;
    }

    public static void SetInteractive(CellData cell, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cell.InteractiveObject = interactive;
    }

    public static void SetGroundLevel(CellData cell, int level)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cell.GroundLevel = Math.Clamp(level, 0, 15);
    }

    public static void SetGroundSlope(CellData cell, int slope)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cell.GroundSlope = Math.Clamp(slope, 0, 15);
    }

    public static void SetFlip(CellData cell, Layer layer, bool flip)
    {
        ArgumentNullException.ThrowIfNull(cell);
        switch (layer)
        {
            case Layer.Ground: cell.FlipGround = flip; break;
            case Layer.Object1: cell.FlipObject1 = flip; break;
            case Layer.Object2: cell.FlipObject2 = flip; break;
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    public static void SetRotation(CellData cell, Layer layer, int rotation)
    {
        ArgumentNullException.ThrowIfNull(cell);
        rotation = ClampRotation(rotation);
        switch (layer)
        {
            case Layer.Ground: cell.GroundRotation = rotation; break;
            case Layer.Object1: cell.Object1Rotation = rotation; break;
            case Layer.Object2:
                throw new InvalidOperationException("Object Layer 2 has no rotation field in MapData.");
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    public static void SyncMapDataString(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.MapData = MapDataCodec.EncodeMap(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
    }

    public static void SyncFightPlaces(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.FightPlaces = FightPlacesCodec.Encode(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
    }

    public static void SyncDocument(MapDocument map)
    {
        SyncMapDataString(map);
        SyncFightPlaces(map);
    }

    private static int ClampRotation(int rotation) => Math.Clamp(rotation, 0, 3);
}
