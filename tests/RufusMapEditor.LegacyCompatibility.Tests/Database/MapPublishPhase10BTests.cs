using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

public sealed class MapPublishPhase10BTests
{
    private static MapDocument Map(int id = 41010)
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
            WorldX = -47,
            WorldY = -12,
            WorldCoordinatesSet = true,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    private static InMemoryMapasRepository RepoWithDefaults()
    {
        var repo = new InMemoryMapasRepository();
        repo.SetSchema(InMemoryMapasRepository.SchemaWithDbDefaultsForPreserved());
        return repo;
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-10b-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Schema_read_exposes_all_introspection_fields()
    {
        var schema = await RepoWithDefaults().GetTableSchemaAsync();
        var id = schema.Find(MapasColumns.Id)!;
        Assert.Equal(MapasColumns.DefaultDatabase, schema.SchemaName);
        Assert.Equal(MapasColumns.DefaultTable, schema.TableName);
        Assert.Equal("int", id.DataType);
        Assert.NotEmpty(id.ColumnType);
        Assert.False(id.IsNullable);
        Assert.Null(id.ColumnDefault);
        Assert.Equal("PRI", id.ColumnKey);
        Assert.True(id.OrdinalPosition > 0);
        Assert.True(schema.IdIsPrimaryKey);
        Assert.False(schema.IdIsAutoIncrement);
    }

    [Fact]
    public async Task Create_dialog_cancel_does_not_insert()
    {
        var repo = RepoWithDefaults();
        var saves = 0;
        var result = await MapPublishWorkflow.ExecuteAsync(
            Map(),
            new MapPublishService(repo, TempDir()),
            _ => { saves++; return Task.FromResult(true); },
            (_, _, _) => true,
            _ => null,
            (_, _) => false);
        Assert.False(result.Success);
        Assert.Equal(0, repo.InsertCount);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Confirmed_create_saves_before_and_after_insert()
    {
        var repo = RepoWithDefaults();
        var saves = 0;
        var result = await MapPublishWorkflow.ExecuteAsync(
            Map(),
            new MapPublishService(repo, TempDir()),
            _ => { saves++; return Task.FromResult(true); },
            (_, _, _) => true,
            _ => null,
            (_, _) => true);
        Assert.True(result.Success);
        Assert.True(result.Created);
        Assert.Equal(1, repo.InsertCount);
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task Insert_uses_revision_zero_and_exact_editor_values()
    {
        var map = Map();
        var repo = RepoWithDefaults();
        var service = new MapPublishService(repo, TempDir());
        var plan = await service.PrepareCreateAsync(map);
        var result = await service.PublishCreateAsync(map, plan);
        Assert.True(result.Success);
        Assert.True(result.Created);
        Assert.Equal("0", map.DateMap);
        Assert.Equal("0", repo.LastInsert!.EditorValues.Fecha);
        Assert.Equal(map.MapData, repo.LastInsert.EditorValues.MapData);
        Assert.Equal(map.FightPlaces, repo.LastInsert.EditorValues.PosPelea);
        Assert.Equal(-47, repo.LastInsert.EditorValues.X);
        Assert.Equal(-12, repo.LastInsert.EditorValues.Y);
        var intent = await File.ReadAllTextAsync(result.BackupPath!);
        Assert.Contains(map.MapData, intent, StringComparison.Ordinal);
        Assert.DoesNotContain("password", intent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rufus_default_fills_when_no_mysql_default()
    {
        var repo = RepoWithDefaults();
        var schema = (await repo.GetTableSchemaAsync()).Columns.Select(c =>
            c.ColumnName == MapasColumns.MaxPeleas
                ? Column(c.ColumnName, nullable: false, defaultValue: null, c.OrdinalPosition)
                : c).ToList();
        repo.SetSchema(schema);
        var plan = await new MapPublishService(repo, TempDir()).PrepareCreateAsync(Map());
        Assert.True(plan.CanInsert);
        var maxPeleas = Assert.Single(plan.Columns, x => x.ColumnName == MapasColumns.MaxPeleas);
        Assert.Equal(InsertColumnSource.ConfiguredValue, maxPeleas.Source);
        Assert.Equal(99, maxPeleas.Value);
        Assert.Contains("RUFUS", maxPeleas.Display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_value_overrides_rufus_default()
    {
        var repo = RepoWithDefaults();
        var schema = (await repo.GetTableSchemaAsync()).Columns.Select(c =>
            c.ColumnName == MapasColumns.MaxPeleas
                ? Column(c.ColumnName, nullable: false, defaultValue: null, c.OrdinalPosition)
                : c).ToList();
        repo.SetSchema(schema);
        var plan = await new MapPublishService(repo, TempDir()).PrepareCreateAsync(
            Map(), new NewMapDefaultsSettings { MaxPeleas = 17 });
        Assert.True(plan.CanInsert);
        var configured = Assert.Single(plan.Columns, x => x.ColumnName == MapasColumns.MaxPeleas);
        Assert.Equal(InsertColumnSource.ConfiguredValue, configured.Source);
        Assert.Equal(17, configured.Value);
        Assert.DoesNotContain("RUFUS", configured.Display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_default_is_omitted_and_applied()
    {
        var repo = RepoWithDefaults();
        var service = new MapPublishService(repo, TempDir());
        var plan = await service.PrepareCreateAsync(Map());
        Assert.Equal(
            InsertColumnSource.DatabaseDefault,
            plan.Columns.Single(x => x.ColumnName == MapasColumns.MaxGrupoMobs).Source);
        Assert.DoesNotContain(plan.Included, x => x.ColumnName == MapasColumns.MaxGrupoMobs);
        Assert.True((await service.PublishCreateAsync(Map(), plan)).Success);
        Assert.Equal(4, (await repo.TryGetAsync(Map().Id))!.MaxGrupoMobs);
    }

    [Fact]
    public async Task Affected_not_one_fails()
    {
        var repo = RepoWithDefaults();
        repo.NextInsertAffectedRows(0);
        var service = new MapPublishService(repo, TempDir());
        var map = Map();
        var result = await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map));
        Assert.False(result.Success);
        Assert.Contains("affected_rows=0", result.Error);
    }

    [Fact]
    public async Task Verify_mismatch_fails()
    {
        var repo = RepoWithDefaults();
        repo.CorruptNextInsertForVerification();
        var service = new MapPublishService(repo, TempDir());
        var map = Map();
        var result = await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map));
        Assert.False(result.Success);
        Assert.Contains("Verification failed", result.Error);
        Assert.Null(await repo.TryGetAsync(map.Id));
    }

    [Fact]
    public async Task Race_existing_id_fails()
    {
        var repo = RepoWithDefaults();
        var map = Map();
        repo.RaceInsertAsExisting(map.Id);
        var service = new MapPublishService(repo, TempDir());
        var result = await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map));
        Assert.False(result.Success);
        Assert.Contains("ya existe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_publish_uses_update_and_increments_zero_to_one()
    {
        var map = Map();
        var repo = RepoWithDefaults();
        var service = new MapPublishService(repo, TempDir());
        Assert.True((await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map))).Success);
        map.BackgroundId++;
        var prepare = await service.PrepareAsync(map);
        Assert.Equal("1", prepare.NewFecha);
        Assert.True((await service.PublishAsync(map, prepare.NewFecha!)).Success);
        Assert.Equal(1, repo.InsertCount);
        Assert.Equal(1, repo.UpdateCount);
    }

    [Fact]
    public async Task Preserved_fields_come_from_schema_defaults()
    {
        var map = Map();
        var repo = RepoWithDefaults();
        var service = new MapPublishService(repo, TempDir());
        Assert.True((await service.PublishCreateAsync(map, await service.PrepareCreateAsync(map))).Success);
        var row = await repo.TryGetAsync(map.Id);
        Assert.Equal("", row!.Key);
        Assert.Equal("", row.Mobs);
        Assert.Equal(99, row.MaxPeleas);
        Assert.Equal(1, row.MinMobsPorGrupo);
    }

    [Fact]
    public void World_coordinate_flag_roundtrips_rufmap()
    {
        var dto = RufmapSerializer.FromDocument(Map(), Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
        var loaded = RufmapSerializer.ToDocument(RufmapSerializer.DeserializeDto(RufmapSerializer.Serialize(dto)));
        Assert.True(loaded.Document.WorldCoordinatesSet);
        Assert.Equal(-47, loaded.Document.WorldX);
        Assert.Equal(-12, loaded.Document.WorldY);
    }

    [Fact]
    public async Task New_map_without_coords_defaults_to_zero_zero_on_create()
    {
        var map = Map();
        map.WorldCoordinatesSet = false;
        map.WorldX = 99;
        map.WorldY = 99;
        var plan = await new MapPublishService(RepoWithDefaults(), TempDir()).PrepareCreateAsync(map);
        Assert.True(plan.CanInsert);
        Assert.True(map.WorldCoordinatesSet);
        Assert.Equal(0, map.WorldX);
        Assert.Equal(0, map.WorldY);
        Assert.Equal(0, plan.EditorValues.X);
        Assert.Equal(0, plan.EditorValues.Y);
    }

    [Fact]
    public void Phase10A_text_revision_remains_protected()
    {
        Assert.False(RevisionLogic.TryIncrement("VULKANIA", out _, out _));
    }

    private static MapColumnSchema Column(string name, bool nullable, string? defaultValue, int ordinal) => new()
    {
        ColumnName = name,
        DataType = "int",
        ColumnType = "int",
        IsNullable = nullable,
        ColumnDefault = defaultValue,
        ColumnKey = "",
        Extra = "",
        OrdinalPosition = ordinal,
    };
}
