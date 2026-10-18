using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class StartQuestLinkUiTests
{
    [Fact]
    public void Resolve_mission_needs_create_when_none_for_npc()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.NeedsCreate, result.Outcome);
        Assert.Null(result.Mission);
    }

    [Fact]
    public void Resolve_mission_links_single_candidate()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = npc.Id;
        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.LinkedExisting, result.Outcome);
        Assert.Same(m, result.Mission);
    }

    [Fact]
    public void Resolve_mission_ambiguous_when_multiple()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var a = ws.Missions.CreateMission();
        a.StartNpcId = npc.Id;
        var b = ws.Missions.CreateMission();
        b.StartNpcId = npc.Id;
        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.Ambiguous, result.Outcome);
    }

    [Fact]
    public void Has_valid_link_when_draft_id_exists()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = m.DraftId,
        };
        Assert.True(StartQuestLinkUi.HasValidLink(action, ws.Missions));
    }

    [Fact]
    public void Has_valid_link_false_when_unlinked()
    {
        var ws = new ContentDraftWorkspace();
        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest };
        Assert.False(StartQuestLinkUi.HasValidLink(action, ws.Missions));
    }

    [Fact]
    public void Resolve_linked_mission_returns_draft_when_valid()
    {
        var ws = new ContentDraftWorkspace();
        var m = ws.Missions.CreateMission();
        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = m.DraftId,
        };
        Assert.Same(m, StartQuestLinkUi.ResolveLinkedMission(action, ws.Missions));
    }

    [Fact]
    public void Resolve_source_of_truth_prioritizes_target_mission_draft_id_over_start_npc_id()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var byNpc = ws.Missions.CreateMission();
        byNpc.Nombre = "Misión 20065";
        byNpc.StartNpcId = npc.Id;
        byNpc.PregDarPreguntaId = q.Id;

        var linked = ws.Missions.CreateMission();
        linked.Nombre = "Nueva misión";

        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = linked.DraftId,
        };

        var resolved = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id);
        Assert.Same(linked, resolved);
        Assert.NotSame(byNpc, resolved);
    }

    [Fact]
    public void Resolve_source_of_truth_falls_back_to_unambiguous_start_npc_when_unlinked()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = npc.Id;

        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest };
        Assert.Same(m, StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));
    }

    [Fact]
    public void Resolve_source_of_truth_returns_null_when_multiple_start_npc_and_no_link()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var a = ws.Missions.CreateMission();
        a.StartNpcId = npc.Id;
        var b = ws.Missions.CreateMission();
        b.StartNpcId = npc.Id;

        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest };
        Assert.Null(StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));
    }

    [Fact]
    public void Resolve_source_of_truth_uses_new_link_after_selector_change()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var first = ws.Missions.CreateMission();
        first.Nombre = "Misión A";
        var second = ws.Missions.CreateMission();
        second.Nombre = "Misión B";

        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = first.DraftId,
        };
        Assert.Same(first, StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));

        action.TargetMissionDraftId = second.DraftId;
        Assert.Same(second, StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id));
    }

    [Fact]
    public void Resolve_unambiguous_mission_ignores_drafts_without_start_npc()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var empty = ws.Missions.CreateMission();
        empty.Nombre = "Nueva misión";

        var seeded = ws.Missions.CreateMission();
        seeded.StartNpcId = npc.Id;

        Assert.Same(seeded, StartQuestLinkUi.ResolveUnambiguousMissionForNpc(npc.Id, ws.Missions.Missions));
    }

    [Fact]
    public void Resolve_mission_for_npc_does_not_count_empty_nueva_mision_as_duplicate()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        ws.Missions.CreateMission().Nombre = MissionDialogRolesUi.DefaultMissionName;
        var seeded = ws.Missions.CreateMission();
        seeded.StartNpcId = npc.Id;

        var result = StartQuestLinkUi.ResolveMissionForNpc(npc, ws.Missions.Missions);
        Assert.Equal(StartQuestLinkUi.ResolveOutcome.LinkedExisting, result.Outcome);
        Assert.Same(seeded, result.Mission);
    }

    [Fact]
    public void Build_mission_choices_for_npc_filters_workspace()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        ws.Missions.CreateMission().Nombre = "legacy";
        var owned = ws.Missions.CreateMission();
        owned.StartNpcId = npc.Id;

        var choices = StartQuestLinkUi.BuildMissionChoicesForNpc(npc.Id, null, ws.Missions.Missions);
        Assert.Single(choices);
        Assert.Same(owned, choices[0]);
    }
}
