using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONTENT.LIBRARY.WORKSPACE.UI.1 — proyección read-only del workspace.</summary>
public sealed class ContentLibraryCatalogTests
{
    [Fact]
    public void Build_lists_npc_mission_and_linked_dialog_without_draft_guids_in_titles()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        ws.Dialogs.SetDbMaxQuestionId(20023);

        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "RUFUS";
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);

        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;

        var entries = ContentLibraryCatalog.Build(ws);
        Assert.Contains(entries, e => e.Kind == ContentLibraryEntryKind.Npc && e.Title == "RUFUS");
        Assert.Contains(entries, e => e.Kind == ContentLibraryEntryKind.Mission && e.Title == "PRUEBA" && e.QuestId == 100004);
        var npcEntry = Assert.Single(entries, e => e.Kind == ContentLibraryEntryKind.Npc && e.Title == "RUFUS");
        Assert.True(npcEntry.HasConversation);
        Assert.True(npcEntry.HasLinkedMission);
        Assert.Contains("PRUEBA", npcEntry.Relation, StringComparison.Ordinal);
        Assert.DoesNotContain(entries, e => e.Kind == ContentLibraryEntryKind.Dialog);
        Assert.All(entries, e => Assert.DoesNotContain(mission.DraftId.ToString(), e.Title, StringComparison.OrdinalIgnoreCase));
        Assert.All(entries, e => Assert.DoesNotContain(mission.DraftId.ToString(), e.Summary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Filter_search_by_quest_id_and_pending_filter()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Guardia";
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Entrega";
        mission.StartNpcId = npc.Id;
        mission.PublishedBd = true;
        mission.PublishedQuestId = 42;
        mission.HasPendingBdChanges = true;

        var all = ContentLibraryCatalog.Build(ws);
        var byQuest = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.Missions, ContentLibraryFilter.All, "42");
        Assert.Contains(byQuest, e => e.Kind == ContentLibraryEntryKind.Mission && e.QuestId == 42);

        var pending = ContentLibraryCatalog.Filter(
            all, ContentLibraryMode.Missions, ContentLibraryFilter.PendingChanges, null);
        Assert.Contains(pending, e => e.Status == ContentLibraryCatalog.StatusCambiosPendientes);
        Assert.All(pending, e => Assert.Equal(ContentLibraryStatusGroup.PendingChanges, e.StatusGroup));
    }

    [Fact]
    public void Duplicate_mission_gets_new_draft_id_and_clears_published_identity()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        var originalId = mission.DraftId;

        var copy = ws.Missions.DuplicateMission(mission);
        Assert.NotEqual(originalId, copy.DraftId);
        Assert.False(copy.PublishedBd);
        Assert.Null(copy.PublishedQuestId);
    }
}
