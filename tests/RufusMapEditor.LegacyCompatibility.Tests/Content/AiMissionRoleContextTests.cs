using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>DIALOG.AI.MISSION_ROLES.CONTEXT.1</summary>
public sealed class AiMissionRoleContextTests
{
    [Fact]
    public void ResolveSemanticRole_uses_MissionDialogRolesUi_tags()
    {
        var mission = new MissionDraft
        {
            PregDarPreguntaId = 10,
            PregIncompletaPreguntaId = 20,
            PregCompletadaPreguntaId = 30,
        };

        Assert.Equal(AiMissionRoleContextBuilder.SemanticOffer, AiMissionRoleContextBuilder.ResolveSemanticRole(mission, 10));
        Assert.Equal(AiMissionRoleContextBuilder.SemanticIncomplete, AiMissionRoleContextBuilder.ResolveSemanticRole(mission, 20));
        Assert.Equal(AiMissionRoleContextBuilder.SemanticComplete, AiMissionRoleContextBuilder.ResolveSemanticRole(mission, 30));
        Assert.Null(AiMissionRoleContextBuilder.ResolveSemanticRole(mission, 99));
        Assert.Equal(AiMissionRoleContextBuilder.SemanticOffer, AiMissionRoleContextBuilder.ToSemantic(MissionDialogRolesUi.TagOffer));
        Assert.Equal(AiMissionRoleContextBuilder.SemanticIncomplete, AiMissionRoleContextBuilder.ToSemantic(MissionDialogRolesUi.TagIncomplete));
        Assert.Equal(AiMissionRoleContextBuilder.SemanticComplete, AiMissionRoleContextBuilder.ToSemantic(MissionDialogRolesUi.TagComplete));
    }

    [Fact]
    public void Conversation_snapshot_and_glossary_distinguish_all_three_roles()
    {
        var ws = NewWorkspace(out var npc);
        var qOffer = ws.Dialogs.CreateQuestion(npc.Id);
        qOffer.TextLocal = "¿Me ayudas a investigar Astrub?";
        ws.Dialogs.SetInitialQuestion(npc, qOffer.Id);
        var rOffer = ws.Dialogs.AddResponse(qOffer);
        rOffer.TextLocal = "Claro, acepto.";
        var start = ws.Dialogs.AddAction(rOffer, DialogActionCodes.StartQuest);

        var qInc = ws.Dialogs.CreateQuestion(npc.Id);
        qInc.TextLocal = "Aún no has terminado la pesquisa.";
        var qComp = ws.Dialogs.CreateQuestion(npc.Id);
        qComp.TextLocal = "¡Bien hecho, Astrub respira otra vez!";

        var mission = ws.Missions.CreateMission();
        mission.Nombre = "Niebla de Astrub";
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = qOffer.Id;
        mission.PregIncompletaPreguntaId = qInc.Id;
        mission.PregCompletadaPreguntaId = qComp.Id;
        ws.Dialogs.LinkStartMission(start, mission.DraftId);

        var resolved = AiMissionRoleContextBuilder.ResolveMission(ws, npc);
        Assert.Same(mission, resolved);

        var snapshot = AiMissionRoleContextBuilder.BuildConversationSnapshotWithRoles(ws, npc, mission);
        Assert.Contains("Rol misión: " + AiMissionRoleContextBuilder.SemanticOffer, snapshot, StringComparison.Ordinal);
        Assert.Contains("Rol misión: " + AiMissionRoleContextBuilder.SemanticIncomplete, snapshot, StringComparison.Ordinal);
        Assert.Contains("Rol misión: " + AiMissionRoleContextBuilder.SemanticComplete, snapshot, StringComparison.Ordinal);
        Assert.Contains("Rol: " + AiMissionRoleContextBuilder.SemanticPlayerReply, snapshot, StringComparison.Ordinal);
        Assert.Contains("¿Me ayudas a investigar Astrub?", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(qOffer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("accion", snapshot, StringComparison.OrdinalIgnoreCase);

        var block = AiMissionRoleContextBuilder.BuildMissionCreativeBlock(mission);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningOffer, block, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningIncomplete, block, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningComplete, block, StringComparison.Ordinal);
        Assert.Contains("Niebla de Astrub", block, StringComparison.Ordinal);

        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarConversacion,
            rolePreset: "",
            customRole: "",
            attitudePreset: "Seco",
            customAttitude: null,
            narrativeContext: "RUFUS sospecha del bioma.",
            additionalInstruction: block,
            length: AiTextLength.Corta,
            currentNpcName: npc.Nombre,
            conversationPriorText: snapshot);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticOffer, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticIncomplete, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticComplete, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("Niebla de Astrub", package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("INCOMPLETA", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("textoIncompleta", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains(AiMissionSnapshotBuilder.AntiInventionRule, package.FullPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Bubble_hint_receives_correct_role_for_each_mission_question()
    {
        var mission = new MissionDraft
        {
            Nombre = "Prueba",
            PregDarPreguntaId = 1,
            PregIncompletaPreguntaId = 2,
            PregCompletadaPreguntaId = 3,
        };
        var qOffer = new DialogQuestionDraft { Id = 1, TextLocal = "oferta" };
        var qInc = new DialogQuestionDraft { Id = 2, TextLocal = "inc" };
        var qComp = new DialogQuestionDraft { Id = 3, TextLocal = "comp" };

        var offer = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, qOffer, isPlayerReply: false);
        var incomplete = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, qInc, isPlayerReply: false);
        var complete = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, qComp, isPlayerReply: false);
        var player = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, qOffer, isPlayerReply: true);

        Assert.Contains(AiMissionRoleContextBuilder.SemanticOffer, offer, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningOffer, offer, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticIncomplete, incomplete, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningIncomplete, incomplete, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticComplete, complete, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.MeaningComplete, complete, StringComparison.Ordinal);
        Assert.Contains(AiMissionRoleContextBuilder.SemanticPlayerReply, player, StringComparison.Ordinal);

        var package = AiPromptComposer.Compose(AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            rolePreset: "",
            customRole: "",
            attitudePreset: "Amable",
            customAttitude: null,
            narrativeContext: "Contexto",
            additionalInstruction: incomplete,
            length: AiTextLength.Corta,
            currentNpcName: "NPC"));
        Assert.Contains(AiMissionRoleContextBuilder.SemanticIncomplete, package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("OFRECER / INCOMPLETA / COMPLETADA", package.TaskInstructions, StringComparison.Ordinal);
    }

    private static ContentDraftWorkspace NewWorkspace(out NpcsModeloDraft npc)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20000);
        ws.Dialogs.SetDbMaxQuestionId(30000);
        ws.Missions.SetDbMaxQuestId(40000);
        npc = ws.Npcs.CreateNew();
        npc.Nombre = "RUFUS";
        npc.DialogMode = NpcDialogMode.Interactive;
        return ws;
    }
}
