using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionRemoveUnconfirmedManualType0Tests
{
    [Fact]
    public void Tipo0_no_aparece_en_selector()
    {
        Assert.DoesNotContain(MissionObjectiveTypes.Manual, MissionObjectiveTypes.UiNormalTypes);
        Assert.False(MissionObjectiveTypes.IsUiNormal(MissionObjectiveTypes.Manual));
    }

    [Fact]
    public void Resto_de_tipos_sigue_disponible()
    {
        Assert.Contains(MissionObjectiveTypes.TalkToNpc, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.ShowItemToNpc, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.DeliverItemsToNpc, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.DiscoverMap, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.DiscoverArea, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.DefeatMobs, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.UseItem, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.ReturnToNpc, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.ReachLevel, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.HaveSpells, MissionObjectiveTypes.UiNormalTypes);
        Assert.Contains(MissionObjectiveTypes.JobLevel, MissionObjectiveTypes.UiNormalTypes);
    }

    [Fact]
    public void No_se_pueden_crear_nuevos_tipo0_via_TryApply()
    {
        var d = new MissionObjectiveDraft { Id = 1, Tipo = MissionObjectiveTypes.TalkToNpc };
        var err = MissionObjectiveUiSync.TryApply(d, new MissionObjectiveUiFields
        {
            Tipo = MissionObjectiveTypes.Manual,
            ManualText = "texto",
        });
        Assert.Equal(MissionObjectiveUiSync.UnsupportedManualMessage, err);
        Assert.Equal(MissionObjectiveTypes.TalkToNpc, d.Tipo);
    }

    [Fact]
    public void Draft_antiguo_tipo0_carga_sin_romper()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        var stage = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        o.Detalle = "legado";
        o.Args = "";

        Assert.Equal(MissionObjectiveTypes.Manual, o.Tipo);
        Assert.Equal("legado", o.Detalle);
        Assert.Equal(MissionObjectiveUiSync.UnsupportedManualUiLabel,
            MissionObjectiveUiSync.UiTypeLabel(o.Tipo));
    }

    [Fact]
    public void Tipo0_antiguo_puede_eliminarse()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(1);
        ws.Missions.SetDbMaxObjectiveId(1);
        var m = ws.Missions.CreateMission();
        var stage = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        Assert.True(ws.Missions.RemoveObjective(m, stage, o));
        Assert.Empty(stage.Objectives);
    }

    [Fact]
    public void Tipo0_antiguo_bloquea_publicacion()
    {
        var ws = BuildPublishableMissionWithManual();
        var plan = ContentPublishPlanBuilder.Build(
            ws,
            new ContentPublishMaxSnapshot
            {
                NpcsModelo = ws.Npcs.DbMaxId,
                NpcPreguntas = ws.Dialogs.DbMaxQuestionId,
                NpcRespuestas = Math.Max(1, ws.Dialogs.DbMaxResponseId),
                Misiones = ws.Missions.DbMaxQuestId,
                MisionEtapas = ws.Missions.DbMaxStageId,
                MisionObjetivos = ws.Missions.DbMaxObjectiveId,
            },
            itemIdExistsInObjetosModelo: _ => true);
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(MissionObjectiveUiSync.UnsupportedManualMessage, StringComparison.Ordinal));
    }

    private static ContentDraftWorkspace BuildPublishableMissionWithManual()
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
        var o = ws.Missions.AddObjective(stage, MissionObjectiveTypes.Manual);
        o.Detalle = "viejo";
        ws.Dialogs.LinkStartMission(a, m.DraftId);
        return ws;
    }
}
