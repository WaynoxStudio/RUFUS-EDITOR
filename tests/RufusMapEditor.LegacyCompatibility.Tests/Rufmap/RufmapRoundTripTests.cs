using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Sql;

namespace RufusMapEditor.LegacyCompatibility.Tests.Rufmap;

public sealed class RufmapRoundTripTests
{
    private static string FixturesRoot
    {
        get
        {
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
            if (Directory.Exists(fromProject))
                return fromProject;
            var fromCwd = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "maps"));
            if (Directory.Exists(fromCwd))
                return fromCwd;
            throw new DirectoryNotFoundException("Could not locate tests/fixtures/maps.");
        }
    }

    private static string ArtifactsRoot
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "rufmap"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string RufmapFixturesRoot
    {
        get
        {
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "rufmap"));
            if (Directory.Exists(fromProject))
                return fromProject;
            throw new DirectoryNotFoundException("Could not locate tests/fixtures/rufmap.");
        }
    }

    private static MapDocument LoadDecoded(string sqlFileName)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, sqlFileName));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Map_10420_edit_save_load_cells_and_MapData_identical()
    {
        var map = LoadDecoded("10420.sql");
        map.BackgroundId = 12;
        map.MusicId = 3;

        MapCellEditor.SetLayerGfx(map.Cells[10], MapCellEditor.Layer.Ground, 111, flip: true, rotation: 2);
        MapCellEditor.SetLayerGfx(map.Cells[11], MapCellEditor.Layer.Object1, 222, flip: false, rotation: 1);
        MapCellEditor.SetLayerGfx(map.Cells[12], MapCellEditor.Layer.Object2, 333, flip: true, rotation: 0);
        map.Cells[13].LineOfSight = false;
        map.Cells[14].Movement = MovementType.Unwalkable;
        map.Cells[15].GroundLevel = 9;
        MapCellEditor.SyncMapDataString(map);
        var expectedMapData = map.MapData;

        var path = Path.Combine(ArtifactsRoot, "10420_edit_roundtrip.rufmap");
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            new RufmapSourceDto { Kind = "LegacyAstria", OriginalMapId = 10420 });
        RufmapIo.SaveAtomic(path, RufmapSerializer.Serialize(dto));

        var loaded = RufmapIo.LoadFile(path);
        Assert.Equal(map.Id, loaded.Document.Id);
        Assert.Equal(map.Width, loaded.Document.Width);
        Assert.Equal(map.Height, loaded.Document.Height);
        Assert.Equal(map.BackgroundId, loaded.Document.BackgroundId);
        Assert.Equal(map.MusicId, loaded.Document.MusicId);
        Assert.Equal(map.Cells.Count, loaded.Document.Cells.Count);

        for (var i = 0; i < map.Cells.Count; i++)
            Assert.True(CellSnapshot.Capture(i, map.Cells[i]).ContentEquals(CellSnapshot.Capture(i, loaded.Document.Cells[i])),
                $"cell {i}");

        Assert.Equal(expectedMapData, MapDataCodec.EncodeMap(loaded.Document.Cells.ToList()));
        Assert.Equal(expectedMapData, loaded.Document.MapData);
    }

    [Fact]
    public void All_fixture_maps_legacy_to_rufmap_to_MapData()
    {
        var sqlFiles = Directory.GetFiles(FixturesRoot, "*.sql").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(30, sqlFiles.Length);

        var failures = new List<string>();
        var tmp = Path.Combine(ArtifactsRoot, "all_roundtrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        foreach (var sql in sqlFiles)
        {
            try
            {
                var map = AstriaSqlMapParser.ParseFile(sql);
                map.Cells = MapDataCodec.DecodeMap(map.MapData);
                if (!string.IsNullOrEmpty(map.Key))
                {
                    failures.Add($"{map.Id}: encrypted");
                    continue;
                }

                var expected = map.MapData;
                var path = Path.Combine(tmp, $"{map.Id}.rufmap");
                var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
                RufmapIo.SaveAtomic(path, RufmapSerializer.Serialize(dto), writeBackup: false);
                var loaded = RufmapIo.LoadFile(path);
                var encoded = MapDataCodec.EncodeMap(loaded.Document.Cells.ToList());
                if (!string.Equals(expected, encoded, StringComparison.Ordinal))
                    failures.Add($"{map.Id}: MapData mismatch");
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(sql)}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Atomic_save_preserves_previous_file_when_replace_fails()
    {
        var map = LoadDecoded("10420.sql");
        var path = Path.Combine(ArtifactsRoot, "atomic_protect.rufmap");
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        var originalJson = RufmapSerializer.Serialize(dto);
        RufmapIo.SaveAtomic(path, originalJson, writeBackup: false);

        map.Cells[0].GroundGfxId = 99999;
        MapCellEditor.SyncMapDataString(map);
        var newJson = RufmapSerializer.Serialize(
            RufmapSerializer.FromDocument(map, dto.DocumentId, dto.CreatedUtc, null));

        using (var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var threw = false;
            try
            {
                RufmapIo.SaveAtomic(path, newJson, writeBackup: true);
            }
            catch (IOException)
            {
                threw = true;
            }

            Assert.True(threw, "Expected IOException while destination locked");
        }

        var after = File.ReadAllText(path);
        Assert.Equal(originalJson, after);
        var reloaded = RufmapSerializer.LoadFromJson(after);
        Assert.NotEqual(99999, reloaded.Document.Cells[0].GroundGfxId);
    }

    [Fact]
    public void Future_version_refuses_load()
    {
        var path = Path.Combine(RufmapFixturesRoot, "future_v99.rufmap");
        var ex = Assert.Throws<RufmapException>(() => RufmapIo.LoadFile(path));
        Assert.Equal(RufmapLoadErrorKind.UnsupportedFutureVersion, ex.Kind);
        Assert.Contains("más reciente", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_json_refuses_load()
    {
        var path = Path.Combine(RufmapFixturesRoot, "corrupt.json.rufmap");
        var ex = Assert.Throws<RufmapException>(() => RufmapIo.LoadFile(path));
        Assert.Equal(RufmapLoadErrorKind.CorruptJson, ex.Kind);
    }

    [Fact]
    public void Missing_required_fields_refuses_load()
    {
        var path = Path.Combine(RufmapFixturesRoot, "missing_map.rufmap");
        var ex = Assert.Throws<RufmapException>(() => RufmapIo.LoadFile(path));
        Assert.True(
            ex.Kind is RufmapLoadErrorKind.MissingRequiredData or RufmapLoadErrorKind.CorruptJson,
            ex.Kind.ToString());
    }

    [Fact]
    public void Valid_v1_fixture_loads()
    {
        var path = Path.Combine(RufmapFixturesRoot, "valid_v1_minimal.rufmap");
        var loaded = RufmapIo.LoadFile(path);
        Assert.Equal(1, loaded.File.FormatVersion);
        Assert.Equal(2, loaded.Document.Cells.Count);
    }

    [Fact]
    public void Dirty_saved_point_across_undo_redo()
    {
        var map = LoadDecoded("10420.sql");
        var history = new EditHistory();
        history.MarkClean();
        Assert.False(history.IsDirty);

        var cmdA = CellBatchEditCommand.Build("A", map, new[] { 0 }, (_, c) => c.GroundGfxId = map.Cells[0].GroundGfxId + 1);
        Assert.NotNull(cmdA);
        history.PushExecuted(cmdA!);
        Assert.True(history.IsDirty);

        history.MarkClean(); // simulate Save
        Assert.False(history.IsDirty);

        var cmdB = CellBatchEditCommand.Build("B", map, new[] { 0 }, (_, c) => c.GroundGfxId = map.Cells[0].GroundGfxId + 1);
        Assert.NotNull(cmdB);
        history.PushExecuted(cmdB!);
        Assert.True(history.IsDirty);

        history.Undo(map); // back to saved
        Assert.False(history.IsDirty);

        history.Undo(map); // before save
        Assert.True(history.IsDirty);

        history.Redo(map); // to saved
        Assert.False(history.IsDirty);

        history.Redo(map); // past save
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void Autosave_json_does_not_affect_clean_marker_semantics()
    {
        // Autosave writes same schema; MarkClean only happens on manual save.
        var map = LoadDecoded("10420.sql");
        var history = new EditHistory();
        var cmd = CellBatchEditCommand.Build("A", map, new[] { 1 }, (_, c) => c.GroundGfxId = map.Cells[1].GroundGfxId + 7);
        Assert.NotNull(cmd);
        history.PushExecuted(cmd!);
        Assert.True(history.IsDirty);

        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        var json = RufmapSerializer.Serialize(dto);
        var reloaded = RufmapSerializer.LoadFromJson(json);
        Assert.Equal(map.Cells[1].GroundGfxId, reloaded.Document.Cells[1].GroundGfxId);
        // History on original session still dirty
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void Extension_alone_is_not_trusted()
    {
        var path = Path.Combine(ArtifactsRoot, "fake.rufmap");
        File.WriteAllText(path, "{ \"formatVersion\": 1 }");
        var ex = Assert.Throws<RufmapException>(() => RufmapIo.LoadFile(path));
        Assert.NotEqual(RufmapLoadErrorKind.None, ex.Kind);
    }
}
