using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>ADMIN.UI.4B.2A.2 — actions in Identidad, compact summary, conditional commerce fields.</summary>
public sealed class NpcClientActionsUiTests
{
    [Fact]
    public void Talk_only_hides_commerce_fields()
    {
        Assert.True(NpcClientActionsUi.IsTalkOnlySelection(new[] { 3 }));
        Assert.False(NpcClientActionsUi.ShowCommerceFields(new[] { 3 }));
    }

    [Fact]
    public void Talk_plus_other_shows_commerce_fields()
    {
        Assert.False(NpcClientActionsUi.IsTalkOnlySelection(new[] { 3, 1 }));
        Assert.True(NpcClientActionsUi.ShowCommerceFields(new[] { 3, 1 }));
    }

    [Fact]
    public void Empty_selection_shows_commerce_fields_conservatively()
    {
        Assert.False(NpcClientActionsUi.IsTalkOnlySelection(Array.Empty<int>()));
        Assert.True(NpcClientActionsUi.ShowCommerceFields(Array.Empty<int>()));
    }

    [Theory]
    [InlineData(new[] { 3 }, "Hablar")]
    [InlineData(new[] { 3, 1 }, "Comprar/Vender + Hablar")]
    [InlineData(new[] { 1, 3 }, "Comprar/Vender + Hablar")]
    [InlineData(new[] { 1, 3, 6 }, "3 acciones seleccionadas")]
    public void Compact_summary_uses_human_labels(int[] ids, string expected)
    {
        Assert.Equal(expected, NpcClientActionsUi.FormatCompactSummary(ids));
    }

    [Fact]
    public void Normalize_preserves_sorted_order_for_serialization()
    {
        var normalized = NpcEsClientActions.Normalize(new[] { 8, 1, 3, 1 });
        Assert.Equal(new[] { 1, 3, 8 }, normalized);
        Assert.Equal("[1,3,8]", NpcEsClientActions.FormatArrayLiteral(normalized));
    }

    [Fact]
    public void Hiding_commerce_does_not_modify_draft_values()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Ventas = "101,202";
        draft.ObjetoCompra = 55;
        draft.NpcEsActionIds = new List<int> { NpcEsClientActions.Talk };

        Assert.False(NpcClientActionsUi.ShowCommerceFields(draft.NpcEsActionIds));
        Assert.Equal("101,202", draft.Ventas);
        Assert.Equal(55, draft.ObjetoCompra);
    }

    [Fact]
    public void Restoring_visible_commerce_preserves_values()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Ventas = "101,202";
        draft.ObjetoCompra = 55;

        draft.NpcEsActionIds = new List<int> { NpcEsClientActions.Talk };
        Assert.False(NpcClientActionsUi.ShowCommerceFields(draft.NpcEsActionIds));

        draft.NpcEsActionIds = new List<int> { NpcEsClientActions.Talk, NpcEsClientActions.BuySell };
        Assert.True(NpcClientActionsUi.ShowCommerceFields(draft.NpcEsActionIds));
        Assert.Equal("101,202", draft.Ventas);
        Assert.Equal(55, draft.ObjetoCompra);
    }

    [Fact]
    public void Dialog_forces_talk_in_resolver_without_other_actions()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Pitor";
        npc.SimpleDialogTextLocal = "Hola aventurero";
        npc.DialogMode = NpcDialogMode.Simple;

        var expected = NpcEsActionResolver.ResolveExpected(ws, npc);
        Assert.Contains(NpcEsClientActions.Talk, expected);
        Assert.True(NpcClientActionsUi.IsTalkOnlySelection(expected));
    }

    [Fact]
    public void Dialog_plus_buy_keeps_commerce_visible()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Mercader";
        npc.SimpleDialogTextLocal = "¿Compras algo?";
        npc.DialogMode = NpcDialogMode.Simple;
        npc.NpcEsActionIds = new List<int> { NpcEsClientActions.BuySell };

        var expected = NpcEsActionResolver.ResolveExpected(ws, npc);
        Assert.Equal(new[] { 1, 3 }, expected);
        Assert.True(NpcClientActionsUi.ShowCommerceFields(expected));
    }

    [Fact]
    public void Workspace_roundtrip_preserves_actions_and_commerce()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Ventas = "10,20";
        npc.ObjetoCompra = 7;
        npc.NpcEsActionIds = new List<int> { 1, 3 };

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        var d = Assert.Single(loaded.Npcs.Drafts);
        Assert.Equal("10,20", d.Ventas);
        Assert.Equal(7, d.ObjetoCompra);
        Assert.Equal(new[] { 1, 3 }, NpcEsClientActions.Normalize(d.NpcEsActionIds));
    }
}
