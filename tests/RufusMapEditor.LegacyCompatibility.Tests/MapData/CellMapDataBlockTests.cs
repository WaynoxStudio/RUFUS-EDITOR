using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapData;

public sealed class CellMapDataBlockTests
{
    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));

    private static MapDocument LoadFixture(int mapId)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, $"{mapId}.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        return map;
    }

    [Theory]
    [InlineData(10420)]
    [InlineData(10421)]
    [InlineData(7411)]
    public void EncodeCellBlock_equals_fragment_of_full_MapData(int mapId)
    {
        var map = LoadFixture(mapId);
        var full = MapDataCodec.EncodeMap(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
        for (var i = 0; i < map.Cells.Count; i++)
        {
            var block = MapDataCodec.EncodeCellBlock(map.Cells[i]);
            Assert.Equal(MapDataConstants.CharsPerCell, block.Length);
            Assert.Equal(MapDataCodec.ExtractCellBlock(full, i), block);
        }
    }

    [Fact]
    public void Cell_index_matches_serialized_order_for_15x17()
    {
        var map = LoadFixture(10420);
        Assert.Equal(479, map.Cells.Count);
        var (start, end) = MapDataCodec.GetCellBlockCharRange(228);
        Assert.Equal(2280, start);
        Assert.Equal(2289, end);
    }

    [Fact]
    public void Change_ground_updates_only_ground_in_10_char_block()
    {
        var map = LoadFixture(10420);
        var cell = MapCellEditor.Clone(map.Cells[100]);
        var originalBlock = MapDataCodec.EncodeCell(cell);
        MapCellEditor.SetLayerGfx(cell, MapCellEditor.Layer.Ground, cell.GroundGfxId + 1);
        var newBlock = MapDataCodec.EncodeCell(cell);
        Assert.NotEqual(originalBlock, newBlock);
        var decoded = MapDataCodec.DecodeCell(newBlock);
        Assert.Equal(cell.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(map.Cells[100].Object1GfxId, decoded.Object1GfxId);
        Assert.Equal(map.Cells[100].Object2GfxId, decoded.Object2GfxId);
        Assert.Equal(map.Cells[100].Movement, decoded.Movement);
    }

    [Fact]
    public void Change_object1_preserves_other_fields_in_block()
    {
        var map = LoadFixture(10420);
        var cell = MapCellEditor.Clone(map.Cells[50]);
        var before = MapCellEditor.Clone(cell);
        MapCellEditor.SetLayerGfx(cell, MapCellEditor.Layer.Object1, 999, flip: true, rotation: 2);
        var decoded = MapDataCodec.DecodeCell(MapDataCodec.EncodeCell(cell));
        Assert.Equal(999, decoded.Object1GfxId);
        Assert.True(decoded.FlipObject1);
        Assert.Equal(2, decoded.Object1Rotation);
        Assert.Equal(before.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(before.Object2GfxId, decoded.Object2GfxId);
        Assert.Equal(before.Movement, decoded.Movement);
        Assert.Equal(before.LineOfSight, decoded.LineOfSight);
    }

    [Fact]
    public void Change_object2_preserves_other_fields_and_no_rotation()
    {
        var map = LoadFixture(10420);
        var cell = MapCellEditor.Clone(map.Cells[50]);
        var before = MapCellEditor.Clone(cell);
        MapCellEditor.SetLayerGfx(cell, MapCellEditor.Layer.Object2, 888, flip: true);
        var decoded = MapDataCodec.DecodeCell(MapDataCodec.EncodeCell(cell));
        Assert.Equal(888, decoded.Object2GfxId);
        Assert.True(decoded.FlipObject2);
        Assert.Equal(before.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(before.Object1GfxId, decoded.Object1GfxId);
        Assert.Equal(before.Object1Rotation, decoded.Object1Rotation);
    }

    [Fact]
    public void Change_movement_preserves_gfx_layers_in_block()
    {
        var map = LoadFixture(10420);
        var cell = MapCellEditor.Clone(map.Cells[10]);
        var before = MapCellEditor.Clone(cell);
        MapCellEditor.SetMovement(cell, MovementType.Unwalkable);
        var decoded = MapDataCodec.DecodeCell(MapDataCodec.EncodeCell(cell));
        Assert.Equal(MovementType.Unwalkable, decoded.Movement);
        Assert.Equal(before.GroundGfxId, decoded.GroundGfxId);
        Assert.Equal(before.Object1GfxId, decoded.Object1GfxId);
        Assert.Equal(before.Object2GfxId, decoded.Object2GfxId);
    }

    [Theory]
    [InlineData(228, "GhaaeaaGpM")]
    [InlineData(230, "Ghaaeaaa_Y")]
    public void Astria_visual_examples_reproducible_from_fixture_10421(int cellId, string expectedBlock)
    {
        var map = LoadFixture(10421);
        var block = MapDataCodec.EncodeCell(map.Cells[cellId]);
        Assert.Equal(expectedBlock, block);
        Assert.Equal(expectedBlock, MapDataCodec.ExtractCellBlock(map.MapData, cellId));
    }

    [Fact]
    public void Export_uses_same_MapData_as_codec_encode()
    {
        var map = LoadFixture(10420);
        MapCellEditor.SyncMapDataString(map);
        var encoded = MapDataCodec.EncodeMap(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
        Assert.Equal(encoded, map.MapData);

        var flasm = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1\Flasm\flasm.exe";
        var blank = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1\Flasm\blank.swf";
        if (!File.Exists(flasm) || !File.Exists(blank))
            return;

        var dest = Path.Combine(Path.GetTempPath(), $"rufus_mapdata_test_{Guid.NewGuid():N}.swf");
        try
        {
            var result = SwfMapExporter.Export(new SwfExportRequest
            {
                Document = map,
                DestinationSwfPath = dest,
                FlasmExePath = flasm,
                BlankSwfTemplatePath = blank,
            });
            Assert.Equal(map.MapData, result.MapDataExported);
        }
        finally
        {
            if (File.Exists(dest))
                File.Delete(dest);
        }
    }
}
