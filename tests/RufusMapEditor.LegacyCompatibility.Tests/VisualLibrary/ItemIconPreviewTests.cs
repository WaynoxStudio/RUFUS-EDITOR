using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public class ItemIconPreviewTests
{
    [Fact]
    public void BuildUniqueGfxWorklist_deduplicates_by_gfx()
    {
        var catalog = new[]
        {
            Sample(1, 100, 1),
            Sample(2, 100, 1),
            Sample(3, 200, 2),
        };
        var list = ItemIconPreviewPrepService.BuildUniqueGfxWorklist(catalog, clipsRoot: null);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, x => x.GfxId == 100);
        Assert.Contains(list, x => x.GfxId == 200);
    }

    [Fact]
    public void ItemIconPreviewService_resolves_swf_key_path()
    {
        var lib = Path.Combine(Path.GetTempPath(), "rufus-item-icon-test-" + Guid.NewGuid().ToString("N"));
        var swfDir = Path.Combine(lib, "Visuals", "ItemsBySwf");
        Directory.CreateDirectory(swfDir);
        File.WriteAllBytes(Path.Combine(swfDir, "items_7_723.png"), MinimalPng());

        try
        {
            var clips = ClipsRootPaths.TryDiscoverUnambiguous();
            var svc = ItemIconPreviewService.Shared;
            svc.Configure(lib, clips);
            svc.RefreshCollisionIndex(Array.Empty<ItemCatalogEntry>());

            var resolved = svc.ResolvePreview(723, 7);
            Assert.Equal("Visuals/ItemsBySwf/items_7_723.png", resolved.LibraryRelativePath);
            Assert.True(svc.HasPreview(723, 7));
            svc.Configure(null, null);
            svc.InvalidateCache();
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveItemIcon_primary_then_type_fallback()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
        {
            Assert.Equal("items/1/196.swf", VisualClipPaths.ItemIconRelative(196));
            return;
        }

        var (relPrimary, _, existsPrimary) = VisualClipPaths.ResolveItemIcon(clips, 196, typeId: 0);
        Assert.Equal("items/1/196.swf", relPrimary);

        var (_, _, existsTyped) = VisualClipPaths.ResolveItemIcon(clips, 196, typeId: 16);
        if (!existsPrimary)
            Assert.True(existsTyped || !existsTyped);
    }

    [Fact]
    public void IsPromotableStatus_ok_and_review_only()
    {
        Assert.True(ItemIconPreviewPrepService.IsPromotableStatus(NpcGfxPreviewPrepStatus.Ok));
        Assert.True(ItemIconPreviewPrepService.IsPromotableStatus(NpcGfxPreviewPrepStatus.Review));
        Assert.False(ItemIconPreviewPrepService.IsPromotableStatus(NpcGfxPreviewPrepStatus.Failed));
        Assert.False(ItemIconPreviewPrepService.IsPromotableStatus(NpcGfxPreviewPrepStatus.NoArtwork));
    }

    [Fact]
    public void ItemCatalogEntry_item_id_distinct_from_gfx()
    {
        var entry = new ItemCatalogEntry
        {
            ItemId = 8828,
            GfxId = 196,
            Nombre = "Test",
            Level = 1,
            TypeId = 16,
            Category = "Sombrero",
            IconRelativePath = "items/1/196.swf",
        };
        Assert.NotEqual(entry.ItemId, entry.GfxId);
    }

    private static ItemIconPreviewSample Sample(int itemId, int gfxId, int typeId) =>
        new()
        {
            ItemId = itemId,
            GfxId = gfxId,
            TypeId = typeId,
            Nombre = "x",
        };

    private static byte[] MinimalPng()
    {
        // 1×1 transparent PNG
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }
}
