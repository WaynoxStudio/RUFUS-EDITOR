using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionLifecycleTests
{
    [Fact]
    public void Create_new_mission_uses_default_name_not_npc_name()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "JAJAJA";

        var m = ws.Missions.CreateMission();
        m.Nombre = MissionDialogRolesUi.DefaultMissionName;
        m.StartNpcId = npc.Id;

        Assert.Equal(MissionDialogRolesUi.DefaultMissionName, m.Nombre);
        Assert.NotEqual(npc.Nombre, m.Nombre);
    }

    [Fact]
    public void Build_mission_choices_excludes_orphan_legacy_missions()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        ws.Missions.CreateMission().Nombre = "hola";
        ws.Missions.CreateMission().Nombre = "Buscacimas";
        var owned = ws.Missions.CreateMission();
        owned.Nombre = "Owned";
        owned.StartNpcId = npc.Id;

        var choices = StartQuestLinkUi.BuildMissionChoicesForNpc(npc.Id, null, ws.Missions.Missions);
        Assert.Single(choices);
        Assert.Same(owned, choices[0]);
    }

    [Fact]
    public void Build_mission_choices_includes_linked_mission_even_without_start_npc()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var linked = ws.Missions.CreateMission();
        linked.Nombre = "Linked orphan";

        var choices = StartQuestLinkUi.BuildMissionChoicesForNpc(npc.Id, linked.DraftId, ws.Missions.Missions);
        Assert.Single(choices);
        Assert.Same(linked, choices[0]);
    }

    [Fact]
    public void Build_mission_choices_includes_npc_missions_and_linked_deduplicated()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var a = ws.Missions.CreateMission();
        a.StartNpcId = npc.Id;
        a.Nombre = "A";
        var b = ws.Missions.CreateMission();
        b.StartNpcId = npc.Id;
        b.Nombre = "B";

        var choices = StartQuestLinkUi.BuildMissionChoicesForNpc(npc.Id, a.DraftId, ws.Missions.Missions);
        Assert.Equal(2, choices.Count);
        Assert.Contains(a, choices);
        Assert.Contains(b, choices);
    }

    [Fact]
    public void Ensure_roles_on_existing_mission_only_when_changed()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = q.Id;
        mission.PregIncompletaPreguntaId = 9001;
        mission.PregCompletadaPreguntaId = 9002;
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9001);
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9002);

        var before = ws.Dialogs.QuestionsForNpc(npc.Id).Count;
        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);
        var after = ws.Dialogs.QuestionsForNpc(npc.Id).Count;

        Assert.False(result.Changed);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Ensure_roles_creates_only_missing_secondary_roles()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = q.Id;

        var before = ws.Dialogs.QuestionsForNpc(npc.Id).Count;
        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);
        var after = ws.Dialogs.QuestionsForNpc(npc.Id).Count;

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(2, result.CreatedQuestionIds.Count);
        Assert.Equal(before + 2, after);
        Assert.NotNull(mission.PregIncompletaPreguntaId);
        Assert.NotNull(mission.PregCompletadaPreguntaId);
        Assert.Equal(q.Id, mission.PregDarPreguntaId);
    }

    [Fact]
    public void Linking_another_mission_does_not_copy_roles()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var qOffer = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, qOffer.Id);

        var missionA = ws.Missions.CreateMission();
        missionA.StartNpcId = npc.Id;
        missionA.PregDarPreguntaId = qOffer.Id;
        missionA.PregIncompletaPreguntaId = 9010;
        missionA.PregCompletadaPreguntaId = 9011;
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9010);
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9011);

        var missionB = ws.Missions.CreateMission();
        missionB.StartNpcId = npc.Id;
        missionB.PregDarPreguntaId = 9020;
        missionB.PregIncompletaPreguntaId = 9021;
        missionB.PregCompletadaPreguntaId = 9022;
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9020);
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9021);
        ws.Dialogs.CreateQuestionWithId(npc.Id, 9022);

        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest };
        ws.Dialogs.LinkStartMission(action, missionA.DraftId);
        Assert.Same(missionA, StartQuestLinkUi.ResolveLinkedMission(action, ws.Missions));

        ws.Dialogs.LinkStartMission(action, missionB.DraftId);
        Assert.Same(missionB, StartQuestLinkUi.ResolveLinkedMission(action, ws.Missions));
        Assert.Equal(9020, missionB.PregDarPreguntaId);
        Assert.Equal(9021, missionB.PregIncompletaPreguntaId);
        Assert.Equal(9022, missionB.PregCompletadaPreguntaId);
        Assert.Equal(9010, missionA.PregIncompletaPreguntaId);
    }

    [Fact]
    public void Resolve_source_of_truth_uses_target_mission_draft_id_not_start_npc_fallback_when_linked()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var byNpc = ws.Missions.CreateMission();
        byNpc.StartNpcId = npc.Id;
        var linked = ws.Missions.CreateMission();
        linked.Nombre = MissionDialogRolesUi.DefaultMissionName;

        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = linked.DraftId,
        };

        Assert.Same(linked, StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));
        Assert.NotSame(byNpc, StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));
    }

    [Fact]
    public void Action44_repair_needs_create_when_no_npc_mission()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.NeedsCreate, result.Outcome);
    }

    [Fact]
    public void Action44_repair_ambiguous_when_multiple_npc_missions()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        ws.Missions.CreateMission().StartNpcId = npc.Id;
        ws.Missions.CreateMission().StartNpcId = npc.Id;
        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.Ambiguous, result.Outcome);
    }

    [Fact]
    public void Has_valid_linked_mission_for_npc_when_action44_linked()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        var r = ws.Dialogs.AddResponse(q);
        var m = ws.Missions.CreateMission();
        m.Nombre = MissionDialogRolesUi.DefaultMissionName;
        m.StartNpcId = npc.Id;
        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest };
        r.Actions.Add(action);

        Assert.False(StartQuestLinkUi.HasValidLinkedMissionForNpc(npc.Id, ws.Dialogs, ws.Missions));

        ws.Dialogs.LinkStartMission(action, m.DraftId);
        Assert.True(StartQuestLinkUi.HasValidLinkedMissionForNpc(npc.Id, ws.Dialogs, ws.Missions));
    }

    [Fact]
    public void Has_valid_linked_mission_false_without_action44_link()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = npc.Id;

        Assert.False(StartQuestLinkUi.HasValidLinkedMissionForNpc(npc.Id, ws.Dialogs, ws.Missions));
    }

    [Fact]
    public void Start_npc_mission_without_link_does_not_imply_has_mission_state()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        ws.Missions.CreateMission().StartNpcId = npc.Id;

        Assert.False(StartQuestLinkUi.HasValidLinkedMissionForNpc(npc.Id, ws.Dialogs, ws.Missions));
    }
}
