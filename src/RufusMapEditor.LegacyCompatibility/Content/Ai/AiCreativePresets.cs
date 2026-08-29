namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.1 — preset labels for the creative assistant UI.</summary>
public static class AiCreativePresets
{
    public const string RoleCustomLabel = "Personalizado";
    public const string AttitudeCustomLabel = "Personalizada";

    public static IReadOnlyList<string> Roles { get; } =
    [
        "Guardia",
        "Minero",
        "Pescador",
        "Mercader",
        "Ermitaño",
        "Aventurero",
        RoleCustomLabel
    ];

    public static IReadOnlyList<string> Attitudes { get; } =
    [
        "Amable",
        "Gruñón",
        "Sarcástico",
        "Desconfiado",
        "Excéntrico",
        "Cobarde",
        "Arrogante",
        "Misterioso",
        "Nervioso",
        "Entusiasta",
        "Hostil",
        "Melancólico",
        AttitudeCustomLabel
    ];

    public static IReadOnlyList<AiTextLength> Lengths { get; } =
    [
        AiTextLength.Corta,
        AiTextLength.Media,
        AiTextLength.Larga
    ];
}
