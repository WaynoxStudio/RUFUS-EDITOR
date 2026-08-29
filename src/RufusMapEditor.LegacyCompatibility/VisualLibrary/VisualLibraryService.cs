using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.2 — shared central catalog for Maps + Content.
/// Monsters: mobs_modelo (identity) + monsters_es version for cache invalidation.
/// Items: items_es (identity = Item ID). No spawn writes.
/// </summary>
public sealed class VisualLibraryService
{
    public static VisualLibraryService Shared { get; } = new();

    private readonly object _gate = new();
    private IReadOnlyList<MonsterCatalogEntry> _monsters = Array.Empty<MonsterCatalogEntry>();
    private IReadOnlyList<ItemCatalogEntry> _items = Array.Empty<ItemCatalogEntry>();
    private IReadOnlyDictionary<int, string> _itemTypeNames = new Dictionary<int, string>();
    private string? _clipsRoot;
    private int? _monstersLangVersion;
    private int? _itemsLangVersion;
    private string _statusMonsters = "Catálogo monstruos: no cargado";
    private string _statusItems = "Catálogo objetos: no cargado";

    public IReadOnlyList<MonsterCatalogEntry> Monsters
    {
        get { lock (_gate) return _monsters; }
    }

    public IReadOnlyList<ItemCatalogEntry> Items
    {
        get { lock (_gate) return _items; }
    }

    public IReadOnlyDictionary<int, string> ItemTypeNames
    {
        get { lock (_gate) return _itemTypeNames; }
    }

    public string? ClipsRoot
    {
        get { lock (_gate) return _clipsRoot; }
    }

    public int? MonstersLangVersion
    {
        get { lock (_gate) return _monstersLangVersion; }
    }

    public int? ItemsLangVersion
    {
        get { lock (_gate) return _itemsLangVersion; }
    }

    public string StatusMonsters
    {
        get { lock (_gate) return _statusMonsters; }
    }

    public string StatusItems
    {
        get { lock (_gate) return _statusItems; }
    }

    public bool MonstersLoaded
    {
        get { lock (_gate) return _monsters.Count > 0; }
    }

    public bool ItemsLoaded
    {
        get { lock (_gate) return _items.Count > 0; }
    }

    public void SetClipsRoot(string? clipsRoot)
    {
        lock (_gate)
        {
            _clipsRoot = string.IsNullOrWhiteSpace(clipsRoot) ? null : Path.GetFullPath(clipsRoot.Trim());
            _monsters = RelinkMonsters(_monsters, _clipsRoot);
            _items = RelinkItems(_items, _clipsRoot);
        }
    }

    public async Task LoadMonstersAsync(
        DatabaseSettings db,
        string dbPassword,
        LangSftpSettings? langSftp = null,
        string? langPassword = null,
        string? clipsRoot = null,
        string? cacheDirectory = null,
        string? langCacheDirectory = null,
        IMobsModeloReadRepository? mobsRepo = null,
        Func<LangSftpSettings, string, ILangSftpReadClient>? sftpFactory = null,
        byte[]? localMonstersSwf = null,
        int? localMonstersVersion = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.IsNullOrWhiteSpace(clipsRoot))
            SetClipsRoot(clipsRoot);

        var cacheDir = string.IsNullOrWhiteSpace(cacheDirectory)
            ? VisualLibraryCache.DefaultDirectory
            : cacheDirectory!;

        int monstersVersion = localMonstersVersion ?? 0;
        string? monstersSha = null;
        string? langError = null;

        if (localMonstersSwf is null && langSftp is not null && !string.IsNullOrEmpty(langPassword))
        {
            try
            {
                var (versions, monstersArt, _, err) = VisualLibraryLangLoader.LoadActive(
                    langSftp,
                    langPassword,
                    langCacheDirectory,
                    sftpFactory,
                    loadMonsters: true,
                    loadItems: false);
                langError = err;
                if (monstersArt is not null)
                {
                    monstersVersion = monstersArt.Version;
                    monstersSha = monstersArt.Sha256;
                    _ = versions;
                }
            }
            catch (Exception ex)
            {
                langError = ex.Message;
            }
        }
        else if (localMonstersSwf is not null && localMonstersVersion is int lv)
        {
            monstersVersion = lv;
            monstersSha = Sha256Hex(localMonstersSwf);
        }

        var repo = mobsRepo ?? new MysqlMobsModeloReadRepository(db, dbPassword);
        var rows = await repo.GetAllAsync(ct).ConfigureAwait(false);
        var fingerprint = FingerprintMobs(rows);

        var cachePath = VisualLibraryCache.MonsterCachePath(cacheDir, monstersVersion, fingerprint);
        var cached = VisualLibraryCache.TryReadMonsters(cachePath);
        List<MonsterCatalogEntry> built;
        if (cached is not null
            && cached.MonstersLangVersion == monstersVersion
            && string.Equals(cached.MobsFingerprint, fingerprint, StringComparison.Ordinal)
            && cached.Entries.Count > 0)
        {
            built = cached.Entries.Select(e => BuildMonster(e.Id, e.Nombre, e.GfxId, e.Levels)).ToList();
        }
        else
        {
            built = new List<MonsterCatalogEntry>(rows.Count);
            var cacheRows = new List<VisualLibraryCache.MonsterCacheRow>(rows.Count);
            foreach (var row in rows)
            {
                var levels = MobGradosLevelsParser.ParseLevels(row.Grados);
                built.Add(BuildMonster(row.Id, row.Nombre, row.GfxId, levels));
                cacheRows.Add(new VisualLibraryCache.MonsterCacheRow
                {
                    Id = row.Id,
                    Nombre = row.Nombre,
                    GfxId = row.GfxId,
                    Levels = levels.ToList(),
                });
            }

            VisualLibraryCache.WriteMonsters(cachePath, new VisualLibraryCache.MonsterCachePayload
            {
                MonstersLangVersion = monstersVersion,
                MobsFingerprint = fingerprint,
                Entries = cacheRows,
            });
        }

        lock (_gate)
        {
            _monstersLangVersion = monstersVersion > 0 ? monstersVersion : null;
            _monsters = RelinkMonsters(built, _clipsRoot);
            _statusMonsters = monstersVersion > 0
                ? $"Catálogo: ✓ Cargado · {_monsters.Count} mobs · monsters,es,{monstersVersion}"
                : $"Catálogo: ✓ Cargado · {_monsters.Count} mobs (mobs_modelo)"
                  + (langError is null ? "" : $" · lang: {langError}");
            _ = monstersSha;
        }
    }

    public Task LoadItemsAsync(
        LangSftpSettings? langSftp = null,
        string? langPassword = null,
        string? clipsRoot = null,
        string? cacheDirectory = null,
        string? langCacheDirectory = null,
        Func<LangSftpSettings, string, ILangSftpReadClient>? sftpFactory = null,
        byte[]? localItemsSwf = null,
        int? localItemsVersion = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(clipsRoot))
            SetClipsRoot(clipsRoot);

        var cacheDir = string.IsNullOrWhiteSpace(cacheDirectory)
            ? VisualLibraryCache.DefaultDirectory
            : cacheDirectory!;

        byte[]? bytes = localItemsSwf;
        int version = localItemsVersion ?? 0;
        string sha = localItemsSwf is null ? "" : Sha256Hex(localItemsSwf);
        string? err = null;

        if (bytes is null && langSftp is not null && !string.IsNullOrEmpty(langPassword))
        {
            try
            {
                var (_, _, itemsArt, loadErr) = VisualLibraryLangLoader.LoadActive(
                    langSftp,
                    langPassword,
                    langCacheDirectory,
                    sftpFactory,
                    loadMonsters: false,
                    loadItems: true);
                err = loadErr;
                if (itemsArt is not null)
                {
                    bytes = itemsArt.Bytes;
                    version = itemsArt.Version;
                    sha = itemsArt.Sha256;
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
        }

        if (bytes is null)
        {
            lock (_gate)
            {
                _statusItems = "Catálogo: error · " + (err ?? "items_es no disponible");
            }
            return Task.CompletedTask;
        }

        var cachePath = VisualLibraryCache.ItemCachePath(cacheDir, version, sha);
        var cached = VisualLibraryCache.TryReadItems(cachePath);
        List<ItemCatalogEntry> built;
        Dictionary<int, string> types;

        if (cached is not null
            && cached.ItemsLangVersion == version
            && string.Equals(cached.ItemsSha256, sha, StringComparison.OrdinalIgnoreCase)
            && cached.Entries.Count > 0)
        {
            types = cached.TypeNames.ToDictionary(
                kv => int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k) ? k : -1,
                kv => kv.Value);
            types.Remove(-1);
            built = cached.Entries.Select(e => BuildItem(e.ItemId, e.Nombre, e.Level, e.TypeId, e.Category, e.GfxId)).ToList();
        }
        else
        {
            var snap = ItemsEsParser.Parse(bytes);
            types = snap.TypeNames.ToDictionary(kv => kv.Key, kv => kv.Value);
            built = new List<ItemCatalogEntry>(snap.Items.Count);
            var cacheRows = new List<VisualLibraryCache.ItemCacheRow>(snap.Items.Count);
            foreach (var kv in snap.Items.OrderBy(x => x.Key))
            {
                var raw = kv.Value;
                var cat = DofusItemTypeNames.Resolve(raw.TypeId, types);
                built.Add(BuildItem(raw.ItemId, raw.Nombre, raw.Level, raw.TypeId, cat, raw.GfxId));
                cacheRows.Add(new VisualLibraryCache.ItemCacheRow
                {
                    ItemId = raw.ItemId,
                    Nombre = raw.Nombre,
                    Level = raw.Level,
                    TypeId = raw.TypeId,
                    Category = cat,
                    GfxId = raw.GfxId,
                });
            }

            VisualLibraryCache.WriteItems(cachePath, new VisualLibraryCache.ItemCachePayload
            {
                ItemsLangVersion = version,
                ItemsSha256 = sha,
                Entries = cacheRows,
                TypeNames = types.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
            });
        }

        lock (_gate)
        {
            _itemsLangVersion = version > 0 ? version : null;
            _itemTypeNames = types;
            _items = RelinkItems(built, _clipsRoot);
            _statusItems = $"Catálogo: ✓ Cargado · {_items.Count} objetos · items,es,{version}"
                           + (err is null ? "" : $" · aviso: {err}");
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<MonsterCatalogEntry> SearchMonsters(string? query)
    {
        var q = (query ?? "").Trim();
        var all = Monsters;
        if (q.Length == 0)
            return all;

        var list = new List<MonsterCatalogEntry>();
        foreach (var m in all)
        {
            if (MatchesMonster(m, q))
                list.Add(m);
        }

        return list;
    }

    public IReadOnlyList<ItemCatalogEntry> SearchItems(string? query, int? typeId = null, int take = 200)
    {
        var q = (query ?? "").Trim();
        var all = Items;
        var list = new List<ItemCatalogEntry>();
        foreach (var it in all)
        {
            if (typeId is int t && it.TypeId != t)
                continue;
            if (q.Length > 0 && !MatchesItem(it, q))
                continue;
            list.Add(it);
            if (list.Count >= take) break;
        }

        return list;
    }

    public MonsterCatalogEntry? GetMonster(int id) =>
        Monsters.FirstOrDefault(m => m.Id == id);

    public ItemCatalogEntry? GetItem(int itemId) =>
        Items.FirstOrDefault(i => i.ItemId == itemId);

    private MonsterCatalogEntry BuildMonster(int id, string nombre, int gfxId, IReadOnlyList<int> levels)
    {
        var artRel = VisualClipPaths.ArtworkRelative(gfxId);
        var sprRel = VisualClipPaths.SpriteRelative(gfxId);
        return new MonsterCatalogEntry
        {
            Id = id,
            Nombre = nombre ?? "",
            GfxId = gfxId,
            Levels = levels,
            ArtworkRelativePath = artRel,
            SpriteRelativePath = sprRel,
        };
    }

    private ItemCatalogEntry BuildItem(int itemId, string nombre, int level, int typeId, string category, int gfxId)
    {
        var iconRel = VisualClipPaths.ItemIconRelative(gfxId);
        return new ItemCatalogEntry
        {
            ItemId = itemId,
            Nombre = nombre ?? "",
            Level = level,
            TypeId = typeId,
            Category = category ?? "",
            GfxId = gfxId,
            IconRelativePath = iconRel,
        };
    }

    private static IReadOnlyList<MonsterCatalogEntry> RelinkMonsters(
        IReadOnlyList<MonsterCatalogEntry> source,
        string? clipsRoot)
    {
        if (source.Count == 0) return source;
        var list = new List<MonsterCatalogEntry>(source.Count);
        foreach (var m in source)
        {
            var art = VisualClipPaths.ResolveFull(clipsRoot, m.ArtworkRelativePath);
            var spr = VisualClipPaths.ResolveFull(clipsRoot, m.SpriteRelativePath);
            list.Add(new MonsterCatalogEntry
            {
                Id = m.Id,
                Nombre = m.Nombre,
                GfxId = m.GfxId,
                Levels = m.Levels,
                ArtworkRelativePath = m.ArtworkRelativePath,
                SpriteRelativePath = m.SpriteRelativePath,
                ArtworkFullPath = art,
                SpriteFullPath = spr,
                ArtworkExists = VisualClipPaths.FileExists(art),
                SpriteExists = VisualClipPaths.FileExists(spr),
            });
        }

        return list;
    }

    private static IReadOnlyList<ItemCatalogEntry> RelinkItems(
        IReadOnlyList<ItemCatalogEntry> source,
        string? clipsRoot)
    {
        if (source.Count == 0) return source;
        var store = new PortableVisualStore();
        store.EnsureConfigured();
        var list = new List<ItemCatalogEntry>(source.Count);
        foreach (var it in source)
        {
            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, it.GfxId, it.TypeId);
            if (!exists && it.GfxId > 0)
            {
                var png = store.GetPngPath(VisualAssetCategory.Items, it.GfxId);
                if (png is not null && File.Exists(png))
                {
                    rel = PortableVisualStore.GetRelativePath(VisualAssetCategory.Items, it.GfxId);
                    full = png;
                    exists = true;
                }
            }

            list.Add(new ItemCatalogEntry
            {
                ItemId = it.ItemId,
                Nombre = it.Nombre,
                Level = it.Level,
                TypeId = it.TypeId,
                Category = it.Category,
                GfxId = it.GfxId,
                IconRelativePath = rel,
                IconFullPath = full,
                IconExists = exists,
            });
        }

        return list;
    }

    private static bool MatchesMonster(MonsterCatalogEntry m, string q)
    {
        if (int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
        {
            if (m.Id == num || m.GfxId == num)
                return true;
        }

        return m.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesItem(ItemCatalogEntry it, string q)
    {
        if (int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
        {
            if (it.ItemId == num || it.GfxId == num)
                return true;
        }

        return it.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static string FingerprintMobs(IReadOnlyList<MobsModeloRow> rows)
    {
        var sb = new StringBuilder(rows.Count * 12);
        foreach (var r in rows)
            sb.Append(r.Id).Append(':').Append(r.GfxId).Append(';');
        return Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
