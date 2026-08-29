namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3 — descriptive player-reply tone only. Never mapped to npc_respuestas.accion.
/// </summary>
public static class AiPlayerTone
{
    public const string Neutral = "neutral";
    public const string Amable = "amable";
    public const string Humoristico = "humoristico";
    public const string Desafiante = "desafiante";

    public static IReadOnlyList<string> Allowed { get; } =
    [
        Neutral,
        Amable,
        Humoristico,
        Desafiante
    ];

    public static bool IsAllowed(string? tone) =>
        !string.IsNullOrWhiteSpace(tone)
        && Allowed.Contains(tone.Trim(), StringComparer.OrdinalIgnoreCase);
}
