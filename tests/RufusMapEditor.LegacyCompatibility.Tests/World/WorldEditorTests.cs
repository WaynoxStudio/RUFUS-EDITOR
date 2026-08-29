using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.World;

namespace RufusMapEditor.LegacyCompatibility.Tests.World;

public sealed class WorldEditorTests
{
    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));

    private static MapDocument LoadMap(int id)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, $"{id}.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Duplicate_deep_copy_modify_copy_leaves_original_unchanged()
    {
        var original = LoadMap(10420);
        var originalSnapshot = CloneViaMapData(original);

        var world = new WorldEditorService().CreateNew();
        var editor = new WorldEditorService();
        var key = editor.AddDocument(world, original, WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, key, 0, 0);

        var dup = editor.DuplicateMap(world, key, 10421);
        var copy = world.Documents[dup.DocumentKey].Document;

        MapCellEditor.SetLayerGfx(copy.Cells[5], MapCellEditor.Layer.Ground, 999);
        copy.BackgroundId = 99;
        MapCellEditor.SyncMapDataString(copy);

        Assert.NotEqual(original.BackgroundId, copy.BackgroundId);
        Assert.True(MapDocumentDuplicator.ContentEquals(original, originalSnapshot));
        Assert.False(MapDocumentDuplicator.ContentEquals(original, copy));
    }

    [Fact]
    public void Local_map_id_proposes_next_free()
    {
        var alloc = new LocalMapIdAllocator();
        var reserved = new HashSet<int> { 10420, 10421, 10422 };
        Assert.Equal(10423, alloc.ProposeNextId(10420, reserved));
        Assert.Equal(10423, alloc.ProposeNextId(10421, reserved));
    }

    [Fact]
    public void Local_map_id_skips_occupied_n_plus_one()
    {
        var alloc = new LocalMapIdAllocator();
        var reserved = new HashSet<int> { 10421 };
        Assert.Equal(10422, alloc.ProposeNextId(10420, reserved));
    }

    [Fact]
    public void World_positions_negative_swap_and_remove()
    {
        var editor = new WorldEditorService();
        var world = editor.CreateNew();
        var a = editor.AddDocument(world, LoadMap(10420), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        var b = editor.AddDocument(world, LoadMap(10421), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, a, -1, 0);
        editor.PlaceAt(world, b, 0, 0);

        Assert.Equal(WorldMoveResult.Occupied, editor.PlaceAt(world, a, 0, 0));
        Assert.Equal(WorldMoveResult.Ok, editor.PlaceAt(world, a, 0, 0, swapIfOccupied: true));
        var placementB = world.Placements.First(p => p.DocumentKey == b);
        Assert.Equal(-1, placementB.WorldX);
        Assert.Equal(0, placementB.WorldY);

        editor.RemoveFromWorld(world, a);
        Assert.Contains(a, world.UnplacedDocumentKeys);
        Assert.DoesNotContain(world.Placements, p => p.DocumentKey == a);
        Assert.True(world.Documents.ContainsKey(a));
    }

    [Fact]
    public void Expand_grid_adds_row_or_column_without_moving_maps()
    {
        var editor = new WorldEditorService();
        var world = editor.CreateNew(gridWidth: 2, gridHeight: 2, originX: 0, originY: 0);
        var key = editor.AddDocument(world, LoadMap(10420), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, key, 0, 0);

        Assert.Equal(WorldGridResizeResult.Ok, editor.ExpandGrid(world, WorldGridEdge.East));
        Assert.Equal(3, world.GridWidth);
        Assert.Equal(2, world.GridHeight);
        Assert.Equal((0, 0), (world.Placements[0].WorldX, world.Placements[0].WorldY));

        Assert.Equal(WorldGridResizeResult.Ok, editor.ExpandGrid(world, WorldGridEdge.West));
        Assert.Equal(4, world.GridWidth);
        Assert.Equal(-1, world.OriginX);
        Assert.Equal((0, 0), (world.Placements[0].WorldX, world.Placements[0].WorldY));

        Assert.Equal(WorldGridResizeResult.Ok, editor.ExpandGrid(world, WorldGridEdge.North));
        Assert.Equal(3, world.GridHeight);
        Assert.Equal(-1, world.OriginY);
    }

    [Fact]
    public void Shrink_grid_unplaces_edge_maps_and_refuses_min_size()
    {
        var editor = new WorldEditorService();
        var world = editor.CreateNew(gridWidth: 2, gridHeight: 1, originX: 0, originY: 0);
        var a = editor.AddDocument(world, LoadMap(10420), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        var b = editor.AddDocument(world, LoadMap(10421), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, a, 0, 0);
        editor.PlaceAt(world, b, 1, 0);

        Assert.Equal(1, editor.CountPlacementsOnEdge(world, WorldGridEdge.East));
        Assert.Equal(WorldGridResizeResult.Ok, editor.ShrinkGrid(world, WorldGridEdge.East, out var removed));
        Assert.Equal(new[] { b }, removed);
        Assert.Equal(1, world.GridWidth);
        Assert.Contains(b, world.UnplacedDocumentKeys);
        Assert.DoesNotContain(world.Placements, p => p.DocumentKey == b);
        Assert.Contains(world.Placements, p => p.DocumentKey == a);

        Assert.Equal(WorldGridResizeResult.MinSize, editor.ShrinkGrid(world, WorldGridEdge.West, out _));
        Assert.Equal(1, world.GridWidth);
    }

    [Fact]
    public void Rufworld_save_load_preserves_placements_and_documents()
    {
        var editor = new WorldEditorService();
        var world = editor.CreateNew("Test");
        var k1 = editor.AddDocument(world, LoadMap(10420), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        var k2 = editor.AddDocument(world, LoadMap(10421), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, k1, -2, 1);
        editor.PlaceAt(world, k2, 3, -1);
        world.View.Zoom = 0.75;
        world.View.MosaicMode = true;

        var path = Path.Combine(Path.GetTempPath(), $"world_{Guid.NewGuid():N}{RufworldFormat.FileExtension}");
        try
        {
            var dto = RufworldSerializer.FromWorld(world);
            RufworldIo.SaveAtomic(path, RufworldSerializer.Serialize(dto));
            var loaded = RufworldSerializer.ToWorld(RufworldSerializer.Deserialize(RufworldIo.LoadFile(path)));

            Assert.Equal(2, loaded.Placements.Count);
            Assert.Equal((-2, 1), (loaded.Placements.First(p => p.DocumentKey == k1).WorldX,
                loaded.Placements.First(p => p.DocumentKey == k1).WorldY));
            Assert.Equal(10421, loaded.Documents[k2].Document.Id);
            Assert.True(loaded.View.MosaicMode);
            Assert.Equal(0.75, loaded.View.Zoom);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Duplicate_places_adjacent_when_right_free()
    {
        var editor = new WorldEditorService();
        var world = editor.CreateNew();
        var key = editor.AddDocument(world, LoadMap(10420), WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        editor.PlaceAt(world, key, 0, 0);
        var dup = editor.DuplicateMap(world, key, 10422);
        Assert.Equal((1, 0), dup.PlacedAt);
    }

    [Fact]
    public void Mosaic_mode_geometry_has_zero_gap()
    {
        var map = LoadMap(10420);
        var (x0, y0, w0, h0) = WorldGeometry.GetMapRect(0, 0, map, mosaicMode: true);
        var (x1, y1, w1, h1) = WorldGeometry.GetMapRect(1, 0, map, mosaicMode: true);
        Assert.Equal(6, WorldGeometry.InfoGapPixels);
        Assert.Equal(x0 + w0, x1);
        Assert.Equal(y0, y1);
        Assert.Equal(w0, w1);
        Assert.Equal(h0, h1);
    }

    private static MapDocument CloneViaMapData(MapDocument source)
    {
        MapCellEditor.SyncMapDataString(source);
        var clone = new MapDocument
        {
            Id = source.Id,
            Width = source.Width,
            Height = source.Height,
            BackgroundId = source.BackgroundId,
            MusicId = source.MusicId,
            AmbianceId = source.AmbianceId,
            Capabilities = source.Capabilities,
            Outdoor = source.Outdoor,
            MapData = source.MapData,
            Cells = source.Cells.Select(MapCellEditor.Clone).ToList(),
        };
        return clone;
    }
}
