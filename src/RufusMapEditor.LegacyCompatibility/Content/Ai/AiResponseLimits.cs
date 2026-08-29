namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.3 — UI/safety length ceilings. Do not silently truncate; reject instead.</summary>
public static class AiResponseLimits
{
    public const int MaxNameLength = 60;
    public const int MaxMotivoLength = 180;
    /// <summary>Spoken NPC text (diálogo / apertura). Media/Larga must stay within this ceiling.</summary>
    public const int MaxDialogueLength = 500;
    public const int MaxPlayerReplyLength = 300;

    public const int ExactNameCount = 3;
    public const int ExactPlayerReplyCount = 3;
}
