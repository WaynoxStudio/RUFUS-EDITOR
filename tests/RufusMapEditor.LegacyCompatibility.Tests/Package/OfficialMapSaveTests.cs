using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering.Package;

namespace RufusMapEditor.LegacyCompatibility.Tests.Package;

public sealed class OfficialMapSaveTests
{
    private static string FixturesRoot
    {
        get
        {
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
            if (Directory.Exists(fromProject))
                return fromProject;
            throw new DirectoryNotFoundException("Could not locate tests/fixtures/maps.");
        }
    }

    private static string ArtifactsRoot
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "official_save"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static MapDocument LoadDecoded(int mapId)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, $"{mapId}.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        return map;
    }

    private static string NewLibraryRoot(string tag)
    {
        var root = Path.Combine(ArtifactsRoot, tag + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Maps"));
        return root;
    }

    private static OfficialMapSaveResult SaveCore(MapDocument map, string libraryRoot) =>
        OfficialMapSave.CreateWithoutGfx().Save(map, new OfficialMapSaveOptions
        {
            LibraryRoot = libraryRoot,
            DocumentId = Guid.NewGuid().ToString("D"),
            ProjectName = $"map_{map.Id}",
            FlasmExePath = Path.Combine(libraryRoot, "__missing_flasm__.exe"),
            BlankSwfTemplatePath = Path.Combine(libraryRoot, "__missing_blank__.swf"),
        });

    [Fact]
    public void Official_folder_path_helper()
    {
        var root = @"C:\fake\Library";
        Assert.Equal(Path.Combine(root, "Maps", "13049"), LibraryMapPaths.GetOfficialMapDirectory(root, 13049));
        Assert.Equal(Path.Combine(root, "Maps", "13049", "13049.rufmap"), LibraryMapPaths.GetOfficialRufmapPath(root, 13049));
        Assert.Equal(Path.Combine(root, "Maps", "13049", "13049.png"), LibraryMapPaths.GetOfficialPngPath(root, 13049));
        Assert.Equal(Path.Combine(root, "Maps", "13049", "13049_AME.swf"), LibraryMapPaths.GetOfficialAmeSwfPath(root, 13049));
        Assert.Equal(Path.Combine(root, "Maps", "13049", "13049_MapData.txt"), LibraryMapPaths.GetOfficialMapDataTxtPath(root, 13049));
    }

    [Fact]
    public void First_save_creates_minimal_official_folder()
    {
        var map = LoadDecoded(10421);
        var lib = NewLibraryRoot("first");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);

        var dir = result.OfficialDirectory;
        Assert.True(File.Exists(Path.Combine(dir, "10421.rufmap")));
        Assert.True(File.Exists(Path.Combine(dir, "10421.png")));
        Assert.True(File.Exists(Path.Combine(dir, "10421_MapData.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "10421_ModeCell.png")));
        Assert.False(File.Exists(Path.Combine(dir, GfxUsageListBuilder.FileName)));
        Assert.False(File.Exists(Path.Combine(dir, "manifest.txt")));
        Assert.Empty(Directory.GetFiles(dir, "*.sql"));
        Assert.Empty(Directory.GetFiles(dir, "*.ame"));
        Assert.False(result.AmeSwfGenerated);
        Assert.False(File.Exists(Path.Combine(dir, "10421_AME.swf")));
    }

    [Fact]
    public void MapData_txt_filename_and_exact_canonical_content()
    {
        var map = LoadDecoded(10420);
        MapCellEditor.SyncDocument(map);
        var canonical = map.MapData ?? "";
        Assert.Equal(MapGeometry.ExpectedMapDataLength(15, 17), canonical.Length);

        var lib = NewLibraryRoot("mapdata");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(Path.Combine(result.OfficialDirectory, "10420_MapData.txt"), result.MapDataTxtPath);
        Assert.True(File.Exists(result.MapDataTxtPath));

        var bytes = File.ReadAllBytes(result.MapDataTxtPath!);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.DoesNotContain((byte)'\n', bytes);

        var fromFile = MapDataPlainText.ReadFile(result.MapDataTxtPath!);
        Assert.Equal(canonical, fromFile);
        Assert.Equal(canonical.Length, fromFile.Length);
        Assert.Equal(fromFile, fromFile.Trim());
        Assert.False(fromFile.StartsWith(' ') || fromFile.EndsWith(' '));
        Assert.DoesNotContain("MapData=", fromFile, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mapData\"", fromFile, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", fromFile, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(map.FightPlaces, fromFile);
        Assert.Equal(canonical.Length, result.MapDataLength);

        var shaCanon = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var shaFile = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(shaCanon, shaFile);

        var loaded = RufmapIo.LoadFile(result.RufmapPath!);
        Assert.Equal(canonical, loaded.Document.MapData);
        Assert.False(string.IsNullOrEmpty(loaded.Document.FightPlaces));
    }

    [Fact]
    public void MapData_txt_15x17_length_is_cellcount_times_10()
    {
        var map = LoadDecoded(10420);
        Assert.Equal(15, map.Width);
        Assert.Equal(17, map.Height);
        var expected = MapGeometry.CellCount(15, 17) * MapDataConstants.CharsPerCell;
        Assert.Equal(4790, expected);

        var lib = NewLibraryRoot("len15");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(expected, MapDataPlainText.ReadFile(result.MapDataTxtPath!).Length);
    }

    [Fact]
    public void MapData_txt_19x22_length_is_cellcount_times_10()
    {
        var cellCount = MapGeometry.CellCount(19, 22);
        Assert.Equal(796, cellCount);
        var expected = cellCount * MapDataConstants.CharsPerCell;
        var map = new MapDocument
        {
            Id = 19022,
            Width = 19,
            Height = 22,
            BackgroundId = 1,
            Outdoor = true,
            Cells = Enumerable.Range(0, cellCount)
                .Select(_ => new CellData { Movement = MovementType.Walkable, LineOfSight = true })
                .ToList(),
        };
        MapCellEditor.SyncDocument(map);
        Assert.Equal(expected, map.MapData!.Length);

        var lib = NewLibraryRoot("len19");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(expected, MapDataPlainText.ReadFile(result.MapDataTxtPath!).Length);
    }

    [Fact]
    public void MapData_txt_golden_cells_10421()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var lib = NewLibraryRoot("golden");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        var txt = MapDataPlainText.ReadFile(result.MapDataTxtPath!);
        Assert.Equal("GhaaeaaGpM", MapDataPlainText.CellBlock(txt, 228));
        Assert.Equal("Ghaaeaaa_Y", MapDataPlainText.CellBlock(txt, 230));
    }

    [Fact]
    public void Second_save_replaces_MapData_same_path()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var lib = NewLibraryRoot("mdoverwrite");
        var first = SaveCore(map, lib);
        Assert.True(first.Success, first.ErrorMessage);
        var path = first.MapDataTxtPath!;
        var mapDataA = MapDataPlainText.ReadFile(path);

        map.Cells[0].GroundGfxId = Math.Max(1, map.Cells[0].GroundGfxId + 1);
        MapCellEditor.SyncDocument(map);
        var mapDataB = map.MapData ?? "";
        Assert.NotEqual(mapDataA, mapDataB);

        var second = SaveCore(map, lib);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(path, second.MapDataTxtPath);
        Assert.Equal(mapDataB, MapDataPlainText.ReadFile(path));
        Assert.Empty(Directory.GetFiles(second.OfficialDirectory, "*_MapData_*.txt"));
        Assert.Single(Directory.GetFiles(second.OfficialDirectory, "*_MapData.txt"));
    }

    [Fact]
    public void Failed_save_preserves_previous_MapData()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        var lib = NewLibraryRoot("mdpreserve");
        var first = SaveCore(map, lib);
        Assert.True(first.Success, first.ErrorMessage);
        var previous = MapDataPlainText.ReadFile(first.MapDataTxtPath!);

        var bad = SaveCore(map, "");
        Assert.False(bad.Success);
        Assert.Equal(previous, MapDataPlainText.ReadFile(first.MapDataTxtPath!));
    }

    [Fact]
    public void Png_15x17_is_728x416()
    {
        var map = LoadDecoded(10421);
        Assert.Equal(15, map.Width);
        Assert.Equal(17, map.Height);
        var lib = NewLibraryRoot("pngdims");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(728, result.PngWidth);
        Assert.Equal(416, result.PngHeight);
        using var bmp = new Bitmap(result.PngPath!);
        Assert.Equal(728, bmp.Width);
        Assert.Equal(416, bmp.Height);
    }

    [Fact]
    public void Second_save_same_mapid_replaces_completely()
    {
        var map = LoadDecoded(10421);
        var lib = NewLibraryRoot("overwrite");
        var first = SaveCore(map, lib);
        Assert.True(first.Success, first.ErrorMessage);

        var dir = first.OfficialDirectory;
        File.WriteAllText(Path.Combine(dir, "old.txt"), "stale");
        File.WriteAllText(Path.Combine(dir, "10421_ModeCell.png"), "fake");
        File.WriteAllText(Path.Combine(dir, "manifest.txt"), "fake");
        File.WriteAllText(Path.Combine(dir, GfxUsageListBuilder.FileName), "fake");
        File.WriteAllBytes(Path.Combine(dir, "10421_AME.swf"), [1, 2, 3, 4]);

        var rufA = SHA256.HashData(File.ReadAllBytes(first.RufmapPath!));

        map.Cells[0].GroundGfxId = Math.Max(1, map.Cells[0].GroundGfxId + 1);
        MapCellEditor.SyncDocument(map);

        var second = SaveCore(map, lib);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(first.OfficialDirectory, second.OfficialDirectory);

        Assert.False(File.Exists(Path.Combine(dir, "old.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "10421_ModeCell.png")));
        Assert.False(File.Exists(Path.Combine(dir, "manifest.txt")));
        Assert.False(File.Exists(Path.Combine(dir, GfxUsageListBuilder.FileName)));
        Assert.False(File.Exists(Path.Combine(dir, "10421_AME.swf")));

        Assert.True(File.Exists(Path.Combine(dir, "10421.rufmap")));
        Assert.True(File.Exists(Path.Combine(dir, "10421.png")));
        Assert.True(File.Exists(Path.Combine(dir, "10421_MapData.txt")));

        var rufB = SHA256.HashData(File.ReadAllBytes(second.RufmapPath!));
        Assert.NotEqual(Convert.ToHexString(rufA), Convert.ToHexString(rufB));
    }

    [Fact]
    public void Save_without_flasm_core_ok()
    {
        var map = LoadDecoded(10420);
        map.Outdoor = true;
        MapCellEditor.SyncDocument(map);
        var lib = NewLibraryRoot("noflasm");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.AmeSwfGenerated);
        Assert.Contains("Flasm", result.AmeSwfWarning ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.RufmapPath));
        Assert.True(File.Exists(result.PngPath));
        Assert.True(File.Exists(result.MapDataTxtPath));
    }

    [Fact]
    public void FightPlaces_roundtrip_in_official_rufmap()
    {
        var map = LoadDecoded(10421);
        MapCellEditor.SyncDocument(map);
        Assert.Equal(1, map.Cells[67].FightCell);
        Assert.Equal(2, map.Cells[330].FightCell);

        var lib = NewLibraryRoot("fight");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);

        var loaded = RufmapIo.LoadFile(result.RufmapPath!);
        Assert.Equal(map.FightPlaces, loaded.Document.FightPlaces);
        FightPlacesCodec.ApplyToCells(loaded.Document.Cells, loaded.Document.FightPlaces);
        Assert.Equal(1, loaded.Document.Cells[67].FightCell);
        Assert.Equal(2, loaded.Document.Cells[330].FightCell);

        var txt = MapDataPlainText.ReadFile(result.MapDataTxtPath!);
        Assert.Equal(map.MapData, txt);
        Assert.NotEqual(map.FightPlaces, txt);
    }

    [Fact]
    public void Loader_coexistence_legacy_sql_and_official_rufmap_no_duplicate()
    {
        var lib = NewLibraryRoot("coexist");
        var maps = Path.Combine(lib, "Maps");

        var legacyDir = Path.Combine(maps, "10420");
        Directory.CreateDirectory(legacyDir);
        File.Copy(Path.Combine(FixturesRoot, "10420.sql"), Path.Combine(legacyDir, "10420.sql"));

        var map = LoadDecoded(10421);
        var save = SaveCore(map, lib);
        Assert.True(save.Success, save.ErrorMessage);

        var ids = new HashSet<int>();
        foreach (var dir in Directory.EnumerateDirectories(maps))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || !int.TryParse(name, out var id) || id <= 0)
                continue;
            if (File.Exists(Path.Combine(dir, $"{id}.rufmap")) || File.Exists(Path.Combine(dir, $"{id}.sql")))
                ids.Add(id);
        }

        Assert.Contains(10420, ids);
        Assert.Contains(10421, ids);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public void Multiple_maps_independent_folders()
    {
        var lib = NewLibraryRoot("multi");
        Assert.True(SaveCore(LoadDecoded(10420), lib).Success);
        Assert.True(SaveCore(LoadDecoded(10421), lib).Success);
        Assert.True(File.Exists(Path.Combine(lib, "Maps", "10420", "10420.rufmap")));
        Assert.True(File.Exists(Path.Combine(lib, "Maps", "10420", "10420_MapData.txt")));
        Assert.True(File.Exists(Path.Combine(lib, "Maps", "10421", "10421.rufmap")));
        Assert.True(File.Exists(Path.Combine(lib, "Maps", "10421", "10421_MapData.txt")));
    }

    [Fact]
    public void No_sql_no_ame_no_client_swf_in_official_folder()
    {
        var map = LoadDecoded(10421);
        var lib = NewLibraryRoot("nosql");
        var result = SaveCore(map, lib);
        Assert.True(result.Success, result.ErrorMessage);
        var dir = result.OfficialDirectory;
        Assert.Empty(Directory.GetFiles(dir, "*.sql", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dir, "*.ame", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dir, "*_CLIENT.swf", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(dir, "Client")));
        Assert.False(Directory.Exists(Path.Combine(dir, "Legacy")));
    }
}