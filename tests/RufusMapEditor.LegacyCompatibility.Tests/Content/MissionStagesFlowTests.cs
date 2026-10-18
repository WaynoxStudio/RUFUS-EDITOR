using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionStagesFlowTests
{
    [Fact]
    public void Add_stage_uses_consecutive_ids_from_max()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(5504);
        var m = ws.Missions.CreateMission();
        var s1 = ws.Missions.AddStage(m);
        var s2 = ws.Missions.AddStage(m);
        Assert.Equal(5505, s1.Id);
        Assert.Equal(5506, s2.Id);
    }

    [Fact]
    public void Move_stage_reorders_mission_stages()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxStageId(5500);
        var m = ws.Missions.CreateMission();
        var a = ws.Missions.AddStage(m);
        a.Nombre = "A";
        var b = ws.Missions.AddStage(m);
        b.Nombre = "B";
        Assert.True(ws.Missions.MoveStage(m, b, -1));
        Assert.Equal(new[] { "B", "A" }, m.Stages.Select(s => s.Nombre).ToArray());
        Assert.Equal($"{b.Id},{a.Id}", MissionStagesUi.BuildMissionStagesCsv(m));
    }

    [Fact]
    public void Remove_stage_updates_csv()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        var a = ws.Missions.AddStage(m);
        var b = ws.Missions.AddStage(m);
        ws.Missions.RemoveStage(m, a);
        Assert.Single(m.Stages, s => s.Id == b.Id);
        Assert.Equal(b.Id.ToString(), m.BuildEtapasCsv());
    }

    [Fact]
    public void Add_objective_uses_consecutive_ids()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxObjectiveId(100);
        var m = ws.Missions.CreateMission();
        var s = ws.Missions.AddStage(m);
        var o1 = ws.Missions.AddObjective(s, MissionObjectiveTypes.TalkToNpc);
        var o2 = ws.Missions.AddObjective(s, MissionObjectiveTypes.Manual);
        Assert.Equal(101, o1.Id);
        Assert.Equal(102, o2.Id);
    }

    [Fact]
    public void Duplicate_objective_gets_new_id()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.SetDbMaxObjectiveId(200);
        var m = ws.Missions.CreateMission();
        var s = ws.Missions.AddStage(m);
        var src = ws.Missions.AddObjective(s, MissionObjectiveTypes.TalkToNpc);
        src.Args = "[55]";
        var copy = ws.Missions.DuplicateObjective(s, src);
        Assert.Equal(202, copy.Id);
        Assert.Equal("[55]", copy.Args);
        Assert.Equal(2, s.Objectives.Count);
        Assert.NotEqual(src.Id, copy.Id);
    }

    [Fact]
    public void Remove_objective_updates_objetivos_field()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        var s = ws.Missions.AddStage(m);
        var o = ws.Missions.AddObjective(s);
        Assert.Equal(o.Id.ToString(), s.BuildObjetivosField());
        ws.Missions.RemoveObjective(s, o);
        Assert.Equal("", s.BuildObjetivosField());
    }

    [Fact]
    public void Multiple_objectives_serialize_with_pipe()
    {
        var ws = new ContentDraftWorkspace();
        var s = ws.Missions.AddStage(ws.Missions.CreateMission());
        ws.Missions.AddObjective(s);
        ws.Missions.AddObjective(s);
        var ids = s.Objectives.Select(o => o.Id).ToArray();
        Assert.Equal($"{ids[0]}|{ids[1]}", s.BuildObjetivosField());
    }

    [Fact]
    public void Rewards_independent_per_stage()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        var a = ws.Missions.AddStage(m);
        var b = ws.Missions.AddStage(m);
        a.Rewards.Exp = 100;
        a.Rewards.Kamas = 50;
        b.Rewards.Exp = 200;
        Assert.Equal("100|50|null|null|null|null|null", a.Rewards.ToRaw());
        Assert.Equal("200|0|null|null|null|null|null", b.Rewards.ToRaw());
    }

    [Fact]
    public void Reward_objects_serialize_item_qty()
    {
        var r = new MissionRewardsDraft();
        r.Objetos.Add(new MissionRewardItem { ItemId = 1, Cantidad = 2 });
        r.Objetos.Add(new MissionRewardItem { ItemId = 3, Cantidad = 4 });
        Assert.Equal("0|0|1,2;3,4|null|null|null|null", r.ToRaw());
    }

    [Fact]
    public void Publish_plan_maps_stage_and_objective_ids()
    {
        var ws = BuildPublishWorkspace();
        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 100,
            NpcPreguntas = 200,
            NpcRespuestas = 300,
            Misiones = 400,
            MisionEtapas = 500,
            MisionObjetivos = 600,
        });
        Assert.Equal("501", plan.Missions.Single().Etapas);
        Assert.Equal("601", plan.Stages.Single().Objetivos);
        Assert.Equal(501, plan.Stages.Single().Id);
        Assert.Equal(601, plan.Objectives.Single().Id);
    }

    [Fact]
    public void Is_objective_configured_detects_empty_vs_filled()
    {
        var empty = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.Manual };
        var filled = new MissionObjectiveDraft { Tipo = MissionObjectiveTypes.TalkToNpc, Args = "[1]" };
        Assert.False(MissionStagesUi.IsObjectiveConfigured(empty));
        Assert.True(MissionStagesUi.IsObjectiveConfigured(filled));
    }

    private static ContentDraftWorkspace BuildPublishWorkspace()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(100);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Npcs.AddLocation(npc).MapId = 1;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = npc.Id;
        mission.Nombre = "Q";
        var stage = ws.Missions.AddStage(mission);
        ws.Missions.AddObjective(stage, MissionObjectiveTypes.TalkToNpc).Args = "[1]";
        ws.Dialogs.LinkStartMission(r.Actions[0], mission.DraftId);
        return ws;
    }
}
