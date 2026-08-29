using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.6C — Editor sends RUFUS Bearer token; 401 surfaces as controlled UI error.</summary>
public sealed class AiBackendAccessTokenEditorTests
{
    [Fact]
    public async Task Generate_without_token_returns_unauthorized_without_http()
    {
        var transport = new CapturingTransport();
        var settings = new AiBackendSettings
        {
            BackendUrl = AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint,
            TimeoutSeconds = 10
        };
        var svc = new AiBackendGenerationService(
            settings, transport, new StaticAiBackendAccessTokenProvider(null));
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeUnauthorized, result.ErrorCode);
        Assert.Equal(AiBackendGenerationService.UnauthorizedUserMessage, result.ErrorMessage);
        Assert.Equal(0, transport.CallCount);
        Assert.DoesNotContain("sk-", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_sends_rufus_bearer_not_openai_key()
    {
        var transport = new CapturingTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateName, AiMockResponses.NamesJson)
        };
        var settings = new AiBackendSettings
        {
            BackendUrl = AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint,
            TimeoutSeconds = 10
        };
        const string rufusToken = "rufus-shared-install-token";
        var svc = new AiBackendGenerationService(
            settings, transport, new StaticAiBackendAccessTokenProvider(rufusToken));
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(rufusToken, transport.LastAuth.BearerToken);
        Assert.DoesNotContain("sk-", transport.LastAuth.BearerToken ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENAI", transport.LastBody ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_name_dialogue_conversation_and_regenerate_with_token()
    {
        var transport = new CapturingTransport();
        var settings = new AiBackendSettings
        {
            BackendUrl = AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint
        };
        var svc = new AiBackendGenerationService(
            settings, transport, new StaticAiBackendAccessTokenProvider("tok"));

        async Task AssertOk(AiCreativeAction action, string wire, string payload)
        {
            transport.NextBody = SuccessEnvelope(wire, payload);
            var (req, package) = Sample(action);
            var result = await svc.GenerateAsync(req, package);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(transport.LastAuth.HasBearer);
        }

        await AssertOk(AiCreativeAction.GenerarNombre, AiBackendWireActions.GenerateName, AiMockResponses.NamesJson);
        await AssertOk(AiCreativeAction.GenerarNombre, AiBackendWireActions.GenerateName, AiMockResponses.NamesJson); // regenerar
        await AssertOk(AiCreativeAction.GenerarDialogo, AiBackendWireActions.GenerateDialogue, AiMockResponses.DialogueJson);
        await AssertOk(AiCreativeAction.GenerarConversacion, AiBackendWireActions.GenerateConversation, AiMockResponses.ConversationJson);
        Assert.Equal(4, transport.CallCount);
    }

    [Fact]
    public async Task Http_401_maps_to_controlled_unauthorized_message()
    {
        var transport = new CapturingTransport
        {
            NextResult = AiBackendTransportResult.Success(401, """
                {"success":false,"error":{"code":"UNAUTHORIZED","message":"No autorizado para utilizar el servicio IA de RUFUS."}}
                """)
        };
        var settings = new AiBackendSettings
        {
            BackendUrl = AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint
        };
        var svc = new AiBackendGenerationService(
            settings, transport, new StaticAiBackendAccessTokenProvider("tok"));
        var (req, package) = Sample(AiCreativeAction.GenerarDialogo);

        var result = await svc.GenerateDialogueAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeUnauthorized, result.ErrorCode);
        Assert.Equal(AiBackendGenerationService.UnauthorizedUserMessage, result.ErrorMessage);
        Assert.DoesNotContain("tok", result.ErrorMessage ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("stack", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Temporary_vps_url_remains_https()
    {
        Assert.StartsWith("https://", AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://vmi3502135", AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Token_provider_abstraction_supports_future_license_swap()
    {
        IAiBackendAccessTokenProvider env = new EnvironmentAiBackendAccessTokenProvider();
        IAiBackendAccessTokenProvider futureLicense = new StaticAiBackendAccessTokenProvider("license-scoped-token");
        Assert.Equal("license-scoped-token", futureLicense.TryGetAccessToken());
        Assert.Equal(AiBackendAccessTokenEnv.VariableName, "RUFUS_AI_ACCESS_TOKEN");
        _ = env;
    }

    [Fact]
    public void Editor_ai_types_have_no_openai_api_key_fields()
    {
        var aiNs = typeof(AiBackendGenerationService).Namespace!;
        foreach (var type in typeof(AiBackendGenerationService).Assembly.GetTypes()
                     .Where(t => t.Namespace == aiNs))
        {
            foreach (var p in type.GetProperties())
            {
                Assert.DoesNotContain("ApiKey", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("OpenAi", p.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static (AiCreativeRequest Request, AiPromptPackage Package) Sample(AiCreativeAction action)
    {
        var req = AiCreativeRequestBuilder.Build(
            action, "Minero", null, "Gruñón", null,
            "Trabaja solo en una cueva.", null, AiTextLength.Corta, null);
        return (req, AiPromptComposer.Compose(req));
    }

    private static string SuccessEnvelope(string action, string resultJsonObject) => $$"""
        {
          "success": true,
          "action": "{{action}}",
          "result": {{resultJsonObject}}
        }
        """;

    private sealed class CapturingTransport : IAiBackendTransport
    {
        public int CallCount { get; private set; }
        public AiBackendRequestAuth LastAuth { get; private set; }
        public string? LastBody { get; private set; }
        public string? NextBody { get; set; }
        public AiBackendTransportResult? NextResult { get; set; }

        public Task<AiBackendTransportResult> PostJsonAsync(
            Uri endpoint,
            string jsonBody,
            TimeSpan timeout,
            AiBackendRequestAuth auth,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAuth = auth;
            LastBody = jsonBody;
            Assert.StartsWith("https://", endpoint.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            if (NextResult is not null)
                return Task.FromResult(NextResult);
            return Task.FromResult(AiBackendTransportResult.Success(200, NextBody ?? "{}"));
        }
    }
}
