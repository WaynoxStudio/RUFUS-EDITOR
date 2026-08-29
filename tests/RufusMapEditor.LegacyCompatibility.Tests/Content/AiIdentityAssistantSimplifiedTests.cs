using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>ADMIN.UI.4B.2A.1 — simplified Identidad AI assistant (no role UI).</summary>
public sealed class AiIdentityAssistantSimplifiedTests
{
    [Fact]
    public void GenerateName_works_without_role_in_request()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarNombre);
        Assert.Equal("", req.Role);
        Assert.Equal("Misterioso", req.Attitude);
        Assert.Equal("Montañas de Poben", req.NarrativeContext);

        var stub = AiCreativeServiceStub.Prepare(req);
        Assert.Contains("Generar nombre", stub.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Misterioso", stub.Preview, StringComparison.Ordinal);
        Assert.Contains("Montañas de Poben", stub.Preview, StringComparison.Ordinal);
        Assert.Contains("(vacío)", stub.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateName_works_without_additional_instruction()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarNombre, additionalInstruction: "");
        Assert.Equal("", req.AdditionalInstruction);

        var package = AiPromptComposer.Compose(req);
        Assert.Equal("(vacío)", package.AdditionalInstructionSummary);
        Assert.Contains("exactamente 3", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Style_remains_from_master_config_without_ui_field()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarNombre);
        Assert.Equal(AiCreativeStyle.RufusDofusRetro, req.Style);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(AiCreativeStyle.RufusDofusRetro, package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("RUFUS", package.MasterInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentNpcName_passed_internally_not_required_in_ui()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarDialogo, currentNpcName: "Salazar Limo");
        Assert.Equal("Salazar Limo", req.CurrentNpcName);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains("Salazar Limo", package.DynamicContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Attitude_and_context_reach_prompt()
    {
        var req = SimplifiedBuild(
            AiCreativeAction.GenerarNombre,
            attitudePreset: "Misterioso",
            narrativeContext: "Está en las montañas de Poben y necesita ayuda.");

        var package = AiPromptComposer.Compose(req);
        Assert.Equal("Misterioso", package.PersonalityLabel);
        Assert.Contains("Poben", package.ContextSummary, StringComparison.Ordinal);
        Assert.Contains("Poben", package.FullPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AiTextLength.Corta)]
    [InlineData(AiTextLength.Media)]
    [InlineData(AiTextLength.Larga)]
    public void Length_reaches_prompt(AiTextLength length)
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarDialogo, length: length);
        var package = AiPromptComposer.Compose(req);
        Assert.Contains(AiCreativeRequestPreview.FormatLength(length), package.LengthSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateName_still_returns_three_names()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarNombre);
        var package = AiPromptComposer.Compose(req);
        var transport = new SimplifiedFakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateName, AiMockResponses.NamesJson)
        };
        var svc = ConfiguredService(transport);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Generation?.Names);
        Assert.Equal(3, result.Generation!.Names!.Nombres.Count);
    }

    [Fact]
    public async Task GenerateDialogue_without_role_regression()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarDialogo, currentNpcName: "Roco");
        var package = AiPromptComposer.Compose(req);
        var transport = new SimplifiedFakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateDialogue, AiMockResponses.DialogueJson)
        };
        var svc = ConfiguredService(transport);

        var result = await svc.GenerateDialogueAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("", result.OutboundRequest!.CreativeRequest.Role);
        Assert.Equal("Roco", result.OutboundRequest.CreativeRequest.CurrentNpcName);
    }

    [Fact]
    public async Task GenerateConversation_without_role_regression()
    {
        var req = SimplifiedBuild(AiCreativeAction.GenerarConversacion);
        var package = AiPromptComposer.Compose(req);
        var transport = new SimplifiedFakeTransport
        {
            NextBody = SuccessEnvelope(
                AiBackendWireActions.GenerateConversation,
                AiMockResponses.ConversationJson)
        };
        var svc = ConfiguredService(transport);

        var result = await svc.GenerateConversationAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("", result.OutboundRequest!.CreativeRequest.Role);
        Assert.Contains("3 respuestas", result.OutboundRequest.Prompt.Task, StringComparison.OrdinalIgnoreCase);
    }

    private static AiCreativeRequest SimplifiedBuild(
        AiCreativeAction action,
        string attitudePreset = "Misterioso",
        string? customAttitude = null,
        string narrativeContext = "Montañas de Poben",
        string additionalInstruction = "",
        AiTextLength length = AiTextLength.Corta,
        string? currentNpcName = null) =>
        AiCreativeRequestBuilder.Build(
            action,
            rolePreset: "",
            customRole: "",
            attitudePreset,
            customAttitude,
            narrativeContext,
            additionalInstruction,
            length,
            currentNpcName);

    private static AiBackendGenerationService ConfiguredService(IAiBackendTransport transport) =>
        new(new AiBackendSettings
        {
            BackendUrl = "https://rufus-ai-backend.test/v1/generate",
            TimeoutSeconds = 10
        }, transport);

    private static string SuccessEnvelope(string action, string resultJsonObject) =>
        $$"""
            {
              "success": true,
              "action": "{{action}}",
              "result": {{resultJsonObject}}
            }
            """;

    private sealed class SimplifiedFakeTransport : IAiBackendTransport
    {
        public string? NextBody { get; set; }

        public Task<AiBackendTransportResult> PostJsonAsync(
            Uri endpoint,
            string jsonBody,
            TimeSpan timeout,
            AiBackendRequestAuth auth,
            CancellationToken cancellationToken) =>
            Task.FromResult(AiBackendTransportResult.Success(200, NextBody ?? "{}"));
    }
}
