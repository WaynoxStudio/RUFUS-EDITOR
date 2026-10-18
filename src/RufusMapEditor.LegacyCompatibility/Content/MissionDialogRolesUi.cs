using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Result of MISSIONS.DIALOG.ROLES.3B role automation.</summary>
public sealed class MissionDialogRolesEnsureResult
{
    public bool Success { get; init; } = true;
    public bool Changed { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<int> CreatedQuestionIds { get; init; } = Array.Empty<int>();
}

/// <summary>Result of removing secondary mission role questions (incomplete/complete).</summary>
public sealed class MissionDialogRolesCleanupResult
{
    public bool Changed { get; init; }
    public Guid? MissionDraftId { get; init; }
    public IReadOnlyList<int> DeletedQuestionIds { get; init; } = Array.Empty<int>();
}

/// <summary>MISSIONS.DIALOG.ROLES.1 — mission dialog role helpers (no BD).</summary>
public static class MissionDialogRolesUi
{
    public const string ReservationFailedMessage =
        "No se pudieron reservar IDs seguros para las preguntas de misión.";

    public const string DefaultMissionName = "Nueva misión";
    public const string RoleNotConfiguredLabel = "Sin configurar";

    public const string TagOffer = "Misión: ofrecer";
    public const string TagIncomplete = "Misión: incompleta";
    public const string TagComplete = "Misión: completada";

    public static string FormatQuestionLabel(DialogQuestionDraft question, int maxTextLength = 48)
    {
        ArgumentNullException.ThrowIfNull(question);
        return FormatQuestionLabel(question.Id, question.TextLocal, maxTextLength);
    }

    public static string FormatQuestionLabel(int questionId, string? text, int maxTextLength = 48)
    {
        text = (text ?? "").ReplaceLineEndings(" ").Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        if (text.Length > maxTextLength)
            text = text[..Math.Max(0, maxTextLength - 3)] + "...";
        return string.IsNullOrWhiteSpace(text)
            ? string.Create(CultureInfo.InvariantCulture, $"#{questionId} · (sin texto)")
            : string.Create(CultureInfo.InvariantCulture, $"#{questionId} · {text}");
    }

    public static string? ResolveRoleTag(MissionDraft mission, int questionId)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.PregDarPreguntaId == questionId) return TagOffer;
        if (mission.PregIncompletaPreguntaId == questionId) return TagIncomplete;
        if (mission.PregCompletadaPreguntaId == questionId) return TagComplete;
        return null;
    }

    /// <summary>MISSIONS.DIALOG.ROLES.4 — panel draft: linked mission wins in acción 44 inline mode.</summary>
    public static MissionDraft? ResolvePanelMissionDraft(
        bool actionInlineMode,
        MissionDraft? linkedMission,
        MissionDraft? selectedMission) =>
        actionInlineMode && linkedMission is not null ? linkedMission : selectedMission;

    /// <summary>Technical preg* previews for dialog role panel.</summary>
    public static (string PregDar, string PregInc, string PregComp) BuildRolePreviews(
        MissionDraft mission,
        int? ownerNpcId = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var npcId = ownerNpcId ?? mission.StartNpcId;
        return (
            MissionDraft.BuildPregPair(npcId, mission.PregDarPreguntaId),
            MissionDraft.BuildPregPair(npcId, mission.PregIncompletaPreguntaId),
            MissionDraft.BuildPregPair(npcId, mission.PregCompletadaPreguntaId));
    }

    /// <summary>True when any assigned role question does not belong to <paramref name="startNpcId"/>.</summary>
    public static bool HasRoleQuestionsOutsideNpc(
        MissionDraft mission,
        DialogDraftBatch dialogs,
        int? startNpcId)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(dialogs);
        if (startNpcId is not int npcId) return false;
        foreach (var qid in new[] { mission.PregDarPreguntaId, mission.PregIncompletaPreguntaId, mission.PregCompletadaPreguntaId })
        {
            if (qid is not int id) continue;
            var q = dialogs.FindQuestion(id);
            if (q is null || q.OwnerNpcId != npcId)
                return true;
        }
        return false;
    }

    public static void ClearDialogRoles(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        mission.PregDarPreguntaId = null;
        mission.PregIncompletaPreguntaId = null;
        mission.PregCompletadaPreguntaId = null;
    }

    /// <summary>Initial NPC question suitable for "Al ofrecer" when creating a mission.</summary>
    public static int? ResolveDefaultOfferQuestionId(
        NpcsModeloDraft npc,
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog = null) =>
        ResolveInitialQuestionId(npc, dialogs, catalog);

    /// <summary>
    /// Unambiguous initial question: npc.Pregunta draft/BD, then BD npc row, then sole local question.
    /// </summary>
    public static int? ResolveInitialQuestionId(
        NpcsModeloDraft npc,
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(dialogs);

        if (npc.Pregunta > 0)
        {
            var q = dialogs.FindQuestion(npc.Pregunta);
            if (q is not null && q.OwnerNpcId == npc.Id)
                return q.Id;
            if (catalog?.FindQuestion(npc.Pregunta) is not null)
                return npc.Pregunta;
        }

        if (catalog?.FindNpc(npc.Id) is { Pregunta: > 0 } bdNpc)
        {
            var bdPreg = bdNpc.Pregunta;
            var local = dialogs.FindQuestion(bdPreg);
            if (local is not null && local.OwnerNpcId == npc.Id)
                return local.Id;
            if (catalog.FindQuestion(bdPreg) is not null)
                return bdPreg;
        }

        var owned = dialogs.QuestionsForNpc(npc.Id);
        if (owned.Count == 1)
            return owned[0].Id;

        return null;
    }

    /// <summary>Seed roles on the MissionDraft referenced by accion=44 TargetMissionDraftId.</summary>
    public static bool SeedLinkedMissionRoles(
        DialogActionDraft action,
        NpcsModeloDraft ownerNpc,
        ContentDraftWorkspace workspace,
        ContentDbCatalogSnapshot? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(ownerNpc);
        ArgumentNullException.ThrowIfNull(workspace);
        if (action.Accion != DialogActionCodes.StartQuest) return false;
        if (action.TargetMissionDraftId is not Guid draftId) return false;
        var mission = workspace.Missions.FindByDraftId(draftId);
        if (mission is null) return false;
        return EnsureAllDialogRolesIfEmpty(mission, ownerNpc, workspace, catalog).Changed;
    }

    /// <summary>
    /// When accion=44 is removed, drop incomplete/completed role questions and clear mission refs.
    /// Skips when another accion=44 still links the same mission. Keeps PregDar intact.
    /// </summary>
    public static MissionDialogRolesCleanupResult RemoveSecondaryRolesOnStartQuestRemoved(
        DialogActionDraft action,
        NpcsModeloDraft ownerNpc,
        ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(ownerNpc);
        ArgumentNullException.ThrowIfNull(workspace);
        if (action.Accion != DialogActionCodes.StartQuest)
            return new MissionDialogRolesCleanupResult();

        var mission = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, workspace.Missions, ownerNpc.Id);
        if (mission is null)
            return new MissionDialogRolesCleanupResult();

        if (workspace.Dialogs.HasStartQuestReferenceToMission(mission.DraftId, action))
            return new MissionDialogRolesCleanupResult();

        return RemoveSecondaryRoleQuestions(mission, ownerNpc, workspace.Dialogs);
    }

    public static MissionDialogRolesCleanupResult RemoveSecondaryRoleQuestions(
        MissionDraft mission,
        NpcsModeloDraft ownerNpc,
        DialogDraftBatch dialogs)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(ownerNpc);
        ArgumentNullException.ThrowIfNull(dialogs);

        var incId = mission.PregIncompletaPreguntaId;
        var compId = mission.PregCompletadaPreguntaId;
        if (incId is null && compId is null)
            return new MissionDialogRolesCleanupResult { MissionDraftId = mission.DraftId };

        mission.PregIncompletaPreguntaId = null;
        mission.PregCompletadaPreguntaId = null;

        var protectId = ownerNpc.Pregunta > 0 ? ownerNpc.Pregunta : mission.PregDarPreguntaId;
        var deleted = new List<int>();
        foreach (var qid in new[] { incId, compId })
        {
            if (qid is not int id || id <= 0 || id == protectId) continue;
            var q = dialogs.FindQuestion(id);
            if (q is null || q.OwnerNpcId != ownerNpc.Id) continue;
            if (dialogs.TryDeleteQuestion(id, unlinkAndDelete: true, out _) == QuestionDeleteResult.Deleted)
                deleted.Add(id);
        }

        return new MissionDialogRolesCleanupResult
        {
            Changed = true,
            MissionDraftId = mission.DraftId,
            DeletedQuestionIds = deleted,
        };
    }

    /// <summary>
    /// MISSIONS.DIALOG.ROLES.3B — fill empty StartNpcId, PregDar, PregInc, PregComp atomically.
    /// </summary>
    public static MissionDialogRolesEnsureResult EnsureAllDialogRolesIfEmpty(
        MissionDraft mission,
        NpcsModeloDraft ownerNpc,
        ContentDraftWorkspace workspace,
        ContentDbCatalogSnapshot? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(ownerNpc);
        ArgumentNullException.ThrowIfNull(workspace);
        var dialogs = workspace.Dialogs;

        var needStartNpc = mission.StartNpcId is null or <= 0;
        var needPregDar = mission.PregDarPreguntaId is null or <= 0;
        var needInc = mission.PregIncompletaPreguntaId is null or <= 0;
        var needComp = mission.PregCompletadaPreguntaId is null or <= 0;

        if (!needStartNpc && !needPregDar && !needInc && !needComp)
            return new MissionDialogRolesEnsureResult();

        int? plannedStartNpc = needStartNpc ? ownerNpc.Id : null;
        int? plannedPregDar = needPregDar
            ? ResolveInitialQuestionId(ownerNpc, dialogs, catalog)
            : null;

        var reserveCount = (needInc ? 1 : 0) + (needComp ? 1 : 0);
        int[] reserved = Array.Empty<int>();
        if (reserveCount > 0
            && !ContentIdReservation.TryReserveQuestionIds(workspace, reserveCount, catalog, out reserved, out var error))
        {
            return new MissionDialogRolesEnsureResult
            {
                Success = false,
                ErrorMessage = error,
            };
        }

        var reservedIdx = 0;
        int? plannedInc = needInc ? reserved[reservedIdx++] : null;
        int? plannedComp = needComp ? reserved[reservedIdx] : null;

        var changed = false;
        var created = new List<int>();

        if (plannedStartNpc is int startNpc && mission.StartNpcId != startNpc)
        {
            mission.StartNpcId = startNpc;
            changed = true;
        }

        if (plannedPregDar is int pregDar && mission.PregDarPreguntaId != pregDar)
        {
            mission.PregDarPreguntaId = pregDar;
            changed = true;
        }

        if (plannedInc is int incId)
        {
            dialogs.CreateQuestionWithId(ownerNpc.Id, incId);
            mission.PregIncompletaPreguntaId = incId;
            created.Add(incId);
            changed = true;
        }

        if (plannedComp is int compId)
        {
            dialogs.CreateQuestionWithId(ownerNpc.Id, compId);
            mission.PregCompletadaPreguntaId = compId;
            created.Add(compId);
            changed = true;
        }

        return new MissionDialogRolesEnsureResult
        {
            Success = true,
            Changed = changed,
            CreatedQuestionIds = created,
        };
    }

    /// <summary>
    /// Seeds all dialog roles when empty for the current NPC context.
    /// </summary>
    public static bool SeedDialogRolesIfEmpty(
        MissionDraft mission,
        NpcsModeloDraft ownerNpc,
        ContentDraftWorkspace workspace,
        ContentDbCatalogSnapshot? catalog = null) =>
        EnsureAllDialogRolesIfEmpty(mission, ownerNpc, workspace, catalog).Changed;

    /// <summary>
    /// Questions for role ComboBoxes: NPC filter + any already-assigned role IDs (stable by Id).
    /// </summary>
    public static IReadOnlyList<DialogQuestionDraft> BuildQuestionChoices(
        DialogDraftBatch dialogs,
        int? startNpcId,
        int? pregDarId,
        int? pregIncId,
        int? pregCompId)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var byId = new Dictionary<int, DialogQuestionDraft>();
        if (startNpcId is int npcId)
        {
            foreach (var q in dialogs.QuestionsForNpc(npcId))
                byId[q.Id] = q;
        }

        foreach (var qid in new[] { pregDarId, pregIncId, pregCompId })
        {
            if (qid is not int id) continue;
            if (dialogs.FindQuestion(id) is { } q)
                byId[id] = q;
        }

        return byId.Values.OrderBy(q => q.Id).ToList();
    }
}
