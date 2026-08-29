using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionDraftBatchTests
{
    [Fact]
    public void Missions_use_DraftId_not_blind_max_quest_id()
    {
        var batch = new MissionDraftBatch();
        var a = batch.CreateMission();
        var b = batch.CreateMission();
        Assert.NotEqual(Guid.Empty, a.DraftId);
        Assert.NotEqual(a.DraftId, b.DraftId);
        Assert.Null(typeof(MissionDraft).GetProperty("Id"));
        Assert.False(batch.HasDuplicateMissionDraftIds());
    }

    [Fact]
    public void First_stage_is_5501_when_max_5500()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        var s = batch.AddStage(m);
        Assert.Equal(5501, s.Id);
        Assert.Equal(5501, batch.AllStageIds[0]);
    }

    [Fact]
    public void Stages_are_global_consecutive_across_missions()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m1 = batch.CreateMission();
        var m2 = batch.CreateMission();
        Assert.Equal(5501, batch.AddStage(m1).Id);
        Assert.Equal(5502, batch.AddStage(m1).Id);
        Assert.Equal(5503, batch.AddStage(m2).Id);
        Assert.False(batch.HasDuplicateStageIds());
    }

    [Fact]
    public void First_objective_is_4215_when_max_4214()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        var s = batch.AddStage(m);
        var o = batch.AddObjective(s);
        Assert.Equal(4215, o.Id);
    }

    [Fact]
    public void Objectives_global_consecutive()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        var s1 = batch.AddStage(m);
        var s2 = batch.AddStage(m);
        Assert.Equal(4215, batch.AddObjective(s1).Id);
        Assert.Equal(4216, batch.AddObjective(s1).Id);
        Assert.Equal(4217, batch.AddObjective(s2).Id);
        Assert.False(batch.HasDuplicateObjectiveIds());
    }

    [Fact]
    public void Accion44_links_DraftMission_without_numeric_quest_args()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20023);
        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Test";
        var q = ws.Dialogs.CreateQuestion(20062);
        var r = ws.Dialogs.AddResponse(q);
        var a = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        ws.Dialogs.LinkStartMission(a, mission.DraftId);
        Assert.Equal(DialogActionCodes.StartQuest, a.Accion);
        Assert.Equal(mission.DraftId, a.TargetMissionDraftId);
        Assert.Equal("", a.Args);
    }

    [Fact]
    public void Preg_strings_built_from_npc_and_question_refs()
    {
        var m = new MissionDraft
        {
            StartNpcId = 20062,
            PregDarPreguntaId = 20024,
            PregIncompletaPreguntaId = 20025,
            PregCompletadaPreguntaId = 20026,
        };
        Assert.Equal("20062;20024", m.BuildPregDar());
        Assert.Equal("20062;20025", m.BuildPregIncompleta());
        Assert.Equal("20062;20026", m.BuildPregCompletada());
    }

    [Fact]
    public void Reorder_stages_preserves_flow_csv_order()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        var a = batch.AddStage(m);
        var b = batch.AddStage(m);
        var c = batch.AddStage(m);
        Assert.Equal("5501,5502,5503", m.BuildEtapasCsv());
        Assert.True(batch.MoveStage(m, c, -1));
        Assert.Equal("5501,5503,5502", m.BuildEtapasCsv());
        Assert.True(batch.MoveStage(m, a, 1));
        Assert.Equal("5503,5501,5502", m.BuildEtapasCsv());
        Assert.Contains(a, m.Stages);
        Assert.Contains(b, m.Stages);
        Assert.Contains(c, m.Stages);
    }

    [Fact]
    public void Delete_mission_referenced_by_accion44_is_protected()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var mission = ws.Missions.CreateMission();
        var q = ws.Dialogs.CreateQuestion(1);
        var r = ws.Dialogs.AddResponse(q);
        var a = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        ws.Dialogs.LinkStartMission(a, mission.DraftId);

        var result = ws.Missions.TryDeleteMission(
            mission.DraftId, false, ws.Dialogs.FindResponseRefsToMission, out var blocked);
        Assert.Equal(MissionDeleteResult.HasReferences, result);
        Assert.Contains(r.DraftId, blocked!.Value.ResponseDraftIds);

        ws.Dialogs.UnlinkAllMissionReferences(mission.DraftId);
        Assert.Null(a.TargetMissionDraftId);
        result = ws.Missions.TryDeleteMission(mission.DraftId, true, null, out _);
        Assert.Equal(MissionDeleteResult.Deleted, result);
    }

    [Fact]
    public void Structured_rewards_preserve_pipe_format()
    {
        var r = new MissionRewardsDraft
        {
            Exp = 175000,
            Kamas = 20000,
        };
        r.Objetos.Add(new MissionRewardItem { ItemId = 17903, Cantidad = 1 });
        r.Objetos.Add(new MissionRewardItem { ItemId = 18373, Cantidad = 1 });
        var raw = r.ToRaw();
        Assert.Equal("175000|20000|17903,1;18373,1|null|null|null|null", raw);
        var back = MissionRewardsDraft.FromRaw(raw);
        Assert.Equal(175000, back.Exp);
        Assert.Equal(20000, back.Kamas);
        Assert.Equal(2, back.Objetos.Count);
        Assert.Equal(17903, back.Objetos[0].ItemId);
    }

    [Fact]
    public void Deliver_items_preset_matches_confirmed_structure()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        var s = batch.AddStage(m);
        var o = batch.AddDeliverItemsObjective(s, 20003, 8682, 5);
        Assert.Equal(3, o.Tipo);
        Assert.Equal("[20003,8682,5]", o.Args);
        Assert.Equal(4215, o.Id);
    }

    [Fact]
    public void Local_quest_texts_stored()
    {
        var batch = new MissionDraftBatch();
        batch.SetDbMaxStageId(5500);
        batch.SetDbMaxObjectiveId(4214);
        var m = batch.CreateMission();
        m.Nombre = "Pieles Mojadas";
        var s = batch.AddStage(m);
        s.Nombre = "Sabiduría Empapada";
        s.Descripcion = "Trae 5 pieles";
        Assert.Equal("Pieles Mojadas", m.Nombre);
        Assert.Equal("Sabiduría Empapada", s.Nombre);
        Assert.Equal("Trae 5 pieles", s.Descripcion);
    }

    [Fact]
    public void Workspace_roundtrip_includes_missions()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Quest";
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = q.Id;
        var stage = ws.Missions.AddStage(mission);
        stage.Nombre = "E1";
        ws.Missions.AddDeliverItemsObjective(stage, npc.Id, 1, 2);

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Single(loaded.Missions.Missions);
        Assert.Equal("Quest", loaded.Missions.Missions[0].Nombre);
        Assert.Equal(npc.Id, loaded.Missions.Missions[0].StartNpcId);
        Assert.Equal(q.Id, loaded.Missions.Missions[0].PregDarPreguntaId);
        Assert.Equal("20062;20024", loaded.Missions.Missions[0].BuildPregDar());
        Assert.Equal(5501, loaded.Missions.Missions[0].Stages[0].Id);
        Assert.Equal(4215, loaded.Missions.Missions[0].Stages[0].Objectives[0].Id);
    }
}
