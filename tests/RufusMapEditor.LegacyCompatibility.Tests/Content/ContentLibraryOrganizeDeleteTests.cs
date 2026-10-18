using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONTENT.LIBRARY.ORGANIZE.DELETE.2</summary>
public sealed class ContentLibraryOrganizeDeleteTests
{
    [Fact]
    public void Organize_separates_npc_missions_dialogs_and_mission_status_groups()
    {
        var ws = SeedWorkspace();
        var all = ContentLibraryCatalog.Build(ws);
        var rows = ContentLibraryCatalog.Organize(all, ContentLibraryFilter.All);

        Assert.Contains(rows, r => r.IsSectionHeader && r.SectionTitle.StartsWith("NPC + Diálogo", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.IsSectionHeader && r.SectionTitle.StartsWith("MISIONES", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.IsSectionHeader && r.SectionTitle.Contains("Borradores", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.IsSectionHeader && r.SectionTitle.Contains("Cambios pendientes", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.IsSectionHeader && r.SectionTitle.Contains("Publicadas", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.IsSectionHeader && r.SectionTitle.StartsWith("DIÁLOGOS", StringComparison.Ordinal));

        var sections = ContentLibraryCatalog.OrganizeSections(all, ContentLibraryFilter.All);
        Assert.Contains(sections, s => s.Title.StartsWith("NPC + Diálogo", StringComparison.Ordinal) && s.Entries.Count > 0);
        Assert.Contains(sections, s => s.IsSubSection && s.Title.Contains("Borradores", StringComparison.Ordinal) && s.Entries.Count > 0);
        Assert.All(sections.Where(s => s.Entries.Count > 0), s =>
            Assert.DoesNotContain(s.Entries, e => e.Title.Contains('-') && e.Title.Length == 36)); // no GUID titles
    }

    [Fact]
    public void Card_face_shows_priority_fields_without_technical_noise()
    {
        var ws = SeedWorkspace();
        var mission = ContentLibraryCatalog.Build(ws)
            .Single(e => e.Kind == ContentLibraryEntryKind.Mission && e.Title == "Pendiente");
        Assert.Equal("CAMBIOS PENDIENTES", mission.Status);
        Assert.Equal("Quest #100", mission.CardIdLine);
        Assert.StartsWith("NPC:", mission.CardLinkLine, StringComparison.Ordinal);
        Assert.Contains("etapa", mission.CardStatsLine, StringComparison.OrdinalIgnoreCase);

        var orphan = ContentLibraryCatalog.Build(ws)
            .Single(e => e.Kind == ContentLibraryEntryKind.Mission && e.Title == "Sin NPC");
        Assert.Equal("Sin NPC inicial", orphan.CardLinkLine);
        Assert.False(orphan.ShowCardIdLine);

        var npc = ContentLibraryCatalog.Build(ws)
            .Single(e => e.Kind == ContentLibraryEntryKind.Npc && e.Title == "RUFUS");
        Assert.True(npc.HasConversation);
        Assert.StartsWith("NPC #", npc.CardIdLine, StringComparison.Ordinal);
        Assert.Contains("Diálogo", npc.CardStatsLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Counters_and_filters_match_workspace()
    {
        var ws = SeedWorkspace();
        var all = ContentLibraryCatalog.Build(ws);
        var counters = ContentLibraryCatalog.Count(all);
        Assert.True(counters.Drafts >= 1);
        Assert.True(counters.PendingChanges >= 1);
        Assert.True(counters.Published >= 1);

        var npcs = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.NpcAndDialog, ContentLibraryFilter.All, null);
        Assert.NotEmpty(npcs);
        Assert.All(npcs, e => Assert.Equal(ContentLibraryEntryKind.Npc, e.Kind));
        Assert.Contains(npcs, e => e.HasConversation);

        var noStart = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.Missions, ContentLibraryFilter.WithoutStartNpc, null);
        Assert.Contains(noStart, e => e.Kind == ContentLibraryEntryKind.Mission && !e.HasStartNpc);
        Assert.All(noStart, e => Assert.Equal(ContentLibraryEntryKind.Mission, e.Kind));
    }

    [Fact]
    public void Modes_keep_npc_without_mission_out_of_missions()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "SoloDialogo";
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var all = ContentLibraryCatalog.Build(ws);
        var npcMode = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.NpcAndDialog, ContentLibraryFilter.All, null);
        Assert.Contains(npcMode, e => e.Kind == ContentLibraryEntryKind.Npc && e.Title == "SoloDialogo");
        Assert.DoesNotContain(npcMode, e => e.Kind == ContentLibraryEntryKind.Dialog);
        Assert.DoesNotContain(npcMode, e => e.Kind == ContentLibraryEntryKind.Mission);

        var missionMode = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.Missions, ContentLibraryFilter.All, null);
        Assert.Empty(missionMode);
        Assert.DoesNotContain(missionMode, e => e.Kind == ContentLibraryEntryKind.Npc);
        Assert.DoesNotContain(missionMode, e => e.Kind == ContentLibraryEntryKind.Dialog);
    }

    [Fact]
    public void Delete_unpublished_mission_removes_from_workspace_and_survives_serialize()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Viejo borrador";
        var id = mission.DraftId;
        Assert.False(ContentLibraryLocalCleanup.IsMissionPublishedLocally(mission));

        var plan = ContentLibraryLocalCleanup.PlanMission(ws, id);
        Assert.Equal(ContentLibraryRemovalKind.DeleteUnpublishedDraft, plan.Kind);
        Assert.True(plan.Allowed);

        Assert.True(ContentLibraryLocalCleanup.RemoveMissionLocal(ws, id));
        Assert.Null(ws.Missions.FindByDraftId(id));

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Null(loaded.Missions.FindByDraftId(id));
        Assert.DoesNotContain(ContentLibraryCatalog.Build(loaded), e => e.MissionDraftId == id);
    }

    [Fact]
    public void Delete_mission_unlinks_action44_keeps_shared_npc_dialog()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "RUFUS";
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var resp = ws.Dialogs.AddResponse(q);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        var action = ws.Dialogs.AddAction(resp, DialogActionCodes.StartQuest);
        ws.Dialogs.LinkStartMission(action, mission.DraftId);
        Assert.Equal(mission.DraftId, action.TargetMissionDraftId);

        Assert.True(ContentLibraryLocalCleanup.DialogRolesExclusiveToMission(ws, mission.DraftId));
        Assert.True(ContentLibraryLocalCleanup.RemoveMissionLocal(ws, mission.DraftId));

        Assert.Null(ws.Missions.FindByDraftId(mission.DraftId));
        Assert.Null(action.TargetMissionDraftId);
        Assert.NotNull(ws.Dialogs.FindQuestion(q.Id));
        Assert.Equal(npc.Id, ws.Npcs.FindById(npc.Id)!.Id);
    }

    [Fact]
    public void Shared_role_question_across_missions_is_not_exclusive()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var npc = ws.Npcs.CreateNew();
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        var a = ws.Missions.CreateMission();
        var b = ws.Missions.CreateMission();
        a.PregDarPreguntaId = q.Id;
        b.PregDarPreguntaId = q.Id;
        Assert.False(ContentLibraryLocalCleanup.DialogRolesExclusiveToMission(ws, a.DraftId));
    }

    [Fact]
    public void Published_mission_plan_is_remove_local_copy_not_production_delete()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Live";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        var plan = ContentLibraryLocalCleanup.PlanMission(ws, mission.DraftId);
        Assert.Equal(ContentLibraryRemovalKind.RemoveLocalCopyOfPublished, plan.Kind);
        Assert.Contains("servidor", plan.Message, StringComparison.OrdinalIgnoreCase);

        var entry = ContentLibraryCatalog.Build(ws).Single(e => e.MissionDraftId == mission.DraftId);
        Assert.True(entry.CanRemoveLocalCopy);
        Assert.False(entry.CanDeleteDraft);
        Assert.Equal("Quitar copia local", entry.RemoveActionLabel);

        Assert.True(ContentLibraryLocalCleanup.RemoveMissionLocal(ws, mission.DraftId));
        Assert.Null(ws.Missions.FindByDraftId(mission.DraftId));
    }

    [Fact]
    public void Remove_local_copy_of_published_preserves_quest_identity_semantics()
    {
        // Quitar copia local no inventa DELETE: solo deja de existir el draft.
        // La identidad de servidor (Quest ID) no se reescribe ni se limpia en remoto.
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 55;
        var questId = mission.PublishedQuestId;
        Assert.True(ContentLibraryLocalCleanup.RemoveMissionLocal(ws, mission.DraftId));
        Assert.Equal(55, questId);
        Assert.Empty(ws.Missions.Missions);
    }

    [Fact]
    public void Delete_unpublished_npc_with_linked_tree_clears_local_children()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Temp";
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "TempMission";
        mission.StartNpcId = npc.Id;
        ws.Missions.AddStage(mission);

        var plan = ContentLibraryLocalCleanup.PlanNpc(ws, npc.Id);
        Assert.True(plan.RequiresLinkedConfirm);
        Assert.True(ContentLibraryLocalCleanup.RemoveNpcLocal(ws, npc.Id, removeLinkedUnpublished: true));

        Assert.Null(ws.Npcs.FindById(npc.Id));
        Assert.Empty(ws.Dialogs.QuestionsForNpc(npc.Id));
        Assert.DoesNotContain(ws.Missions.Missions, m => m.DraftId == mission.DraftId);
    }

    [Fact]
    public void Duplicate_still_clears_published_identity()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 9;
        var copy = ws.Missions.DuplicateMission(mission);
        Assert.NotEqual(mission.DraftId, copy.DraftId);
        Assert.False(copy.PublishedBd);
        Assert.Null(copy.PublishedQuestId);
        Assert.True(ContentLibraryCatalog.Build(ws).Count(e => e.Kind == ContentLibraryEntryKind.Mission) >= 2);
    }

    [Fact]
    public void Library_refresh_projection_drops_removed_mission()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "X";
        Assert.Contains(ContentLibraryCatalog.Build(ws), e => e.MissionDraftId == mission.DraftId);
        ContentLibraryLocalCleanup.RemoveMissionLocal(ws, mission.DraftId);
        Assert.DoesNotContain(ContentLibraryCatalog.Build(ws), e => e.MissionDraftId == mission.DraftId);
    }

    private static ContentDraftWorkspace SeedWorkspace()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "RUFUS";
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var draft = ws.Missions.CreateMission();
        draft.Nombre = "Borrador";
        draft.StartNpcId = npc.Id;

        var pending = ws.Missions.CreateMission();
        pending.Nombre = "Pendiente";
        pending.StartNpcId = npc.Id;
        pending.PublishedBd = true;
        pending.PublishedQuestId = 100;
        pending.HasPendingBdChanges = true;

        var published = ws.Missions.CreateMission();
        published.Nombre = "Publicada";
        published.StartNpcId = npc.Id;
        published.PublishedBd = true;
        published.PublishedQuestId = 101;
        published.QuestsEsPublished = true;

        var orphan = ws.Missions.CreateMission();
        orphan.Nombre = "Sin NPC";
        return ws;
    }
}
