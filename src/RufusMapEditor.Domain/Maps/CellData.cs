namespace RufusMapEditor.Domain.Maps;

/// <summary>
/// Classical DOFUS Retro cell as represented in the 10-character MapData chunk.
/// Properties mirror the bit fields used by Astria Builder / Map.UncompressCell.
/// Editor-only state (FightCell, TriggerName, etc.) is not part of MapData.
/// </summary>
public sealed class CellData
{
    /// <summary>Active / "used" flag (Astria always encodes this as set — bit 0x20 of byte 0).</summary>
    public bool Active { get; set; } = true;

    public bool LineOfSight { get; set; } = true;

    public MovementType Movement { get; set; } = MovementType.Walkable;

    public int GroundGfxId { get; set; }
    public int Object1GfxId { get; set; }
    public int Object2GfxId { get; set; }

    public bool FlipGround { get; set; }
    public bool FlipObject1 { get; set; }
    public bool FlipObject2 { get; set; }

    /// <summary>0..3</summary>
    public int GroundRotation { get; set; }

    /// <summary>0..3</summary>
    public int Object1Rotation { get; set; }

    /// <summary>Ground elevation level 0..15 (Astria default 7).</summary>
    public int GroundLevel { get; set; } = 7;

    /// <summary>Ground slope 0..15 (Astria default 1).</summary>
    public int GroundSlope { get; set; } = 1;

    /// <summary>Interactive object flag.</summary>
    public bool InteractiveObject { get; set; }

    /// <summary>0 = none, 1 = team 1, 2 = team 2. Persisted in <see cref="MapDocument.FightPlaces"/>, not MapData.</summary>
    public int FightCell { get; set; }
}
