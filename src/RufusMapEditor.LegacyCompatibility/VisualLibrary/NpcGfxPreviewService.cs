using System.Collections.Concurrent;
using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public sealed class NpcGfxPreviewService
{
    public static NpcGfxPreviewService Shared { get; } = new();

    private readonly SpritePreviewCache _spriteCache = new();
    private readonly ArtworkPreviewService _artwork = ArtworkPreviewService.Shared;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<int, NpcGfxPreviewResolveInfo> _lastResolve = new();

    public SpritePreviewCache SpriteCache => _spriteCache;

    public NpcGfxPreviewResolveInfo? GetLastResolveInfo(int gfxId) =>
        _lastResolve.TryGetValue(gfxId, out var info) ? info : null;

    public void Configure(string? clipsRoot, string? libraryRoot)
    {
        _artwork.Configure(clipsRoot, libraryRoot);
        _spriteCache.ConfigureLibraryRoot(
            string.IsNullOrWhiteSpace(libraryRoot) ? null : Path.GetFullPath(libraryRoot.Trim()));
    }

    public string? ResolveSpriteSwfPath(int gfxId)
    {
        if (string.IsNullOrWhiteSpace(_artwork.ClipsRoot))
            return null;
        return VisualClipPaths.ResolveFull(_artwork.ClipsRoot, VisualClipPaths.SpriteRelative(gfxId));
    }

    public async Task<byte[]?> GetOrCreatePngAsync(int gfxId, CancellationToken ct = default)
    {
        var (png, _) = await GetOrCreatePngWithInfoAsync(gfxId, ct).ConfigureAwait(false);
        return png;
    }

    public async Task<(byte[]? Png, NpcGfxPreviewResolveInfo Info)> GetOrCreatePngWithInfoAsync(
        int gfxId,
        CancellationToken ct = default)
    {
        var paths = PreviewCacheUtility.ResolvePaths(_artwork.LibraryRoot);
        var info = new NpcGfxPreviewResolveInfo
        {
            GfxId = gfxId,
            LibraryRoot = paths.LibraryRoot,
            Renderer = nameof(SwfSpriteThumbnailRenderer),
        };

        if (gfxId <= 0)
        {
            info.Source = NpcGfxPreviewSource.Placeholder;
            StoreResolve(gfxId, info);
            return (null, info);
        }

        if (_spriteCache.TryReadCachedPng(gfxId, out var cached) && cached is not null)
        {
            info.Source = NpcGfxPreviewSource.CacheSprite;
            info.CachePath = _spriteCache.GetCachedPngPath(gfxId);
            StoreResolve(gfxId, info);
            RufusLog.Info($"NpcPreview gfx={gfxId} source=CACHE_SPRITE path={info.CachePath}");
            return (cached, info);
        }

        if (_spriteCache.WasFailed(gfxId))
        {
            var artPng = await _artwork.GetOrCreatePngAsync(gfxId, ct).ConfigureAwait(false);
            info.Source = artPng is null ? NpcGfxPreviewSource.Placeholder : NpcGfxPreviewSource.ArtworkFallback;
            info.UsedArtworkFallback = true;
            info.CachePath = _artwork.Cache.GetCachedPngPath(gfxId);
            info.Renderer = nameof(SwfArtworkRasterizer);
            StoreResolve(gfxId, info);
            RufusLog.Info($"NpcPreview gfx={gfxId} source=ARTWORK_FALLBACK (sprite failed earlier)");
            return (artPng, info);
        }

        var gate = _gates.GetOrAdd(gfxId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_spriteCache.TryReadCachedPng(gfxId, out cached) && cached is not null)
            {
                info.Source = NpcGfxPreviewSource.CacheSprite;
                info.CachePath = _spriteCache.GetCachedPngPath(gfxId);
                StoreResolve(gfxId, info);
                return (cached, info);
            }

            var swfPath = ResolveSpriteSwfPath(gfxId);
            if (swfPath is null || !File.Exists(swfPath))
            {
                _spriteCache.MarkFailed(gfxId);
                var artPng = await _artwork.GetOrCreatePngAsync(gfxId, ct).ConfigureAwait(false);
                info.Source = artPng is null ? NpcGfxPreviewSource.Placeholder : NpcGfxPreviewSource.ArtworkFallback;
                info.UsedArtworkFallback = true;
                info.Renderer = nameof(SwfArtworkRasterizer);
                StoreResolve(gfxId, info);
                return (artPng, info);
            }

            byte[]? png = null;
            try
            {
                var swfBytes = await File.ReadAllBytesAsync(swfPath, ct).ConfigureAwait(false);
                SwfSpriteThumbnailRenderer.RenderDiagnostics? diag = null;
                png = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    using var bmp = SwfSpriteThumbnailRenderer.RasterizeToBitmap(
                        swfBytes,
                        SwfSpriteThumbnailRenderer.DefaultSize,
                        gfxId,
                        out diag);
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return ms.ToArray();
                }, ct).ConfigureAwait(false);

                info.Source = NpcGfxPreviewSource.SpriteRenderer;
                info.SpriteId = diag?.SpriteId;
                info.FrameIndex = diag?.FrameIndex;
                info.SelectionReason = diag?.SelectionReason;
                info.CachePath = _spriteCache.GetCachedPngPath(gfxId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _spriteCache.MarkFailed(gfxId);
                var artPng = await _artwork.GetOrCreatePngAsync(gfxId, ct).ConfigureAwait(false);
                info.Source = artPng is null ? NpcGfxPreviewSource.Placeholder : NpcGfxPreviewSource.ArtworkFallback;
                info.UsedArtworkFallback = true;
                info.Renderer = nameof(SwfArtworkRasterizer);
                info.SelectionReason = "sprite error: " + ex.Message;
                StoreResolve(gfxId, info);
                RufusLog.Info($"NpcPreview gfx={gfxId} source=ARTWORK_FALLBACK err={ex.Message}");
                return (artPng, info);
            }

            try
            {
                _spriteCache.WriteCachedPng(gfxId, png);
            }
            catch
            {
                // still return png
            }

            StoreResolve(gfxId, info);
            RufusLog.Info(
                $"NpcPreview gfx={gfxId} source=SPRITE_RENDERER sprite={info.SpriteId} frame={info.FrameIndex} reason={info.SelectionReason}");
            return (png, info);
        }
        finally
        {
            gate.Release();
        }
    }

    private void StoreResolve(int gfxId, NpcGfxPreviewResolveInfo info)
    {
        _lastResolve[gfxId] = info;
    }
}
