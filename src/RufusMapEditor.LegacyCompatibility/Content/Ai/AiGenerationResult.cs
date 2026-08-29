namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3 — validated generation result, separate from applying anything to an NPC draft.
/// Preview ← this. Application to draft is a future phase (Usar).
/// </summary>
public sealed class AiGenerationResult
{
    private AiGenerationResult(
        AiCreativeAction action,
        bool isValid,
        string? errorDetail,
        AiNameGenerationResponse? names,
        AiDialogueGenerationResponse? dialogue,
        AiConversationGenerationResponse? conversation,
        string? rawJson)
    {
        Action = action;
        IsValid = isValid;
        ErrorDetail = errorDetail;
        Names = names;
        Dialogue = dialogue;
        Conversation = conversation;
        RawJson = rawJson;
    }

    public AiCreativeAction Action { get; }
    public bool IsValid { get; }
    public string? ErrorDetail { get; }

    public AiNameGenerationResponse? Names { get; }
    public AiDialogueGenerationResponse? Dialogue { get; }
    public AiConversationGenerationResponse? Conversation { get; }

    /// <summary>Original JSON when available (debug). Never contains API keys.</summary>
    public string? RawJson { get; }

    public static AiGenerationResult OkNames(AiNameGenerationResponse response, string? rawJson = null) =>
        new(AiCreativeAction.GenerarNombre, true, null, response, null, null, rawJson);

    public static AiGenerationResult OkDialogue(AiDialogueGenerationResponse response, string? rawJson = null) =>
        new(AiCreativeAction.GenerarDialogo, true, null, null, response, null, rawJson);

    public static AiGenerationResult OkConversation(AiConversationGenerationResponse response, string? rawJson = null) =>
        new(AiCreativeAction.GenerarConversacion, true, null, null, null, response, rawJson);

    public static AiGenerationResult Invalid(AiCreativeAction action, string errorDetail, string? rawJson = null) =>
        new(action, false, errorDetail, null, null, null, rawJson);
}
