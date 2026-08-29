using System.Net.Http;
using System.Reflection;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.4A — backend connection layer (mocked; no real HTTP / OpenAI).</summary>
public sealed class AiBackendConnectionTests
{
    private static readonly string[] ForbiddenRequestProps =
    [
        "NpcId", "QuestionId", "ResponseId", "QuestId", "ItemId", "MapId", "CellId",
        "ActionId", "Args", "ApiKey", "OpenAi", "Authorization"
    ];

    [Fact]
    public async Task GenerateName_builds_correct_backend_request()
    {
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);
        var transport = new FakeTransport();
        var svc = ConfiguredService(transport);

        transport.NextBody = SuccessEnvelope(
            AiBackendWireActions.GenerateName,
            AiMockResponses.NamesJson);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.OutboundRequest);
        Assert.Equal(1, result.OutboundRequest!.Version);
        Assert.Equal(AiBackendWireActions.GenerateName, result.OutboundRequest.Action);
        Assert.Equal("Minero", result.OutboundRequest.CreativeRequest.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.OutboundRequest.Prompt.Master));
        Assert.False(string.IsNullOrWhiteSpace(result.OutboundRequest.Prompt.Task));
        Assert.Contains("3", result.OutboundRequest.Prompt.Task, StringComparison.Ordinal);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task GenerateDialogue_builds_correct_backend_request()
    {
        var (req, package) = Sample(AiCreativeAction.GenerarDialogo, name: "Roco");
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateDialogue, AiMockResponses.DialogueJson)
        };
        var svc = ConfiguredService(transport);

        var result = await svc.GenerateDialogueAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(AiBackendWireActions.GenerateDialogue, result.OutboundRequest!.Action);
        Assert.Equal("Roco", result.OutboundRequest.CreativeRequest.CurrentNpcName);
        Assert.Contains("texto hablado", result.OutboundRequest.Prompt.Task, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateConversation_builds_correct_backend_request()
    {
        var (req, package) = Sample(AiCreativeAction.GenerarConversacion);
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(
                AiBackendWireActions.GenerateConversation,
                AiMockResponses.ConversationJson)
        };
        var svc = ConfiguredService(transport);

        var result = await svc.GenerateConversationAsync(req, package);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(AiBackendWireActions.GenerateConversation, result.OutboundRequest!.Action);
        Assert.Contains("3 respuestas", result.OutboundRequest.Prompt.Task, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AiCreativeAction.GenerarNombre, AiBackendWireActions.GenerateName)]
    [InlineData(AiCreativeAction.GenerarDialogo, AiBackendWireActions.GenerateDialogue)]
    [InlineData(AiCreativeAction.GenerarConversacion, AiBackendWireActions.GenerateConversation)]
    public void Wire_actions_are_controlled(AiCreativeAction action, string wire)
    {
        Assert.Equal(wire, AiBackendWireActions.ToWire(action));
        Assert.True(AiBackendWireActions.TryParse(wire, out var parsed));
        Assert.Equal(action, parsed);
        Assert.False(AiBackendWireActions.TryParse("generate_quest", out _));
        Assert.False(AiBackendWireActions.IsKnown("whatever"));
    }

    [Fact]
    public void Backend_request_has_no_technical_ids()
    {
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);
        var outbound = AiBackendRequestBuilder.Build(req, package);
        var json = AiBackendRequestBuilder.Serialize(outbound);

        Assert.DoesNotContain("npcId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mapId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("questionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);

        foreach (var type in new[]
                 {
                     typeof(AiBackendGenerateRequest),
                     typeof(AiBackendCreativeRequestDto),
                     typeof(AiBackendPromptDto)
                 })
        {
            var names = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var forbidden in ForbiddenRequestProps)
                Assert.DoesNotContain(forbidden, names);
        }
    }

    [Fact]
    public async Task Backend_not_configured_returns_clear_error_without_http()
    {
        var transport = new FakeTransport();
        var svc = new AiBackendGenerationService(new AiBackendSettings(), transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        Assert.Equal(AiGenerationServiceStatus.NotConfigured, svc.Status);
        Assert.False(svc.IsConfigured);

        var result = await svc.GenerateAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeNotConfigured, result.ErrorCode);
        Assert.Equal(AiBackendGenerationService.NotConfiguredUserMessage, result.ErrorMessage);
        Assert.Equal(0, transport.CallCount);
        Assert.NotNull(result.OutboundRequest);
    }

    [Fact]
    public async Task Timeout_simulated()
    {
        var transport = new FakeTransport
        {
            NextResult = AiBackendTransportResult.Fail(AiServiceCallResult.CodeTimeout, "Timeout del backend IA.")
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarDialogo);

        var result = await svc.GenerateDialogueAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_simulated()
    {
        var transport = new FakeTransport
        {
            NextResult = AiBackendTransportResult.Fail(AiServiceCallResult.CodeCancelled, "Conexión cancelada.")
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        using var cts = new CancellationTokenSource();
        // Transport returns cancelled; also verify token path via delay transport
        var result = await svc.GenerateNameAsync(req, package, cts.Token);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeCancelled, result.ErrorCode);
    }

    [Fact]
    public async Task CancellationToken_aborts_before_or_during_transport()
    {
        var transport = new DelayingFakeTransport(TimeSpan.FromSeconds(5));
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var result = await svc.GenerateNameAsync(req, package, cts.Token);

        Assert.False(result.Success);
        Assert.True(
            result.ErrorCode is AiServiceCallResult.CodeCancelled or AiServiceCallResult.CodeTimeout,
            result.ErrorCode);
    }

    [Fact]
    public async Task Http_error_simulated()
    {
        var transport = new FakeTransport
        {
            NextResult = AiBackendTransportResult.Success(503, "unavailable")
        };
        Assert.False(transport.NextResult.Ok);

        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);
        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeUnavailable, result.ErrorCode);
        Assert.Contains("no disponible", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_error_with_json_envelope_is_parsed()
    {
        var body = """
            {
              "success": false,
              "action": "generate_name",
              "error": { "code": "AI_NOT_CONFIGURED", "message": "OPENAI_API_KEY no configurada en el backend." }
            }
            """;
        var transport = new FakeTransport
        {
            NextResult = AiBackendTransportResult.Success(503, body)
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);
        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal("AI_NOT_CONFIGURED", result.ErrorCode);
    }

    [Fact]
    public async Task Corrupt_json_simulated()
    {
        var transport = new FakeTransport { NextBody = "{ not-json" };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeCorruptJson, result.ErrorCode);
        Assert.Contains(AiBackendResponseParser.InvalidUserMessage, result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_action_in_response_blocked()
    {
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateDialogue, AiMockResponses.DialogueJson)
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeWrongAction, result.ErrorCode);
    }

    [Fact]
    public async Task Valid_response_passes_ai3()
    {
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateName, AiMockResponses.NamesJson)
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.True(result.Success);
        Assert.NotNull(result.Generation);
        Assert.True(result.Generation!.IsValid);
        Assert.Equal(3, result.Generation.Names!.Nombres.Count);
    }

    [Fact]
    public async Task Invalid_ai3_payload_blocked()
    {
        var badNames = """{ "nombres": [ { "nombre": "SoloUno", "motivo": "x" } ] }""";
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateName, badNames)
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarNombre);

        var result = await svc.GenerateNameAsync(req, package);

        Assert.False(result.Success);
        Assert.Equal(AiServiceCallResult.CodeInvalidAi3, result.ErrorCode);
        Assert.Equal(AiBackendResponseParser.InvalidUserMessage, result.ErrorMessage);
        Assert.Null(result.Generation);
    }

    [Fact]
    public void No_openai_api_key_in_ai_assembly_types()
    {
        var aiTypes = typeof(IAiGenerationService).Assembly.GetTypes()
            .Where(t => t.Namespace == "RufusMapEditor.LegacyCompatibility.Content.Ai");

        foreach (var type in aiTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.DoesNotContain("ApiKey", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("OpenAiKey", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("OpenAI", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.DoesNotContain("ApiKey", field.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("sk-", field.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Http_transport_rejects_openai_host()
    {
        using var transport = new AiBackendHttpTransport(new HttpClient(), ownsClient: true);
        var result = await transport.PostJsonAsync(
            new Uri("https://api.openai.com/v1/responses"),
            "{}",
            TimeSpan.FromSeconds(5),
            AiBackendRequestAuth.None,
            CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal(AiServiceCallResult.CodeUnavailable, result.ErrorCode);
    }

    [Fact]
    public void Settings_default_has_no_invented_url()
    {
        var settings = new AiBackendSettings();
        Assert.Null(settings.BackendUrl);
        Assert.False(settings.IsConfigured);
        Assert.Equal(AiBackendSettings.DefaultTimeoutSeconds, settings.TimeoutSeconds);
    }

    [Fact]
    public void Activity_log_sanitizes_secrets()
    {
        AiGenerationActivityLog.Clear();
        AiGenerationActivityLog.Info("Authorization: Bearer sk-secret");
        var snap = AiGenerationActivityLog.Snapshot();
        Assert.Contains(snap, l => l.Contains("[omitido", StringComparison.Ordinal));
        Assert.DoesNotContain(snap, l => l.Contains("sk-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_dispatches_by_action()
    {
        var transport = new FakeTransport
        {
            NextBody = SuccessEnvelope(AiBackendWireActions.GenerateDialogue, AiMockResponses.DialogueJson)
        };
        var svc = ConfiguredService(transport);
        var (req, package) = Sample(AiCreativeAction.GenerarDialogo);

        var result = await svc.GenerateAsync(req, package);
        Assert.True(result.Success);
        Assert.Equal(AiCreativeAction.GenerarDialogo, result.Action);
    }

    private static AiBackendGenerationService ConfiguredService(IAiBackendTransport transport)
    {
        // Placeholder URL for unit tests only — never used as a real default in the editor.
        var settings = new AiBackendSettings
        {
            BackendUrl = "https://rufus-ai-backend.test/v1/generate",
            TimeoutSeconds = 10
        };
        return new AiBackendGenerationService(settings, transport);
    }

    private static (AiCreativeRequest Request, AiPromptPackage Package) Sample(
        AiCreativeAction action,
        string? name = null)
    {
        var req = AiCreativeRequestBuilder.Build(
            action,
            "Minero",
            null,
            "Gruñón",
            null,
            "Cueva oscura",
            "Breve",
            AiTextLength.Corta,
            name);
        return (req, AiPromptComposer.Compose(req));
    }

    private static string SuccessEnvelope(string action, string resultJsonObject)
    {
        // resultJsonObject is a full JSON object; embed as raw JSON.
        return $$"""
            {
              "success": true,
              "action": "{{action}}",
              "result": {{resultJsonObject}}
            }
            """;
    }

    private sealed class FakeTransport : IAiBackendTransport
    {
        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }
        public AiBackendRequestAuth LastAuth { get; private set; }
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
            LastBody = jsonBody;
            LastAuth = auth;
            if (NextResult is not null)
                return Task.FromResult(NextResult);
            return Task.FromResult(AiBackendTransportResult.Success(200, NextBody ?? "{}"));
        }
    }

    private sealed class DelayingFakeTransport(TimeSpan delay) : IAiBackendTransport
    {
        public async Task<AiBackendTransportResult> PostJsonAsync(
            Uri endpoint,
            string jsonBody,
            TimeSpan timeout,
            AiBackendRequestAuth auth,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return AiBackendTransportResult.Success(200, "{}");
        }
    }
}
