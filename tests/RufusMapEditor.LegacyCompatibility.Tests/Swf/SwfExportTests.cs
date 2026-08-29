using System.Text;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.LegacyCompatibility.Swf;

namespace RufusMapEditor.LegacyCompatibility.Tests.Swf;

public sealed class SwfExportTests
{
    private static string AstriaRoot
    {
        get
        {
            var p = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1";
            if (!Directory.Exists(p))
                throw new DirectoryNotFoundException("Astria install not found (read-only reference).");
            return p;
        }
    }

    private static string FlasmExe => Path.Combine(AstriaRoot, "Flasm", "flasm.exe");
    private static string BlankSwf => Path.Combine(AstriaRoot, "Flasm", "blank.swf");

    private static string FixturesRoot
    {
        get
        {
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
            if (Directory.Exists(fromProject)) return fromProject;
            throw new DirectoryNotFoundException("fixtures/maps missing");
        }
    }

    private static string ArtifactsRoot
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "swf"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static MapDocument LoadMapWithSwfMeta(int mapId)
    {
        var sql = Path.Combine(FixturesRoot, $"{mapId}.sql");
        if (!File.Exists(sql))
            sql = Path.Combine(AstriaRoot, "Maps", mapId.ToString(), $"{mapId}.sql");
        var map = AstriaSqlMapParser.ParseFile(sql);
        map.Cells = MapDataCodec.DecodeMap(map.MapData);

        var folder = Path.Combine(AstriaRoot, "Maps", mapId.ToString());
        var swf = FlasmSwfMetadataReader.ResolvePreferredSwf(folder, mapId);
        if (swf is not null && File.Exists(FlasmExe))
        {
            var meta = FlasmSwfMetadataReader.Read(swf, FlasmExe, includeMapData: false);
            FlasmSwfMetadataReader.ApplyToDocument(map, meta);
        }

        return map;
    }

    [Fact]
    public void Export_10420_roundtrip_MapData_and_metadata()
    {
        var map = LoadMapWithSwfMeta(10420);
        Assert.NotNull(map.Outdoor);

        var dest = Path.Combine(ArtifactsRoot, "10420_rufus.swf");
        if (File.Exists(dest)) File.Delete(dest);

        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = dest,
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(dest));
        Assert.True(result.OutputBytes > 0);
        Assert.Equal(map.MapData, result.ReadBackMapData);
        Assert.NotNull(result.ReadBack);
        Assert.Equal(map.Id, result.ReadBack!.Id);
        Assert.Equal(map.Width, result.ReadBack.Width);
        Assert.Equal(map.Height, result.ReadBack.Height);
        Assert.Equal(map.BackgroundId, result.ReadBack.BackgroundNum);
        Assert.Equal(map.AmbianceId, result.ReadBack.AmbianceId);
        Assert.Equal(map.MusicId, result.ReadBack.MusicId);
        Assert.Equal(map.Outdoor, result.ReadBack.Outdoor);
        Assert.Equal(map.Capabilities, result.ReadBack.Capabilities);

        // Semantic compare vs Astria SWF (not byte-identical)
        var astriaSwf = FlasmSwfMetadataReader.ResolvePreferredSwf(Path.Combine(AstriaRoot, "Maps", "10420"), 10420)!;
        var astria = FlasmSwfMetadataReader.Read(astriaSwf, FlasmExe, includeMapData: true);
        var report = new StringBuilder();
        report.AppendLine("| Campo | Astria | RUFUS | Igual |");
        report.AppendLine("|---|---|---|---|");
        void Row(string name, string a, string r)
        {
            report.AppendLine($"| {name} | {a} | {r} | {(a == r ? "SÍ" : "NO")} |");
        }
        Row("id", astria.Id.ToString(), result.ReadBack.Id.ToString());
        Row("width", astria.Width.ToString(), result.ReadBack.Width.ToString());
        Row("height", astria.Height.ToString(), result.ReadBack.Height.ToString());
        Row("backgroundNum", astria.BackgroundNum.ToString(), result.ReadBack.BackgroundNum.ToString());
        Row("ambianceId", astria.AmbianceId.ToString(), result.ReadBack.AmbianceId.ToString());
        Row("musicId", astria.MusicId.ToString(), result.ReadBack.MusicId.ToString());
        Row("bOutdoor", astria.Outdoor.ToString(), result.ReadBack.Outdoor.ToString());
        Row("capabilities", astria.Capabilities.ToString(), result.ReadBack.Capabilities.ToString());
        Row("mapData", $"len={astria.MapData.Length}", $"len={result.ReadBackMapData!.Length}");
        report.AppendLine();
        report.AppendLine($"MapData contenido idéntico Astria↔RUFUS: {astria.MapData == result.ReadBackMapData}");
        report.AppendLine($"RUFUS bytes: {result.OutputBytes}; Astria bytes: {new FileInfo(astriaSwf).Length}");
        File.WriteAllText(Path.Combine(ArtifactsRoot, "10420_astria_vs_rufus.md"), report.ToString());
    }

    [Fact]
    public void Export_10420_modified_cells_appear_in_SWF()
    {
        var map = LoadMapWithSwfMeta(10420);
        var cellId = 50;
        var before = CellSnapshotLike(map.Cells[cellId]);
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Ground, 750, flip: true, rotation: 1);
        MapCellEditor.SetLayerGfx(map.Cells[cellId], MapCellEditor.Layer.Object1, 1101, flip: false, rotation: 2);
        map.Cells[cellId].LineOfSight = !map.Cells[cellId].LineOfSight;
        MapCellEditor.SyncMapDataString(map);
        var expected = map.MapData;

        var dest = Path.Combine(ArtifactsRoot, "10420_modified.swf");
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = dest,
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(expected, result.ReadBackMapData);

        var decoded = MapDataCodec.DecodeMap(result.ReadBackMapData!);
        Assert.Equal(750, decoded[cellId].GroundGfxId);
        Assert.True(decoded[cellId].FlipGround);
        Assert.Equal(1, decoded[cellId].GroundRotation);
        Assert.Equal(1101, decoded[cellId].Object1GfxId);
        Assert.Equal(2, decoded[cellId].Object1Rotation);
        Assert.Equal(map.Cells[cellId].LineOfSight, decoded[cellId].LineOfSight);

        // Unmodified neighbour still matches original snapshot fields we didn't touch on cell 51
        var other = 51;
        Assert.Equal(before.GroundLevel, decoded[other].GroundLevel); // weak check — better: reload original
        var original = LoadMapWithSwfMeta(10420);
        for (var i = 0; i < original.Cells.Count; i++)
        {
            if (i == cellId) continue;
            Assert.Equal(original.Cells[i].GroundGfxId, decoded[i].GroundGfxId);
            Assert.Equal(original.Cells[i].Object1GfxId, decoded[i].Object1GfxId);
            Assert.Equal(original.Cells[i].Object2GfxId, decoded[i].Object2GfxId);
            Assert.Equal(original.Cells[i].LineOfSight, decoded[i].LineOfSight);
            Assert.Equal(original.Cells[i].Movement, decoded[i].Movement);
        }
    }

    [Fact]
    public void Export_from_rufmap_without_sql()
    {
        var map = LoadMapWithSwfMeta(10420);
        Assert.NotNull(map.Outdoor);
        var rufPath = Path.GetFullPath(Path.Combine(ArtifactsRoot, "..", "manual", "10420_test.rufmap"));
        Directory.CreateDirectory(Path.GetDirectoryName(rufPath)!);
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow,
            new RufmapSourceDto { Kind = "LegacyAstria", OriginalMapId = 10420 }, "10420_test");
        RufmapIo.SaveAtomic(rufPath, RufmapSerializer.Serialize(dto));

        var loaded = RufmapIo.LoadFile(rufPath);
        Assert.NotNull(loaded.Document.Outdoor);

        var dest = Path.Combine(ArtifactsRoot, "10420_from_rufmap.swf");
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = loaded.Document,
            DestinationSwfPath = dest,
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(loaded.Document.MapData, result.ReadBackMapData);
    }

    [Fact]
    public void Export_all_30_fixtures_report()
    {
        var sqlFiles = Directory.GetFiles(FixturesRoot, "*.sql").OrderBy(f => f).ToArray();
        Assert.Equal(30, sqlFiles.Length);

        var sb = new StringBuilder();
        sb.AppendLine("# SWF export report");
        sb.AppendLine();
        sb.AppendLine("| MapID | Export | ReadBack | MapData | Metadata | Errores Flasm | Estado |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        var ok = 0;
        foreach (var sql in sqlFiles)
        {
            var id = Path.GetFileNameWithoutExtension(sql);
            try
            {
                var map = LoadMapWithSwfMeta(int.Parse(id));
                if (map.Outdoor is null)
                {
                    sb.AppendLine($"| {id} | SKIP | — | — | Outdoor ausente | — | METADATA_GAP |");
                    continue;
                }

                var dest = Path.Combine(ArtifactsRoot, "batch", $"{id}_rufus.swf");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var result = SwfMapExporter.Export(new SwfExportRequest
                {
                    Document = map,
                    DestinationSwfPath = dest,
                    FlasmExePath = FlasmExe,
                    BlankSwfTemplatePath = BlankSwf,
                });

                if (!result.Success)
                {
                    sb.AppendLine($"| {id} | FAIL | — | — | — | {Escape(result.ErrorMessage)} | ERROR |");
                    continue;
                }

                var mdOk = result.ReadBackMapData == map.MapData ? "OK" : "DIFF";
                var metaOk = SwfMapExporter.Compare(map, result.ReadBack!).Count == 0 ? "OK" : "DIFF";
                var flasm = result.FlasmAssemble?.ExitCode == 0 ? "OK" : $"exit {result.FlasmAssemble?.ExitCode}";
                var state = mdOk == "OK" && metaOk == "OK" ? "PASS" : "FAIL";
                if (state == "PASS") ok++;
                sb.AppendLine($"| {id} | OK | OK | {mdOk} | {metaOk} | {flasm} | {state} |");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"| {id} | FAIL | — | — | — | {Escape(ex.Message)} | ERROR |");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"## Summary: {ok}/30 PASS");
        sb.AppendLine();
        sb.AppendLine("## Resource Gap 30001–30004");
        sb.AppendLine("Background GfxID 340 may be missing as PNG; SWF still stores `backgroundNum=340` unchanged.");
        File.WriteAllText(Path.Combine(ArtifactsRoot, "swf_export_report.md"), sb.ToString());
        Assert.Equal(30, ok);
    }

    [Fact]
    public void Flasm_missing_exe_does_not_crash()
    {
        var map = LoadMapWithSwfMeta(10420);
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = Path.Combine(ArtifactsRoot, "should_not_exist.swf"),
            FlasmExePath = Path.Combine(ArtifactsRoot, "no-such-flasm.exe"),
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.False(result.Success);
        Assert.Contains("Flasm no encontrado", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Flasm_invalid_input_handled()
    {
        var map = LoadMapWithSwfMeta(10420);
        map.Outdoor = null;
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = Path.Combine(ArtifactsRoot, "no_outdoor.swf"),
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.False(result.Success);
        Assert.Contains("Outdoor", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_works_with_spaces_in_paths()
    {
        var map = LoadMapWithSwfMeta(10420);
        // Astria path already has spaces; also destination with spaces
        var destDir = Path.Combine(ArtifactsRoot, "path with spaces");
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "10420 rufus.swf");
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = dest,
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(dest));
        Assert.Contains(" ", FlasmExe);
    }

    [Fact]
    public void Background_340_preserved_without_png()
    {
        var map = LoadMapWithSwfMeta(30001);
        Assert.Equal(340, map.BackgroundId);
        Assert.NotNull(map.Outdoor);
        var dest = Path.Combine(ArtifactsRoot, "30001_bg340.swf");
        var result = SwfMapExporter.Export(new SwfExportRequest
        {
            Document = map,
            DestinationSwfPath = dest,
            FlasmExePath = FlasmExe,
            BlankSwfTemplatePath = BlankSwf,
        });
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(340, result.ReadBack!.BackgroundNum);
    }

    private static (int GroundLevel, int GroundGfxId) CellSnapshotLike(CellData c) => (c.GroundLevel, c.GroundGfxId);

    private static string Escape(string? s) =>
        (s ?? "").Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
