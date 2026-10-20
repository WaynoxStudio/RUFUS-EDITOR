using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionDraftsPersistencePublishedEditTests
{
    private static ContentDraftWorkspace BuildPublishedPruebaLike()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxQuestId(100004);
        ws.Missions.SetDbMaxStageId(5501);
        ws.Missions.SetDbMaxObjectiveId(4661);
        var m = ws.Missions.CreateMission();
        m.Nombre = "PRUEBA";
        m.PublishedQuestId = 100004;
        m.PublishedBd = true;
        m.StartNpcId = 20065;
        var s = new MissionStageDraft
        {
            Id = 5501,
            Nombre = "Etapa",
            Descripcion = "Desc",
            Rewards = new MissionRewardsDraft { Exp = 10, Kamas = 5 },
        };
        s.Objectives.Add(new MissionObjectiveDraft
        {
            Id = 4661,
            Tipo = 4,
            Args = "[7411]",
            Detalle = "",
        });
        m.Stages.Add(s);
        m.MarkPublishedSnapshot();
        return ws;
    }

    [Fact]
    public void Guardar_borrador_roundtrip_sobrevive_serializer()
    {
        var ws = BuildPublishedPruebaLike();
        ws.Missions.Missions[0].MarkPendingBdEdit();
        ws.Missions.Missions[0].Nombre = "PRUEBA edit";
        var json = ContentWorkspaceSerializer.Serialize(ws);
        var loaded = ContentWorkspaceSerializer.Deserialize(json);
        var m = Assert.Single(loaded.Missions.Missions);
        Assert.Equal("PRUEBA edit", m.Nombre);
        Assert.Equal(100004, m.PublishedQuestId);
        Assert.True(m.PublishedBd);
        Assert.True(m.HasPendingBdChanges);
        Assert.Equal(MissionDraft.StatusCambiosPendientes, m.DisplayStatus);
        Assert.Equal(5501, m.Stages[0].Id);
        Assert.Equal(4661, m.Stages[0].Objectives[0].Id);
        Assert.Contains(5501, m.PublishedStageIds);
        Assert.Contains(4661, m.PublishedObjectiveIds);
    }

    [Fact]
    public void Estado_borrador_publicada_cambios_pendientes()
    {
        var draft = new MissionDraft { Nombre = "X" };
        Assert.Equal(MissionDraft.StatusBorrador, draft.DisplayStatus);

        var pub = new MissionDraft { PublishedBd = true, PublishedQuestId = 1 };
        pub.MarkPublishedSnapshot();
        Assert.Equal(MissionDraft.StatusPublicada, pub.DisplayStatus);

        pub.MarkPendingBdEdit();
        Assert.Equal(MissionDraft.StatusCambiosPendientes, pub.DisplayStatus);
    }

    [Fact]
    public void Duplicar_deep_clone_nuevo_DraftId_limpia_publicados()
    {
        var ws = BuildPublishedPruebaLike();
        var src = ws.Missions.Missions[0];
        src.Stages[0].Objectives[0].IsAutoReturnToStartNpc = false;
        var map = src.Stages[0].Objectives[0];
        map.Tipo = 4;
        map.Args = "[7411]";
        src.StartNpcId = 20065;

        var copy = ws.Missions.DuplicateMission(src);
        Assert.NotEqual(src.DraftId, copy.DraftId);
        Assert.Null(copy.PublishedQuestId);
        Assert.False(copy.PublishedBd);
        Assert.False(copy.HasPendingBdChanges);
        Assert.Empty(copy.PublishedStageIds);
        Assert.Empty(copy.PublishedObjectiveIds);
        Assert.Null(copy.PregDarPreguntaId);
        Assert.Equal(MissionDraft.StatusBorrador, copy.DisplayStatus);
        Assert.Equal(src.Nombre, copy.Nombre);
        Assert.Equal(src.StartNpcId, copy.StartNpcId);
        Assert.NotEqual(src.Stages[0].Id, copy.Stages[0].Id);
        Assert.NotEqual(src.Stages[0].Objectives[0].Id, copy.Stages[0].Objectives[0].Id);

        copy.Nombre = "COPIA";
        Assert.Equal("PRUEBA", src.Nombre);
        Assert.Equal(100004, src.PublishedQuestId);
    }

    [Fact]
    public void Duplicar_no_crea_accion44_ni_toca_original()
    {
        var ws = BuildPublishedPruebaLike();
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(20065);
        var r = ws.Dialogs.AddResponse(q);
        var a = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        var src = ws.Missions.Missions[0];
        ws.Dialogs.LinkStartMission(a, src.DraftId);

        var copy = ws.Missions.DuplicateMission(src);
        Assert.Equal(src.DraftId, a.TargetMissionDraftId);
        Assert.DoesNotContain(
            ws.Dialogs.Questions.SelectMany(x => x.Responses).SelectMany(x => x.Actions),
            act => act.TargetMissionDraftId == copy.DraftId);
    }

    [Fact]
    public void Duplicar_auto_return_clonado_con_StartNpc_copia()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4660);
        var m = ws.Missions.CreateMission();
        m.Nombre = "AR";
        m.StartNpcId = 20065;
        var s = ws.Missions.AddStage(m);
        ws.Missions.AddObjective(s, tipo: 4).Args = "[7411]";
        ws.Missions.SyncAutoReturns(m);
        var ret = Assert.Single(s.Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal(9, ret.Tipo);
        Assert.Equal("[20065]", ret.Args);

        var copy = ws.Missions.DuplicateMission(m);
        copy.StartNpcId = 20099;
        ws.Missions.SyncAutoReturns(copy);
        var copyRet = Assert.Single(copy.Stages[0].Objectives, o => o.IsAutoReturnToStartNpc);
        Assert.Equal("[20099]", copyRet.Args);
        Assert.Equal("[20065]", ret.Args);
        Assert.NotSame(ret, copyRet);
    }

    [Fact]
    public void Editar_publicada_conserva_quest_e_hijos_nuevo_objetivo_max_plus_one()
    {
        var ws = BuildPublishedPruebaLike();
        var m = ws.Missions.Missions[0];
        m.MarkPendingBdEdit();
        m.Nombre = "PRUEBA rename";
        m.Stages[0].Nombre = "Etapa rename";
        m.Stages[0].Objectives[0].Detalle = "texto nuevo";
        var neu = ws.Missions.AddObjective(m.Stages[0], MissionObjectiveTypes.TalkToNpc);
        neu.Args = "[1]";

        Assert.Equal(100004, m.PublishedQuestId);
        Assert.Equal(5501, m.Stages[0].Id);
        Assert.Equal(4661, m.Stages[0].Objectives[0].Id);
        Assert.Equal(4662, neu.Id);

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            Misiones = 100004,
            MisionEtapas = 5501,
            MisionObjetivos = 4661,
        });
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Empty(plan.ReservedQuestIds);
        Assert.Empty(plan.ReservedStageIds);
        Assert.Equal(new[] { 4662 }, plan.ReservedObjectiveIds.ToArray());
        Assert.True(Assert.Single(plan.Missions).IsUpdate);
        Assert.Equal(100004, plan.Missions[0].Id);
        Assert.True(plan.Stages.Single(s => s.Id == 5501).IsUpdate);
        Assert.True(plan.Objectives.Single(o => o.Id == 4661).IsUpdate);
        Assert.False(plan.Objectives.Single(o => o.Id == 4662).IsUpdate);

        var preview = plan.FormatMissionWritePreview();
        Assert.Contains("ACTUALIZACIONES:", preview);
        Assert.Contains("Quest 100004", preview);
        Assert.Contains("Etapa 5501", preview);
        Assert.Contains("Objetivo 4661", preview);
        Assert.Contains("NUEVOS:", preview);
        Assert.Contains("Objetivo 4662", preview);
    }

    [Fact]
    public async Task Update_publicado_no_inserta_quest_misma_id()
    {
        var ws = BuildPublishedPruebaLike();
        var m = ws.Missions.Missions[0];
        m.MarkPendingBdEdit();
        m.Nombre = "edit";
        var maxes = new ContentPublishMaxSnapshot
        {
            Misiones = 100004,
            MisionEtapas = 5501,
            MisionObjetivos = 4661,
        };
        var store = new InMemoryContentPublishStore(maxes, allMyisam: false);
        store.Missions[100004] = new MisionInsertRow { Id = 100004, Nombre = "old" };
        store.Stages[5501] = new MisionEtapaInsertRow { Id = 5501, Nombre = "old" };
        store.Objectives[4661] = new MisionObjetivoInsertRow { Id = 4661, Tipo = 4 };
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "rufus-draft-tests-" + Guid.NewGuid().ToString("N")));
        var outcome = await svc.PublishAsync(ws);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal("edit", store.Missions[100004].Nombre);
        Assert.False(m.HasPendingBdChanges);
        Assert.Equal(MissionDraft.StatusPublicada, m.DisplayStatus);
    }

    [Fact]
    public void Remove_published_objective_ok_stage_sigue_bloqueada()
    {
        var ws = BuildPublishedPruebaLike();
        var m = ws.Missions.Missions[0];
        var s = m.Stages[0];
        var o = s.Objectives[0];
        Assert.True(ws.Missions.RemoveObjective(m, s, o));
        Assert.Empty(s.Objectives);
        Assert.Contains(o.Id, m.PendingDeletedObjectiveIds);
        Assert.False(ws.Missions.RemoveStage(m, s));
        Assert.Single(m.Stages);
    }

    [Fact]
    public void Plan_publicado_incluye_ObjectiveIdsToDelete()
    {
        var ws = BuildPublishedPruebaLike();
        var m = ws.Missions.Missions[0];
        m.MarkPendingBdEdit();
        var s = m.Stages[0];
        var o = s.Objectives[0];
        Assert.True(ws.Missions.RemoveObjective(m, s, o));

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            Misiones = 100004,
            MisionEtapas = 5501,
            MisionObjetivos = 4661,
        });
        Assert.Contains(o.Id, plan.ObjectiveIdsToDelete);
        Assert.DoesNotContain(plan.Objectives, row => row.Id == o.Id);
    }

    [Fact]
    public void Draft_serialize_no_toca_bd_ni_cliente()
    {
        var ws = BuildPublishedPruebaLike();
        var json = ContentWorkspaceSerializer.Serialize(ws);
        Assert.Contains("publishedQuestId", json);
        Assert.DoesNotContain("INSERT", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quests_es", json, StringComparison.OrdinalIgnoreCase);
    }
}
