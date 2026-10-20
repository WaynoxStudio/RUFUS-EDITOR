using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class ContentPublishNpcSelectionTests
{
    [Fact]
    public void Plan_BD_solo_incluye_NPCs_seleccionados()
    {
        var ws = new ContentDraftWorkspace();
        var a = ws.Npcs.CreateNew();
        a.Id = 20067;
        a.Nombre = "Silvio";
        a.DialogMode = NpcDialogMode.Simple;
        a.SimpleDialogTextLocal = "A";
        a.PublishedBd = false;
        ws.Npcs.AddLocation(a).MapId = 1;

        var b = ws.Npcs.CreateNew();
        b.Id = 20068;
        b.Nombre = "Auro";
        b.DialogMode = NpcDialogMode.Simple;
        b.SimpleDialogTextLocal = "B";
        b.PublishedBd = false;
        ws.Npcs.AddLocation(b).MapId = 2;

        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20066,
            NpcPreguntas = 20030,
            NpcRespuestas = 100,
            Misiones = 100,
            MisionEtapas = 100,
            MisionObjetivos = 100,
        };

        var all = ContentPublishPlanBuilder.Build(ws, maxes);
        Assert.Equal(2, all.Npcs.Count);

        var one = ContentPublishPlanBuilder.Build(ws, maxes, npcIdFilter: new HashSet<int> { 20068 });
        Assert.Single(one.Npcs);
        Assert.True(one.NpcIdMap.ContainsKey(20068));
        Assert.False(one.NpcIdMap.ContainsKey(20067));
        Assert.Equal("Auro", one.Npcs[0].Nombre);
    }

    [Fact]
    public void ValidatePlan_no_bloquea_NPC_no_seleccionado_con_dialog_es_pendiente()
    {
        var ws = new ContentDraftWorkspace();
        var ready = ws.Npcs.CreateNew();
        ready.Id = 20067;
        ready.Nombre = "Listo";
        ready.DialogMode = NpcDialogMode.Simple;
        ready.SimpleDialogTextLocal = "Ok";
        ready.Pregunta = 20031;
        ready.DialogEsPublished = true;
        ready.PublishedBd = false;
        ws.Npcs.AddLocation(ready).MapId = 1;

        var pending = ws.Npcs.CreateNew();
        pending.Id = 20068;
        pending.Nombre = "Pendiente";
        pending.DialogMode = NpcDialogMode.Simple;
        pending.SimpleDialogTextLocal = "Nuevo";
        pending.Pregunta = 0;
        pending.DialogEsPublished = false;
        pending.PublishedBd = false;
        ws.Npcs.AddLocation(pending).MapId = 2;

        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20066,
            NpcPreguntas = 20030,
            NpcRespuestas = 100,
            Misiones = 100,
            MisionEtapas = 100,
            MisionObjetivos = 100,
        };

        var blocked = ContentPublishPlanBuilder.Build(ws, maxes);
        Assert.False(blocked.IsValid);
        Assert.Contains(blocked.Errors, e => e.Contains("20068", StringComparison.Ordinal));

        var filtered = ContentPublishPlanBuilder.Build(ws, maxes, npcIdFilter: new HashSet<int> { 20067 });
        Assert.True(filtered.IsValid, string.Join("; ", filtered.Errors));
        Assert.Single(filtered.Npcs);
    }

    [Fact]
    public void ListClientCandidates_marca_dialog_y_npc()
    {
        var ws = new ContentDraftWorkspace();
        var n = ws.Npcs.CreateNew();
        n.Id = 10;
        n.Nombre = "Test";
        n.DialogMode = NpcDialogMode.Simple;
        n.SimpleDialogTextLocal = "Hola";
        n.DialogEsPublished = false;
        n.NpcEsPublished = false;

        var list = ContentPublishNpcSelection.ListClientCandidates(ws);
        Assert.Single(list);
        Assert.Equal(10, list[0].Id);
        Assert.Contains("dialog_es", list[0].Reason, StringComparison.Ordinal);
        Assert.Contains("npc_es", list[0].Reason, StringComparison.Ordinal);
    }
}
