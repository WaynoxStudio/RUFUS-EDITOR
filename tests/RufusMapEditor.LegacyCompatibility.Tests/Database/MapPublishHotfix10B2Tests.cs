using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

/// <summary>HOTFIX 10B.2 — new-map CREATE defaults without blocking on empty X/Y or key/mobs/subArea.</summary>
public sealed class MapPublishHotfix10B2Tests
{
    private static MapDocument NewMap(int id = 61010, int? x = null, int? y = null)
    {
        var cells = Enumerable.Range(0, MapGeometry.CellCount(15, 17))
            .Select(_ => new CellData())
            .ToList();
        cells[4].FightCell = 1;
        cells[8].FightCell = 2;
        var map = new MapDocument
        {
            Id = id,
            Width = 15,
            Height = 17,
            DateMap = "AME",
            BackgroundId = 12,
            BackgroundDefined = true,
            MusicId = 3,
            MusicDefined = true,
            AmbianceId = 4,
            AmbianceDefined = true,
            Outdoor = true,
            Capabilities = 7,
            CapabilitiesDefined = true,
            WorldX = x ?? 0,
            WorldY = y ?? 0,
            WorldCoordinatesSet = x is not null && y is not null,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-10b2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static InMemoryMapasRepository RepoNoMysqlDefaults()
    {
        var repo = new InMemoryMapasRepository();
        repo.SetSchema(InMemoryMapasRepository.SchemaWithoutDbDefaultsForPreserved());
        return repo;
    }

    [Fact]
    public async Task New_map_without_xy_creates_at_zero_zero()
    {
        var map = NewMap();
        Assert.False(map.WorldCoordinatesSet);
        var repo = RepoNoMysqlDefaults();
        var service = new MapPublishService(repo, TempDir());
        var plan = await service.PrepareCreateAsync(map);
        Assert.True(plan.CanInsert);
        var result = await service.PublishCreateAsync(map, plan);
        Assert.True(result.Success);
        Assert.Equal(0, map.WorldX);
        Assert.Equal(0, map.WorldY);
        Assert.True(map.WorldCoordinatesSet);
        var row = await repo.TryGetAsync(map.Id);
        Assert.Equal(0, row!.X);
        Assert.Equal(0, row.Y);
    }

    [Fact]
    public async Task New_map_explicit_xy_is_respected()
    {
        var map = NewMap(x: -47, y: 33);
        var repo = RepoNoMysqlDefaults();
        var service = new MapPublishService(repo, TempDir());
        var plan = await service.PrepareCreateAsync(map);
        Assert.True(plan.CanInsert);
        Assert.Equal(-47, plan.EditorValues.X);
        Assert.Equal(33, plan.EditorValues.Y);
        Assert.True((await service.PublishCreateAsync(map, plan)).Success);
        var row = await repo.TryGetAsync(map.Id);
        Assert.Equal(-47, row!.X);
        Assert.Equal(33, row.Y);
    }

    [Fact]
    public async Task New_map_gets_rufus_key_mobs_subarea_and_secondary_defaults()
    {
        var map = NewMap(x: 1, y: 2);
        var repo = RepoNoMysqlDefaults();
        var service = new MapPublishService(repo, TempDir());
        var plan = await service.PrepareCreateAsync(map);
        Assert.True(plan.CanInsert);

        object? Val(string col) => plan.Columns.Single(c => c.ColumnName == col).Value;
        Assert.Equal("", Val(MapasColumns.Key));
        Assert.Equal("", Val(MapasColumns.Mobs));
        Assert.Equal(0, Val(MapasColumns.SubArea));
        Assert.Equal(4, Val(MapasColumns.MaxGrupoMobs));
        Assert.Equal(8, Val(MapasColumns.MaxMobsPorGrupo));
        Assert.Equal(0, Val(MapasColumns.MinNivelGrupoMob));
        Assert.Equal(0, Val(MapasColumns.MaxNivelGrupoMob));
        Assert.Equal(5, Val(MapasColumns.MaxMercantes));
        Assert.Equal(99, Val(MapasColumns.MaxPeleas));
        Assert.Equal(1, Val(MapasColumns.MinMobsPorGrupo));

        Assert.True((await service.PublishCreateAsync(map, plan)).Success);
        var row = await repo.TryGetAsync(map.Id);
        Assert.Equal("", row!.Key);
        Assert.Equal("", row.Mobs);
        Assert.Equal(0, row.SubArea);
        Assert.Equal(4, row.MaxGrupoMobs);
        Assert.Equal(8, row.MaxMobsPorGrupo);
        Assert.Equal(0, row.MinNivelGrupoMob);
        Assert.Equal(0, row.MaxNivelGrupoMob);
        Assert.Equal(5, row.MaxMercantes);
        Assert.Equal(99, row.MaxPeleas);
        Assert.Equal(1, row.MinMobsPorGrupo);
    }

    [Fact]
    public void Legacy_without_xy_stays_undefined_until_create_ensure()
    {
        var map = NewMap();
        Assert.False(map.WorldCoordinatesSet);
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        var loaded = RufmapSerializer.ToDocument(RufmapSerializer.DeserializeDto(RufmapSerializer.Serialize(dto)));
        Assert.False(loaded.Document.WorldCoordinatesSet);
        Assert.Throws<InvalidOperationException>(() => MapPublishLogic.FromDocument(loaded.Document, "1"));
    }

    [Fact]
    public async Task Existing_map_update_does_not_apply_new_map_defaults()
    {
        var map = NewMap(x: -10, y: 20);
        var repo = RepoNoMysqlDefaults();
        var service = new MapPublishService(repo, TempDir());
        Assert.True((await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map))).Success);

        var before = await repo.TryGetAsync(map.Id);
        Assert.Equal("", before!.Key);
        // Simulate server-side metadata that must survive UPDATE (10A).
        repo.Seed(new MapasRow
        {
            Id = before.Id,
            Fecha = before.Fecha,
            Ancho = before.Ancho,
            Alto = before.Alto,
            BgId = before.BgId,
            MusicId = before.MusicId,
            AmbienteId = before.AmbienteId,
            OutDoor = before.OutDoor,
            Capabilities = before.Capabilities,
            PosPelea = before.PosPelea,
            MapData = before.MapData,
            X = before.X,
            Y = before.Y,
            Key = "keep-key",
            Mobs = "1,2,3",
            SubArea = 42,
            MaxGrupoMobs = before.MaxGrupoMobs,
            MaxMobsPorGrupo = before.MaxMobsPorGrupo,
            MinNivelGrupoMob = before.MinNivelGrupoMob,
            MaxNivelGrupoMob = before.MaxNivelGrupoMob,
            MaxMercantes = before.MaxMercantes,
            MaxPeleas = 12,
            MinMobsPorGrupo = before.MinMobsPorGrupo,
        });

        map.BackgroundId++;
        var prepare = await service.PrepareAsync(map);
        Assert.True(prepare.Success);
        Assert.False(prepare.MissingRow);
        Assert.True((await service.PublishAsync(map, prepare.NewFecha!)).Success);
        Assert.Equal(1, repo.InsertCount);
        Assert.Equal(1, repo.UpdateCount);

        var after = await repo.TryGetAsync(map.Id);
        Assert.Equal("keep-key", after!.Key);
        Assert.Equal("1,2,3", after.Mobs);
        Assert.Equal(42, after.SubArea);
        Assert.Equal(12, after.MaxPeleas);
    }

    [Fact]
    public async Task Second_publish_uses_update_not_insert_again()
    {
        var map = NewMap(x: 3, y: 4);
        var repo = RepoNoMysqlDefaults();
        var service = new MapPublishService(repo, TempDir());
        Assert.True((await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map))).Success);
        map.MusicId++;
        Assert.True((await service.PublishAsync(map, (await service.PrepareAsync(map)).NewFecha!)).Success);
        Assert.Equal(1, repo.InsertCount);
        Assert.Equal(1, repo.UpdateCount);
    }
}
