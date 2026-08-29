using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Editing;

/// <summary>
/// Full MapData-backed snapshot of a cell. Used for undo/copy — never drop unknown bits.
/// </summary>
public sealed class CellSnapshot
{
    public int CellId { get; init; }
    public bool Active { get; init; }
    public bool LineOfSight { get; init; }
    public MovementType Movement { get; init; }
    public int GroundGfxId { get; init; }
    public int Object1GfxId { get; init; }
    public int Object2GfxId { get; init; }
    public bool FlipGround { get; init; }
    public bool FlipObject1 { get; init; }
    public bool FlipObject2 { get; init; }
    public int GroundRotation { get; init; }
    public int Object1Rotation { get; init; }
    public int GroundLevel { get; init; }
    public int GroundSlope { get; init; }
    public bool InteractiveObject { get; init; }
    public int FightCell { get; init; }

    public static CellSnapshot Capture(int cellId, CellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return new CellSnapshot
        {
            CellId = cellId,
            Active = cell.Active,
            LineOfSight = cell.LineOfSight,
            Movement = cell.Movement,
            GroundGfxId = cell.GroundGfxId,
            Object1GfxId = cell.Object1GfxId,
            Object2GfxId = cell.Object2GfxId,
            FlipGround = cell.FlipGround,
            FlipObject1 = cell.FlipObject1,
            FlipObject2 = cell.FlipObject2,
            GroundRotation = cell.GroundRotation,
            Object1Rotation = cell.Object1Rotation,
            GroundLevel = cell.GroundLevel,
            GroundSlope = cell.GroundSlope,
            InteractiveObject = cell.InteractiveObject,
            FightCell = cell.FightCell,
        };
    }

    public void ApplyTo(CellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        cell.Active = Active;
        cell.LineOfSight = LineOfSight;
        cell.Movement = Movement;
        cell.GroundGfxId = GroundGfxId;
        cell.Object1GfxId = Object1GfxId;
        cell.Object2GfxId = Object2GfxId;
        cell.FlipGround = FlipGround;
        cell.FlipObject1 = FlipObject1;
        cell.FlipObject2 = FlipObject2;
        cell.GroundRotation = GroundRotation;
        cell.Object1Rotation = Object1Rotation;
        cell.GroundLevel = GroundLevel;
        cell.GroundSlope = GroundSlope;
        cell.InteractiveObject = InteractiveObject;
        cell.FightCell = FightCell;
    }

    public CellData ToCellData()
    {
        var cell = new CellData();
        ApplyTo(cell);
        return cell;
    }

    public bool ContentEquals(CellSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Active == other.Active
            && LineOfSight == other.LineOfSight
            && Movement == other.Movement
            && GroundGfxId == other.GroundGfxId
            && Object1GfxId == other.Object1GfxId
            && Object2GfxId == other.Object2GfxId
            && FlipGround == other.FlipGround
            && FlipObject1 == other.FlipObject1
            && FlipObject2 == other.FlipObject2
            && GroundRotation == other.GroundRotation
            && Object1Rotation == other.Object1Rotation
            && GroundLevel == other.GroundLevel
            && GroundSlope == other.GroundSlope
            && InteractiveObject == other.InteractiveObject
            && FightCell == other.FightCell;
    }
}
