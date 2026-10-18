using System.Collections.Concurrent;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.RuntimeTools;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>
/// CONTENT.ITEMS.PICKER.AUTO_SYNC.1 — background queue for missing ItemsBySwf previews (max 2 workers).
/// </summary>
public sealed class ItemIconMissingPreviewSyncService : IDisposable
{
    public const int MaxWorkers = 2;

    private readonly ConcurrentDictionary<string, byte> _queuedOrDone = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<WorkItem> _high = new();
    private readonly ConcurrentQueue<WorkItem> _normal = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _activeWorkers;
    private bool _disposed;

    public event Action<ItemIconPreviewSyncResult>? PreviewCompleted;

    public sealed class ItemIconPreviewSyncResult
    {
        public required string PreviewKey { get; init; }
        public required string PreviewStem { get; init; }
        public required bool Success { get; init; }
        public string? Reason { get; init; }
        public string? OutputPng { get; init; }
    }

    private sealed class WorkItem
    {
        public required ItemIconPreviewKey Key { get; init; }
        public required string ResolvedSwfFullPath { get; init; }
    }

    /// <summary>
    /// Classify catalog entries: existing preview → skip; SWF missing → skip; else enqueue (dedup by PreviewKey).
    /// </summary>
    public int EnqueueMissing(
        IEnumerable<ItemCatalogEntry> entries,
        ItemIconPreviewService previewService,
        string? clipsRoot,
        bool highPriority)
    {
        if (string.IsNullOrWhiteSpace(clipsRoot) || !Directory.Exists(clipsRoot))
            return 0;

        var added = 0;
        foreach (var entry in entries)
        {
            if (entry.GfxId <= 0)
                continue;

            if (previewService.HasPreview(entry))
                continue;

            var (rel, full, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, entry.GfxId, entry.TypeId);
            if (!exists || string.IsNullOrWhiteSpace(full) || !File.Exists(full))
                continue;

            if (!ItemIconPreviewKey.TryFromResolvedSwfRelativePath(rel, out var key))
                continue;

            // Prefer explicit path matching PreviewKey when ResolveItemIcon remapped.
            var keyedFull = Path.GetFullPath(Path.Combine(
                clipsRoot,
                key.ResolvedSwfRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(keyedFull))
                full = keyedFull;

            if (!_queuedOrDone.TryAdd(key.Key, 0))
                continue;

            var item = new WorkItem { Key = key, ResolvedSwfFullPath = full! };
            if (highPriority)
                _high.Enqueue(item);
            else
                _normal.Enqueue(item);
            added++;
        }

        return added;
    }

    public void Start(
        string libraryRoot,
        string clipsRoot,
        string? stagingRoot = null)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_loop is { IsCompleted: false })
                return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var staging = string.IsNullOrWhiteSpace(stagingRoot)
                ? Path.Combine(RuntimeToolsPaths.ToolsCacheRoot, "item-preview-staging")
                : stagingRoot!;
            Directory.CreateDirectory(staging);

            _loop = Task.Run(() => RunWorkersAsync(libraryRoot, clipsRoot, staging, ct), ct);
        }
    }

    public void CancelPending()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunWorkersAsync(
        string libraryRoot,
        string clipsRoot,
        string stagingRoot,
        CancellationToken ct)
    {
        var workers = new Task[MaxWorkers];
        for (var i = 0; i < MaxWorkers; i++)
            workers[i] = Task.Run(() => WorkerLoop(libraryRoot, clipsRoot, stagingRoot, ct), ct);

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on close
        }
        catch (Exception ex)
        {
            RufusLog.Warn("Item preview sync workers · " + ex.Message);
        }
    }

    private void WorkerLoop(
        string libraryRoot,
        string clipsRoot,
        string stagingRoot,
        CancellationToken ct)
    {
        var generator = new ItemIconSinglePreviewService();
        while (!ct.IsCancellationRequested)
        {
            if (!TryDequeue(out var work) || work is null)
            {
                // Idle briefly; exit if queues stay empty and no siblings active with work.
                Thread.Sleep(120);
                if (_high.IsEmpty && _normal.IsEmpty)
                {
                    // Keep living while window open — new EnqueueMissing may arrive from filter.
                    // Exit only when cancelled.
                    continue;
                }

                continue;
            }

            Interlocked.Increment(ref _activeWorkers);
            try
            {
                ProcessOne(generator, work, libraryRoot, clipsRoot, stagingRoot, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
            }
        }
    }

    private bool TryDequeue(out WorkItem? item)
    {
        if (_high.TryDequeue(out item))
            return true;
        if (_normal.TryDequeue(out item))
            return true;
        item = null;
        return false;
    }

    private void ProcessOne(
        ItemIconSinglePreviewService generator,
        WorkItem work,
        string libraryRoot,
        string clipsRoot,
        string stagingRoot,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        // Skip if preview appeared meanwhile (e.g. another path).
        var previewSvc = ItemIconPreviewService.Shared;
        var existing = PortableVisualStore.ResolveItemSwfPreviewPngPath(libraryRoot, work.Key.FileStem);
        if (existing is not null && File.Exists(existing))
        {
            previewSvc.InvalidateCache(work.Key.FileStem);
            Raise(new ItemIconPreviewSyncResult
            {
                PreviewKey = work.Key.Key,
                PreviewStem = work.Key.FileStem,
                Success = true,
                Reason = "SKIP existing",
                OutputPng = existing,
            });
            return;
        }

        try
        {
            var jobStaging = Path.Combine(stagingRoot, work.Key.FileStem);
            Directory.CreateDirectory(jobStaging);

            var gen = generator.GenerateOneWithRuntimeTools(
                clipsRoot,
                jobStaging,
                work.Key.Key,
                libraryRoot: libraryRoot,
                processTimeout: TimeSpan.FromSeconds(90));

            if (ct.IsCancellationRequested
                && gen.Status is not NpcGfxPreviewPrepStatus.Ok and not NpcGfxPreviewPrepStatus.Review)
            {
                return;
            }

            var promo = ItemIconSinglePreviewService.PromoteOne(
                gen,
                libraryRoot,
                overwrite: false,
                includeReview: true);

            var ok = promo.Ok
                     && ItemIconPreviewPrepService.IsPromotableStatus(gen.Status);
            if (ok)
                previewSvc.InvalidateCache(work.Key.FileStem);
            else
                RufusLog.Warn(
                    $"Item preview sync FAIL {work.Key.Key} · {gen.Status} · {gen.Reason} · promote={promo.Reason}");

            Raise(new ItemIconPreviewSyncResult
            {
                PreviewKey = work.Key.Key,
                PreviewStem = work.Key.FileStem,
                Success = ok,
                Reason = ok ? (gen.Reason ?? "OK") : (gen.Reason ?? promo.Reason ?? "FAILED"),
                OutputPng = promo.DestinationPng ?? gen.OutputPng,
            });
        }
        catch (Exception ex)
        {
            RufusLog.Warn($"Item preview sync EX {work.Key.Key} · {ex.GetType().Name}: {ex.Message}");
            Raise(new ItemIconPreviewSyncResult
            {
                PreviewKey = work.Key.Key,
                PreviewStem = work.Key.FileStem,
                Success = false,
                Reason = "FfdecFailed: " + ex.Message,
            });
        }
    }

    private void Raise(ItemIconPreviewSyncResult result)
    {
        try { PreviewCompleted?.Invoke(result); }
        catch (Exception ex) { RufusLog.Warn("Item preview sync UI notify · " + ex.Message); }
    }

    /// <summary>Test helper: build unique missing PreviewKeys without starting workers.</summary>
    public static IReadOnlyList<ItemIconPreviewKey> CollectMissingPreviewKeys(
        IEnumerable<ItemCatalogEntry> entries,
        ItemIconPreviewService previewService,
        string? clipsRoot)
    {
        var list = new List<ItemIconPreviewKey>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(clipsRoot) || !Directory.Exists(clipsRoot))
            return list;

        foreach (var entry in entries)
        {
            if (entry.GfxId <= 0 || previewService.HasPreview(entry))
                continue;
            var (rel, _, exists) = VisualClipPaths.ResolveItemIcon(clipsRoot, entry.GfxId, entry.TypeId);
            if (!exists || !ItemIconPreviewKey.TryFromResolvedSwfRelativePath(rel, out var key))
                continue;
            if (!seen.Add(key.Key))
                continue;
            list.Add(key);
        }

        return list;
    }
}
