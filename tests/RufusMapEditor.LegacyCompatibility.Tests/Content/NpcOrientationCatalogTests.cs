using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class NpcOrientationCatalogTests
{
    [Theory]
    [InlineData(1, "Abajo-derecha")]
    [InlineData(2, "Abajo")]
    [InlineData(3, "Abajo-izquierda")]
    [InlineData(4, "Izquierda")]
    [InlineData(5, "Arriba-izquierda")]
    [InlineData(6, "Arriba")]
    [InlineData(7, "Arriba-derecha")]
    [InlineData(8, "Derecha")]
    [InlineData(0, "Sin definir")]
    public void Friendly_names_match_posiciones_reference(int orientation, string expected)
    {
        Assert.Equal(expected, NpcOrientationCatalog.GetFriendlyName(orientation));
    }

    [Fact]
    public void Visual_range_is_1_to_8_and_zero_remains_valid_unset()
    {
        Assert.False(NpcOrientationCatalog.IsVisualDirection(0));
        Assert.True(NpcOrientationCatalog.IsVisualDirection(1));
        Assert.True(NpcOrientationCatalog.IsVisualDirection(8));
        Assert.False(NpcOrientationCatalog.IsVisualDirection(9));
        Assert.Contains("sin definir", NpcOrientationCatalog.FormatSelectedLabel(0), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("6", NpcOrientationCatalog.FormatSelectedLabel(6));
        Assert.Contains("Arriba", NpcOrientationCatalog.FormatSelectedLabel(6));
    }

    [Fact]
    public void Duplicate_npc_preserves_location_orientation()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var src = batch.CreateNew();
        var loc = batch.AddLocation(src);
        loc.Orientation = 6;
        loc.MapId = 1048;
        loc.CellId = 227;

        var copy = batch.Duplicate(src);
        Assert.Single(copy.Locations);
        Assert.Equal(6, copy.Locations[0].Orientation);
        Assert.Equal(1048, copy.Locations[0].MapId);
        Assert.Equal(227, copy.Locations[0].CellId);
        Assert.Equal(6, src.Locations[0].Orientation);
    }

    [Fact]
    public void Workspace_roundtrip_keeps_orientation()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        var loc = ws.Npcs.AddLocation(npc);
        loc.Orientation = 1;

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Equal(1, loaded.Npcs.Drafts[0].Locations[0].Orientation);
    }
}
