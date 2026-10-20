namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MISSIONS.ACTION44.LINK.1 — resolve which MissionDraft links to an unlinked accion=44.</summary>
public static class StartQuestLinkUi
{
    public enum ResolveOutcome
    {
        LinkedExisting,
        NeedsCreate,
        Ambiguous,
    }

    public sealed class ResolveResult
    {
        public ResolveOutcome Outcome { get; init; }
        public MissionDraft? Mission { get; init; }
    }

    public static ResolveResult ResolveMissionForNpc(
        NpcsModeloDraft npc,
        IEnumerable<MissionDraft> missions)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(missions);
        var candidates = missions.Where(m => m.StartNpcId == npc.Id).ToList();
        return candidates.Count switch
        {
            1 => new ResolveResult { Outcome = ResolveOutcome.LinkedExisting, Mission = candidates[0] },
            0 => new ResolveResult { Outcome = ResolveOutcome.NeedsCreate },
            _ => new ResolveResult { Outcome = ResolveOutcome.Ambiguous },
        };
    }

    public static bool HasValidLink(DialogActionDraft action, MissionDraftBatch missions)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(missions);
        return action.Accion == DialogActionCodes.StartQuest
            && action.TargetMissionDraftId is Guid id
            && missions.FindByDraftId(id) is not null;
    }

    /// <summary>MissionDraft referenced by accion=44 TargetMissionDraftId, if valid.</summary>
    public static MissionDraft? ResolveLinkedMission(DialogActionDraft? action, MissionDraftBatch missions)
    {
        ArgumentNullException.ThrowIfNull(missions);
        if (action?.Accion != DialogActionCodes.StartQuest) return null;
        if (action.TargetMissionDraftId is not Guid draftId) return null;
        return missions.FindByDraftId(draftId);
    }

    /// <summary>MISSIONS.PANEL.STATE.1 — inline panel: TargetMissionDraftId only, no IsStartQuestAction gate.</summary>
    public static MissionDraft? ResolveLinkedMissionByTargetDraftId(
        DialogActionDraft? action,
        MissionDraftBatch missions)
    {
        ArgumentNullException.ThrowIfNull(missions);
        if (action?.TargetMissionDraftId is not Guid draftId) return null;
        return missions.FindByDraftId(draftId);
    }

    /// <summary>Single mission with StartNpcId == npcId; null if none or ambiguous.</summary>
    public static MissionDraft? ResolveUnambiguousMissionForNpc(int npcId, IEnumerable<MissionDraft> missions)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var candidates = missions.Where(m => m.StartNpcId == npcId).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// MISSIONS.ACTION44.SOURCEOFTRUTH.1 — linked draft wins; StartNpcId only when unambiguous and no link.
    /// </summary>
    public static MissionDraft? ResolveMissionSourceOfTruth(
        DialogActionDraft? action,
        MissionDraftBatch missions,
        int? npcId = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        if (ResolveLinkedMissionByTargetDraftId(action, missions) is { } linkedByTarget)
            return linkedByTarget;

        if (ResolveLinkedMission(action, missions) is { } linked)
            return linked;

        if (npcId is int id)
            return ResolveUnambiguousMissionForNpc(id, missions.Missions);

        return null;
    }

    /// <summary>
    /// MISSIONS.LIFECYCLE.1 — acción 44 selector: vinculada actual + misiones del NPC (sin huérfanas legacy).
    /// </summary>
    public static IReadOnlyList<MissionDraft> BuildMissionChoicesForNpc(
        int npcId,
        Guid? linkedDraftId,
        IEnumerable<MissionDraft> missions)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var byId = new Dictionary<Guid, MissionDraft>();
        foreach (var m in missions)
        {
            if (m.StartNpcId == npcId)
                byId[m.DraftId] = m;
        }

        if (linkedDraftId is Guid linkId)
        {
            foreach (var m in missions)
            {
                if (m.DraftId == linkId)
                {
                    byId[linkId] = m;
                    break;
                }
            }
        }

        return byId.Values.OrderBy(m => m.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when any accion=44 on the NPC has a valid TargetMissionDraftId.</summary>
    public static bool HasValidLinkedMissionForNpc(
        int npcId,
        DialogDraftBatch dialogs,
        MissionDraftBatch missions)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(missions);
        foreach (var q in dialogs.QuestionsForNpc(npcId))
        {
            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (a.Accion == DialogActionCodes.StartQuest && HasValidLink(a, missions))
                        return true;
                }
            }
        }

        return false;
    }
}
