namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.1 — creative fields the user may ask the assistant to use.
/// Intentionally excludes NPC/Quest/Item/Map/Cell IDs, accion, args, and technical conditions.
/// </summary>
public sealed class AiCreativeRequest
{
    public AiCreativeAction Action { get; init; }

    /// <summary>Resolved role / profession text (preset or custom).</summary>
    public string Role { get; init; } = "";

    /// <summary>Resolved attitude / personality text (preset or custom).</summary>
    public string Attitude { get; init; } = "";

    /// <summary>Narrative location/context — not Map ID.</summary>
    public string NarrativeContext { get; init; } = "";

    /// <summary>Optional extra creative instruction.</summary>
    public string AdditionalInstruction { get; init; } = "";

    /// <summary>Affects generated text length only. RUFUS default: Corta.</summary>
    public AiTextLength Length { get; init; } = AiTextLength.Corta;

    /// <summary>Fixed project style for the future master prompt.</summary>
    public string Style { get; init; } = AiCreativeStyle.RufusDofusRetro;

    /// <summary>
    /// Current NPC display name when known (creative context only).
    /// Never an NPC ID.
    /// </summary>
    public string CurrentNpcName { get; init; } = "";
}
