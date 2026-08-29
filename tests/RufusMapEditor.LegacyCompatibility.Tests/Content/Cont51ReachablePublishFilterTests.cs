using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class Cont51ReachablePublishFilterTests
{
    private static ContentPublishMaxSnapshot Maxes() => new()
    {
        NpcsModelo = 20061,
        NpcPreguntas = 20023,
        NpcRespuestas = 90001,
        Misiones = 100003,
        MisionEtapas = 5500,
        MisionObjetivos = 4214,
    };

    [Fact]
    public void Npc_without_linked_mission_excludes_orphan_missions_stages_objectives()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        npc.Nombre = "Solo";

        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        // Orphans: no StartNpcId / not linked to this NPC (toggle off leftovers).
        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var orphanA = ws.Missions.CreateMission();
        orphanA.Nombre = "Huérfana A";
        orphanA.StartNpcId = null;
        var stageA = ws.Missions.AddStage(orphanA);
        ws.Missions.AddObjective(stageA, tipo: 0);

        var orphanB = ws.Missions.CreateMission();
        orphanB.Nombre = "Huérfana B";
        orphanB.StartNpcId = 999999; // NPC inexistente

        Assert.Equal(2, ws.Missions.Missions.Count);

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Single(plan.Npcs);
        Assert.Empty(plan.Missions);
        Assert.Empty(plan.Stages);
        Assert.Empty(plan.Objectives);
        Assert.Empty(plan.ReservedQuestIds);
        Assert.Empty(plan.ReservedStageIds);
        Assert.Empty(plan.ReservedObjectiveIds);
    }

    [Fact]
    public void Two_npcs_publishes_only_mission_of_npc_with_link()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var a = ws.Npcs.CreateNew();
        a.DialogMode = NpcDialogMode.Interactive;
        a.Nombre = "Con misión";
        var b = ws.Npcs.CreateNew();
        b.DialogMode = NpcDialogMode.Interactive;
        b.Nombre = "Sin misión";

        ws.Dialogs.SetDbMaxQuestionId(20023);
        var qa = ws.Dialogs.CreateQuestion(a.Id);
        ws.Dialogs.SetInitialQuestion(a, qa.Id);
        var qb = ws.Dialogs.CreateQuestion(b.Id);
        ws.Dialogs.SetInitialQuestion(b, qb.Id);

        ws.Missions.SetDbMaxStageId(5500);
        ws.Missions.SetDbMaxObjectiveId(4214);
        var missionA = ws.Missions.CreateMission();
        missionA.Nombre = "Quest A";
        missionA.StartNpcId = a.Id;
        missionA.PregDarPreguntaId = qa.Id;
        var stage = ws.Missions.AddStage(missionA);
        ws.Missions.AddObjective(stage, tipo: 1).Args = "[1]";

        var orphan = ws.Missions.CreateMission();
        orphan.Nombre = "Basura";
        orphan.StartNpcId = null;
        var orphanStage = ws.Missions.AddStage(orphan);
        ws.Missions.AddObjective(orphanStage, tipo: 0);

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(2, plan.Npcs.Count);
        Assert.Single(plan.Missions);
        Assert.Equal("Quest A", plan.Missions[0].Nombre);
        Assert.Single(plan.Stages);
        Assert.Single(plan.Objectives);
    }

    [Fact]
    public void Orphan_questions_of_deleted_npc_are_excluded()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var keep = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, keep.Id);

        // Question owned by NPC id that is not in the batch.
        var orphanQ = ws.Dialogs.CreateQuestion(88888);
        orphanQ.TextLocal = "huérfana";

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes());
        Assert.Single(plan.Questions);
        Assert.Equal(keep.Id, plan.Questions[0].ProvisionalId);
    }

    [Fact]
    public void RemoveOrphanMissions_keeps_missions_of_other_npcs_in_lote()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var a = ws.Npcs.CreateNew();
        var b = ws.Npcs.CreateNew();

        var mA = ws.Missions.CreateMission();
        mA.StartNpcId = a.Id;
        var mB = ws.Missions.CreateMission();
        mB.StartNpcId = b.Id;
        var orphan = ws.Missions.CreateMission();
        orphan.StartNpcId = null;

        var removed = ws.Missions.RemoveOrphanMissions(ws.Npcs.Drafts.Select(n => n.Id).ToList());
        Assert.Equal(1, removed);
        Assert.Equal(2, ws.Missions.Missions.Count);
        Assert.Contains(ws.Missions.Missions, m => m.StartNpcId == a.Id);
        Assert.Contains(ws.Missions.Missions, m => m.StartNpcId == b.Id);
    }
}
