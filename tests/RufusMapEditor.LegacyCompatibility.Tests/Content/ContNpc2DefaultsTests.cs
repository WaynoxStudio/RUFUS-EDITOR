using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class ContNpc2DefaultsTests
{
    [Fact]
    public void New_npc_defaults_gfx_id_to_71_and_remains_editable()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        Assert.Equal(71, npc.GfxId);
        Assert.Equal(NpcsModeloDraft.DefaultGfxId, npc.GfxId);

        npc.GfxId = 999;
        Assert.Equal(999, npc.GfxId);
    }

    [Fact]
    public void Location_publish_nombre_follows_current_npc_nombre_for_all_locations()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.Pregunta = 1075;
        npc.Nombre = "Viejo";
        var a = ws.Npcs.AddLocation(npc);
        a.MapId = 1;
        a.CellId = 10;
        a.Name = "stale-should-be-ignored";
        var b = ws.Npcs.AddLocation(npc);
        b.MapId = 2;
        b.CellId = 20;

        npc.Nombre = "Pitor Reo";

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 0,
            NpcRespuestas = 0,
            Misiones = 0,
            MisionEtapas = 0,
            MisionObjetivos = 0,
        });

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(2, plan.Locations.Count);
        Assert.All(plan.Locations, loc => Assert.Equal("Pitor Reo", loc.Nombre));
        Assert.DoesNotContain(plan.Locations, loc => loc.Nombre == "stale-should-be-ignored");
        Assert.DoesNotContain(plan.Locations, loc => loc.Nombre == "Viejo");
    }

    [Fact]
    public void Add_location_does_not_store_independent_nombre_copy()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        npc.Nombre = "Pitor Reo";
        var loc = batch.AddLocation(npc);
        Assert.Equal("", loc.Name);
    }
}
