using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.5 — apply creative AI results to local NPC drafts (no BD).</summary>
public sealed class AiDraftApplierTests
{
    [Fact]
    public void Use_name_on_empty_applies_directly_without_motivo()
    {
        var ws = NewWorkspace(out var npc);
        var r = AiDraftApplier.ApplyName(npc, "PicoQueja", replaceConfirmed: false, npc.Id);
        Assert.Equal(AiDraftApplyKind.Applied, r.Kind);
        Assert.Equal("PicoQueja", npc.Nombre);
        Assert.DoesNotContain("motivo", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replace_name_applies_directly_without_confirmation()
    {
        var ws = NewWorkspace(out var npc);
        npc.Nombre = "Viejo";
        var ok = AiDraftApplier.ApplyName(npc, "Nuevo", replaceConfirmed: false, npc.Id);
        Assert.Equal(AiDraftApplyKind.Applied, ok.Kind);
        Assert.Equal("Nuevo", npc.Nombre);
        Assert.DoesNotContain("¿Quieres", ok.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Use_dialogue_simple_empty_applies()
    {
        var ws = NewWorkspace(out var npc);
        npc.DialogMode = NpcDialogMode.Simple;
        var r = AiDraftApplier.ApplyDialogue(ws, npc, "Hola cueva", replaceConfirmed: false, npc.Id);
        Assert.Equal(AiDraftApplyKind.Applied, r.Kind);
        Assert.Equal("Hola cueva", npc.SimpleDialogTextLocal);
        Assert.Equal(0, npc.Pregunta); // no D.q invented
    }

    [Fact]
    public void Replace_dialogue_needs_confirmation()
    {
        var ws = NewWorkspace(out var npc);
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Antes";
        var pending = AiDraftApplier.ApplyDialogue(ws, npc, "Después", replaceConfirmed: false, npc.Id);
        Assert.Equal(AiDraftApplyKind.NeedsConfirmation, pending.Kind);
        Assert.Equal("Antes", npc.SimpleDialogTextLocal);

        var ok = AiDraftApplier.ApplyDialogue(ws, npc, "Después", replaceConfirmed: true, npc.Id);
        Assert.Equal(AiDraftApplyKind.Applied, ok.Kind);
        Assert.Equal("Después", npc.SimpleDialogTextLocal);
    }

    [Fact]
    public void Apply_conversation_writes_single_reply_and_preserves_actions()
    {
        var ws = NewWorkspace(out var npc);
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        q.TextLocal = "Viejo";
        var r0 = ws.Dialogs.AddResponse(q);
        r0.TextLocal = "A";
        r0.Actions.Add(new DialogActionDraft { Accion = 1, Args = "99", Condicion = "QI=1" });
        ws.Dialogs.AddResponse(q).TextLocal = "B";
        ws.Dialogs.AddResponse(q).TextLocal = "C";

        var conv = new AiConversationResult
        {
            TextoNpc = "Apertura IA",
            TextoIncompleta = "Aún no has terminado.",
            TextoCompletada = "Ya lo lograste.",
            RespuestasJugador =
            [
                new AiPlayerResponseSuggestion { Texto = "Neutra", Tono = "neutral" }
            ]
        };

        var result = AiDraftApplier.ApplyConversation(
            ws, npc, conv, replaceConfirmed: true, interactiveSwitchConfirmed: true, npc.Id);

        Assert.Equal(AiDraftApplyKind.Applied, result.Kind);
        Assert.Equal("Apertura IA", q.TextLocal);
        Assert.Single(q.Responses);
        Assert.Equal("Neutra", q.Responses[0].TextLocal);
        // Tone must NOT become accion; existing technical action preserved.
        Assert.Single(q.Responses[0].Actions);
        Assert.Equal(1, q.Responses[0].Actions[0].Accion);
        Assert.Equal("99", q.Responses[0].Actions[0].Args);
        Assert.Equal("QI=1", q.Responses[0].Actions[0].Condicion);
    }

    [Fact]
    public void Apply_question_text_does_not_touch_responses()
    {
        var ws = NewWorkspace(out var npc);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Stay";
        r.Actions.Add(new DialogActionDraft { Accion = 44, Args = "", Condicion = "" });
        var applied = AiDraftApplier.ApplyQuestionText(q, "Nuevo NPC");
        Assert.Equal(AiDraftApplyKind.Applied, applied.Kind);
        Assert.Equal("Nuevo NPC", q.TextLocal);
        Assert.Equal("Stay", r.TextLocal);
        Assert.Single(r.Actions);
        Assert.Equal(44, r.Actions[0].Accion);
    }

    [Fact]
    public void Conversation_on_simple_needs_interactive_switch()
    {
        var ws = NewWorkspace(out var npc);
        npc.DialogMode = NpcDialogMode.Simple;
        var conv = SampleConversation();
        var pending = AiDraftApplier.ApplyConversation(
            ws, npc, conv, replaceConfirmed: false, interactiveSwitchConfirmed: false, npc.Id);
        Assert.Equal(AiDraftApplyKind.NeedsInteractiveSwitch, pending.Kind);
        Assert.Equal(NpcDialogMode.Simple, npc.DialogMode);

        var ok = AiDraftApplier.ApplyConversation(
            ws, npc, conv, replaceConfirmed: true, interactiveSwitchConfirmed: true, npc.Id);
        Assert.Equal(AiDraftApplyKind.Applied, ok.Kind);
        Assert.Equal(NpcDialogMode.Interactive, npc.DialogMode);
        Assert.True(npc.Pregunta > 0);
        var q = ws.Dialogs.FindQuestion(npc.Pregunta)!;
        Assert.Single(q.Responses);
    }

    [Fact]
    public void Wrong_npc_blocks_apply()
    {
        var ws = NewWorkspace(out var npc);
        var r = AiDraftApplier.ApplyName(npc, "X", replaceConfirmed: true, expectedNpcId: npc.Id + 99);
        Assert.Equal(AiDraftApplyKind.WrongNpc, r.Kind);
        Assert.NotEqual("X", npc.Nombre);
    }

    [Fact]
    public void Empty_name_invalid()
    {
        var ws = NewWorkspace(out var npc);
        var r = AiDraftApplier.ApplyName(npc, "   ", replaceConfirmed: true, npc.Id);
        Assert.Equal(AiDraftApplyKind.Invalid, r.Kind);
    }

    [Fact]
    public void Apply_conversation_writes_incomplete_and_completed_role_questions()
    {
        var ws = NewWorkspace(out var npc);
        ws.Missions.SetDbMaxQuestId(40000);
        npc.DialogMode = NpcDialogMode.Interactive;

        var qOffer = ws.Dialogs.CreateQuestion(npc.Id);
        qOffer.TextLocal = "viejo ofrecer";
        ws.Dialogs.SetInitialQuestion(npc, qOffer.Id);
        var r0 = ws.Dialogs.AddResponse(qOffer);
        r0.TextLocal = "vieja respuesta";
        var start = ws.Dialogs.AddAction(r0, DialogActionCodes.StartQuest);

        var qInc = ws.Dialogs.CreateQuestion(npc.Id);
        qInc.TextLocal = "vieja incompleta";
        var qComp = ws.Dialogs.CreateQuestion(npc.Id);
        qComp.TextLocal = "vieja completada";

        var mission = ws.Missions.CreateMission();
        mission.Nombre = "PRUEBA";
        mission.StartNpcId = npc.Id;
        mission.PregDarPreguntaId = qOffer.Id;
        mission.PregIncompletaPreguntaId = qInc.Id;
        mission.PregCompletadaPreguntaId = qComp.Id;
        ws.Dialogs.LinkStartMission(start, mission.DraftId);

        var conv = new AiConversationResult
        {
            TextoNpc = "OFERTA NUEVA",
            TextoIncompleta = "INCOMPLETA NUEVA",
            TextoCompletada = "COMPLETADA NUEVA",
            RespuestasJugador =
            [
                new AiPlayerResponseSuggestion { Texto = "R1", Tono = "neutral" }
            ]
        };

        var result = AiDraftApplier.ApplyConversation(
            ws, npc, conv, replaceConfirmed: true, interactiveSwitchConfirmed: true, npc.Id);

        Assert.Equal(AiDraftApplyKind.Applied, result.Kind);
        Assert.Equal("OFERTA NUEVA", qOffer.TextLocal);
        Assert.Equal("R1", qOffer.Responses[0].TextLocal);
        Assert.Single(qOffer.Responses);
        Assert.Equal("INCOMPLETA NUEVA", qInc.TextLocal);
        Assert.Equal("COMPLETADA NUEVA", qComp.TextLocal);
        Assert.Equal(DialogActionCodes.StartQuest, qOffer.Responses[0].Actions[0].Accion);
    }

    private static ContentDraftWorkspace NewWorkspace(out NpcsModeloDraft npc)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20000);
        ws.Dialogs.SetDbMaxQuestionId(30000);
        npc = ws.Npcs.CreateNew();
        npc.Nombre = "";
        return ws;
    }

    private static AiConversationResult SampleConversation() => new()
    {
        TextoNpc = "NPC dice",
        TextoIncompleta = "Todavía no.",
        TextoCompletada = "Ya está.",
        RespuestasJugador =
        [
            new AiPlayerResponseSuggestion { Texto = "Uno", Tono = "neutral" }
        ]
    };
}
