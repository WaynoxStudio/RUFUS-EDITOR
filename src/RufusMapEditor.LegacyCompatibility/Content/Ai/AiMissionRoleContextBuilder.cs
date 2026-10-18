using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// DIALOG.AI.MISSION_ROLES.CONTEXT.1 — creative-only mission role labels for AI prompts.
/// Source of truth: <see cref="MissionDialogRolesUi.ResolveRoleTag"/> (PregDar / PregInc / PregComp).
/// No IDs, actions, or BD fields in output.
/// </summary>
public static class AiMissionRoleContextBuilder
{
    public const string SemanticOffer = "OFRECER";
    public const string SemanticIncomplete = "INCOMPLETA";
    public const string SemanticComplete = "COMPLETADA";
    public const string SemanticPlayerReply = "RESPUESTA DEL JUGADOR";

    public const string MeaningOffer =
        "OFRECER: el NPC presenta o proporciona la misión al jugador.";

    public const string MeaningIncomplete =
        "INCOMPLETA: el jugador todavía NO ha terminado la misión; el NPC habla en ese estado.";

    public const string MeaningComplete =
        "COMPLETADA: el jugador YA ha cumplido la misión y el NPC reacciona al resultado.";

    public const string MeaningPlayerReply =
        "RESPUESTA DEL JUGADOR: texto hablado por el jugador, no por el NPC.";

    /// <summary>Same mission resolution as canvas tags: linked acción 44, else unambiguous StartNpcId.</summary>
    public static MissionDraft? ResolveMission(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogActionDraft? preferredStartQuestAction = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var action = preferredStartQuestAction ?? FindFirstLinkedStartQuestAction(workspace, npc.Id);
        return StartQuestLinkUi.ResolveMissionSourceOfTruth(action, workspace.Missions, npc.Id);
    }

    public static string? ResolveSemanticRole(MissionDraft? mission, int questionId)
    {
        if (mission is null)
            return null;
        var tag = MissionDialogRolesUi.ResolveRoleTag(mission, questionId);
        return ToSemantic(tag);
    }

    public static string? ToSemantic(string? missionRoleTag) => missionRoleTag switch
    {
        MissionDialogRolesUi.TagOffer => SemanticOffer,
        MissionDialogRolesUi.TagIncomplete => SemanticIncomplete,
        MissionDialogRolesUi.TagComplete => SemanticComplete,
        _ => null
    };

    public static string BuildRoleGlossary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Roles semánticos de diálogo de misión (solo contexto creativo):");
        sb.AppendLine(MeaningOffer);
        sb.AppendLine(MeaningIncomplete);
        sb.AppendLine(MeaningComplete);
        sb.AppendLine(MeaningPlayerReply);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Spoken snapshot with role labels for GenerateConversation.</summary>
    public static string BuildConversationSnapshotWithRoles(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        MissionDraft? mission)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var lines = new List<string>();
        foreach (var q in workspace.Dialogs.QuestionsForNpc(npc.Id).OrderBy(q => q.Id))
        {
            var role = ResolveSemanticRole(mission, q.Id);
            var npcLabel = role is null ? "NPC" : "NPC / Rol misión: " + role;
            var text = string.IsNullOrWhiteSpace(q.TextLocal) ? "(sin texto)" : q.TextLocal.Trim();
            lines.Add(npcLabel + Environment.NewLine + text);

            foreach (var r in q.Responses)
            {
                var reply = string.IsNullOrWhiteSpace(r.TextLocal) ? "(sin texto)" : r.TextLocal.Trim();
                lines.Add("Jugador / Rol: " + SemanticPlayerReply + Environment.NewLine + reply);
            }
        }

        if (lines.Count == 0)
            return "";

        if (lines.Count > AiDialogThreadContextBuilder.MaxTurns)
            lines = lines.TakeLast(AiDialogThreadContextBuilder.MaxTurns).ToList();

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>Mission name + glossary for AdditionalInstruction (creative only).</summary>
    public static string BuildMissionCreativeBlock(MissionDraft? mission)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildRoleGlossary());
        if (mission is not null && !string.IsNullOrWhiteSpace(mission.Nombre))
        {
            sb.AppendLine();
            sb.AppendLine("Misión vinculada (solo contexto narrativo): " + mission.Nombre.Trim());
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Role hint for a single bubble generation (✨).</summary>
    public static string BuildBubbleRoleHint(
        MissionDraft? mission,
        DialogQuestionDraft? question,
        bool isPlayerReply)
    {
        if (isPlayerReply)
            return "Rol de este bocadillo: " + SemanticPlayerReply + ". " + MeaningPlayerReply;

        if (question is null)
            return "";

        var role = ResolveSemanticRole(mission, question.Id);
        if (role is null)
            return "";

        var meaning = role switch
        {
            SemanticOffer => MeaningOffer,
            SemanticIncomplete => MeaningIncomplete,
            SemanticComplete => MeaningComplete,
            _ => ""
        };

        return string.IsNullOrWhiteSpace(meaning)
            ? "Rol de este bocadillo: " + role + "."
            : "Rol de este bocadillo: " + role + ". " + meaning;
    }

    private static DialogActionDraft? FindFirstLinkedStartQuestAction(
        ContentDraftWorkspace workspace,
        int npcId)
    {
        foreach (var q in workspace.Dialogs.QuestionsForNpc(npcId))
        {
            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (a.Accion == DialogActionCodes.StartQuest && a.TargetMissionDraftId is not null)
                        return a;
                }
            }
        }

        return null;
    }
}
