using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>ADMIN.UI.4B.2B.AI.1 — bubble AI context + apply-text without touching actions.</summary>
public sealed class AiDialogBubbleContextTests
{
    [Fact]
    public void Npc_question_request_includes_profile_instruction_and_prior_thread()
    {
        var ws = NewWorkspace(out var npc);
        var q1 = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q1.Id);
        q1.TextLocal = "He visto algo extraño en Astrub.";
        var r1 = ws.Dialogs.AddResponse(q1);
        r1.TextLocal = "¿Qué has visto?";
        var q2 = ws.Dialogs.CreateQuestion(npc.Id);
        q2.TextLocal = "";
        var link = ws.Dialogs.AddAction(r1, DialogActionCodes.GotoQuestion);
        ws.Dialogs.LinkGotoQuestion(link, q2.Id);

        var prior = AiDialogThreadContextBuilder.BuildPriorForQuestion(ws, npc, q2);
        Assert.Contains("NPC: He visto algo extraño en Astrub.", prior, StringComparison.Ordinal);
        Assert.Contains("Jugador: ¿Qué has visto?", prior, StringComparison.Ordinal);
        Assert.DoesNotContain(q2.Id.ToString(), prior, StringComparison.Ordinal);
        Assert.DoesNotContain("accion", prior, StringComparison.OrdinalIgnoreCase);

        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            rolePreset: "",
            customRole: "",
            attitudePreset: "Amable",
            customAttitude: null,
            narrativeContext: "Mago de Astrub que conoce rumores sobre Rufus.",
            additionalInstruction: "Que advierta de algo extraño cerca de Astrub.",
            length: AiTextLength.Corta,
            currentNpcName: "Rufus Mago",
            conversationPriorText: prior,
            parentNpcQuestionText: null,
            generatePlayerReply: false);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains("Rufus Mago", package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("Amable", package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("Mago de Astrub", package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("Que advierta de algo extraño cerca de Astrub.", package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("He visto algo extraño en Astrub.", package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("Corta", package.LengthSummary, StringComparison.Ordinal);
        Assert.Contains("NO generar", package.TaskInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain("Map ID", req.AdditionalInstruction, StringComparison.Ordinal);
        Assert.False(req.GeneratePlayerReply);
        Assert.Equal("", q2.TextLocal);
    }

    [Fact]
    public void Player_reply_request_includes_exact_parent_question()
    {
        const string parent = "Una niebla extraña está cubriendo Astrub desde hace días.";
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            rolePreset: "",
            customRole: "",
            attitudePreset: "Amable",
            customAttitude: null,
            narrativeContext: "Conoce rumores sobre Rufus.",
            additionalInstruction: "Que muestre curiosidad.",
            length: AiTextLength.Corta,
            currentNpcName: "Rufus Mago",
            conversationPriorText: "",
            parentNpcQuestionText: parent,
            generatePlayerReply: true);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(parent, package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains(parent, package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("RESPUESTA DEL JUGADOR", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("Que muestre curiosidad.", package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("NO generar", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("acciones", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.True(req.GeneratePlayerReply);
    }

    [Fact]
    public void Empty_profile_still_composes_and_does_not_block()
    {
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            rolePreset: "",
            customRole: "",
            attitudePreset: "",
            customAttitude: null,
            narrativeContext: "",
            additionalInstruction: "Que se presente.",
            length: AiTextLength.Corta,
            currentNpcName: "",
            conversationPriorText: "",
            parentNpcQuestionText: "",
            generatePlayerReply: false);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains("Que se presente.", package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("(vacío)", package.ContextSummary, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(package.TaskInstructions));
    }

    [Fact]
    public void Compose_does_not_apply_text_to_the_draft()
    {
        var ws = NewWorkspace(out var npc);
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        q.TextLocal = "Original";

        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "",
            null,
            "Amable",
            null,
            "Astrub",
            "Que salude.",
            AiTextLength.Corta,
            "Rufus Mago");
        _ = AiPromptComposer.Compose(req);

        Assert.Equal("Original", q.TextLocal);
        Assert.Empty(q.Responses);
    }

    [Fact]
    public void Apply_question_text_writes_only_text_local()
    {
        var ws = NewWorkspace(out var npc);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Antes";
        q.Params = "keep-params";
        q.Alternos = "keep-alternos";

        _ = AiPromptComposer.Compose(AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo, "", null, "Amable", null, "", "instr", AiTextLength.Corta, "N"));
        Assert.Equal("Antes", q.TextLocal);

        var result = AiDraftApplier.ApplyQuestionText(q, "Después");
        Assert.Equal(AiDraftApplyKind.Applied, result.Kind);
        Assert.Equal("Después", q.TextLocal);
        Assert.Equal("keep-params", q.Params);
        Assert.Equal("keep-alternos", q.Alternos);
        Assert.Equal(npc.Id, q.OwnerNpcId);
    }

    [Fact]
    public void Apply_response_text_preserves_actions_args_and_links()
    {
        var ws = NewWorkspace(out var npc);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Viejo";
        r.Actions.Add(new DialogActionDraft
        {
            Accion = DialogActionCodes.GotoQuestion,
            Args = "99",
            Condicion = "QI=1",
            TargetQuestionId = 99
        });

        var composed = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "",
            null,
            "Amable",
            null,
            "",
            "Que muestre curiosidad.",
            AiTextLength.Corta,
            "N",
            parentNpcQuestionText: q.TextLocal,
            generatePlayerReply: true);
        _ = AiPromptComposer.Compose(composed);
        Assert.Equal("Viejo", r.TextLocal);
        Assert.Equal(DialogActionCodes.GotoQuestion, r.Actions[0].Accion);

        var result = AiDraftApplier.ApplyResponseText(r, "¿Una niebla? ¿Sabes de dónde está saliendo?");
        Assert.Equal(AiDraftApplyKind.Applied, result.Kind);
        Assert.Equal("¿Una niebla? ¿Sabes de dónde está saliendo?", r.TextLocal);
        Assert.Single(r.Actions);
        Assert.Equal(DialogActionCodes.GotoQuestion, r.Actions[0].Accion);
        Assert.Equal("99", r.Actions[0].Args);
        Assert.Equal("QI=1", r.Actions[0].Condicion);
        Assert.Equal(99, r.Actions[0].TargetQuestionId);
    }

    [Fact]
    public void Prior_thread_skips_empty_lines_and_caps_turns()
    {
        var ws = NewWorkspace(out var npc);
        DialogResponseDraft? prevR = null;
        for (var i = 0; i < 12; i++)
        {
            var q = ws.Dialogs.CreateQuestion(npc.Id);
            q.TextLocal = $"NPC línea {i}";
            if (i == 0)
                ws.Dialogs.SetInitialQuestion(npc, q.Id);
            if (prevR is not null)
            {
                var a = ws.Dialogs.AddAction(prevR, DialogActionCodes.GotoQuestion);
                ws.Dialogs.LinkGotoQuestion(a, q.Id);
            }

            prevR = ws.Dialogs.AddResponse(q);
            prevR.TextLocal = $"Jugador línea {i}";
        }

        var last = ws.Dialogs.CreateQuestion(npc.Id);
        last.TextLocal = "";
        var link = ws.Dialogs.AddAction(prevR!, DialogActionCodes.GotoQuestion);
        ws.Dialogs.LinkGotoQuestion(link, last.Id);

        var prior = AiDialogThreadContextBuilder.BuildPriorForQuestion(ws, npc, last);
        var lines = prior.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= AiDialogThreadContextBuilder.MaxTurns);
        Assert.Contains("Jugador línea 11", prior, StringComparison.Ordinal);
        Assert.DoesNotContain("NPC línea 0", prior, StringComparison.Ordinal);
        Assert.Equal("", last.TextLocal);
    }

    [Fact]
    public void Find_parent_question_for_response()
    {
        var ws = NewWorkspace(out var npc);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Padre";
        var r = ws.Dialogs.AddResponse(q);
        var found = AiDialogThreadContextBuilder.FindParentQuestion(ws, r);
        Assert.Same(q, found);
    }

    [Fact]
    public void Editor_ai_profile_is_cloned_with_npc_and_is_not_a_bd_column()
    {
        var npc = NpcsModeloDraft.CreateWithDefaults(20001);
        npc.AiAttitudePreset = "Gruñón";
        npc.AiCustomAttitude = "";
        npc.AiNarrativeContext = "Viejo minero atrapado en una cueva";
        npc.AiLength = AiTextLength.Media;

        var copy = npc.CloneData();
        Assert.Equal("Gruñón", copy.AiAttitudePreset);
        Assert.Equal("Viejo minero atrapado en una cueva", copy.AiNarrativeContext);
        Assert.Equal(AiTextLength.Media, copy.AiLength);
        Assert.Null(typeof(NpcsModeloDraft).GetProperty("PreguntaAi"));
    }

    [Fact]
    public void Backend_dto_still_has_no_new_wire_fields()
    {
        var names = typeof(AiBackendCreativeRequestDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("AdditionalInstruction", names);
        Assert.DoesNotContain("ConversationPriorText", names);
        Assert.DoesNotContain("ParentNpcQuestionText", names);
        Assert.DoesNotContain("GeneratePlayerReply", names);
    }

    private static ContentDraftWorkspace NewWorkspace(out NpcsModeloDraft npc)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20000);
        ws.Dialogs.SetDbMaxQuestionId(30000);
        npc = ws.Npcs.CreateNew();
        npc.Nombre = "Rufus Mago";
        npc.DialogMode = NpcDialogMode.Interactive;
        return ws;
    }
}
