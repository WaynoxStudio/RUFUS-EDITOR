using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapData;

public sealed class MapCellEditorRoundTripTests
{
    private static CellData SampleCell() => new()
    {
        Active = true,
        LineOfSight = true,
        Movement = MovementType.Walkable,
        GroundGfxId = 100,
        Object1GfxId = 200,
        Object2GfxId = 300,
        FlipGround = true,
        FlipObject1 = false,
        FlipObject2 = true,
        GroundRotation = 2,
        Object1Rotation = 1,
        GroundLevel = 7,
        GroundSlope = 1,
        InteractiveObject = true,
    };

    private static CellData RoundTrip(CellData cell) =>
        MapDataCodec.DecodeCell(MapDataCodec.EncodeCell(cell));

    [Fact]
    public void Change_Ground_preserves_other_fields()
    {
        var original = SampleCell();
        var edited = MapCellEditor.Clone(original);
        MapCellEditor.SetLayerGfx(edited, MapCellEditor.Layer.Ground, 555);
        var decoded = RoundTrip(edited);

        Assert.Equal(555, decoded.GroundGfxId);
        Assert.Equal(original.Object1GfxId, decoded.Object1GfxId);
        Assert.Equal(original.Object2GfxId, decoded.Object2GfxId);
        Assert.Equal(original.Movement, decoded.Movement);
        Assert.Equal(original.LineOfSight, decoded.LineOfSight);
        Assert.Equal(original.InteractiveObject, decoded.InteractiveObject);
        Assert.Equal(original.GroundLevel, decoded.GroundLevel);
        Assert.Equal(original.GroundSlope, decoded.GroundSlope);
        Assert.Equal(original.FlipObject1, decoded.FlipObject1);
        Assert.Equal(original.Object1Rotation, decoded.Object1Rotation);
    }

    [Fact]
    public void Change_Object1_preserves_other_fields()
    {
        var original = SampleCell();
        var edited = MapCellEditor.Clone(original);
        MapCellEditor.SetLayerGfx(edited, MapCellEditor.Layer.Object1, 777, flip: true, rotation: 3);
        var decoded = RoundTrip(edited);

        Assert.Equal(777, decoded.Object1GfxId);
        Assert.True(decoded.FlipObject1);
        Assert.Equal(3, decoded.Object1Rotation);
        Assert.Equal(original.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(original.Object2GfxId, decoded.Object2GfxId);
        Assert.Equal(original.Movement, decoded.Movement);
        Assert.Equal(original.FlipGround, decoded.FlipGround);
    }

    [Fact]
    public void Change_Object2_preserves_other_fields()
    {
        var original = SampleCell();
        var edited = MapCellEditor.Clone(original);
        MapCellEditor.SetLayerGfx(edited, MapCellEditor.Layer.Object2, 888, flip: false);
        var decoded = RoundTrip(edited);

        Assert.Equal(888, decoded.Object2GfxId);
        Assert.False(decoded.FlipObject2);
        Assert.Equal(original.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(original.Object1GfxId, decoded.Object1GfxId);
        Assert.Equal(original.Object1Rotation, decoded.Object1Rotation);
    }

    [Fact]
    public void Clear_each_layer_roundtrips_to_zero()
    {
        foreach (MapCellEditor.Layer layer in Enum.GetValues<MapCellEditor.Layer>())
        {
            var cell = SampleCell();
            MapCellEditor.ClearLayer(cell, layer);
            var decoded = RoundTrip(cell);
            switch (layer)
            {
                case MapCellEditor.Layer.Ground:
                    Assert.Equal(0, decoded.GroundGfxId);
                    Assert.Equal(200, decoded.Object1GfxId);
                    break;
                case MapCellEditor.Layer.Object1:
                    Assert.Equal(0, decoded.Object1GfxId);
                    Assert.Equal(100, decoded.GroundGfxId);
                    break;
                case MapCellEditor.Layer.Object2:
                    Assert.Equal(0, decoded.Object2GfxId);
                    Assert.Equal(100, decoded.GroundGfxId);
                    break;
            }
        }
    }

    [Theory]
    [InlineData(MovementType.Unwalkable)]
    [InlineData(MovementType.Door)]
    [InlineData(MovementType.Trigger)]
    [InlineData(MovementType.Walkable)]
    [InlineData(MovementType.Paddock)]
    [InlineData(MovementType.Path)]
    public void Movement_roundtrip(MovementType movement)
    {
        var cell = SampleCell();
        MapCellEditor.SetMovement(cell, movement);
        var decoded = RoundTrip(cell);
        Assert.Equal(movement, decoded.Movement);
        Assert.Equal(SampleCell().GroundGfxId, decoded.GroundGfxId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoS_and_IO_roundtrip(bool flag)
    {
        var cell = SampleCell();
        MapCellEditor.SetLineOfSight(cell, flag);
        MapCellEditor.SetInteractive(cell, flag);
        var decoded = RoundTrip(cell);
        Assert.Equal(flag, decoded.LineOfSight);
        Assert.Equal(flag, decoded.InteractiveObject);
    }

    [Fact]
    public void GroundLevel_and_Slope_clamp_and_roundtrip()
    {
        var cell = SampleCell();
        MapCellEditor.SetGroundLevel(cell, 20);
        MapCellEditor.SetGroundSlope(cell, -3);
        Assert.Equal(15, cell.GroundLevel);
        Assert.Equal(0, cell.GroundSlope);
        var decoded = RoundTrip(cell);
        Assert.Equal(15, decoded.GroundLevel);
        Assert.Equal(0, decoded.GroundSlope);
        Assert.Equal(SampleCell().Movement, decoded.Movement);
    }

    [Fact]
    public void Flips_and_rotations_roundtrip()
    {
        var cell = SampleCell();
        MapCellEditor.SetFlip(cell, MapCellEditor.Layer.Ground, false);
        MapCellEditor.SetFlip(cell, MapCellEditor.Layer.Object1, true);
        MapCellEditor.SetFlip(cell, MapCellEditor.Layer.Object2, false);
        MapCellEditor.SetRotation(cell, MapCellEditor.Layer.Ground, 3);
        MapCellEditor.SetRotation(cell, MapCellEditor.Layer.Object1, 0);
        var decoded = RoundTrip(cell);
        Assert.False(decoded.FlipGround);
        Assert.True(decoded.FlipObject1);
        Assert.False(decoded.FlipObject2);
        Assert.Equal(3, decoded.GroundRotation);
        Assert.Equal(0, decoded.Object1Rotation);
    }

    [Fact]
    public void Object2_rotation_is_not_supported()
    {
        var cell = SampleCell();
        Assert.Throws<InvalidOperationException>(() =>
            MapCellEditor.SetRotation(cell, MapCellEditor.Layer.Object2, 1));
    }

    [Fact]
    public void Full_map_encode_after_edit_preserves_untouched_cells()
    {
        var cells = new[] { SampleCell(), SampleCell(), SampleCell() };
        cells[1].GroundGfxId = 42;
        var beforeDecoded = MapDataCodec.DecodeMap(MapDataCodec.EncodeMap(cells));
        MapCellEditor.SetLayerGfx(cells[1], MapCellEditor.Layer.Object1, 999);
        var after = MapDataCodec.EncodeMap(cells);
        var decoded = MapDataCodec.DecodeMap(after);
        Assert.True(MapCellEditor.CellEquals(beforeDecoded[0], decoded[0]));
        Assert.Equal(999, decoded[1].Object1GfxId);
        Assert.Equal(42, decoded[1].GroundGfxId);
        Assert.True(MapCellEditor.CellEquals(beforeDecoded[2], decoded[2]));
    }
}
