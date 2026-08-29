using System.Collections.Concurrent;
using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Portable;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.4.2 — on-demand PNG cache keyed by gfxID under <c>Library/cache/artworks/{gfx}.png</c>.
/// </summary>
public sealed class ArtworkPreviewCache
{
    public const string CacheFolderName = "cache";
    public const string ArtworksFolderName = "artworks";

    private readonly ConcurrentDictionary<int, byte> _negative = new();
    private string? _cacheRoot;

    public string? CacheRoot => _cacheRoot;

    public void ConfigureLibraryRoot(string? libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            _cacheRoot = null;
            return;
        }

        _cacheRoot = Path.Combine(
            Path.GetFullPath(libraryRoot),
            CacheFolderName,
            ArtworksFolderName,
            "v" + SwfArtworkThumbnailRenderer.CacheVersion.ToString(CultureInfo.InvariantCulture));
    }

    public void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_cacheRoot))
            return;

        var lib = RufusLibraryPaths.TryResolveEffectiveLibrary(out _);
        if (lib is not null)
            ConfigureLibraryRoot(lib);
    }

    public string? GetCachedPngPath(int gfxId)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_cacheRoot))
            return null;
        return Path.Combine(_cacheRoot, gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
    }

    public bool TryReadCachedPng(int gfxId, out byte[]? png)
    {
        png = null;
        var path = GetCachedPngPath(gfxId);
        if (path is null || !File.Exists(path))
            return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
                return false;
            png = bytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void WriteCachedPng(int gfxId, byte[] png)
    {
        var path = GetCachedPngPath(gfxId);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
        _negative.TryRemove(gfxId, out _);
    }

    public void MarkFailed(int gfxId) => _negative[gfxId] = 1;

    public bool WasFailed(int gfxId) => _negative.ContainsKey(gfxId);

    public void ClearFailed(int gfxId) => _negative.TryRemove(gfxId, out _);

    public int ClearCache()
    {
        EnsureConfigured();
        _negative.Clear();
        var n = 0;
        if (!string.IsNullOrWhiteSpace(_cacheRoot) && Directory.Exists(_cacheRoot))
        {
            foreach (var f in Directory.EnumerateFiles(_cacheRoot, "*.png"))
            {
                try
                {
                    File.Delete(f);
                    n++;
                }
                catch
                {
                    // ignore locked files
                }
            }
        }

        var legacyRoot = _cacheRoot is null
            ? null
            : Path.GetDirectoryName(_cacheRoot);
        if (!string.IsNullOrWhiteSpace(legacyRoot) && Directory.Exists(legacyRoot))
        {
            foreach (var f in Directory.EnumerateFiles(legacyRoot, "*.png"))
            {
                try
                {
                    File.Delete(f);
                    n++;
                }
                catch
                {
                    // ignore
                }
            }
        }

        return n;
    }
}

/// <summary>
/// LIB.4.2/4.3 — resolve mob artwork preview by gfxID.
/// Priority: manual <c>Library/Visuals/Mobs/{gfx}.png</c> → SWF cache → SWF rasterize → null.
/// </summary>
public sealed class ArtworkPreviewService
{
    public static ArtworkPreviewService Shared { get; } = new();

    private readonly ArtworkPreviewCache _cache = new();
    private readonly PortableVisualStore _visuals = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public ArtworkPreviewCache Cache => _cache;
    public PortableVisualStore Visuals => _visuals;

    public string? ClipsRoot { get; private set; }
    public string? LibraryRoot { get; private set; }

    public string ClipsStatus { get; private set; } = "Clips: —";

    /// <summary>Raised when a manual visual for a gfxID changes (import/replace/delete).</summary>
    public event Action<int>? ManualVisualChanged;

    public void Configure(string? clipsRoot, string? libraryRoot)
    {
        ClipsRoot = string.IsNullOrWhiteSpace(clipsRoot) ? null : Path.GetFullPath(clipsRoot.Trim());
        LibraryRoot = string.IsNullOrWhiteSpace(libraryRoot) ? null : Path.GetFullPath(libraryRoot.Trim());
        _cache.ConfigureLibraryRoot(LibraryRoot);
        _visuals.ConfigureLibraryRoot(LibraryRoot);
        RefreshClipsStatus();
    }

    public void RefreshClipsStatus()
    {
        if (string.IsNullOrWhiteSpace(ClipsRoot))
        {
            ClipsStatus = "⚠ Ruta de clips no configurada";
            return;
        }

        var art = Path.Combine(ClipsRoot, "artworks", "big");
        ClipsStatus = Directory.Exists(art)
            ? "Clips: ✓ Encontrado"
            : "Clips: ⚠ carpeta artworks/big no encontrada";
    }

    public string? ResolveArtworkSwfPath(int gfxId)
    {
        if (string.IsNullOrWhiteSpace(ClipsRoot))
            return null;
        return VisualClipPaths.ResolveFull(ClipsRoot, VisualClipPaths.ArtworkRelative(gfxId));
    }

    public bool HasManualVisual(int gfxId) =>
        _visuals.Exists(VisualAssetCategory.Mobs, gfxId);

    /// <summary>
    /// File IO only (safe on any thread). Does NOT raise <see cref="ManualVisualChanged"/>.
    /// Call <see cref="NotifyManualVisualChanged"/> on the UI thread afterwards.
    /// </summary>
    public void ImportManualMobVisualFile(int gfxId, string sourceFilePath)
    {
        _visuals.ImportFromFile(VisualAssetCategory.Mobs, gfxId, sourceFilePath);
        _cache.ClearFailed(gfxId);
    }

    /// <summary>UI-thread notification after a successful manual import/replace/delete.</summary>
    public void NotifyManualVisualChanged(int gfxId) =>
        ManualVisualChanged?.Invoke(gfxId);

    /// <summary>
    /// Import + notify. Prefer splitting file work to background and calling
    /// <see cref="NotifyManualVisualChanged"/> on the UI thread from WPF code.
    /// </summary>
    public void ImportManualMobVisual(int gfxId, string sourceFilePath)
    {
        ImportManualMobVisualFile(gfxId, sourceFilePath);
        NotifyManualVisualChanged(gfxId);
    }

    public bool DeleteManualMobVisual(int gfxId)
    {
        var ok = _visuals.Delete(VisualAssetCategory.Mobs, gfxId);
        if (ok)
            NotifyManualVisualChanged(gfxId);
        return ok;
    }

    /// <summary>
    /// Returns PNG bytes or null if unavailable.
    /// Order: manual Visuals/Mobs → auto cache → SWF rasterize.
    /// </summary>
    public async Task<byte[]?> GetOrCreatePngAsync(int gfxId, CancellationToken ct = default)
    {
        if (gfxId <= 0) return null;

        if (_visuals.TryReadPng(VisualAssetCategory.Mobs, gfxId, out var manual) && manual is not null)
            return manual;

        if (_cache.TryReadCachedPng(gfxId, out var cached) && cached is not null)
            return cached;

        if (_cache.WasFailed(gfxId))
            return null;

        var gate = _gates.GetOrAdd(gfxId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_visuals.TryReadPng(VisualAssetCategory.Mobs, gfxId, out manual) && manual is not null)
                return manual;
            if (_cache.TryReadCachedPng(gfxId, out cached) && cached is not null)
                return cached;

            var swfPath = ResolveArtworkSwfPath(gfxId);
            if (swfPath is null || !File.Exists(swfPath))
            {
                _cache.MarkFailed(gfxId);
                return null;
            }

            byte[] png;
            try
            {
                var swfBytes = await File.ReadAllBytesAsync(swfPath, ct).ConfigureAwait(false);
                png = await Task.Run(
                        () => SwfArtworkThumbnailRenderer.RasterizeToPng(swfBytes, SwfArtworkThumbnailRenderer.DefaultThumbnailSize, gfxId),
                        ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                _cache.MarkFailed(gfxId);
                return null;
            }

            try
            {
                _cache.WriteCachedPng(gfxId, png);
            }
            catch
            {
                // Cache write failure still returns the PNG for this session.
            }

            return png;
        }
        finally
        {
            gate.Release();
        }
    }
}
