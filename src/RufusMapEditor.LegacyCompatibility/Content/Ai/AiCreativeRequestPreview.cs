using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.1/AI.2 — human-readable preview of request + composed prompt (debug).</summary>
public static class AiCreativeRequestPreview
{
    public static string Format(AiCreativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Format(request, AiPromptComposer.Compose(request));
    }

    public static string Format(AiCreativeRequest request, AiPromptPackage package)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);

        var sb = new StringBuilder();
        sb.AppendLine("═══ SOLICITUD CREATIVA ═══");
        sb.AppendLine();
        sb.AppendLine("Acción:");
        sb.AppendLine(FormatAction(request.Action));
        sb.AppendLine();
        sb.AppendLine("Rol:");
        sb.AppendLine(Blank(request.Role));
        sb.AppendLine();
        sb.AppendLine("Actitud:");
        sb.AppendLine(Blank(request.Attitude));
        sb.AppendLine();
        sb.AppendLine("Contexto:");
        sb.AppendLine(Blank(request.NarrativeContext));
        sb.AppendLine();
        sb.AppendLine("Longitud:");
        sb.AppendLine(FormatLength(request.Length));
        sb.AppendLine();
        sb.AppendLine("Estilo:");
        sb.AppendLine(Blank(request.Style));
        sb.AppendLine();
        sb.AppendLine("Instrucción:");
        sb.AppendLine(Blank(request.AdditionalInstruction));

        if (request.Action is AiCreativeAction.GenerarDialogo or AiCreativeAction.GenerarConversacion
            || !string.IsNullOrWhiteSpace(request.CurrentNpcName))
        {
            sb.AppendLine();
            sb.AppendLine("Nombre NPC actual:");
            sb.AppendLine(Blank(request.CurrentNpcName));
        }

        sb.AppendLine();
        sb.AppendLine("═══ PROMPT COMPUESTO ═══");
        sb.AppendLine();
        sb.AppendLine("Tarea:");
        sb.AppendLine(package.TaskSummary);
        sb.AppendLine();
        sb.AppendLine("Rol:");
        sb.AppendLine(package.RoleSummary);
        sb.AppendLine();
        sb.AppendLine("Personalidad:");
        sb.AppendLine(package.PersonalityLabel);
        sb.AppendLine(package.PersonalityGuidance);
        sb.AppendLine();
        sb.AppendLine("Contexto:");
        sb.AppendLine(package.ContextSummary);
        sb.AppendLine();
        sb.AppendLine("Longitud:");
        sb.AppendLine(package.LengthSummary);
        sb.AppendLine();
        sb.AppendLine("Instrucción adicional:");
        sb.AppendLine(package.AdditionalInstructionSummary);
        sb.AppendLine();
        sb.AppendLine("Reglas maestras aplicadas:");
        sb.AppendLine(package.MasterRulesSummary);
        sb.AppendLine();
        sb.AppendLine("--- Prompt completo (debug) ---");
        sb.AppendLine(package.FullPrompt);

        return sb.ToString().TrimEnd();
    }

    public static string FormatAction(AiCreativeAction action) => action switch
    {
        AiCreativeAction.GenerarNombre => "Generar nombre",
        AiCreativeAction.GenerarDialogo => "Generar diálogo",
        AiCreativeAction.GenerarConversacion => "Generar conversación",
        _ => action.ToString()
    };

    public static string FormatLength(AiTextLength length) => length switch
    {
        AiTextLength.Corta => "Corta",
        AiTextLength.Media => "Media",
        AiTextLength.Larga => "Larga",
        _ => length.ToString()
    };

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(vacío)" : value.Trim();
}
