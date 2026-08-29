using System.Text.Json;
using System.Text.Json.Nodes;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.3 — structural validation of AI responses before any UI application.
/// Invalid results are never partially applied.
/// </summary>
public static class AiResponseValidator
{
    private static readonly string[] ForbiddenTechnicalNames =
    [
        "npcId", "questionId", "responseId", "questId", "stageId", "itemId",
        "mapId", "cellId", "gfxId", "action", "args", "condition",
        "dQ", "dA", "dq", "da", "D.q", "D.a",
        "npc_id", "pregunta", "respuesta", "accion"
    ];

    /// <summary>Parse + validate JSON for the expected action. Returns a non-applied generation result.</summary>
    public static AiGenerationResult ParseAndValidate(AiCreativeAction action, string json)
    {
        if (ContainsForbiddenTechnicalFields(json, out var forbidden))
        {
            var err = $"Resultado IA inválido: campo técnico prohibido «{forbidden}».";
            AiResponseDebugLog.Log(action, ok: false, err, json);
            return AiGenerationResult.Invalid(action, err, json);
        }

        if (!AiResponseSerializer.TryDeserializeForAction(action, json, out var payload, out var serError))
        {
            var err = "Resultado IA inválido: " + (serError ?? "deserialización fallida.");
            AiResponseDebugLog.Log(action, ok: false, err, json);
            return AiGenerationResult.Invalid(action, err, json);
        }

        return payload switch
        {
            AiNameGenerationResponse names => ValidateNames(names, json),
            AiDialogueGenerationResponse dialogue => ValidateDialogue(dialogue, json),
            AiConversationGenerationResponse conversation => ValidateConversation(conversation, json),
            _ => Fail(action, "Tipo de respuesta incorrecto para la acción.", json)
        };
    }

    public static AiGenerationResult ValidateNames(AiNameGenerationResponse response, string? rawJson = null)
    {
        const AiCreativeAction action = AiCreativeAction.GenerarNombre;
        if (response.Nombres is null)
            return Fail(action, "Falta el array «nombres».", rawJson);

        if (response.Nombres.Count != AiResponseLimits.ExactNameCount)
            return Fail(action,
                $"Se esperaban exactamente {AiResponseLimits.ExactNameCount} nombres; hay {response.Nombres.Count}.",
                rawJson);

        for (var i = 0; i < response.Nombres.Count; i++)
        {
            var item = response.Nombres[i];
            if (item is null)
                return Fail(action, $"Nombre[{i}] nulo.", rawJson);
            if (string.IsNullOrWhiteSpace(item.Nombre))
                return Fail(action, $"Nombre[{i}] vacío.", rawJson);
            if (item.Nombre.Trim().Length > AiResponseLimits.MaxNameLength)
                return Fail(action,
                    $"Nombre[{i}] supera {AiResponseLimits.MaxNameLength} caracteres.",
                    rawJson);
            if (item.Motivo is { Length: > AiResponseLimits.MaxMotivoLength })
                return Fail(action,
                    $"Motivo[{i}] supera {AiResponseLimits.MaxMotivoLength} caracteres.",
                    rawJson);
        }

        var ok = AiGenerationResult.OkNames(response, rawJson);
        AiResponseDebugLog.Log(action, ok: true, null, rawJson);
        return ok;
    }

    public static AiGenerationResult ValidateDialogue(AiDialogueGenerationResponse response, string? rawJson = null)
    {
        const AiCreativeAction action = AiCreativeAction.GenerarDialogo;
        if (response.Dialogo is null)
            return Fail(action, "Falta «dialogo».", rawJson);
        if (string.IsNullOrWhiteSpace(response.Dialogo.Texto))
            return Fail(action, "Texto de diálogo vacío.", rawJson);
        if (response.Dialogo.Texto.Trim().Length > AiResponseLimits.MaxDialogueLength)
            return Fail(action,
                $"Diálogo supera {AiResponseLimits.MaxDialogueLength} caracteres.",
                rawJson);

        var ok = AiGenerationResult.OkDialogue(response, rawJson);
        AiResponseDebugLog.Log(action, ok: true, null, rawJson);
        return ok;
    }

    public static AiGenerationResult ValidateConversation(
        AiConversationGenerationResponse response,
        string? rawJson = null)
    {
        const AiCreativeAction action = AiCreativeAction.GenerarConversacion;
        var c = response.Conversacion;
        if (c is null)
            return Fail(action, "Falta «conversacion».", rawJson);
        if (string.IsNullOrWhiteSpace(c.TextoNpc))
            return Fail(action, "Apertura del NPC vacía.", rawJson);
        if (c.TextoNpc.Trim().Length > AiResponseLimits.MaxDialogueLength)
            return Fail(action,
                $"Apertura NPC supera {AiResponseLimits.MaxDialogueLength} caracteres.",
                rawJson);
        if (c.RespuestasJugador is null)
            return Fail(action, "Falta «respuestasJugador».", rawJson);
        if (c.RespuestasJugador.Count != AiResponseLimits.ExactPlayerReplyCount)
            return Fail(action,
                $"Se esperaban exactamente {AiResponseLimits.ExactPlayerReplyCount} respuestas; hay {c.RespuestasJugador.Count}.",
                rawJson);

        for (var i = 0; i < c.RespuestasJugador.Count; i++)
        {
            var r = c.RespuestasJugador[i];
            if (r is null)
                return Fail(action, $"Respuesta[{i}] nula.", rawJson);
            if (string.IsNullOrWhiteSpace(r.Texto))
                return Fail(action, $"Respuesta[{i}] vacía.", rawJson);
            if (r.Texto.Trim().Length > AiResponseLimits.MaxPlayerReplyLength)
                return Fail(action,
                    $"Respuesta[{i}] supera {AiResponseLimits.MaxPlayerReplyLength} caracteres.",
                    rawJson);
            if (!AiPlayerTone.IsAllowed(r.Tono))
                return Fail(action,
                    $"Respuesta[{i}] tono no permitido «{r.Tono}». Permitidos: {string.Join(", ", AiPlayerTone.Allowed)}.",
                    rawJson);
        }

        var ok = AiGenerationResult.OkConversation(response, rawJson);
        AiResponseDebugLog.Log(action, ok: true, null, rawJson);
        return ok;
    }

    private static AiGenerationResult Fail(AiCreativeAction action, string detail, string? rawJson)
    {
        var err = "Resultado IA inválido: " + detail;
        AiResponseDebugLog.Log(action, ok: false, err, rawJson);
        return AiGenerationResult.Invalid(action, err, rawJson);
    }

    /// <summary>Scans JSON property names for known technical fields (any nesting).</summary>
    public static bool ContainsForbiddenTechnicalFields(string json, out string? found)
    {
        found = null;
        try
        {
            var node = JsonNode.Parse(json);
            return ScanNode(node, out found);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ScanNode(JsonNode? node, out string? found)
    {
        found = null;
        if (node is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                if (IsForbidden(prop.Key))
                {
                    found = prop.Key;
                    return true;
                }
                if (ScanNode(prop.Value, out found))
                    return true;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (ScanNode(item, out found))
                    return true;
            }
        }
        return false;
    }

    private static bool IsForbidden(string name) =>
        ForbiddenTechnicalNames.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
}
