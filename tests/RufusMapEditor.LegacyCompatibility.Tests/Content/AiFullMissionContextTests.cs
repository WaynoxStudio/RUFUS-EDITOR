using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>DIALOG.AI.FULL_MISSION_CONTEXT.1</summary>
public sealed class AiFullMissionContextTests
{
    [Fact]
    public void Mission_snapshot_includes_stage_objective_roles_and_anti_invention()
    {
        var ws = NewWorkspace(out var npc);
        var qOffer = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, qOffer.Id);
        var r = ws.Dialogs.AddResponse(qOffer);
        var start = ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        var qInc = ws.Dialogs.CreateQuestion(npc.Id);
        var qComp = ws.Dialogs.CreateQuestion(npc.Id);

        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = qOffer.Id;
        mission.PregIncompletaPreguntaId = qInc.Id;
        mission.PregCompletadaPreguntaId = qComp.Id;
        ws.Dialogs.LinkStartMission(start, mission.DraftId);

        var stage = new MissionStageDraft { Nombre = "Prueba", Descripcion = "Etapa de prueba" };
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "742",
            Detalle = ""
        });
        mission.Stages.Add(stage);

        var snapshot = AiMissionSnapshotBuilder.Build(ws, npc, mission);
        Assert.Contains("PRUEBA", snapshot, StringComparison.Ordinal);
        Assert.Contains("Prueba", snapshot, StringComparison.Ordinal);
        Assert.Contains(MissionObjectiveUiSync.UiTypeLabel(MissionObjectiveTypes.DiscoverMap), snapshot, StringComparison.Ordinal);
        Assert.Contains(AiMissionSnapshotBuilder.AntiInventionRule, snapshot, StringComparison.Ordinal);
        Assert.Contains(AiMissionSnapshotBuilder.NarrativeVsMissionRule, snapshot, StringComparison.Ordinal);
        Assert.Contains("OFRECER", snapshot, StringComparison.Ordinal);
        Assert.Contains("INCOMPLETA", snapshot, StringComparison.Ordinal);
        Assert.Contains("COMPLETADA", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("tráeme pruebas", snapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conversation_request_prompt_contains_snapshot_and_anti_invention()
    {
        var ws = NewWorkspace(out var npc);
        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        var stage = new MissionStageDraft { Nombre = "Prueba" };
        stage.Objectives.Add(new MissionObjectiveDraft
        {
            Tipo = MissionObjectiveTypes.DiscoverMap,
            Args = "100",
        });
        mission.Stages.Add(stage);

        var snapshot = AiMissionSnapshotBuilder.Build(ws, npc, mission);
        const string narrative =
            "RUFUS ha detectado una alteración extraña cerca de Astrub y quiere que el jugador investigue el lugar.";

        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarConversacion,
            rolePreset: "",
            customRole: "",
            attitudePreset: "Seco",
            customAttitude: null,
            narrativeContext: narrative,
            additionalInstruction: snapshot,
            length: AiTextLength.Corta,
            currentNpcName: "RUFUS",
            conversationPriorText: "NPC / Rol misión: OFRECER\nHola");

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(narrative, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains(AiMissionSnapshotBuilder.AntiInventionRule, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("Ve a un mapa", package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("textoIncompleta", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("textoCompletada", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains(AiMissionSnapshotBuilder.NewlineGuidance, package.FullPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_dto_requires_incomplete_and_completed_texts()
    {
        var ok = AiResponseValidator.ParseAndValidate(
            AiCreativeAction.GenerarConversacion, AiMockResponses.ConversationJson);
        Assert.True(ok.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(ok.Conversation!.Conversacion!.TextoIncompleta));
        Assert.False(string.IsNullOrWhiteSpace(ok.Conversation.Conversacion.TextoCompletada));

        var missing = """
            {
              "conversacion": {
                "textoNpc": "Hola",
                "respuestasJugador": [
                  { "texto": "A", "tono": "neutral" }
                ]
              }
            }
            """;
        // JsonUnmappedMemberHandling.Disallow does not require missing properties — validator catches empties.
        var parsed = AiResponseSerializer.TryDeserializeForAction(
            AiCreativeAction.GenerarConversacion, missing, out var obj, out _);
        Assert.True(parsed);
        var bad = AiResponseValidator.ValidateConversation((AiConversationGenerationResponse)obj!);
        Assert.False(bad.IsValid);
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
