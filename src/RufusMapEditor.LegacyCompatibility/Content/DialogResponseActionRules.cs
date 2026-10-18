namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// DIALOGFLOW.3 — per-response friendly action uniqueness and confirmed incompatibilities (0 ↔ 1).
/// </summary>
public static class DialogResponseActionRules
{
    public static readonly int[] FriendlyCatalog =
    {
        DialogActionCodes.Teleport,
        DialogActionCodes.GotoQuestion,
        DialogActionCodes.StartQuest,
        DialogActionCodes.ConfirmObjective,
        DialogActionCodes.ConfirmMission,
    };

    public static bool IsFriendlyCatalogAction(int accion) =>
        FriendlyCatalog.Contains(accion);

    public static bool CanAddAction(DialogResponseDraft response, int accion)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (IsFriendlyCatalogAction(accion) && response.Actions.Any(a => a.Accion == accion))
            return false;

        if (accion == DialogActionCodes.Teleport
            && response.Actions.Any(a => a.Accion == DialogActionCodes.GotoQuestion))
            return false;

        if (accion == DialogActionCodes.GotoQuestion
            && response.Actions.Any(a => a.Accion == DialogActionCodes.Teleport))
            return false;

        return true;
    }

    public static string DescribeAddBlock(DialogResponseDraft response, int accion)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (IsFriendlyCatalogAction(accion) && response.Actions.Any(a => a.Accion == accion))
            return $"Esta respuesta ya tiene la acción «{FormatFriendlyName(accion)}».";

        if (accion == DialogActionCodes.Teleport
            && response.Actions.Any(a => a.Accion == DialogActionCodes.GotoQuestion))
            return "No se puede añadir Teletransporte: la respuesta ya continúa a otra pregunta.";

        if (accion == DialogActionCodes.GotoQuestion
            && response.Actions.Any(a => a.Accion == DialogActionCodes.Teleport))
            return "No se puede añadir Ir a otra pregunta: la respuesta ya tiene Teletransporte.";

        return "No se puede añadir esta acción en la respuesta actual.";
    }

    public static IReadOnlyList<string> ValidateResponse(DialogResponseDraft response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var errors = new List<string>();

        foreach (var code in FriendlyCatalog)
        {
            if (response.Actions.Count(a => a.Accion == code) > 1)
                errors.Add($"Esta respuesta contiene acciones duplicadas: {FormatFriendlyName(code)}.");
        }

        var hasTeleport = response.Actions.Any(a => a.Accion == DialogActionCodes.Teleport);
        var hasGoto = response.Actions.Any(a => a.Accion == DialogActionCodes.GotoQuestion);
        if (hasTeleport && hasGoto)
        {
            errors.Add(
                "Esta respuesta contiene acciones incompatibles: Ir a otra pregunta y Teletransporte.");
        }

        return errors;
    }

    public static string FormatFriendlyName(int accion) => accion switch
    {
        DialogActionCodes.Teleport => "Teletransporte",
        DialogActionCodes.GotoQuestion => "Ir a otra pregunta",
        DialogActionCodes.StartQuest => "Iniciar misión",
        DialogActionCodes.ConfirmObjective => "Confirmar objetivo",
        DialogActionCodes.ConfirmMission => "Confirmar misión",
        _ => $"Acción (ID {accion})",
    };
}
