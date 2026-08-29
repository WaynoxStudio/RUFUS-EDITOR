using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Tests.Editing;

public sealed class EditHistoryTests
{
    private static MapDocument SampleMap(int cells = 30)
    {
        var list = new List<CellData>();
        for (var i = 0; i < cells; i++)
        {
            list.Add(new CellData
            {
                GroundGfxId = i,
                Object1GfxId = i * 2,
                Movement = MovementType.Walkable,
                LineOfSight = true,
                GroundLevel = 7,
                GroundSlope = 1,
            });
        }

        return new MapDocument
        {
            Id = 1,
            Width = 15,
            Height = 17,
            Cells = list,
            MapData = MapDataCodec.EncodeMap(list),
        };
    }

    [Fact]
    public void Undo_restores_exact_cell_snapshot()
    {
        var map = SampleMap();
        var before = CellSnapshot.Capture(5, map.Cells[5]);
        var cmd = CellBatchEditCommand.Build("Pintar Ground", map, new[] { 5 }, (_, c) =>
        {
            c.GroundGfxId = 999;
            c.FlipGround = true;
        });
        Assert.NotNull(cmd);
        var history = new EditHistory();
        history.PushExecuted(cmd!);

        Assert.Equal(999, map.Cells[5].GroundGfxId);
        history.Undo(map);
        Assert.True(before.ContentEquals(CellSnapshot.Capture(5, map.Cells[5])));
    }

    [Fact]
    public void Undo_then_Redo_restores_edit()
    {
        var map = SampleMap();
        var cmd = CellBatchEditCommand.Build("Pintar Ground", map, new[] { 3 }, (_, c) => c.GroundGfxId = 42);
        var history = new EditHistory();
        history.PushExecuted(cmd!);
        history.Undo(map);
        Assert.True(map.Cells[3].GroundGfxId != 42);
        history.Redo(map);
        Assert.Equal(42, map.Cells[3].GroundGfxId);
    }

    [Fact]
    public void New_edit_clears_redo_branch()
    {
        var map = SampleMap();
        var history = new EditHistory();
        history.PushExecuted(CellBatchEditCommand.Build("A", map, new[] { 0 }, (_, c) => c.GroundGfxId = 1)!);
        history.PushExecuted(CellBatchEditCommand.Build("B", map, new[] { 0 }, (_, c) => c.GroundGfxId = 2)!);
        history.Undo(map);
        Assert.True(history.CanRedo);
        history.PushExecuted(CellBatchEditCommand.Build("C", map, new[] { 0 }, (_, c) => c.GroundGfxId = 3)!);
        Assert.False(history.CanRedo);
        Assert.Equal(3, map.Cells[0].GroundGfxId);
    }

    [Fact]
    public void Noop_returns_null_command()
    {
        var map = SampleMap();
        var g = map.Cells[1].GroundGfxId;
        var cmd = CellBatchEditCommand.Build("noop", map, new[] { 1 }, (_, c) => c.GroundGfxId = g);
        Assert.Null(cmd);
    }

    [Fact]
    public void Stroke_of_many_cells_is_one_command()
    {
        var map = SampleMap();
        var ids = Enumerable.Range(0, 20).ToArray();
        var befores = ids.Select(id => CellSnapshot.Capture(id, map.Cells[id])).ToList();
        foreach (var id in ids)
            map.Cells[id].GroundGfxId = 777;
        var afters = ids.Select(id => CellSnapshot.Capture(id, map.Cells[id])).ToList();
        var pairs = befores.Zip(afters, (b, a) => (b, a)).ToList();
        var cmd = CellBatchEditCommand.FromSnapshots("Pintar Ground", pairs);
        Assert.NotNull(cmd);
        Assert.Equal(20, cmd!.ChangeCount);

        var history = new EditHistory();
        history.PushExecuted(cmd);
        history.Undo(map);
        for (var i = 0; i < 20; i++)
            Assert.True(befores[i].ContentEquals(CellSnapshot.Capture(i, map.Cells[i])));
    }

    [Fact]
    public void Dirty_false_when_undone_to_clean()
    {
        var map = SampleMap();
        var history = new EditHistory();
        history.MarkClean();
        Assert.False(history.IsDirty);
        history.PushExecuted(CellBatchEditCommand.Build("edit", map, new[] { 2 }, (_, c) => c.LineOfSight = false)!);
        Assert.True(history.IsDirty);
        history.Undo(map);
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Clear_empties_stacks_and_marks_clean()
    {
        var map = SampleMap();
        var history = new EditHistory();
        history.PushExecuted(CellBatchEditCommand.Build("e", map, new[] { 0 }, (_, c) => c.GroundGfxId = 9)!);
        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void Multi_property_edit_is_one_command()
    {
        var map = SampleMap();
        var ids = new[] { 1, 2, 3 };
        var cmd = CellBatchEditCommand.Build("Cambiar LoS", map, ids, (_, c) => c.LineOfSight = false);
        Assert.NotNull(cmd);
        Assert.Equal(3, cmd!.ChangeCount);
        var history = new EditHistory();
        history.PushExecuted(cmd);
        history.Undo(map);
        Assert.All(ids, id => Assert.True(map.Cells[id].LineOfSight));
    }

    [Fact]
    public void Capacity_discards_oldest()
    {
        var map = SampleMap(5);
        var history = new EditHistory(capacity: 3);
        for (var i = 0; i < 5; i++)
        {
            var idx = i % 5;
            history.PushExecuted(CellBatchEditCommand.Build($"e{i}", map, new[] { idx }, (_, c) => c.GroundGfxId = 100 + i)!);
        }

        Assert.Equal(3, history.UndoCount);
    }
}
