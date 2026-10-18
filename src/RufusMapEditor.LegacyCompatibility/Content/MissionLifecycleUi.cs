namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MISSIONS.LIFECYCLE.1 — mission draft creation and selector filtering.</summary>
public static class MissionLifecycleUi
{
    public const string DefaultNewMissionName = "Nueva misión";

    /// <summary>
    /// Creates a new MissionDraft with default name, owner NPC, optional PregDar from initial question,
    /// and all dialog roles (incomplete/complete) when missing.
    /// </summary>
    public static MissionDraft CreateNewMissionWithRoles(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        ContentDbCatalogSnapshot? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var mission = workspace.Missions.CreateMission();
        mission.Nombre = DefaultNewMissionName;
        mission.StartNpcId = npc.Id;
        if (MissionDialogRolesUi.ResolveDefaultOfferQuestionId(npc, workspace.Dialogs, catalog) is int offerId)
            mission.PregDarPreguntaId = offerId;

        MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(mission, npc, workspace, catalog);
        return mission;
    }

    /// <summary>
    /// Mission selector choices for acción 44: linked draft + missions owned by <paramref name="npcId"/>.
    /// Excludes orphan/legacy drafts from other contexts. Deduplicates by DraftId.
    /// </summary>
    public static IReadOnlyList<MissionDraft> BuildMissionChoicesForNpc(
        int npcId,
        IEnumerable<MissionDraft> missions,
        Guid? linkedDraftId = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var byId = new Dictionary<Guid, MissionDraft>();

        if (linkedDraftId is Guid linkId)
        {
            var linked = missions.FirstOrDefault(m => m.DraftId == linkId);
            if (linked is not null)
                byId[linkId] = linked;
        }

        foreach (var m in missions)
        {
            if (m.StartNpcId == npcId)
                byId[m.DraftId] = m;
        }

        return byId.Values
            .OrderBy(m => m.Nombre, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.DraftId)
            .ToList();
    }
}
