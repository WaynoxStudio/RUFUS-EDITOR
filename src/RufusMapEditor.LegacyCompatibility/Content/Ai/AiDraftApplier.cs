namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.5 — outcome of applying creative AI text to a local NPC draft (no BD/IDs).</summary>
public enum AiDraftApplyKind
{
    Applied,
    NeedsConfirmation,
    NeedsInteractiveSwitch,
    WrongNpc,
    NoSelection,
    Invalid
}

public sealed class AiDraftApplyResult
{
    public AiDraftApplyKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string? ProposedValue { get; init; }

    public static AiDraftApplyResult Applied(string message) =>
        new() { Kind = AiDraftApplyKind.Applied, Message = message };

    public static AiDraftApplyResult NeedsConfirmation(string message, string? proposed = null) =>
        new() { Kind = AiDraftApplyKind.NeedsConfirmation, Message = message, ProposedValue = proposed };

    public static AiDraftApplyResult NeedsInteractiveSwitch(string message) =>
        new() { Kind = AiDraftApplyKind.NeedsInteractiveSwitch, Message = message };

    public static AiDraftApplyResult WrongNpc(string message) =>
        new() { Kind = AiDraftApplyKind.WrongNpc, Message = message };

    public static AiDraftApplyResult NoSelection() =>
        new() { Kind = AiDraftApplyKind.NoSelection, Message = "No hay NPC seleccionado." };

    public static AiDraftApplyResult Invalid(string message) =>
        new() { Kind = AiDraftApplyKind.Invalid, Message = message };
}

/// <summary>
/// AI.5 — applies creative AI results to in-memory NPC/dialog drafts only.
/// Never writes BD, SWF, SFTP, or invents technical IDs beyond existing draft helpers.
/// </summary>
public static class AiDraftApplier
{
    public static AiDraftApplyResult ApplyName(
        NpcsModeloDraft? npc,
        string nombre,
        bool replaceConfirmed,
        int? expectedNpcId)
    {
        if (npc is null) return AiDraftApplyResult.NoSelection();
        if (expectedNpcId is int exp && npc.Id != exp)
            return AiDraftApplyResult.WrongNpc(
                "El resultado IA pertenece a otro NPC. Cambia de NPC o vuelve a generar.");

        var name = (nombre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return AiDraftApplyResult.Invalid("Nombre vacío.");
        if (name.Length > AiResponseLimits.MaxNameLength)
            return AiDraftApplyResult.Invalid($"Nombre supera {AiResponseLimits.MaxNameLength} caracteres.");

        // ADMIN.UI.3.2 — "Usar" applies immediately (replaceConfirmed kept for API compat; unused).
        _ = replaceConfirmed;

        npc.Nombre = name;
        return AiDraftApplyResult.Applied("✓ Nombre aplicado al borrador");
    }

    public static AiDraftApplyResult ApplyDialogue(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft? npc,
        string texto,
        bool replaceConfirmed,
        int? expectedNpcId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (npc is null) return AiDraftApplyResult.NoSelection();
        if (expectedNpcId is int exp && npc.Id != exp)
            return AiDraftApplyResult.WrongNpc(
                "El resultado IA pertenece a otro NPC. Cambia de NPC o vuelve a generar.");

        var text = (texto ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return AiDraftApplyResult.Invalid("Texto de diálogo vacío.");
        if (text.Length > AiResponseLimits.MaxDialogueLength)
            return AiDraftApplyResult.Invalid($"Diálogo supera {AiResponseLimits.MaxDialogueLength} caracteres.");

        if (npc.DialogMode == NpcDialogMode.Simple)
        {
            var current = (npc.SimpleDialogTextLocal ?? "").Trim();
            if (!string.IsNullOrEmpty(current)
                && !string.Equals(current, text, StringComparison.Ordinal)
                && !replaceConfirmed)
            {
                return AiDraftApplyResult.NeedsConfirmation(
                    "Este NPC ya tiene un diálogo. ¿Quieres sustituirlo?",
                    text);
            }

            npc.SimpleDialogTextLocal = text;
            return AiDraftApplyResult.Applied("✓ Diálogo aplicado al borrador");
        }

        // Interactive: apply to initial question spoken text only.
        var q = EnsureInitialQuestion(workspace, npc);
        var currentQ = (q.TextLocal ?? "").Trim();
        if (!string.IsNullOrEmpty(currentQ)
            && !string.Equals(currentQ, text, StringComparison.Ordinal)
            && !replaceConfirmed)
        {
            return AiDraftApplyResult.NeedsConfirmation(
                "Este NPC ya tiene un diálogo. ¿Quieres sustituirlo?",
                text);
        }

        q.TextLocal = text;
        return AiDraftApplyResult.Applied("✓ Diálogo aplicado al borrador");
    }

    /// <summary>Writes spoken text to one NPC question bubble. Does not touch params/alternos/IDs.</summary>
    public static AiDraftApplyResult ApplyQuestionText(DialogQuestionDraft? question, string texto)
    {
        if (question is null)
            return AiDraftApplyResult.NoSelection();

        var text = (texto ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return AiDraftApplyResult.Invalid("Texto de diálogo vacío.");
        if (text.Length > AiResponseLimits.MaxDialogueLength)
            return AiDraftApplyResult.Invalid($"Diálogo supera {AiResponseLimits.MaxDialogueLength} caracteres.");

        question.TextLocal = text;
        return AiDraftApplyResult.Applied("✓ Texto aplicado al borrador");
    }

    /// <summary>Writes spoken text to one player reply. Does not touch actions/args/condiciones.</summary>
    public static AiDraftApplyResult ApplyResponseText(DialogResponseDraft? response, string texto)
    {
        if (response is null)
            return AiDraftApplyResult.NoSelection();

        var text = (texto ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return AiDraftApplyResult.Invalid("Texto de diálogo vacío.");
        if (text.Length > AiResponseLimits.MaxPlayerReplyLength)
            return AiDraftApplyResult.Invalid($"Respuesta supera {AiResponseLimits.MaxPlayerReplyLength} caracteres.");

        response.TextLocal = text;
        return AiDraftApplyResult.Applied("✓ Texto aplicado al borrador");
    }

    public static AiDraftApplyResult ApplyConversation(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft? npc,
        AiConversationResult conversation,
        bool replaceConfirmed,
        bool interactiveSwitchConfirmed,
        int? expectedNpcId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(conversation);
        if (npc is null) return AiDraftApplyResult.NoSelection();
        if (expectedNpcId is int exp && npc.Id != exp)
            return AiDraftApplyResult.WrongNpc(
                "El resultado IA pertenece a otro NPC. Cambia de NPC o vuelve a generar.");

        var opening = (conversation.TextoNpc ?? "").Trim();
        if (string.IsNullOrWhiteSpace(opening))
            return AiDraftApplyResult.Invalid("Apertura NPC vacía.");
        var incomplete = (conversation.TextoIncompleta ?? "").Trim();
        if (string.IsNullOrWhiteSpace(incomplete))
            return AiDraftApplyResult.Invalid("Texto de misión incompleta vacío.");
        var completed = (conversation.TextoCompletada ?? "").Trim();
        if (string.IsNullOrWhiteSpace(completed))
            return AiDraftApplyResult.Invalid("Texto de misión completada vacío.");
        if (conversation.RespuestasJugador is null
            || conversation.RespuestasJugador.Count != AiResponseLimits.ExactPlayerReplyCount)
            return AiDraftApplyResult.Invalid(
                $"Se requiere exactamente {AiResponseLimits.ExactPlayerReplyCount} respuesta del jugador.");

        if (npc.DialogMode == NpcDialogMode.Simple && !interactiveSwitchConfirmed)
        {
            return AiDraftApplyResult.NeedsInteractiveSwitch(
                "La conversación generada contiene respuestas del jugador y necesita el modo interactivo. ¿Quieres utilizarla como conversación interactiva?");
        }

        if (HasCreativeInteractiveContent(workspace, npc) && !replaceConfirmed)
        {
            return AiDraftApplyResult.NeedsConfirmation(
                "Este NPC ya contiene diálogo/respuestas. Aplicar la conversación IA sustituirá el texto creativo actual.");
        }

        if (npc.DialogMode == NpcDialogMode.Simple)
        {
            // Safe transition: same as editor Interactive radio — drop simple drafts owned by this NPC.
            var ownedIds = workspace.Dialogs.QuestionsForNpc(npc.Id).Select(q => q.Id).ToHashSet();
            workspace.Dialogs.RemoveQuestionsForNpc(npc.Id);
            foreach (var id in ownedIds)
                workspace.Missions.ClearPreguntaReferences(id);
            if (ownedIds.Contains(npc.Pregunta))
                npc.Pregunta = 0;
            npc.DialogMode = NpcDialogMode.Interactive;
            npc.SimpleDialogTextLocal = "";
        }

        var mission = AiMissionRoleContextBuilder.ResolveMission(workspace, npc);

        // OFRECER → PregDar if configured, else initial question (do not invent role links).
        var offerQuestion = ResolveRoleQuestion(workspace, mission?.PregDarPreguntaId)
                            ?? EnsureInitialQuestion(workspace, npc);
        offerQuestion.TextLocal = opening;

        for (var i = 0; i < AiResponseLimits.ExactPlayerReplyCount; i++)
        {
            var suggestion = conversation.RespuestasJugador[i];
            var reply = (suggestion.Texto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reply))
                return AiDraftApplyResult.Invalid($"Respuesta jugador {i + 1} vacía.");

            DialogResponseDraft response;
            if (i < offerQuestion.Responses.Count)
                response = offerQuestion.Responses[i];
            else
                response = workspace.Dialogs.AddResponse(offerQuestion);

            // Creative text only — preserve existing actions/args/conditions.
            response.TextLocal = reply;
        }

        // DIALOG.AI.AUTO_APPLY.SINGLE_REPLY.2 — keep a single player reply on the offer block.
        while (offerQuestion.Responses.Count > AiResponseLimits.ExactPlayerReplyCount)
        {
            var extra = offerQuestion.Responses[^1];
            workspace.Dialogs.RemoveResponse(offerQuestion, extra);
        }

        if (ResolveRoleQuestion(workspace, mission?.PregIncompletaPreguntaId) is { } incompleteQ)
            incompleteQ.TextLocal = incomplete;

        if (ResolveRoleQuestion(workspace, mission?.PregCompletadaPreguntaId) is { } completedQ)
            completedQ.TextLocal = completed;

        return AiDraftApplyResult.Applied("✓ Conversación aplicada al borrador (ofrecer / respuesta / incompleta / completada)");
    }

    private static DialogQuestionDraft? ResolveRoleQuestion(ContentDraftWorkspace workspace, int? questionId)
    {
        if (questionId is not int id || id <= 0)
            return null;
        return workspace.Dialogs.FindQuestion(id);
    }

    public static bool HasCreativeInteractiveContent(ContentDraftWorkspace workspace, NpcsModeloDraft npc)
    {
        if (npc.DialogMode == NpcDialogMode.Simple)
            return !string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal);

        var mission = AiMissionRoleContextBuilder.ResolveMission(workspace, npc);
        foreach (var id in new int?[]
                 {
                     npc.Pregunta > 0 ? npc.Pregunta : null,
                     mission?.PregDarPreguntaId,
                     mission?.PregIncompletaPreguntaId,
                     mission?.PregCompletadaPreguntaId
                 })
        {
            if (id is not int qid) continue;
            var q = workspace.Dialogs.FindQuestion(qid);
            if (q is null) continue;
            if (!string.IsNullOrWhiteSpace(q.TextLocal)) return true;
            if (q.Responses.Any(r => !string.IsNullOrWhiteSpace(r.TextLocal))) return true;
        }

        return false;
    }

    private static DialogQuestionDraft EnsureInitialQuestion(ContentDraftWorkspace workspace, NpcsModeloDraft npc)
    {
        var existing = workspace.Dialogs.FindQuestion(npc.Pregunta);
        if (existing is not null && existing.OwnerNpcId == npc.Id)
            return existing;

        var q = workspace.Dialogs.CreateQuestion(npc.Id);
        workspace.Dialogs.SetInitialQuestion(npc, q.Id);
        return q;
    }
}
