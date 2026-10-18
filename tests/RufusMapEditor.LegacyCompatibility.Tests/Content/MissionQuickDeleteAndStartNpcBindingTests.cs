using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionQuickDeleteAndStartNpcBindingTests
{
    [Fact]
    public void RemoveObjective_draft_deletes_immediately_and_updates_list()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        m.Nombre = "M";
        var stage = ws.Missions.AddStage(m);
        var a = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        var b = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        Assert.Equal(2, stage.Objectives.Count);

        Assert.True(ws.Missions.RemoveObjective(m, stage, a));
        Assert.Single(stage.Objectives);
        Assert.Equal(b.Id, stage.Objectives[0].Id);
        Assert.False(m.PublishedBd);
    }

    [Fact]
    public void RemoveObjective_published_objective_elimina_draft_y_encola_delete()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(10);
        var m = ws.Missions.CreateMission();
        var stage = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        m.PublishedBd = true;
        m.PublishedObjectiveIds.Add(o.Id);

        Assert.True(ws.Missions.RemoveObjective(m, stage, o));
        Assert.Empty(stage.Objectives);
        Assert.Contains(o.Id, m.PendingDeletedObjectiveIds);
        Assert.True(m.HasPendingBdChanges);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    public void Tipo_usa_StartNpc_automaticamente(int tipo)
    {
        var d = new MissionObjectiveDraft { Tipo = tipo };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = tipo,
            NpcId = "20066",
            ItemId = "13484",
            Qty = "2",
        });
        Assert.Null(err);
        Assert.Equal("[20066,13484,2]", d.Args);
        Assert.Equal(
            MissionObjectiveArgsCodec.BuildShowOrDeliver(20066, 13484, 2, null, null),
            d.Args);
    }

    [Fact]
    public void ClearNpcReferences_quita_auto_return_y_fuerza_quests_es()
    {
        var batch = new MissionDraftBatch();
        var m = batch.CreateMission();
        m.Nombre = "Guerreros";
        m.PublishedBd = true;
        m.PublishedQuestId = 100005;
        m.QuestsEsPublished = true;
        m.StartNpcId = 20067;
        m.PublishedObjectiveIds.Add(4663);
        var stage = batch.AddStage(m);
        stage.Id = 5502;
        var o = batch.AddObjective(stage, MissionObjectiveTypes.ReturnToNpc);
        o.Id = 4663;
        o.Args = "[20067], x: 8, y: 2";
        o.IsAutoReturnToStartNpc = true;

        batch.ClearNpcReferences(20067);

        Assert.Null(m.StartNpcId);
        Assert.Empty(stage.Objectives);
        Assert.Contains(4663, m.PendingDeletedObjectiveIds);
        Assert.True(m.HasPendingBdChanges);
        Assert.False(m.QuestsEsPublished);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    public void Cambio_StartNpc_resincroniza_args(int tipo)
    {
        var m = new MissionDraft { Nombre = "M", StartNpcId = 10 };
        var stage = new MissionStageDraft { Id = 1, Nombre = "E1" };
        m.Stages.Add(stage);
        var o = new MissionObjectiveDraft
        {
            Id = 1,
            Tipo = tipo,
            Args = MissionObjectiveArgsCodec.BuildShowOrDeliver(10, 8682, 5, null, null),
        };
        stage.Objectives.Add(o);

        m.StartNpcId = 99;
        Assert.True(MissionShowDeliverNpcBinding.SyncMission(m));
        Assert.Equal("[99,8682,5]", o.Args);
        Assert.True(MissionObjectiveNpcRemap.TryReadLeadingNpc(o.Args, out var npc));
        Assert.Equal(99, npc);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    public void No_permite_conservar_npc_diferente_tras_sync(int tipo)
    {
        var o = new MissionObjectiveDraft
        {
            Tipo = tipo,
            Args = MissionObjectiveArgsCodec.BuildShowOrDeliver(111, 5, 1, null, null),
        };
        Assert.True(MissionShowDeliverNpcBinding.SyncObjective(o, 222));
        Assert.Equal("[222,5,1]", o.Args);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    public void Remapeo_provisional_a_definitivo_en_plan(int tipo)
    {
        var ws = BuildMissionWithBoundObjective(tipo, itemId: 13484, staleNpcInArgs: 99999);
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20000,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        };
        var plan = ContentPublishPlanBuilder.Build(ws, maxes, itemIdExistsInObjetosModelo: _ => true);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        var row = Assert.Single(plan.Objectives, o => o.Tipo == tipo);
        Assert.True(MissionObjectiveNpcRemap.TryReadLeadingNpc(row.Args, out var npc));
        Assert.Equal(plan.ReservedNpcIds[0], npc);
        Assert.True(MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(row.Args, out var item));
        Assert.Equal(13484, item);
    }

    [Theory]
    [InlineData(MissionObjectiveTypes.ShowItemToNpc)]
    [InlineData(MissionObjectiveTypes.DeliverItemsToNpc)]
    public void Sin_StartNpc_bloquea_publicacion(int tipo)
    {
        var ws = BuildMissionWithBoundObjective(tipo, itemId: 10);
        var m = ws.Missions.Missions[0];
        // Published update path keeps mission in plan even if StartNpc cleared.
        m.PublishedBd = true;
        m.PublishedQuestId = 100100;
        m.HasPendingBdChanges = true;
        m.PublishedStageIds.Add(m.Stages[0].Id);
        m.PublishedObjectiveIds.Add(m.Stages[0].Objectives[0].Id);
        m.StartNpcId = null;

        var plan = ContentPublishPlanBuilder.Build(
            ws,
            DefaultMaxes(ws),
            itemIdExistsInObjetosModelo: _ => true);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(MissionShowDeliverNpcBinding.MissingStartNpcMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Sin_StartNpc_TryApply_muestra_mensaje()
    {
        var d = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.ShowItemToNpc };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.ShowItemToNpc,
            NpcId = "",
            ItemId = "1",
            Qty = "1",
        });
        Assert.Equal(MissionShowDeliverNpcBinding.MissingStartNpcMessage, err);
    }

    [Fact]
    public void Draft_existente_compatible_item_qty_conservados()
    {
        var args = MissionObjectiveArgsCodec.BuildShowOrDeliver(7, 30500, 3, 1, -2);
        Assert.True(MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(args, out var item));
        Assert.Equal(30500, item);
        var rewritten = MissionShowDeliverNpcBinding.RewriteArgsNpc(args, 20066);
        Assert.Equal("[20066,30500,3], x: 1, y: -2", rewritten);
    }

    [Fact]
    public void Serializacion_tipo2_sin_regresion()
    {
        Assert.Equal(
            "[1,2,3]",
            MissionObjectiveArgsCodec.BuildShowOrDeliver(1, 2, 3, null, null));
    }

    [Fact]
    public void Serializacion_tipo3_sin_regresion()
    {
        Assert.Equal(
            "[20003,8682,5]",
            MissionObjectiveArgsCodec.BuildShowOrDeliver(20003, 8682, 5, null, null));
    }

    [Fact]
    public void ApplyPublishedState_fuerza_StartNpc_definitivo()
    {
        var ws = BuildMissionWithBoundObjective(MissionObjectiveTypes.DeliverItemsToNpc, 8682);
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20000,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        };
        var plan = ContentPublishPlanBuilder.Build(ws, maxes, itemIdExistsInObjetosModelo: _ => true);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));

        var m = ws.Missions.Missions[0];
        Assert.True(plan.NpcIdMap.TryGetValue(m.StartNpcId!.Value, out var finalNpc));
        m.StartNpcId = finalNpc;
        foreach (var o in m.Stages.SelectMany(s => s.Objectives))
        {
            if (MissionShowDeliverNpcBinding.IsBoundTipo(o.Tipo))
                MissionShowDeliverNpcBinding.SyncObjective(o, finalNpc);
        }

        var obj = m.Stages[0].Objectives[0];
        Assert.True(MissionObjectiveNpcRemap.TryReadLeadingNpc(obj.Args, out var npc));
        Assert.Equal(finalNpc, npc);
    }

    private static ContentDraftWorkspace BuildMissionWithBoundObjective(
        int tipo,
        int itemId,
        int? staleNpcInArgs = null)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20000);
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
        var npcInArgs = staleNpcInArgs ?? npc.Id;
        o.Args = MissionObjectiveArgsCodec.BuildShowOrDeliver(npcInArgs, itemId, 1, null, null);
        ws.Dialogs.LinkStartMission(a, m.DraftId);
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
