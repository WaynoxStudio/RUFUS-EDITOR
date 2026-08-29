using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

public sealed class MapPublishPhase10ATests
{
    private static MapDocument SampleMap(
        int id = 30010,
        string dateMap = "1",
        int worldX = -47,
        int worldY = 33,
        int backgroundId = 12)
    {
        var cellCount = MapGeometry.CellCount(15, 17);
        var cells = new List<CellData>(cellCount);
        for (var i = 0; i < cellCount; i++)
            cells.Add(new CellData());
        cells[10].GroundGfxId = 111;
        cells[20].FightCell = 1;
        cells[30].FightCell = 2;

        var map = new MapDocument
        {
            Id = id,
            Width = 15,
            Height = 17,
            DateMap = dateMap,
            BackgroundId = backgroundId,
            BackgroundDefined = true,
            MusicId = 3,
            MusicDefined = true,
            AmbianceId = 4,
            AmbianceDefined = true,
            Capabilities = 0,
            CapabilitiesDefined = true,
            Outdoor = true,
            WorldX = worldX,
            WorldY = worldY,
            WorldCoordinatesSet = true,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    private static MapasRow RowFromMap(MapDocument map, string? fechaOverride = null) => new()
    {
        Id = map.Id,
        Fecha = fechaOverride ?? map.DateMap,
        Ancho = map.Width,
        Alto = map.Height,
        BgId = map.BackgroundId,
        MusicId = map.MusicId,
        AmbienteId = map.AmbianceId,
        OutDoor = map.Outdoor == true ? 1 : 0,
        Capabilities = map.Capabilities,
        PosPelea = map.FightPlaces ?? "",
        MapData = map.MapData ?? "",
        X = map.WorldX,
        Y = map.WorldY,
        Key = "KEEPKEY",
        Mobs = "KEEPMOBS",
        SubArea = 99,
        MaxGrupoMobs = 8,
        MaxMobsPorGrupo = 4,
        MinNivelGrupoMob = 1,
        MaxNivelGrupoMob = 50,
        MaxMercantes = 2,
        MaxPeleas = 3,
        MinMobsPorGrupo = 1,
    };

    private static string TempBackupDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rufus-db-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Password_protector_roundtrip_and_not_plaintext()
    {
        const string plain = "s3cret-pass!";
        var protectedB64 = DatabasePasswordProtector.Protect(plain);
        Assert.False(string.IsNullOrWhiteSpace(protectedB64));
        Assert.DoesNotContain(plain, protectedB64, StringComparison.Ordinal);
        Assert.Equal(plain, DatabasePasswordProtector.Unprotect(protectedB64));
    }

    [Fact]
    public void Settings_serialization_stores_protected_password_only()
    {
        var s = new DatabaseSettings
        {
            Host = "127.0.0.1",
            Port = 3306,
            User = "editor",
            Database = MapasColumns.DefaultDatabase,
            Table = MapasColumns.DefaultTable,
            PasswordProtectedBase64 = DatabasePasswordProtector.Protect("hidden"),
        };
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        Assert.DoesNotContain("hidden", json, StringComparison.Ordinal);
        Assert.Contains("PasswordProtectedBase64", json, StringComparison.Ordinal);
        Assert.Equal("estaticos", MapasColumns.DefaultDatabase);
        Assert.Equal("mapas", MapasColumns.DefaultTable);
    }

    [Fact]
    public async Task Connection_validation_ok_on_inmemory()
    {
        await new InMemoryMapasRepository().TestConnectionAsync();
    }

    [Fact]
    public async Task Schema_validation_reports_missing_columns()
    {
        var repo = new InMemoryMapasRepository();
        repo.RemoveColumn(MapasColumns.PosPelea);
        var svc = new MapPublishService(repo, TempBackupDir());
        var schema = await svc.ValidateSchemaAsync();
        Assert.False(schema.Ok);
        Assert.Contains(MapasColumns.PosPelea, schema.Missing);
    }

    [Fact]
    public async Task Schema_validation_ok_when_required_present()
    {
        var svc = new MapPublishService(new InMemoryMapasRepository(), TempBackupDir());
        Assert.True((await svc.ValidateSchemaAsync()).Ok);
    }

    [Fact]
    public async Task Row_read_returns_seeded_map()
    {
        var map = SampleMap();
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(map));
        var row = await repo.TryGetAsync(30010);
        Assert.NotNull(row);
        Assert.Equal(-47, row!.X);
        Assert.Equal(33, row.Y);
    }

    [Fact]
    public void Numeric_revision_increments()
    {
        Assert.True(RevisionLogic.TryIncrement("2", out var next, out _));
        Assert.Equal("3", next);
        Assert.True(RevisionLogic.IsNumeric("0"));
    }

    [Fact]
    public void Text_revision_is_protected()
    {
        Assert.False(RevisionLogic.TryIncrement("VULKANIA", out _, out var err));
        Assert.Contains("VULKANIA", err, StringComparison.Ordinal);
        Assert.False(RevisionLogic.IsNumeric("VULKANIA"));
    }

    [Fact]
    public void Negative_world_xy_preserved_in_values()
    {
        var map = SampleMap(worldX: -47, worldY: 33);
        var values = MapPublishLogic.FromDocument(map, "2");
        Assert.Equal(-47, values.X);
        Assert.Equal(33, values.Y);
    }

    [Fact]
    public void FightPlaces_and_MapData_exact_from_document()
    {
        var map = SampleMap();
        var values = MapPublishLogic.FromDocument(map, "2");
        Assert.Equal(map.FightPlaces, values.PosPelea);
        Assert.Equal(map.MapData, values.MapData);
        Assert.Contains('|', values.PosPelea);
    }

    [Fact]
    public void Diff_marks_content_changes()
    {
        var map = SampleMap();
        var db = RowFromMap(map);
        db = new MapasRow
        {
            Id = db.Id,
            Fecha = db.Fecha,
            Ancho = db.Ancho,
            Alto = db.Alto,
            BgId = db.BgId,
            MusicId = db.MusicId,
            AmbienteId = db.AmbienteId,
            OutDoor = db.OutDoor,
            Capabilities = db.Capabilities,
            PosPelea = db.PosPelea,
            MapData = "OLDDATA",
            X = db.X,
            Y = db.Y,
            Key = db.Key,
            Mobs = db.Mobs,
            SubArea = db.SubArea,
            MaxGrupoMobs = db.MaxGrupoMobs,
            MaxMobsPorGrupo = db.MaxMobsPorGrupo,
            MinNivelGrupoMob = db.MinNivelGrupoMob,
            MaxNivelGrupoMob = db.MaxNivelGrupoMob,
            MaxMercantes = db.MaxMercantes,
            MaxPeleas = db.MaxPeleas,
            MinMobsPorGrupo = db.MinMobsPorGrupo,
        };
        var diff = MapPublishLogic.BuildDiff(db, map, "2");
        Assert.True(diff.HasContentChange);
        Assert.Contains("MODIFICADO", diff.MapData.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_whitelist_does_not_touch_preserved_fields()
    {
        var baseline = SampleMap(backgroundId: 12);
        var map = SampleMap(backgroundId: 42);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(baseline, "1"));
        var svc = new MapPublishService(repo, TempBackupDir());

        var prep = await svc.PrepareAsync(map);
        Assert.True(prep.Success);
        Assert.False(prep.NoChanges);
        var pub = await svc.PublishAsync(map, prep.NewFecha!);
        Assert.True(pub.Success);
        Assert.Equal(1, repo.UpdateCount);
        Assert.Equal(42, repo.LastUpdate!.BgId);

        var after = await repo.TryGetAsync(map.Id);
        Assert.Equal("KEEPKEY", after!.Key);
        Assert.Equal("KEEPMOBS", after.Mobs);
        Assert.Equal(99, after.SubArea);
        Assert.Equal(8, after.MaxGrupoMobs);
        Assert.Equal(4, after.MaxMobsPorGrupo);
        Assert.Equal(1, after.MinNivelGrupoMob);
        Assert.Equal(50, after.MaxNivelGrupoMob);
        Assert.Equal(2, after.MaxMercantes);
        Assert.Equal(3, after.MaxPeleas);
        Assert.Equal(1, after.MinMobsPorGrupo);
    }

    [Fact]
    public async Task No_changes_skips_update_and_revision()
    {
        var map = SampleMap();
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(map, map.DateMap));
        var svc = new MapPublishService(repo, TempBackupDir());
        var prep = await svc.PrepareAsync(map);
        Assert.True(prep.NoChanges);
        Assert.Equal(0, repo.UpdateCount);
        Assert.Equal(map.DateMap, prep.NewFecha);
    }

    [Fact]
    public async Task Missing_row_does_not_insert()
    {
        var map = SampleMap(id: 999999);
        var repo = new InMemoryMapasRepository();
        var svc = new MapPublishService(repo, TempBackupDir());
        var prep = await svc.PrepareAsync(map);
        Assert.False(prep.Success);
        Assert.True(prep.MissingRow);
        Assert.Equal(0, repo.UpdateCount);
        Assert.Contains("no existe", prep.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_revision_requires_manual_without_auto_increment()
    {
        var map = SampleMap(backgroundId: 7);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(SampleMap(), "VULKANIA"));
        var svc = new MapPublishService(repo, TempBackupDir());
        var prep = await svc.PrepareAsync(map);
        Assert.False(prep.Success);
        Assert.True(prep.NeedsManualRevision);
        Assert.Equal(0, repo.UpdateCount);

        var withManual = await svc.PrepareAsync(map, "10");
        Assert.True(withManual.Success);
        Assert.Equal("10", withManual.NewFecha);
    }

    [Fact]
    public async Task Publish_creates_backup_and_updates_local_datemap()
    {
        var map = SampleMap(dateMap: "5", backgroundId: 9);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(SampleMap(dateMap: "5"), "5"));
        var backupDir = TempBackupDir();
        var svc = new MapPublishService(repo, backupDir);
        var prep = await svc.PrepareAsync(map);
        Assert.Equal("6", prep.NewFecha);
        var pub = await svc.PublishAsync(map, prep.NewFecha!);
        Assert.True(pub.Success);
        Assert.False(string.IsNullOrWhiteSpace(pub.BackupPath));
        Assert.True(File.Exists(pub.BackupPath!));
        Assert.Equal("6", map.DateMap);
        var json = await File.ReadAllTextAsync(pub.BackupPath!);
        Assert.Contains("KEEPKEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_failure_rolls_back_outcome_without_marking_success()
    {
        var map = SampleMap();
        map.Capabilities = 1;
        MapCellEditor.SyncDocument(map);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(SampleMap(), "1"));
        repo.FailNextUpdate();
        var svc = new MapPublishService(repo, TempBackupDir());
        var pub = await svc.PublishAsync(map, "2");
        Assert.False(pub.Success);
        Assert.Equal(0, repo.UpdateCount);
        Assert.Equal("1", (await repo.TryGetAsync(map.Id))!.Fecha);
    }

    [Fact]
    public async Task Official_save_failure_blocks_publish()
    {
        var map = SampleMap(backgroundId: 1);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(SampleMap(), "1"));
        var svc = new MapPublishService(repo, TempBackupDir());

        var outcome = await MapPublishWorkflow.ExecuteAsync(
            map,
            svc,
            _ => Task.FromResult(false),
            (_, _, _) => true,
            _ => null);

        Assert.False(outcome.Success);
        Assert.Equal(0, repo.UpdateCount);
        Assert.Contains("Official Save", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Workflow_publishes_with_confirm_and_second_save()
    {
        var map = SampleMap(backgroundId: 55);
        var repo = new InMemoryMapasRepository();
        repo.Seed(RowFromMap(SampleMap(), "3"));
        var svc = new MapPublishService(repo, TempBackupDir());
        var saves = 0;

        var outcome = await MapPublishWorkflow.ExecuteAsync(
            map,
            svc,
            _ =>
            {
                saves++;
                return Task.FromResult(true);
            },
            (_, _, _) => true,
            _ => null);

        Assert.True(outcome.Success);
        Assert.Equal(2, saves);
        Assert.Equal(1, repo.UpdateCount);
        Assert.Equal("4", map.DateMap);
    }

    [Fact]
    public void Rufmap_persists_world_xy()
    {
        var map = SampleMap(worldX: -47, worldY: 33);
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        Assert.Equal(-47, dto.Map.WorldX);
        Assert.Equal(33, dto.Map.WorldY);
        var json = RufmapSerializer.Serialize(dto);
        var loaded = RufmapSerializer.DeserializeDto(json);
        Assert.Equal(-47, loaded.Map.WorldX);
        Assert.Equal(33, loaded.Map.WorldY);
    }

    [Fact]
    public void Updated_columns_whitelist_excludes_preserved()
    {
        foreach (var p in MapasColumns.Preserved)
            Assert.DoesNotContain(p, MapasColumns.Updated);
        Assert.Contains(MapasColumns.MapData, MapasColumns.Updated);
        Assert.Contains(MapasColumns.X, MapasColumns.Updated);
        Assert.Contains(MapasColumns.Y, MapasColumns.Updated);
        Assert.Contains(MapasColumns.Fecha, MapasColumns.Updated);
        Assert.Contains(MapasColumns.PosPelea, MapasColumns.Updated);
        Assert.DoesNotContain(MapasColumns.Id, MapasColumns.Updated);
    }
}
