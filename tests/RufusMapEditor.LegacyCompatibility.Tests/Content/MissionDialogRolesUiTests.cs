using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionDialogRolesUiTests
{
    [Fact]
    public void Format_question_label_shows_id_and_text()
    {
        var q = new DialogQuestionDraft { Id = 20025, TextLocal = "Hola aventurero" };
        Assert.Equal("#20025 · Hola aventurero", MissionDialogRolesUi.FormatQuestionLabel(q));
    }

    [Fact]
    public void Format_question_label_id_only_when_no_text()
    {
        var q = new DialogQuestionDraft { Id = 42, TextLocal = "   " };
        Assert.Equal("#42 · (sin texto)", MissionDialogRolesUi.FormatQuestionLabel(q));
    }

    [Fact]
    public void Resolve_default_offer_uses_npc_initial_question()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        Assert.Equal(q.Id, MissionDialogRolesUi.ResolveDefaultOfferQuestionId(npc, ws.Dialogs));
    }

    [Fact]
    public void Resolve_default_offer_uses_bd_catalog_when_no_local_draft()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        npc.Pregunta = 20024;
        var catalog = new ContentDbCatalogSnapshot(
            [],
            [new DbQuestionCatalogRow { Id = 20024 }],
            [],
            null,
            true);
        Assert.Equal(20024, MissionDialogRolesUi.ResolveDefaultOfferQuestionId(npc, ws.Dialogs, catalog));
    }

    [Fact]
    public void Seed_dialog_roles_fills_all_three_roles_when_empty()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        ws.Dialogs.SetDbMaxQuestionId(20025);
        var npc = ws.Npcs.CreateNew();
        Assert.Equal(20065, npc.Id);
        var q = ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var m = ws.Missions.CreateMission();

        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);
        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(20065, m.StartNpcId);
        Assert.Equal(20025, m.PregDarPreguntaId);
        Assert.Equal(20026, m.PregIncompletaPreguntaId);
        Assert.Equal(20027, m.PregCompletadaPreguntaId);
        Assert.Equal("20065;20025", m.BuildPregDar());
        Assert.Equal("20065;20026", m.BuildPregIncompleta());
        Assert.Equal("20065;20027", m.BuildPregCompletada());
    }

    [Fact]
    public void Ensure_roles_case_a_max_bd_20025_assigns_20026_and_20027()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        ws.Dialogs.SetDbMaxQuestionId(20025);
        var npc = ws.Npcs.CreateNew();
        ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        ws.Dialogs.SetInitialQuestion(npc, 20025);
        var m = ws.Missions.CreateMission();

        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);

        Assert.Equal(20025, m.PregDarPreguntaId);
        Assert.Equal(20026, m.PregIncompletaPreguntaId);
        Assert.Equal(20027, m.PregCompletadaPreguntaId);
    }

    [Fact]
    public void Ensure_roles_case_b_skips_local_draft_20026()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20025);
        var npc = ws.Npcs.CreateNew();
        ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        ws.Dialogs.SetInitialQuestion(npc, 20025);
        ws.Dialogs.CreateQuestionWithId(npc.Id, 20026);
        var m = ws.Missions.CreateMission();

        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);

        Assert.Equal(20027, m.PregIncompletaPreguntaId);
        Assert.Equal(20028, m.PregCompletadaPreguntaId);
    }

    [Fact]
    public void Ensure_roles_case_c_is_idempotent()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20025);
        var npc = ws.Npcs.CreateNew();
        ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        ws.Dialogs.SetInitialQuestion(npc, 20025);
        var m = ws.Missions.CreateMission();

        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);
        var countAfterFirst = ws.Dialogs.Questions.Count;
        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);

        Assert.False(result.Changed);
        Assert.Equal(countAfterFirst, ws.Dialogs.Questions.Count);
        Assert.Equal(20026, m.PregIncompletaPreguntaId);
        Assert.Equal(20027, m.PregCompletadaPreguntaId);
    }

    [Fact]
    public void Ensure_roles_case_d_only_creates_completada_when_partial()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20027);
        var npc = ws.Npcs.CreateNew();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = npc.Id;
        m.PregDarPreguntaId = 20025;
        m.PregIncompletaPreguntaId = 20026;

        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Single(result.CreatedQuestionIds);
        Assert.Equal(20028, m.PregCompletadaPreguntaId);
        Assert.Equal(20025, m.PregDarPreguntaId);
        Assert.Equal(20026, m.PregIncompletaPreguntaId);
    }

    [Fact]
    public void Ensure_roles_case_e_aborts_on_reservation_conflict()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20025);
        var npc = ws.Npcs.CreateNew();
        ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        ws.Dialogs.SetInitialQuestion(npc, 20025);
        var catalog = new ContentDbCatalogSnapshot(
            [],
            [new DbQuestionCatalogRow { Id = 20026 }, new DbQuestionCatalogRow { Id = 20027 }],
            [],
            null,
            true);
        var m = ws.Missions.CreateMission();

        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(m, npc, ws, catalog);

        Assert.False(result.Success);
        Assert.Equal(MissionDialogRolesUi.ReservationFailedMessage, result.ErrorMessage);
        Assert.Null(m.StartNpcId);
        Assert.Null(m.PregDarPreguntaId);
        Assert.Null(m.PregIncompletaPreguntaId);
        Assert.Null(m.PregCompletadaPreguntaId);
        Assert.Equal(1, ws.Dialogs.Questions.Count);
    }

    [Fact]
    public void Seed_dialog_roles_preserves_existing_start_and_preg_dar()
    {
        var ws = new ContentDraftWorkspace();
        ws.Dialogs.SetDbMaxQuestionId(20027);
        var npc = ws.Npcs.CreateNew();
        var m = ws.Missions.CreateMission();
        m.StartNpcId = 99;
        m.PregDarPreguntaId = 55;
        Assert.True(MissionDialogRolesUi.SeedDialogRolesIfEmpty(m, npc, ws));
        Assert.Equal(99, m.StartNpcId);
        Assert.Equal(55, m.PregDarPreguntaId);
        Assert.Equal(20028, m.PregIncompletaPreguntaId);
        Assert.Equal(20029, m.PregCompletadaPreguntaId);
    }

    [Fact]
    public void Has_role_questions_outside_npc_when_owner_mismatch()
    {
        var ws = new ContentDraftWorkspace();
        var a = ws.Npcs.CreateNew();
        var b = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(a.Id);
        var m = ws.Missions.CreateMission();
        m.StartNpcId = b.Id;
        m.PregDarPreguntaId = q.Id;
        Assert.True(MissionDialogRolesUi.HasRoleQuestionsOutsideNpc(m, ws.Dialogs, b.Id));
    }

    [Fact]
    public void Clear_dialog_roles_only_clears_three_fields()
    {
        var m = new MissionDraft
        {
            PregDarPreguntaId = 1,
            PregIncompletaPreguntaId = 2,
            PregCompletadaPreguntaId = 3,
            StartNpcId = 99,
        };
        MissionDialogRolesUi.ClearDialogRoles(m);
        Assert.Null(m.PregDarPreguntaId);
        Assert.Null(m.PregIncompletaPreguntaId);
        Assert.Null(m.PregCompletadaPreguntaId);
        Assert.Equal(99, m.StartNpcId);
    }

    [Fact]
    public void Build_question_choices_includes_npc_questions_and_assigned_role_ids()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q1 = ws.Dialogs.CreateQuestion(npc.Id);
        q1.TextLocal = "Pregunta A";
        var q2 = ws.Dialogs.CreateQuestion(npc.Id);
        q2.TextLocal = "Pregunta B";
        var otherNpc = ws.Npcs.CreateNew();
        var orphan = ws.Dialogs.CreateQuestion(otherNpc.Id);
        orphan.TextLocal = "Fuera de NPC";

        var choices = MissionDialogRolesUi.BuildQuestionChoices(
            ws.Dialogs,
            npc.Id,
            pregDarId: q2.Id,
            pregIncId: orphan.Id,
            pregCompId: null);

        Assert.Equal(3, choices.Count);
        Assert.Contains(choices, q => q.Id == q1.Id);
        Assert.Contains(choices, q => q.Id == q2.Id);
        Assert.Contains(choices, q => q.Id == orphan.Id);
    }

    [Fact]
    public void Resolve_initial_question_uses_sole_owned_question_when_pregunta_unset()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        Assert.Equal(q.Id, MissionDialogRolesUi.ResolveInitialQuestionId(npc, ws.Dialogs));
    }

    [Fact]
    public void Resolve_initial_question_uses_bd_npc_pregunta_when_local_unset()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var catalog = new ContentDbCatalogSnapshot(
            [new DbNpcCatalogRow { Id = npc.Id, Nombre = "NPC", Pregunta = 20024 }],
            [new DbQuestionCatalogRow { Id = 20024 }],
            [],
            null,
            true);
        Assert.Equal(20024, MissionDialogRolesUi.ResolveInitialQuestionId(npc, ws.Dialogs, catalog));
    }

    [Fact]
    public void Seed_linked_mission_roles_uses_target_mission_draft_id_not_other_mission()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var other = ws.Missions.CreateMission();
        other.StartNpcId = npc.Id;
        other.PregDarPreguntaId = 999;

        var linked = ws.Missions.CreateMission();
        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest, TargetMissionDraftId = linked.DraftId };

        Assert.True(MissionDialogRolesUi.SeedLinkedMissionRoles(action, npc, ws));
        Assert.Equal(npc.Id, linked.StartNpcId);
        Assert.Equal(q.Id, linked.PregDarPreguntaId);
        Assert.NotNull(linked.PregIncompletaPreguntaId);
        Assert.NotNull(linked.PregCompletadaPreguntaId);
        Assert.Equal(999, other.PregDarPreguntaId);
    }

    [Fact]
    public void Resolve_role_tag_from_mission_draft()
    {
        var m = new MissionDraft { PregIncompletaPreguntaId = 55 };
        Assert.Equal(MissionDialogRolesUi.TagIncomplete, MissionDialogRolesUi.ResolveRoleTag(m, 55));
        Assert.Null(MissionDialogRolesUi.ResolveRoleTag(m, 56));
    }

    [Fact]
    public void Seed_on_linked_empty_draft_populates_roles_for_canvas_and_panel()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var other = ws.Missions.CreateMission();
        other.StartNpcId = npc.Id;
        other.PregDarPreguntaId = 999;

        var linked = ws.Missions.CreateMission();
        linked.Nombre = "Nueva misión";
        var action = new DialogActionDraft { Accion = DialogActionCodes.StartQuest, TargetMissionDraftId = linked.DraftId };

        Assert.True(MissionDialogRolesUi.SeedLinkedMissionRoles(action, npc, ws));
        Assert.Equal(npc.Id, linked.StartNpcId);
        Assert.Equal(q.Id, linked.PregDarPreguntaId);
        Assert.NotNull(linked.PregIncompletaPreguntaId);
        Assert.NotNull(linked.PregCompletadaPreguntaId);
        Assert.Equal(MissionDialogRolesUi.TagOffer, MissionDialogRolesUi.ResolveRoleTag(linked, q.Id));
        Assert.NotEmpty(linked.BuildPregDar());
    }

    [Fact]
    public void Remove_secondary_roles_on_start_quest_removed_deletes_inc_comp_questions()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        npc.Pregunta = q.Id;
        var response = ws.Dialogs.AddResponse(q);
        var action = ws.Dialogs.AddAction(response, DialogActionCodes.StartQuest);
        var mission = ws.Missions.CreateMission();
        ws.Dialogs.LinkStartMission(action, mission.DraftId);

        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);

        var incId = mission.PregIncompletaPreguntaId!.Value;
        var compId = mission.PregCompletadaPreguntaId!.Value;

        var result = MissionDialogRolesUi.RemoveSecondaryRolesOnStartQuestRemoved(action, npc, ws);

        Assert.True(result.Changed);
        Assert.Null(mission.PregIncompletaPreguntaId);
        Assert.Null(mission.PregCompletadaPreguntaId);
        Assert.Null(ws.Dialogs.FindQuestion(incId));
        Assert.Null(ws.Dialogs.FindQuestion(compId));
        Assert.NotNull(ws.Dialogs.FindQuestion(q.Id));
        Assert.Equal(q.Id, mission.PregDarPreguntaId);
    }

    [Fact]
    public void Ensure_roles_after_secondary_removed_restores_inc_comp_questions()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        npc.Pregunta = q.Id;
        var response = ws.Dialogs.AddResponse(q);
        var action = ws.Dialogs.AddAction(response, DialogActionCodes.StartQuest);
        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = npc.Id;
        ws.Dialogs.LinkStartMission(action, mission.DraftId);
        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);

        MissionDialogRolesUi.RemoveSecondaryRolesOnStartQuestRemoved(action, npc, ws);
        Assert.Null(mission.PregIncompletaPreguntaId);
        Assert.Null(mission.PregCompletadaPreguntaId);

        var restored = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);
        Assert.True(restored.Changed);
        Assert.True(mission.PregIncompletaPreguntaId is int incId && incId > 0);
        Assert.True(mission.PregCompletadaPreguntaId is int compId && compId > 0);
        Assert.NotNull(ws.Dialogs.FindQuestion(mission.PregIncompletaPreguntaId!.Value));
        Assert.NotNull(ws.Dialogs.FindQuestion(mission.PregCompletadaPreguntaId!.Value));
    }

    [Fact]
    public void Remove_secondary_roles_skips_when_another_start_quest_still_links_mission()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        npc.Pregunta = q.Id;
        var r1 = ws.Dialogs.AddResponse(q);
        var a1 = ws.Dialogs.AddAction(r1, DialogActionCodes.StartQuest);
        var r2 = ws.Dialogs.AddResponse(q);
        var a2 = ws.Dialogs.AddAction(r2, DialogActionCodes.StartQuest);
        var mission = ws.Missions.CreateMission();
        ws.Dialogs.LinkStartMission(a1, mission.DraftId);
        ws.Dialogs.LinkStartMission(a2, mission.DraftId);
        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, ws);
        var incId = mission.PregIncompletaPreguntaId!.Value;

        var skipped = MissionDialogRolesUi.RemoveSecondaryRolesOnStartQuestRemoved(a1, npc, ws);
        ws.Dialogs.RemoveAction(r1, a1);

        Assert.False(skipped.Changed);
        Assert.Equal(incId, mission.PregIncompletaPreguntaId);
        Assert.NotNull(ws.Dialogs.FindQuestion(incId));

        var removed = MissionDialogRolesUi.RemoveSecondaryRolesOnStartQuestRemoved(a2, npc, ws);
        ws.Dialogs.RemoveAction(r2, a2);

        Assert.True(removed.Changed);
        Assert.Null(mission.PregIncompletaPreguntaId);
        Assert.Null(ws.Dialogs.FindQuestion(incId));
    }

    [Fact]
    public void Sync_start_npc_from_owner_sets_internal_start_npc_id()
    {
        var m = new MissionDraft { Nombre = "Nueva misión" };
        Assert.True(MissionDraft.SyncStartNpcFromOwner(m, 20065));
        Assert.Equal(20065, m.StartNpcId);
        Assert.False(MissionDraft.SyncStartNpcFromOwner(m, 20065));
    }

    [Fact]
    public void Build_preg_pair_uses_owner_npc_for_preview_without_manual_start_npc()
    {
        const int ownerNpcId = 20065;
        const int questionId = 20025;
        Assert.Equal("20065;20025", MissionDraft.BuildPregPair(ownerNpcId, questionId));
        Assert.Equal("", MissionDraft.BuildPregPair(null, questionId));
    }

    [Fact]
    public void Panel_resolves_linked_mission_when_selected_mission_is_null()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        var npc = ws.Npcs.CreateNew();
        Assert.Equal(20065, npc.Id);

        var qDar = ws.Dialogs.CreateQuestionWithId(npc.Id, 20025);
        qDar.TextLocal = "";
        var qInc = ws.Dialogs.CreateQuestionWithId(npc.Id, 20026);
        qInc.TextLocal = "DDDD";
        var qComp = ws.Dialogs.CreateQuestionWithId(npc.Id, 20027);
        qComp.TextLocal = "DDDD";

        var mission = ws.Missions.CreateMission();
        mission.StartNpcId = 20065;
        mission.PregDarPreguntaId = 20025;
        mission.PregIncompletaPreguntaId = 20026;
        mission.PregCompletadaPreguntaId = 20027;

        var action = new DialogActionDraft
        {
            Accion = DialogActionCodes.StartQuest,
            TargetMissionDraftId = mission.DraftId,
        };

        var linked = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id);
        Assert.Same(mission, linked);

        var panelMission = MissionDialogRolesUi.ResolvePanelMissionDraft(
            actionInlineMode: true,
            linkedMission: linked,
            selectedMission: null);
        Assert.Same(mission, panelMission);

        Assert.Equal(20025, panelMission!.PregDarPreguntaId);
        Assert.Equal(20026, panelMission.PregIncompletaPreguntaId);
        Assert.Equal(20027, panelMission.PregCompletadaPreguntaId);

        var previews = MissionDialogRolesUi.BuildRolePreviews(panelMission, npc.Id);
        Assert.Equal("20065;20025", previews.PregDar);
        Assert.Equal("20065;20026", previews.PregInc);
        Assert.Equal("20065;20027", previews.PregComp);

        var choices = MissionDbCatalogUi.BuildQuestionChoiceItems(
            ws.Dialogs,
            null,
            npc.Id,
            panelMission.PregDarPreguntaId,
            panelMission.PregIncompletaPreguntaId,
            panelMission.PregCompletadaPreguntaId);

        Assert.Equal(3, choices.Count);
        Assert.Equal("#20025 · (sin texto)", choices.First(c => c.Id == 20025).Label);
        Assert.Equal("#20026 · DDDD", choices.First(c => c.Id == 20026).Label);
        Assert.Equal("#20027 · DDDD", choices.First(c => c.Id == 20027).Label);
    }
}
