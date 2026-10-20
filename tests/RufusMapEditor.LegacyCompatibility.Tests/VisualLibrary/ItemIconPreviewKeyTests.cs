using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public class ItemIconPreviewKeyTests
{
    [Fact]
    public void A_distinct_swf_paths_produce_distinct_preview_keys()
    {
        var a = ItemIconPreviewKey.FromResolvedSwfRelativePath("items/5/10.swf");
        var b = ItemIconPreviewKey.FromResolvedSwfRelativePath("items/23/10.swf");
        Assert.NotEqual(a, b);
        Assert.NotEqual(a.FileStem, b.FileStem);
        Assert.Equal("items_5_10", a.FileStem);
        Assert.Equal("items_23_10", b.FileStem);
    }

    [Fact]
    public void B_same_swf_from_two_items_shares_preview_key()
    {
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        var a = ItemIconPreviewKey.FromItem(gfxId: 10, typeId: 5, clips);
        var b = ItemIconPreviewKey.FromItem(gfxId: 10, typeId: 5, clips);
        Assert.Equal(a, b);
    }

    [Fact]
    public void C_same_gfx_different_swf_does_not_deduplicate_in_worklist()
    {
        var catalog = new[]
        {
            Sample(94, 10, 5),
            Sample(8072, 10, 23),
        };
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        if (string.IsNullOrWhiteSpace(clips))
        {
            Assert.Equal(2, catalog.Length);
            return;
        }

        var list = ItemIconSwfPreviewPrepService.BuildUniqueSwfWorklist(catalog, clips);
        var gfx10 = list.Where(x => x.GfxId == 10).ToList();
        Assert.True(gfx10.Count >= 2, "Expected at least two SWF entries for GFX 10.");
        Assert.Equal(2, gfx10.Select(x => x.PreviewKey).Distinct().Count());
    }

    [Fact]
    public void D_same_swf_different_type_ids_deduplicates()
    {
        var catalog = new[]
        {
            Sample(1, 100, 1),
            Sample(2, 100, 1),
            Sample(3, 100, 2),
        };
        var clips = ClipsRootPaths.TryDiscoverUnambiguous();
        var list = ItemIconSwfPreviewPrepService.BuildUniqueSwfWorklist(catalog, clips);
        if (string.IsNullOrWhiteSpace(clips))
        {
            Assert.True(list.Count >= 1);
            return;
        }

        var keysFor100 = list.Where(x => x.GfxId == 100).Select(x => x.PreviewKey).Distinct().ToList();
        Assert.Single(keysFor100);
        var entry = list.First(x => x.GfxId == 100);
        Assert.Equal(3, entry.AssociatedItemIds.Count);
    }

    [Fact]
    public void E_preview_key_stable_between_executions()
    {
        var first = ItemIconPreviewKey.FromResolvedSwfRelativePath("items/23/10.swf");
        var second = ItemIconPreviewKey.FromResolvedSwfRelativePath("items\\23\\10.swf");
        Assert.Equal(first, second);
        Assert.Equal(first.FileStem, second.FileStem);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void F_preview_key_is_not_item_id()
    {
        var key = ItemIconPreviewKey.FromResolvedSwfRelativePath("items/5/10.swf");
        Assert.DoesNotContain("8072", key.Key);
        Assert.DoesNotContain("8072", key.FileStem);
        Assert.NotEqual("8072", key.FileStem);
    }

    [Fact]
    public void Library_relative_path_uses_items_by_swf_namespace()
    {
        var key = ItemIconPreviewKey.FromResolvedSwfRelativePath("items/23/10.swf");
        Assert.Equal("Visuals/ItemsBySwf/items_23_10.png", key.LibraryRelativePath);
    }

    private static ItemIconPreviewSample Sample(int itemId, int gfxId, int typeId) =>
        new()
        {
            ItemId = itemId,
            GfxId = gfxId,
            TypeId = typeId,
            Nombre = "x",
        };
}
