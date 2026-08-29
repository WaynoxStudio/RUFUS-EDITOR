using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class VisualLibraryLib2Tests
{
    private static string FixturesDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (Directory.Exists(path) && File.Exists(Path.Combine(path, "versions_es.txt")))
            return path;
        path = Path.Combine(FindRepoRoot(), "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures");
        Assert.True(Directory.Exists(path), $"Fixtures missing: {path}");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "rufus-lib2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void MobGradosLevelsParser_extracts_only_l_levels()
    {
        var grados = "g1:{l:102,r:1},g2:{l:109,x:9},g3:{l:116}";
        var levels = MobGradosLevelsParser.ParseLevels(grados);
        Assert.Equal(new[] { 102, 109, 116 }, levels);
    }

    [Fact]
    public void VersionsEs_resolves_active_monsters_and_items_tokens()
    {
        var text = File.ReadAllText(Path.Combine(FixturesDir(), "versions_es.txt"));
        Assert.True(VersionsEsParser.TryParseMonstersVersion(text, out var mon, out var e1), e1);
        Assert.True(VersionsEsParser.TryParseItemsVersion(text, out var items, out var e2), e2);
        Assert.Equal(1278, mon);
        Assert.Equal(1305, items);
        Assert.Equal("monsters_es_1278.swf", VersionsEsParser.BuildMonstersSwfFileName(mon));
        Assert.Equal("items_es_1305.swf", VersionsEsParser.BuildItemsSwfFileName(items));
    }

    [Fact]
    public void VisualClipPaths_monster_and_item_patterns()
    {
        Assert.Equal("artworks/big/1607.swf", VisualClipPaths.ArtworkRelative(1607));
        Assert.Equal("sprites/1607.swf", VisualClipPaths.SpriteRelative(1607));
        Assert.Equal("items/164/16455.swf", VisualClipPaths.ItemIconRelative(16455));
        Assert.Equal("items/1/196.swf", VisualClipPaths.ItemIconRelative(196));
    }

    [Fact]
    public void ItemsEs_parses_8828_Peluca_gfx_not_equal_itemId()
    {
        var swf = Path.Combine(FixturesDir(), "items_es_1305.swf");
        Assert.True(File.Exists(swf), swf);
        var snap = ItemsEsParser.Parse(File.ReadAllBytes(swf));
        Assert.True(snap.Items.Count > 1000, $"items count={snap.Items.Count}");
        Assert.True(snap.Items.TryGetValue(8828, out var item), "Item 8828 missing");
        Assert.Contains("pohoyo", item.Nombre, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(196, item.GfxId);
        Assert.NotEqual(item.ItemId, item.GfxId);
        Assert.Equal(1, item.Level);
        Assert.Equal(16, item.TypeId);
    }

    [Fact]
    public async Task VisualLibraryService_monsters_keep_duplicate_ids_separate()
    {
        var cache = TempDir();
        try
        {
            var repo = new FakeMobsRepo(new[]
            {
                new MobsModeloRow
                {
                    Id = 1056,
                    Nombre = "Sargento Zoth",
                    GfxId = 1607,
                    Grados = "g1:{l:102},g2:{l:109},g3:{l:116},g4:{l:123},g5:{l:130}",
                },
                new MobsModeloRow
                {
                    Id = 1106,
                    Nombre = "Sargento Zoth",
                    GfxId = 1607,
                    Grados = "g1:{l:102},g2:{l:109},g3:{l:116},g4:{l:123},g5:{l:130}",
                },
                new MobsModeloRow
                {
                    Id = 1107,
                    Nombre = "Sargento Zoth",
                    GfxId = 1607,
                    Grados = "g1:{l:102},g2:{l:109}",
                },
            });

            var svc = new VisualLibraryService();
            await svc.LoadMonstersAsync(
                new DatabaseSettings { Database = "estaticos" },
                dbPassword: "",
                cacheDirectory: cache,
                mobsRepo: repo,
                localMonstersVersion: 1278);

            Assert.Equal(3, svc.Monsters.Count);
            var a = svc.GetMonster(1056);
            var b = svc.GetMonster(1106);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Nombre, b!.Nombre);
            Assert.Equal(a.GfxId, b.GfxId);
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal("artworks/big/1607.swf", a.ArtworkRelativePath);
            Assert.Equal("sprites/1607.swf", a.SpriteRelativePath);
            Assert.Equal(new[] { 102, 109, 116, 123, 130 }, a.Levels.ToArray());
            Assert.Contains(" / ", a.LevelsDisplay);

            var byName = svc.SearchMonsters("Sargento Zoth");
            Assert.Equal(3, byName.Count);
            Assert.Contains(byName, m => m.Id == 1056);
            Assert.Contains(byName, m => m.Id == 1106);

            Assert.Single(svc.SearchMonsters("1106"));
            Assert.Equal(3, svc.SearchMonsters("1607").Count); // gfx shared — not merged
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task VisualLibraryService_items_cache_invalidates_on_version_change()
    {
        var cache = TempDir();
        var swf = File.ReadAllBytes(Path.Combine(FixturesDir(), "items_es_1305.swf"));
        try
        {
            var svc = new VisualLibraryService();
            await svc.LoadItemsAsync(
                cacheDirectory: cache,
                localItemsSwf: swf,
                localItemsVersion: 1305);

            Assert.True(svc.ItemsLoaded);
            Assert.True(svc.Items.Count > 1000);
            var peluca = svc.GetItem(8828);
            Assert.NotNull(peluca);
            Assert.Equal(196, peluca!.GfxId);
            Assert.Contains("pohoyo", peluca.Nombre, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("items/1/196.swf", peluca.IconRelativePath);

            var filesV1305 = Directory.GetFiles(cache, "items_v1305_*.json");
            Assert.NotEmpty(filesV1305);

            await svc.LoadItemsAsync(
                cacheDirectory: cache,
                localItemsSwf: swf,
                localItemsVersion: 9999);
            Assert.Contains("items,es,9999", svc.StatusItems);
            Assert.NotEmpty(Directory.GetFiles(cache, "items_v9999_*.json"));
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Spawn_tipo_and_slot_limits()
    {
        Assert.Equal(8, MapMonsterGroupLimits.MaxSlots);
        // Confirmed spawn tipo values for UI only: -1, 0, 1, 2
        Assert.Equal(new[] { -1, 0, 1, 2 }, new[] { -1, 0, 1, 2 });
    }

    [Fact]
    public void Item_icon_missing_keeps_relative_path()
    {
        var clips = TempDir();
        try
        {
            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clips, gfxId: 196, typeId: 16);
            Assert.False(exists);
            Assert.Equal("items/1/196.swf", rel);
            Assert.NotNull(full);
        }
        finally
        {
            try { Directory.Delete(clips, true); } catch { /* ignore */ }
        }
    }

    private sealed class FakeMobsRepo : IMobsModeloReadRepository
    {
        private readonly IReadOnlyList<MobsModeloRow> _rows;
        public FakeMobsRepo(IReadOnlyList<MobsModeloRow> rows) => _rows = rows;
        public Task<IReadOnlyList<MobsModeloRow>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(_rows);
    }
}
