using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

/// <summary>HOTFIX 10A.2 — safe BD metadata hydration; no production writes.</summary>
public sealed class MapPublishHotfix10A2Tests
{
    private static MapDocument LegacyDirty10420Editor()
    {
        var cells = Enumerable.Range(0, MapGeometry.CellCount(15, 17)).Select(_ => new CellData()).ToList();
        cells[5].FightCell = 1;
        var map = new MapDocument
        {
            Id = 10420,
            Width = 15,
            Height = 17,
            DateMap = "AME",
            BackgroundId = 284,
            MusicId = 15,
            AmbianceId = 0,
            Capabilities = 0,
            Outdoor = null,
            WorldX = 0,
            WorldY = 0,
            WorldCoordinatesSet = false,
            Cells = cells,
            FightPlaces = "a|b",
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    private static MapasRow Db10420Matching(MapDocument map) => new()
    {
        Id = 10420,
        Fecha = "0706141524",
        Ancho = map.Width,
        Alto = map.Height,
        BgId = 0,
        MusicId = 0,
        AmbienteId = 4,
        OutDoor = 1,
        Capabilities = 4,
        PosPelea = map.FightPlaces ?? "",
        MapData = map.MapData ?? "",
        X = -45,
        Y = -2,
        Key = "KEEP",
        Mobs = "KEEPMOBS",
    };

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "rufus-10a2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Legacy_rufmap_missing_XY_loads_as_undefined_not_zero_sentinel()
    {
        var map = LegacyDirty10420Editor();
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        Assert.False(dto.Map.WorldCoordinatesSet);
        var json = RufmapSerializer.Serialize(dto);
        json = json.Replace("\"worldCoordinatesSet\": false,", "", StringComparison.Ordinal);
        json = json.Replace("\"worldCoordinatesSet\":false,", "", StringComparison.Ordinal);
        var loaded = RufmapSerializer.ToDocument(RufmapSerializer.DeserializeDto(json));
        Assert.False(loaded.Document.WorldCoordinatesSet);
        Assert.False(loaded.Document.BackgroundDefined);
    }

    [Fact]
    public void Undefined_world_coords_are_not_equal_to_zero_zero_for_publish()
    {
        var map = LegacyDirty10420Editor();
        map.FightPlaces = "db|fp";
        MapCellEditor.SyncDocument(map);
        var db = Db10420Matching(map);

        Assert.True(MapPublishLogic.ContentMatchesDb(db, map));
        var diff = MapPublishLogic.BuildDiff(db, map, "0706141525");
        Assert.False(diff.WorldX.Changed);
        Assert.False(diff.WorldY.Changed);
        Assert.Equal("-45", diff.WorldX.Before);
        Assert.Equal("-45", diff.WorldX.After);
        Assert.Equal("-2", diff.WorldY.Before);
        Assert.Equal("-2", diff.WorldY.After);
        Assert.False(diff.Background.Changed);
        Assert.False(diff.Music.Changed);
        Assert.False(diff.Ambiance.Changed);
        Assert.False(diff.Outdoor.Changed);
        Assert.False(diff.Capabilities.Changed);
    }

    [Fact]
    public void Hydrate_XY_negative_from_db_via_sync()
    {
        var map = LegacyDirty10420Editor();
        var beforeMd = map.MapData;
        var snap = MapPublishLogic.SyncMetadataFromDatabase(map, Db10420Matching(map));
        Assert.Equal(-45, map.WorldX);
        Assert.Equal(-2, map.WorldY);
        Assert.True(map.WorldCoordinatesSet);
        Assert.Equal(0, map.BackgroundId);
        Assert.Equal(0, map.MusicId);
        Assert.Equal(4, map.AmbianceId);
        Assert.True(map.Outdoor);
        Assert.Equal(4, map.Capabilities);
        Assert.Equal(beforeMd, map.MapData);
        Assert.Equal(-45, snap.X);
        Assert.Equal(-2, snap.Y);
    }

    [Fact]
    public async Task Untouched_undefined_metadata_excluded_from_UPDATE()
    {
        var map = LegacyDirty10420Editor();
        map.FightPlaces = "db|fp";
        MapCellEditor.SyncDocument(map);
        var baseline = Db10420Matching(map);

        map.Cells[5].GroundGfxId = 999;
        MapCellEditor.SyncDocument(map);

        var repo = new InMemoryMapasRepository();
        repo.Seed(baseline);
        var svc = new MapPublishService(repo, TempDir());

        var prep = await svc.PrepareAsync(map);
        Assert.True(prep.Success);
        Assert.False(prep.NoChanges);
        Assert.False(prep.Diff!.Background.Changed);
        Assert.False(prep.Diff.WorldX.Changed);
        Assert.False(prep.Diff.WorldY.Changed);
        Assert.True(prep.Diff.MapData.Changed);

        var pub = await svc.PublishAsync(map, prep.NewFecha!);
        Assert.True(pub.Success);
        Assert.Equal(1, repo.UpdateCount);
        var cols = repo.LastUpdate!.EffectiveUpdateColumns;
        Assert.DoesNotContain(MapasColumns.X, cols);
        Assert.DoesNotContain(MapasColumns.Y, cols);
        Assert.DoesNotContain(MapasColumns.BgId, cols);
        Assert.DoesNotContain(MapasColumns.MusicId, cols);
        Assert.DoesNotContain(MapasColumns.AmbienteId, cols);
        Assert.DoesNotContain(MapasColumns.OutDoor, cols);
        Assert.DoesNotContain(MapasColumns.Capabilities, cols);
        Assert.Contains(MapasColumns.MapData, cols);

        var after = await repo.TryGetAsync(10420);
        Assert.Equal(-45, after!.X);
        Assert.Equal(-2, after.Y);
        Assert.Equal(0, after.BgId);
        Assert.Equal(4, after.AmbienteId);
        Assert.Equal(1, after.OutDoor);
        Assert.Equal(4, after.Capabilities);
        Assert.Equal(map.MapData, after.MapData);
        Assert.Equal("0706141525", after.Fecha);
    }

    [Fact]
    public async Task Explicit_edit_of_X_is_included_in_UPDATE()
    {
        var map = LegacyDirty10420Editor();
        map.FightPlaces = "db|fp";
        MapCellEditor.SyncDocument(map);
        var baseline = Db10420Matching(map);

        map.WorldX = -44;
        map.WorldY = -2;
        map.WorldCoordinatesSet = true;

        var repo = new InMemoryMapasRepository();
        repo.Seed(baseline);
        var svc = new MapPublishService(repo, TempDir());
        var prep = await svc.PrepareAsync(map);
        Assert.True(prep.Diff!.WorldX.Changed);
        Assert.Equal("-45", prep.Diff.WorldX.Before);
        Assert.Equal("-44", prep.Diff.WorldX.After);

        var pub = await svc.PublishAsync(map, prep.NewFecha!);
        Assert.True(pub.Success);
        Assert.Contains(MapasColumns.X, repo.LastUpdate!.EffectiveUpdateColumns);
        var after = await repo.TryGetAsync(10420);
        Assert.Equal(-44, after!.X);
        Assert.Equal(-2, after.Y);
    }

    [Fact]
    public async Task Sync_metadata_does_not_write_db_and_does_not_touch_MapData()
    {
        var map = LegacyDirty10420Editor();
        var beforeMd = map.MapData;
        var beforeFp = map.FightPlaces;
        var repo = new InMemoryMapasRepository();
        repo.Seed(Db10420Matching(map));
        var svc = new MapPublishService(repo, TempDir());

        var (ok, err, snap) = await svc.SyncMetadataFromDatabaseAsync(map);
        Assert.True(ok);
        Assert.Null(err);
        Assert.NotNull(snap);
        Assert.Equal(0, repo.UpdateCount);
        Assert.Equal(0, repo.InsertCount);
        Assert.Equal(beforeMd, map.MapData);
        Assert.Equal(beforeFp, map.FightPlaces);
        Assert.Equal(-45, map.WorldX);
        Assert.Equal(-2, map.WorldY);
        Assert.Equal("0706141524", map.DateMap);
    }

    [Fact]
    public void Revision_0706141524_increments_to_0706141525()
    {
        Assert.True(RevisionLogic.TryIncrement("0706141524", out var next, out _));
        Assert.Equal("0706141525", next);
    }

    [Fact]
    public async Task Case_10420_comparator_after_resolve_has_no_false_default_diffs()
    {
        var map = LegacyDirty10420Editor();
        map.FightPlaces = "db|fp";
        MapCellEditor.SyncDocument(map);
        var repo = new InMemoryMapasRepository();
        repo.Seed(Db10420Matching(map));
        var svc = new MapPublishService(repo, TempDir());
        var prep = await svc.PrepareAsync(map);
        Assert.True(prep.Success);
        Assert.True(prep.NoChanges);
        Assert.Equal(0, repo.UpdateCount);
    }
}
