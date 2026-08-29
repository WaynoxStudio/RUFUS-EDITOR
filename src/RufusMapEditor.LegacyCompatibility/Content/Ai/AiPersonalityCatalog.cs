namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.2 — centralized personality catalog. Edit here; never in XAML or button handlers.
/// </summary>
public static class AiPersonalityCatalog
{
    private static readonly IReadOnlyDictionary<string, AiPersonalityProfile> ByLabel =
        new Dictionary<string, AiPersonalityProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Amable"] = new(
                "Amable",
                "Cercano, cordial y dispuesto a ayudar. Puede bromear de forma ligera. " +
                "No debe sonar excesivamente formal ni empalagoso."),
            ["Gruñón"] = new(
                "Gruñón",
                "Protestón, impaciente y seco. Se queja con facilidad. " +
                "Puede ayudar igualmente, aunque parezca que le molesta hacerlo."),
            ["Sarcástico"] = new(
                "Sarcástico",
                "Utiliza ironía ligera y comentarios mordaces. " +
                "Puede burlarse de la situación o del jugador sin resultar ofensivo constantemente."),
            ["Desconfiado"] = new(
                "Desconfiado",
                "Sospecha de las intenciones del jugador. Pregunta, duda y evita revelar información demasiado rápido. " +
                "Puede terminar colaborando."),
            ["Excéntrico"] = new(
                "Excéntrico",
                "Tiene ideas, costumbres o formas de hablar peculiares. Puede obsesionarse con detalles absurdos. " +
                "Humor extraño pero comprensible."),
            ["Cobarde"] = new(
                "Cobarde",
                "Temeroso, inseguro y preocupado por peligros reales o imaginarios. Puede exagerar amenazas. " +
                "Evita hacerse el héroe."),
            ["Arrogante"] = new(
                "Arrogante",
                "Se considera superior, experto o especialmente importante. Habla con seguridad y cierta condescendencia. " +
                "No convertirlo automáticamente en villano."),
            ["Misterioso"] = new(
                "Misterioso",
                "Sugiere más de lo que explica. Utiliza frases indirectas y deja pequeñas incógnitas. " +
                "No debe convertirse en texto críptico incomprensible."),
            ["Nervioso"] = new(
                "Nervioso",
                "Apresurado, inseguro o fácilmente alterable. Puede interrumpirse, corregirse o preocuparse. " +
                "Mantener el texto legible."),
            ["Entusiasta"] = new(
                "Entusiasta",
                "Energético y emocionado por su oficio, objetivo o situación. Habla con ganas. " +
                "No abusar de mayúsculas ni exclamaciones."),
            ["Hostil"] = new(
                "Hostil",
                "Seco, poco receptivo y claramente incómodo con el jugador. Puede mostrar rechazo o desprecio. " +
                "No utilizar insultos extremos gratuitamente."),
            ["Melancólico"] = new(
                "Melancólico",
                "Nostálgico, cansado o reflexivo. Puede recordar tiempos mejores. " +
                "Mantener cierta ligereza si el contexto lo permite.")
        };

    /// <summary>All predefined (non-custom) personality profiles.</summary>
    public static IReadOnlyCollection<AiPersonalityProfile> Presets => ByLabel.Values.ToArray();

    public static bool TryGetPreset(string? label, out AiPersonalityProfile profile)
    {
        profile = null!;
        if (string.IsNullOrWhiteSpace(label))
            return false;
        if (string.Equals(label.Trim(), AiCreativePresets.AttitudeCustomLabel, StringComparison.OrdinalIgnoreCase))
            return false;
        return ByLabel.TryGetValue(label.Trim(), out profile!);
    }

    /// <summary>
    /// Resolves AI.1 attitude field: known preset label → catalog guidance;
    /// otherwise treat as custom text and keep the user's meaning unchanged.
    /// </summary>
    public static AiPersonalityProfile Resolve(string? attitudeFromRequest)
    {
        var text = (attitudeFromRequest ?? "").Trim();
        if (TryGetPreset(text, out var preset))
            return preset;

        return new AiPersonalityProfile(
            AiCreativePresets.AttitudeCustomLabel,
            text,
            isCustom: true);
    }
}
