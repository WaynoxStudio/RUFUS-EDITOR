using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionObjectiveItemIdDirectInputTests
{
    public MissionObjectiveItemIdDirectInputTests()
    {
        ObjetosModeloNameCache.Shared = null;
    }

    [Fact]
    public void Tipo2_acepta_item_id_manual_y_serializacion_identica()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.ShowItemToNpc };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ShowItemToNpc,
            NpcId = "20003",
            ItemId = "13484",
            Qty = "3",
        });
        Assert.Null(err);
        Assert.Equal("[20003,13484,3]", d.Args);
        Assert.Equal(
            MissionObjectiveArgsCodec.BuildShowOrDeliver(20003, 13484, 3, null, null),
            d.Args);
    }

    [Fact]
    public void Tipo3_acepta_item_id_manual_y_serializacion_identica()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.DeliverItemsToNpc };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "7",
            ItemId = "30500",
            Qty = "1",
        });
        Assert.Null(err);
        Assert.Equal("[7,30500,1]", d.Args);
        Assert.Equal(
            MissionObjectiveArgsCodec.BuildShowOrDeliver(7, 30500, 1, null, null),
            d.Args);
    }

    [Fact]
    public void Cantidad_se_conserva()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.DeliverItemsToNpc };
        Assert.Null(MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            NpcId = "1",
            ItemId = "10",
            Qty = "42",
        }));
        Assert.Equal("[1,10,42]", d.Args);
        Assert.True(MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(d.Args, out var item));
        Assert.Equal(10, item);
    }

    [Fact]
    public void Cache_item_existente_devuelve_nombre_real()
    {
        var cache = new ObjetosModeloNameCache(id =>
            Task.FromResult(id == 13484 ? "Pergamino de hechizo" : null));
        Assert.Equal("Pergamino de hechizo", cache.ResolveSync(13484));
        Assert.True(cache.Exists(13484));
    }

    [Fact]
    public void Cache_item_inexistente_objeto_no_encontrado()
    {
        var cache = new ObjetosModeloNameCache(_ => Task.FromResult<string?>(null));
        Assert.Null(cache.ResolveSync(999999));
        Assert.False(cache.Exists(999999));
        Assert.Equal("Objeto no encontrado.", ObjetosModeloNameCache.NotFoundMessage);
    }

    [Fact]
    public void Cambiar_id_actualiza_nombre_en_cache()
    {
        var cache = new ObjetosModeloNameCache(id => Task.FromResult(id switch
        {
            13484 => "A",
            30500 => "B",
            _ => null,
        }));
        Assert.Equal("A", cache.ResolveSync(13484));
        Assert.Equal("B", cache.ResolveSync(30500));
    }

    [Fact]
    public void Draft_existente_carga_item_id_desde_args()
    {
        var args = MissionObjectiveArgsCodec.BuildShowOrDeliver(20066, 13484, 2, 1, 1);
        Assert.True(MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(args, out var itemId));
        Assert.Equal(13484, itemId);
    }

    [Fact]
    public void No_requiere_gfx_para_aplicar_objetivo()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.ShowItemToNpc };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ShowItemToNpc,
            NpcId = "1",
            ItemId = "8682",
            Qty = "1",
        });
        Assert.Null(err);
        Assert.Equal("[1,8682,1]", d.Args);
    }

    [Fact]
    public void Publish_bloqueado_si_item_id_inexistente()
    {
        var ws = BuildMissionWithItemObjective(MissionObjectiveTypes.DeliverItemsToNpc, 999001);
        var maxes = DefaultMaxes(ws);
        var plan = ContentPublishPlanBuilder.Build(
            ws,
            maxes,
            itemIdExistsInObjetosModelo: _ => false);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(ObjetosModeloNameCache.NotFoundMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Publish_bloqueado_si_item_id_vacio()
    {
        var ws = BuildMissionWithRawObjective(MissionObjectiveTypes.ShowItemToNpc, "[1]");
        var plan = ContentPublishPlanBuilder.Build(ws, DefaultMaxes(ws), itemIdExistsInObjetosModelo: _ => true);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains("Indica un Item ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Publish_ok_si_item_existe_sin_gfx()
    {
        var ws = BuildMissionWithItemObjective(MissionObjectiveTypes.ShowItemToNpc, 13484);
        var plan = ContentPublishPlanBuilder.Build(
            ws,
            DefaultMaxes(ws),
            itemIdExistsInObjetosModelo: id => id == 13484);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        var row = Assert.Single(plan.Objectives, o => o.Tipo == MissionObjectiveTypes.ShowItemToNpc);
        Assert.True(MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(row.Args, out var publishedItem));
        Assert.Equal(13484, publishedItem);
        Assert.Contains(",13484,1]", row.Args, StringComparison.Ordinal);
    }

    [Fact]
    public void Recompensas_raw_no_sufren_regresion()
    {
        var r = new MissionRewardsDraft { Exp = 200000, Kamas = 40000 };
        r.Objetos.Add(new MissionRewardItem { ItemId = 30500, Cantidad = 1 });
        Assert.Equal("200000|40000|30500,1|null|null|null|null", r.ToRaw());
    }

    [Fact]
    public void Columnas_objetos_modelo_confirmadas()
    {
        Assert.Equal("objetos_modelo", ObjetosModeloColumns.DefaultTable);
        Assert.Equal("id", ObjetosModeloColumns.Id);
        Assert.Equal("nombre", ObjetosModeloColumns.Nombre);
    }

    private static ContentDraftWorkspace BuildMissionWithItemObjective(int tipo, int itemId)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "N";
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(1);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Q";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        var a = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);

        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "M";
        m.StartNpcId = npc.Id;
        m.PregDarPreguntaId = q.Id;
        var stage = ws.Missions.AddStage(m);
        stage.Nombre = "E1";
        var o = ws.Missions.AddObjective(stage, tipo);
        o.Args = MissionObjectiveArgsCodec.BuildShowOrDeliver(npc.Id, itemId, 1, null, null);
        ws.Dialogs.LinkStartMission(a, m.DraftId);
        return ws;
    }

    private static ContentDraftWorkspace BuildMissionWithRawObjective(int tipo, string args)
    {
        var ws = BuildMissionWithItemObjective(tipo, 1);
        var o = ws.Missions.Missions[0].Stages[0].Objectives[0];
        o.Args = args;
        return ws;
    }

    private static ContentPublishMaxSnapshot DefaultMaxes(ContentDraftWorkspace ws) => new()
    {
        NpcsModelo = ws.Npcs.DbMaxId,
        NpcPreguntas = ws.Dialogs.DbMaxQuestionId,
        NpcRespuestas = Math.Max(1, ws.Dialogs.DbMaxResponseId),
        Misiones = ws.Missions.DbMaxQuestId,
        MisionEtapas = ws.Missions.DbMaxStageId,
        MisionObjetivos = ws.Missions.DbMaxObjectiveId,
    };
}
