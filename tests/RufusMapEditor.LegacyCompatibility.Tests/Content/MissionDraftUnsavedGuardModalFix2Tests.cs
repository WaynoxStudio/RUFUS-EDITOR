using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>MISSIONS.DRAFTS.UNSAVED_GUARD.MODAL_FIX.2</summary>
public sealed class MissionDraftUnsavedGuardModalFix2Tests
{
    [Fact]
    public void Dirty_same_draft_rebind_does_not_require_prompt()
    {
        var id = Guid.NewGuid();
        Assert.False(MissionDraftNavigationGuard.RequiresUnsavedPrompt(id, hasUnsavedLocalEdits: true, nextDraftId: id));
    }

    [Fact]
    public void Dirty_change_mission_requires_prompt()
    {
        Assert.True(MissionDraftNavigationGuard.RequiresUnsavedPrompt(
            Guid.NewGuid(), hasUnsavedLocalEdits: true, nextDraftId: Guid.NewGuid()));
    }

    [Fact]
    public void Dirty_clear_selection_requires_prompt()
    {
        Assert.True(MissionDraftNavigationGuard.RequiresUnsavedPrompt(
            Guid.NewGuid(), hasUnsavedLocalEdits: true, nextDraftId: null));
    }

    [Fact]
    public void Clean_change_mission_no_prompt()
    {
        Assert.False(MissionDraftNavigationGuard.RequiresUnsavedPrompt(
            Guid.NewGuid(), hasUnsavedLocalEdits: false, nextDraftId: Guid.NewGuid()));
    }

    [Fact]
    public void Closing_publish_preview_reload_keeps_dirty_and_puedeRepetirse()
    {
        var ws = new ContentDraftWorkspace();
        var mission = ws.Missions.CreateMission();
        mission.PublishedBd = true;
        mission.PublishedQuestId = 100004;
        mission.Nombre = "PRUEBA";
        mission.PuedeRepetirse = false;
        mission.Stages.Add(new MissionStageDraft { Id = 5501, Nombre = "Prueba" });
        mission.MarkPublishedSnapshot();

        // Simulate editor dirty edit (puedeRepetirse) — same as Persist path.
        mission.PuedeRepetirse = true;
        mission.MarkPendingBdEdit();
        Assert.True(mission.HasPendingBdChanges);
        Assert.True(mission.PuedeRepetirse);

        var currentId = mission.DraftId;
        // Reload rebuilds list wrappers — same DraftId must not count as leave.
        Assert.False(MissionDraftNavigationGuard.RequiresUnsavedPrompt(
            currentId, hasUnsavedLocalEdits: true, nextDraftId: currentId));

        // After "close preview" simulation, BD pending state remains.
        Assert.True(mission.HasPendingBdChanges);
        Assert.True(mission.PuedeRepetirse);
        Assert.Equal(MissionDraft.StatusCambiosPendientes, mission.DisplayStatus);
    }

    [Fact]
    public void Close_admin_equivalent_dirty_requires_prompt()
    {
        // Leaving workspace (nextDraftId null) with dirty = same rule as close ADMIN.
        Assert.True(MissionDraftNavigationGuard.RequiresUnsavedPrompt(
            Guid.NewGuid(), hasUnsavedLocalEdits: true, nextDraftId: null));
    }

    [Fact]
    public void Preview_close_is_not_draft_abandonment()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        // Opening/closing BD or client preview rebinds the same draft via Reload.
        Assert.False(MissionDraftNavigationGuard.RequiresUnsavedPrompt(id, true, id));
        Assert.False(MissionDraftNavigationGuard.RequiresUnsavedPrompt(id, true, id));
    }
}
