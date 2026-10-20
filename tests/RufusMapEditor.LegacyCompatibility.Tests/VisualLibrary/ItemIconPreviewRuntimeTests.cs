using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public class ItemIconPreviewRuntimeTests
{
    [Fact]
    public void A_same_gfx_distinct_swf_distinct_preview_paths()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        var a = ItemIconPreviewKey.FromItem(10, 5, clips);
        var b = ItemIconPreviewKey.FromItem(10, 23, clips);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a.LibraryRelativePath, b.LibraryRelativePath);
    }

    [Fact]
    public void B_same_swf_same_preview_path_for_different_items()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        var key5 = ItemIconPreviewKey.FromItem(10, 5, clips);
        var key8072 = ItemIconPreviewKey.FromItem(10, 23, clips);
        var (_, _, exists8072) = VisualClipPaths.ResolveItemIcon(clips, 10, 23);
        if (!exists8072)
            return;

        Assert.NotEqual(key5, key8072);
        Assert.Equal("items/23/10.swf", key8072.ResolvedSwfRelativePath, ignoreCase: true);
    }

    [Fact]
    public void C_item_8072_resolves_items_23_10_swf()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
            return;

        var (rel, _, exists) = VisualClipPaths.ResolveItemIcon(clips, 10, 23);
        Assert.True(exists);
        Assert.Equal("items/23/10.swf", rel, ignoreCase: true);
    }

    [Fact]
    public void D_runtime_lookup_uses_preview_key_path()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-swf-runtime-" + Guid.NewGuid().ToString("N"));
        var swfDir = Path.Combine(lib, "Visuals", "ItemsBySwf");
        Directory.CreateDirectory(swfDir);
        File.WriteAllBytes(Path.Combine(swfDir, "items_23_10.png"), MinimalPng());

        try
        {
            var clips = ClipsRootPaths.TryDiscoverUnambiguous();
            var svc = ItemIconPreviewService.Shared;
            svc.Configure(lib, clips);
            svc.RefreshCollisionIndex(Array.Empty<ItemCatalogEntry>());

            var resolved = svc.ResolvePreview(10, 23);
            Assert.Equal(ItemIconPreviewService.PreviewSource.SwfKey, resolved.Source);
            Assert.Equal("Visuals/ItemsBySwf/items_23_10.png", resolved.LibraryRelativePath);
        }
        finally
        {
            ResetService();
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void E_cache_uses_preview_key_stem()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-cache-" + Guid.NewGuid().ToString("N"));
        var swfDir = Path.Combine(lib, "Visuals", "ItemsBySwf");
        Directory.CreateDirectory(swfDir);
        File.WriteAllBytes(Path.Combine(swfDir, "items_5_10.png"), MinimalPng());

        try
        {
            var clips = ClipsRootPaths.TryDiscoverUnambiguous();
            var svc = ItemIconPreviewService.Shared;
            svc.Configure(lib, clips);
            svc.RefreshCollisionIndex(Array.Empty<ItemCatalogEntry>());

            var first = svc.TryReadPngAsync(10, 5).GetAwaiter().GetResult();
            var second = svc.TryReadPngAsync(10, 5).GetAwaiter().GetResult();
            Assert.NotNull(first);
            Assert.Same(first, second);
        }
        finally
        {
            ResetService();
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void F_legacy_fallback_blocked_for_collision_gfx()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-collision-" + Guid.NewGuid().ToString("N"));
        var legacyDir = Path.Combine(lib, "Visuals", "Items");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllBytes(Path.Combine(legacyDir, "10.png"), MinimalPng());

        try
        {
            var clips = ClipsRootPaths.TryDiscoverUnambiguous();
            if (string.IsNullOrWhiteSpace(clips))
                return;

            var svc = ItemIconPreviewService.Shared;
            svc.Configure(lib, clips);
            svc.RefreshCollisionIndex(
            [
                new ItemCatalogEntry
                {
                    ItemId = 94, GfxId = 10, TypeId = 5, Nombre = "a", Level = 1, Category = "x",
                    IconRelativePath = "items/0/10.swf",
                },
                new ItemCatalogEntry
                {
                    ItemId = 8072, GfxId = 10, TypeId = 23, Nombre = "b", Level = 1, Category = "x",
                    IconRelativePath = "items/0/10.swf",
                },
            ]);

            var resolved = svc.ResolvePreview(10, 23);
            Assert.Equal(ItemIconPreviewService.PreviewSource.None, resolved.Source);
        }
        finally
        {
            ResetService();
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void G_placeholder_when_no_preview()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(lib, "Visuals", "ItemsBySwf"));

        try
        {
            var clips = ClipsRootPaths.TryDiscoverUnambiguous();
            var svc = ItemIconPreviewService.Shared;
            svc.Configure(lib, clips);
            svc.RefreshCollisionIndex(Array.Empty<ItemCatalogEntry>());
            Assert.False(svc.HasPreview(10, 23));
        }
        finally
        {
            ResetService();
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void H_item_catalog_entry_preserves_item_id()
    {
        var entry = new ItemCatalogEntry
        {
            ItemId = 8072,
            GfxId = 10,
            TypeId = 23,
            Nombre = "Dofus Kalipto",
            Level = 6,
            Category = "Dofus",
            IconRelativePath = "items/0/10.swf",
        };
        Assert.Equal(8072, entry.ItemId);
        Assert.NotEqual(entry.ItemId, entry.GfxId);
    }

    private static void ResetService()
    {
        ItemIconPreviewService.Shared.Configure(null, null);
        ItemIconPreviewService.Shared.InvalidateCache();
    }

    private static byte[] MinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
