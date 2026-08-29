using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.2 — transforms <see cref="AiCreativeRequest"/> into a reusable prompt package.
/// No WPF, no HTTP, no invented creative output.
/// </summary>
public static class AiPromptComposer
{
    public static AiPromptPackage Compose(AiCreativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var personality = AiPersonalityCatalog.Resolve(request.Attitude);
        var lengthGuidance = DescribeLength(request.Length, request.Action);
        var master = AiMasterPrompt.FullMasterInstructions;
        var dynamicContext = BuildDynamicContext(request, personality, lengthGuidance);
        var task = BuildTask(request);
        var full = JoinBlocks(master, dynamicContext, task);

        return new AiPromptPackage
        {
            SourceRequest = request,
            MasterInstructions = master,
            DynamicContext = dynamicContext,
            TaskInstructions = task,
            FullPrompt = full,
            TaskSummary = AiCreativeRequestPreview.FormatAction(request.Action),
            RoleSummary = Blank(request.Role),
            PersonalityLabel = personality.Label,
            PersonalityGuidance = Blank(personality.InternalGuidance),
            ContextSummary = Blank(request.NarrativeContext),
            LengthSummary = AiCreativeRequestPreview.FormatLength(request.Length) + " — " + lengthGuidance,
            AdditionalInstructionSummary = Blank(request.AdditionalInstruction),
            MasterRulesSummary = SummarizeMasterRules()
        };
    }

    private static string BuildDynamicContext(
        AiCreativeRequest request,
        AiPersonalityProfile personality,
        string lengthGuidance)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CONTEXTO DINÁMICO ===");
        sb.AppendLine();
        sb.AppendLine("Estilo de proyecto:");
        sb.AppendLine(Blank(request.Style));
        sb.AppendLine();
        sb.AppendLine("Rol / profesión:");
        sb.AppendLine(Blank(request.Role));
        sb.AppendLine(
            "Usa el rol como contexto creativo: vocabulario, preocupaciones, referencias y posibles juegos de palabras " +
            "ligados al oficio. No inventes diccionarios técnicos ni datos de juego.");
        sb.AppendLine();
        sb.AppendLine("Personalidad:");
        if (personality.IsCustom)
        {
            sb.AppendLine("Personalizada (texto del usuario — no alterar su significado):");
            sb.AppendLine(Blank(personality.InternalGuidance));
            sb.AppendLine("Mantén esta personalidad de forma consistente en todo el contenido generado.");
        }
        else
        {
            sb.AppendLine(personality.Label + ":");
            sb.AppendLine(personality.InternalGuidance);
            sb.AppendLine("Mantén esta personalidad de forma consistente en todo el contenido generado.");
        }

        sb.AppendLine();
        sb.AppendLine("Contexto / ubicación narrativa (prioridad sobre generalidades):");
        sb.AppendLine(Blank(request.NarrativeContext));
        sb.AppendLine(
            "Refleja esta situación. Es contexto narrativo, no Map ID ni dato técnico.");
        sb.AppendLine();
        sb.AppendLine("Longitud:");
        sb.AppendLine(AiCreativeRequestPreview.FormatLength(request.Length));
        sb.AppendLine(lengthGuidance);
        sb.AppendLine();
        sb.AppendLine("Instrucción adicional del usuario (respetarla si no contradice las reglas maestras):");
        sb.AppendLine(Blank(request.AdditionalInstruction));

        if (!string.IsNullOrWhiteSpace(request.CurrentNpcName))
        {
            sb.AppendLine();
            sb.AppendLine("Nombre actual del NPC (si aplica a la tarea):");
            sb.AppendLine(request.CurrentNpcName.Trim());
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildTask(AiCreativeRequest request) => request.Action switch
    {
        AiCreativeAction.GenerarNombre => BuildNameTask(),
        AiCreativeAction.GenerarDialogo => BuildDialogTask(request),
        AiCreativeAction.GenerarConversacion => BuildConversationTask(request),
        _ => "Tarea desconocida. Genera solo contenido creativo."
    };

    private static string BuildNameTask()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TAREA: GENERAR NOMBRE ===");
        sb.AppendLine();
        sb.AppendLine("Propón exactamente 3 nombres diferentes para el NPC.");
        sb.AppendLine("Requisitos:");
        sb.AppendLine("- Nombres memorables, no genéricos (evitar «Guardia Pedro» y similares).");
        sb.AppendLine("- Juegos de palabras cuando encajen con rol/contexto y tono RUFUS / DOFUS Retro.");
        sb.AppendLine("- Relacionados con el oficio y el contexto narrativo.");
        sb.AppendLine("- No excesivamente largos.");
        sb.AppendLine("- No incluir IDs ni datos técnicos.");
        sb.AppendLine("- No añadir explicaciones largas: solo las 3 propuestas (lista breve).");
        sb.AppendLine("La longitud solicitada NO debe alargar artificialmente los nombres.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildDialogTask(AiCreativeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TAREA: GENERAR DIÁLOGO ===");
        sb.AppendLine();
        sb.AppendLine("Genera el texto hablado por el NPC (contenido creativo de diálogo).");
        if (!string.IsNullOrWhiteSpace(request.CurrentNpcName))
            sb.AppendLine("El NPC se llama: " + request.CurrentNpcName.Trim() + ".");
        sb.AppendLine("Respeta rol, personalidad, contexto narrativo, longitud e instrucción adicional.");
        sb.AppendLine("NO generar: IDs, acciones, D.q, D.a, columnas BD ni datos técnicos.");
        sb.AppendLine("Si el flujo es diálogo simple: solo el contenido hablado por el NPC.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildConversationTask(AiCreativeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== TAREA: GENERAR CONVERSACIÓN ===");
        sb.AppendLine();
        sb.AppendLine("Genera contenido creativo de conversación con:");
        sb.AppendLine("1) Un texto inicial del NPC (apertura).");
        sb.AppendLine("2) Exactamente 3 respuestas posibles del jugador:");
        sb.AppendLine("   - una neutra");
        sb.AppendLine("   - una amable / interesada");
        sb.AppendLine("   - una humorística o desafiante cuando encaje");
        sb.AppendLine("Las respuestas del jugador pueden tener humor y personalidad.");
        sb.AppendLine("Deja margen para una continuación coherente si se solicita después.");
        if (!string.IsNullOrWhiteSpace(request.CurrentNpcName))
            sb.AppendLine("El NPC se llama: " + request.CurrentNpcName.Trim() + ".");
        sb.AppendLine("NO asignar acciones técnicas, args, IDs ni datos BD a las respuestas.");
        return sb.ToString().TrimEnd();
    }

    private static string DescribeLength(AiTextLength length, AiCreativeAction action)
    {
        // Length mainly targets dialogs/conversations, not names.
        if (action == AiCreativeAction.GenerarNombre)
            return "La longitud no fuerza nombres más largos; prioriza memorabilidad y brevedad.";

        return length switch
        {
            AiTextLength.Corta =>
                "Frases concisas, estilo diálogo normal de NPC.",
            AiTextLength.Media =>
                "Algo más de desarrollo, sin convertirse en un párrafo enorme.",
            AiTextLength.Larga =>
                "Mayor desarrollo narrativo, manteniendo diálogo natural.",
            _ => "Frases concisas, estilo diálogo normal de NPC."
        };
    }

    private static string SummarizeMasterRules() =>
        "RUFUS Retro · tono DOFUS Retro · juegos de palabras · " +
        "solo contenido creativo · prohibido inventar IDs/acciones/Map/Cell/GFX/BD/SWF.";

    private static string JoinBlocks(params string[] blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
            sb.Append(block.Trim());
        }
        return sb.ToString();
    }

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(vacío)" : value.Trim();
}
