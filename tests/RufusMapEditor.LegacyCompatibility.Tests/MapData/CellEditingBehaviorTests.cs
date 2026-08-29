using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapData;

public sealed class CellEditingBehaviorTests
{
    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));

    [Theory]
    [InlineData(10, "Hhaaeaaaaa", MovementType.Unwalkable, true)]
    [InlineData(228, "GhaaeaaGpM", MovementType.Unwalkable, false)]
    [InlineData(405, "GhGaeaaaaa", MovementType.Walkable, false)]
    public void Golden_10421_movement_and_los(int cellId, string expectedBlock, MovementType movement, bool los)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, "10421.sql"));
        var cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        var cell = cells[cellId];
        Assert.Equal(movement, cell.Movement);
        Assert.Equal(los, cell.LineOfSight);
        Assert.Equal(expectedBlock, MapDataCodec.EncodeCell(cell));
    }

    [Fact]
    public void Unwalkable_clears_fight_cell_preserves_los()
    {
        var cell = new CellData
        {
            Movement = MovementType.Walkable,
            LineOfSight = false,
            FightCell = 1,
            GroundGfxId = 100,
        };
        MapCellEditor.SetMovement(cell, MovementType.Unwalkable);
        Assert.Equal(MovementType.Unwalkable, cell.Movement);
        Assert.Equal(0, cell.FightCell);
        Assert.False(cell.LineOfSight);
    }

    [Fact]
    public void Paint_semantics_block_fight_on_unwalkable_via_movement_check()
    {
        var cell = new CellData { Movement = MovementType.Unwalkable, FightCell = 0 };
        Assert.Equal(MovementType.Unwalkable, cell.Movement);
        MapCellEditor.SetFightCell(cell, 1);
        Assert.Equal(1, cell.FightCell);
    }

    [Fact]
    public void Fight_team2_replaces_team1()
    {
        var cell = new CellData { Movement = MovementType.Walkable, FightCell = 1 };
        MapCellEditor.SetFightCell(cell, 2);
        Assert.Equal(2, cell.FightCell);
    }

    [Fact]
    public void Los_does_not_change_movement()
    {
        var cell = new CellData { Movement = MovementType.Walkable, LineOfSight = true };
        MapCellEditor.SetLineOfSight(cell, false);
        Assert.Equal(MovementType.Walkable, cell.Movement);
        Assert.False(cell.LineOfSight);
    }

    [Fact]
    public void Undo_restores_fight_after_unwalkable()
    {
        var map = new MapDocument
        {
            Id = 1,
            Width = 15,
            Height = 17,
            Cells = Enumerable.Range(0, MapGeometry.CellCount(15, 17))
                .Select(_ => new CellData { Movement = MovementType.Walkable, LineOfSight = true })
                .ToList(),
        };
        map.Cells[5].FightCell = 1;
        var before = CellSnapshot.Capture(5, map.Cells[5]);
        MapCellEditor.SetMovement(map.Cells[5], MovementType.Unwalkable);
        MapCellEditor.SetFightCell(map.Cells[5], 0);
        before.ApplyTo(map.Cells[5]);
        Assert.Equal(1, map.Cells[5].FightCell);
        Assert.Equal(MovementType.Walkable, map.Cells[5].Movement);
    }
}
