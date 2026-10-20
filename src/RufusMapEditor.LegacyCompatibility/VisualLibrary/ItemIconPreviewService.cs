using System.Collections.Concurrent;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1B — item icon previews keyed by resolved SWF path.
/// Primary: <c>Library/Visuals/ItemsBySwf/{previewStem}.png</c>.
/// Legacy fallback (SAFE GfxId only): <c>Library/Visuals/Items/{gfxId}.png</c>.
/// </summary>
public sealed class ItemIconPreviewService
{
    public static ItemIconPreviewService Shared { get; } = new();

    public enum PreviewSource
    {
        None = 0,
        SwfKey = 1,
        LegacySafeGfx = 2,
    }

    public sealed record PreviewResolution(
        ItemIconPreviewKey? PreviewKey,
        PreviewSource Source,
        string? LibraryRelativePath,
        string? FullPath);

    private readonly PortableVisualStore _store = new();
    private readonly ConcurrentDictionary<string, byte[]?> _pngCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _libraryRoot;
    private string? _clipsRoot;
    private ItemIconGfxCollisionIndex? _collisionIndex;

    public string? LibraryRoot => _libraryRoot;
    public string? ClipsRoot => _clipsRoot;

    public void Configure(string? libraryRoot, string? clipsRoot = null)
    {
        _libraryRoot = string.IsNullOrWhiteSpace(libraryRoot) ? null : Path.GetFullPath(libraryRoot.Trim());
        _clipsRoot = string.IsNullOrWhiteSpace(clipsRoot) ? null : ClipsRootPaths.Validate(clipsRoot).NormalizedPath ?? clipsRoot.Trim();
        _store.ConfigureLibraryRoot(_libraryRoot);
        _pngCache.Clear();
    }

    public void RefreshCollisionIndex(IEnumerable<ItemCatalogEntry> catalogEntries)
    {
        _collisionIndex = ItemIconGfxCollisionIndex.BuildFromEntries(catalogEntries, _clipsRoot);
    }

    public void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_libraryRoot))
            return;
        var lib = RufusLibraryPaths.TryResolveEffectiveLibrary(out _);
        if (lib is not null)
            Configure(lib, _clipsRoot);
    }

    public ItemIconPreviewKey? ResolvePreviewKey(int gfxId, int typeId)
    {
        if (gfxId <= 0)
            return null;
        var (rel, _, _) = VisualClipPaths.ResolveItemIcon(_clipsRoot, gfxId, typeId);
        return ItemIconPreviewKey.TryFromResolvedSwfRelativePath(rel, out var key) ? key : null;
    }

    public PreviewResolution ResolvePreview(int gfxId, int typeId)
    {
        EnsureConfigured();
        if (gfxId <= 0)
            return new PreviewResolution(null, PreviewSource.None, null, null);

        var key = ResolvePreviewKey(gfxId, typeId);
        if (key is null)
            return new PreviewResolution(null, PreviewSource.None, null, null);

        var swfPath = PortableVisualStore.ResolveItemSwfPreviewPngPath(_libraryRoot, key.Value.FileStem);
        if (swfPath is not null && File.Exists(swfPath))
        {
            return new PreviewResolution(
                key,
                PreviewSource.SwfKey,
                key.Value.LibraryRelativePath,
                swfPath);
        }

        if (_collisionIndex?.IsSafeGfx(gfxId) == true)
        {
            var legacyRel = PortableVisualStore.GetRelativePath(VisualAssetCategory.Items, gfxId);
            var legacyFull = _store.GetPngPath(VisualAssetCategory.Items, gfxId);
            if (legacyFull is not null && File.Exists(legacyFull))
            {
                return new PreviewResolution(key, PreviewSource.LegacySafeGfx, legacyRel, legacyFull);
            }
        }

        return new PreviewResolution(key, PreviewSource.None, null, null);
    }

    public bool HasPreview(int gfxId, int typeId) =>
        ResolvePreview(gfxId, typeId).Source != PreviewSource.None;

    public bool HasPreview(ItemCatalogEntry entry) =>
        HasPreview(entry.GfxId, entry.TypeId);

    public string? GetPreviewRelativePath(int gfxId, int typeId) =>
        ResolvePreview(gfxId, typeId).LibraryRelativePath;

    public string? GetPreviewFullPath(int gfxId, int typeId) =>
        ResolvePreview(gfxId, typeId).FullPath;

    public async Task<byte[]?> TryReadPngAsync(int gfxId, int typeId, CancellationToken ct = default)
    {
        EnsureConfigured();
        var key = ResolvePreviewKey(gfxId, typeId);
        var cacheKey = key?.FileStem;
        if (!string.IsNullOrWhiteSpace(cacheKey) && _pngCache.TryGetValue(cacheKey, out var hit))
            return hit;

        var resolved = ResolvePreview(gfxId, typeId);
        if (resolved.Source == PreviewSource.None || string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            if (!string.IsNullOrWhiteSpace(cacheKey))
                _pngCache[cacheKey] = null;
            return null;
        }

        cacheKey ??= resolved.PreviewKey?.FileStem ?? resolved.FullPath;
        if (_pngCache.TryGetValue(cacheKey, out hit))
            return hit;

        var fullPath = resolved.FullPath;
        var bytes = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(fullPath))
                    return null;
                var png = File.ReadAllBytes(fullPath);
                if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50)
                    return null;
                return png;
            }
            catch
            {
                return null;
            }
        }, ct).ConfigureAwait(false);

        _pngCache[cacheKey] = bytes;
        return bytes;
    }

    public void InvalidateCache(string? previewKeyStem = null)
    {
        if (string.IsNullOrWhiteSpace(previewKeyStem))
            _pngCache.Clear();
        else
            _pngCache.TryRemove(previewKeyStem, out _);

        try { CacheInvalidated?.Invoke(previewKeyStem); }
        catch
        {
            // UI listeners must not break prep
        }
    }

    /// <summary>Raised after cache invalidation. Arg = preview stem or null for full clear.</summary>
    public event Action<string?>? CacheInvalidated;
}

