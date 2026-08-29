using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A.3C — in-memory confirmed NPC gfx catalog (READ ONLY BD).</summary>
public sealed class NpcGfxCatalogService
{
    public static NpcGfxCatalogService Shared { get; } = new();

    private readonly object _gate = new();
    private IReadOnlyList<NpcGfxCatalogEntry> _entries = Array.Empty<NpcGfxCatalogEntry>();
    private IReadOnlyList<NpcGfxUsageRow> _usageRows = Array.Empty<NpcGfxUsageRow>();
    private IReadOnlyDictionary<int, string> _spriteNames = new Dictionary<int, string>();
    private string? _clipsRoot;
    private string? _effectiveClipsRoot;
    private string _status = "Catálogo apariencias NPC: no cargado";
    private string? _loadError;

    public IReadOnlyList<NpcGfxCatalogEntry> Entries
    {
        get { lock (_gate) return _entries; }
    }

    public string Status
    {
        get { lock (_gate) return _status; }
    }

    public string? LoadError
    {
        get { lock (_gate) return _loadError; }
    }

    public bool IsLoaded
    {
        get { lock (_gate) return _entries.Count > 0; }
    }

    public bool HasSpriteNames
    {
        get { lock (_gate) return _spriteNames.Count > 0; }
    }

    public bool HasEffectiveClipsRoot
    {
        get { lock (_gate) return !string.IsNullOrWhiteSpace(_effectiveClipsRoot); }
    }

    public string? EffectiveClipsRoot
    {
        get { lock (_gate) return _effectiveClipsRoot; }
    }

    public void SetClipsRoot(string? clipsRoot)
    {
        lock (_gate)
        {
            _clipsRoot = string.IsNullOrWhiteSpace(clipsRoot) ? null : Path.GetFullPath(clipsRoot.Trim());
            _effectiveClipsRoot = ClipsRootPaths.ResolveEffective(_clipsRoot);
        }
    }

    public async Task LoadAsync(
        DatabaseSettings db,
        string dbPassword,
        string? clipsRoot = null,
        INpcsGfxUsageReadRepository? usageRepo = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.IsNullOrWhiteSpace(clipsRoot))
            SetClipsRoot(clipsRoot);

        string? clips;
        lock (_gate)
            clips = _clipsRoot;

        var effectiveClips = ClipsRootPaths.ResolveEffective(clips);
        var parsedSprites = LoadSpriteNames(effectiveClips);

        try
        {
            var repo = usageRepo ?? new MysqlNpcsGfxUsageReadRepository(db, dbPassword);
            var rows = await repo.GetAllGfxUsageAsync(ct).ConfigureAwait(false);
            var built = NpcGfxCatalogBuilder.Build(rows, parsedSprites.Names, effectiveClips, parsedSprites.Warnings);

            lock (_gate)
            {
                _usageRows = rows;
                _entries = built.Entries;
                _spriteNames = parsedSprites.Names;
                _effectiveClipsRoot = effectiveClips;
                _loadError = null;
                _status = BuildStatusMessage(built.Entries.Count, parsedSprites.Names.Count, effectiveClips);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _usageRows = Array.Empty<NpcGfxUsageRow>();
                _entries = Array.Empty<NpcGfxCatalogEntry>();
                _spriteNames = new Dictionary<int, string>();
                _effectiveClipsRoot = null;
                _loadError = ex.Message;
                _status = "Catálogo: error";
            }

            throw;
        }
    }

    /// <summary>Re-read sprites.xml and refresh display names without reloading BD rows.</summary>
    public bool ReloadSpriteMetadata(string? clipsRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(clipsRoot))
            SetClipsRoot(clipsRoot);

        IReadOnlyList<NpcGfxUsageRow> rows;
        string? configured;
        lock (_gate)
        {
            rows = _usageRows;
            configured = _clipsRoot;
        }

        if (rows.Count == 0)
            return false;

        var effectiveClips = ClipsRootPaths.ResolveEffective(configured);
        var parsedSprites = LoadSpriteNames(effectiveClips);
        var built = NpcGfxCatalogBuilder.Build(rows, parsedSprites.Names, effectiveClips, parsedSprites.Warnings);

        lock (_gate)
        {
            _entries = built.Entries;
            _spriteNames = parsedSprites.Names;
            _effectiveClipsRoot = effectiveClips;
            _status = BuildStatusMessage(built.Entries.Count, parsedSprites.Names.Count, effectiveClips);
        }

        NpcGfxAppearanceNames.Invalidate();
        return true;
    }

    public IReadOnlyList<NpcGfxCatalogEntry> Search(string? query) =>
        NpcGfxCatalogSearch.Filter(Entries, query);

    public NpcGfxCatalogEntry? TryGet(int gfxId) =>
        Entries.FirstOrDefault(e => e.GfxId == gfxId);

    public string ResolveDisplayName(int gfxId)
    {
        var entry = TryGet(gfxId);
        if (entry is not null)
            return entry.DisplayName;

        lock (_gate)
        {
            if (_spriteNames.TryGetValue(gfxId, out var name) && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        return NpcGfxCatalogFormatting.FormatDisplayName(gfxId, null);
    }

    private static SpritesXmlParser.ParseResult LoadSpriteNames(string? effectiveClipsRoot)
    {
        var spritesXml = SpritesXmlParser.ResolveSpritesXmlPath(effectiveClipsRoot);
        return spritesXml is null
            ? SpritesXmlParser.ParseFile("")
            : SpritesXmlParser.ParseFile(spritesXml);
    }

    private static string BuildStatusMessage(int entryCount, int spriteNameCount, string? effectiveClips)
    {
        if (entryCount <= 0)
            return "Catálogo: vacío (npcs_modelo sin filas)";

        var baseMsg = $"Catálogo: ✓ {entryCount} apariencias NPC confirmadas";
        if (string.IsNullOrWhiteSpace(effectiveClips))
            return baseMsg + " · nombres: configura carpeta clips";

        return spriteNameCount > 0
            ? baseMsg + $" · nombres: ✓ {spriteNameCount}"
            : baseMsg + " · nombres: sprites.xml no encontrado";
    }
}
