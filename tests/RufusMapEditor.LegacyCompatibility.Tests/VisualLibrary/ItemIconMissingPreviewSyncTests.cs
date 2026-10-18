using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public class ItemIconMissingPreviewSyncTests
{
    [Fact]
    public void A_existing_preview_not_enqueued()
    {
        var lib = TempLib();
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        try
        {
            var key = ItemIconPreviewKey.FromItem(10, 5, clips);
            Directory.CreateDirectory(Path.Combine(lib, "Visuals", "ItemsBySwf"));
            File.WriteAllBytes(Path.Combine(lib, "Visuals", "ItemsBySwf", key.FileStem + ".png"), MinimalPng());

            var svc = new ItemIconPreviewService();
            svc.Configure(lib, clips);
            using var sync = new ItemIconMissingPreviewSyncService();
            var n = sync.EnqueueMissing(
            [
                new ItemCatalogEntry
                {
                    ItemId = 1, GfxId = 10, TypeId = 5, Nombre = "x", Level = 1, Category = "c",
                    IconRelativePath = key.ResolvedSwfRelativePath,
                },
            ], svc, clips, highPriority: true);
            Assert.Equal(0, n);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    [Fact]
    public void C_two_items_same_preview_key_enqueue_once()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        var (rel, _, exists) = VisualClipPaths.ResolveItemIcon(clips, 10, 5);
        if (!exists || !ItemIconPreviewKey.TryFromResolvedSwfRelativePath(rel, out var key))
            return;

        var lib = TempLib();
        try
        {
            var svc = new ItemIconPreviewService();
            svc.Configure(lib, clips);
            using var sync = new ItemIconMissingPreviewSyncService();
            var entries = new[]
            {
                new ItemCatalogEntry
                {
                    ItemId = 100, GfxId = 10, TypeId = 5, Nombre = "a", Level = 1, Category = "c",
                    IconRelativePath = key.ResolvedSwfRelativePath,
                },
                new ItemCatalogEntry
                {
                    ItemId = 101, GfxId = 10, TypeId = 5, Nombre = "b", Level = 1, Category = "c",
                    IconRelativePath = key.ResolvedSwfRelativePath,
                },
            };
            var n = sync.EnqueueMissing(entries, svc, clips, highPriority: true);
            Assert.Equal(1, n);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    [Fact]
    public void D_null_cache_then_invalidate_allows_reload()
    {
        var lib = TempLib();
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        try
        {
            var key = ItemIconPreviewKey.FromItem(10, 5, clips);
            var svc = new ItemIconPreviewService();
            svc.Configure(lib, clips);

            var missing = svc.TryReadPngAsync(10, 5).GetAwaiter().GetResult();
            Assert.Null(missing);

            Directory.CreateDirectory(Path.Combine(lib, "Visuals", "ItemsBySwf"));
            File.WriteAllBytes(Path.Combine(lib, "Visuals", "ItemsBySwf", key.FileStem + ".png"), MinimalPng());

            // Without invalidate, null stays cached.
            var stillNull = svc.TryReadPngAsync(10, 5).GetAwaiter().GetResult();
            Assert.Null(stillNull);

            svc.InvalidateCache(key.FileStem);
            var loaded = svc.TryReadPngAsync(10, 5).GetAwaiter().GetResult();
            Assert.NotNull(loaded);
            Assert.True(loaded!.Length > 0);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    [Fact]
    public void E_swf_missing_not_enqueued()
    {
        var lib = TempLib();
        try
        {
            var svc = new ItemIconPreviewService();
            svc.Configure(lib, lib); // invalid clips root without SWFs
            using var sync = new ItemIconMissingPreviewSyncService();
            var n = sync.EnqueueMissing(
            [
                new ItemCatalogEntry
                {
                    ItemId = 1, GfxId = 999999, TypeId = 1, Nombre = "x", Level = 1, Category = "c",
                    IconRelativePath = "items/0/999999.swf",
                },
            ], svc, lib, highPriority: true);
            Assert.Equal(0, n);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    [Fact]
    public void CollectMissing_dedupes_by_preview_key()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        var (rel, _, exists) = VisualClipPaths.ResolveItemIcon(clips, 10, 5);
        if (!exists)
            return;

        var lib = TempLib();
        try
        {
            var svc = new ItemIconPreviewService();
            svc.Configure(lib, clips);
            var keys = ItemIconMissingPreviewSyncService.CollectMissingPreviewKeys(
            [
                new ItemCatalogEntry
                {
                    ItemId = 1, GfxId = 10, TypeId = 5, Nombre = "a", Level = 1, Category = "c",
                    IconRelativePath = rel,
                },
                new ItemCatalogEntry
                {
                    ItemId = 2, GfxId = 10, TypeId = 5, Nombre = "b", Level = 1, Category = "c",
                    IconRelativePath = rel,
                },
            ], svc, clips);
            Assert.Single(keys);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    [Fact]
    public void H_invalid_clips_enqueue_zero()
    {
        var lib = TempLib();
        try
        {
            var svc = new ItemIconPreviewService();
            svc.Configure(lib, null);
            using var sync = new ItemIconMissingPreviewSyncService();
            var n = sync.EnqueueMissing(
            [
                new ItemCatalogEntry
                {
                    ItemId = 1, GfxId = 10, TypeId = 5, Nombre = "x", Level = 1, Category = "c",
                    IconRelativePath = "items/0/10.swf",
                },
            ], svc, clipsRoot: null, highPriority: true);
            Assert.Equal(0, n);
        }
        finally
        {
            TryDelete(lib);
        }
    }

    private static string TempLib()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(lib, "Visuals", "ItemsBySwf"));
        return lib;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { /* ignore */ }
    }

    private static byte[] MinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
