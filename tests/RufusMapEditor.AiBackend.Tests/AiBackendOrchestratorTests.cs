using System.Reflection;
using System.Text.Json;
using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.AiBackend.Tests;

public sealed class AiBackendOrchestratorTests
{
    [Fact]
    public async Task Generate_name_ok_with_mock_openai()
    {
        var orch = CreateOrchestrator(configured: true, openAi: FakeOpenAi.Success(AiMockResponses.NamesJson));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateName), null, legacyAuth: true, default);
        Assert.True(result.Success);
        Assert.Equal(AiBackendWireActions.GenerateName, result.Action);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Generate_dialogue_ok_with_mock_openai()
    {
        var orch = CreateOrchestrator(configured: true, openAi: FakeOpenAi.Success(AiMockResponses.DialogueJson));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateDialogue), null, legacyAuth: true, default);
        Assert.True(result.Success);
        Assert.Equal(AiBackendWireActions.GenerateDialogue, result.Action);
    }

    [Fact]
    public async Task Generate_conversation_ok_with_mock_openai()
    {
        var orch = CreateOrchestrator(configured: true, openAi: FakeOpenAi.Success(AiMockResponses.ConversationJson));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateConversation), null, legacyAuth: true, default);
        Assert.True(result.Success);
        Assert.Equal(AiBackendWireActions.GenerateConversation, result.Action);
    }

    [Fact]
    public async Task Invalid_action_does_not_call_openai()
    {
        var fake = FakeOpenAi.Unused();
        var orch = CreateOrchestrator(configured: true, openAi: fake);
        var req = SampleRequest(AiBackendWireActions.GenerateName);
        req.Action = "generate_quest";
        var result = await orch.GenerateAsync(req, null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.InvalidAction, result.Error!.Code);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Invalid_request_version()
    {
        var fake = FakeOpenAi.Unused();
        var orch = CreateOrchestrator(configured: true, openAi: fake);
        var req = SampleRequest(AiBackendWireActions.GenerateName);
        req.Version = 99;
        var result = await orch.GenerateAsync(req, null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.InvalidRequest, result.Error!.Code);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Api_key_absent_returns_ai_not_configured()
    {
        var fake = FakeOpenAi.Unused();
        var orch = CreateOrchestrator(configured: false, openAi: fake);
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateName), null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.AiNotConfigured, result.Error!.Code);
        Assert.Equal(0, fake.CallCount);
        Assert.DoesNotContain("sk-", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAi_timeout_mapped()
    {
        var orch = CreateOrchestrator(
            configured: true,
            openAi: FakeOpenAi.Fail(AiBackendErrorCodes.OpenAiTimeout, "Timeout OpenAI."));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateDialogue), null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.OpenAiTimeout, result.Error!.Code);
    }

    [Fact]
    public async Task OpenAi_error_mapped()
    {
        var orch = CreateOrchestrator(
            configured: true,
            openAi: FakeOpenAi.Fail(AiBackendErrorCodes.OpenAiError, "OpenAI rate limit."));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateName), null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.OpenAiError, result.Error!.Code);
    }

    [Fact]
    public async Task Invalid_structured_payload_blocked_by_backend_validation()
    {
        var bad = """{ "nombres": [ { "nombre": "Solo", "motivo": "x" } ] }""";
        var orch = CreateOrchestrator(configured: true, openAi: FakeOpenAi.Success(bad));
        var result = await orch.GenerateAsync(SampleRequest(AiBackendWireActions.GenerateName), null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.InvalidAiResponse, result.Error!.Code);
    }

    [Fact]
    public async Task Null_request_invalid()
    {
        var orch = CreateOrchestrator(configured: true, openAi: FakeOpenAi.Unused());
        var result = await orch.GenerateAsync(null, null, legacyAuth: true, default);
        Assert.False(result.Success);
        Assert.Equal(AiBackendErrorCodes.InvalidRequest, result.Error!.Code);
    }

    [Fact]
    public void Default_model_is_gpt5mini_single_source()
    {
        Assert.Equal("gpt-5-mini", OpenAiOptions.DefaultModel);
        var opts = new OpenAiOptions { ApiKey = null, Model = OpenAiOptions.DefaultModel };
        Assert.Equal("gpt-5-mini", opts.Model);
    }

    [Fact]
    public void Strict_schemas_have_additionalProperties_false_and_names()
    {
        foreach (var action in new[]
                 {
                     AiCreativeAction.GenerarNombre,
                     AiCreativeAction.GenerarDialogo,
                     AiCreativeAction.GenerarConversacion
                 })
        {
            var (name, schema) = AiOpenAiStrictSchema.ForAction(action);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
            Assert.DoesNotContain("$schema", schema.ToJsonString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Parse_openai_responses_body_extracts_output_text()
    {
        var body = """
            {
              "output_text": "{\"dialogo\":{\"texto\":\"Hola\"}}",
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """;
        var parsed = OpenAiResponsesClient.ParseSuccessBody(body, "gpt-5-mini");
        Assert.True(parsed.Success);
        Assert.Contains("dialogo", parsed.OutputJson!, StringComparison.Ordinal);
        Assert.Equal(10, parsed.InputTokens);
        Assert.Equal(5, parsed.OutputTokens);
    }

    [Fact]
    public void Safe_log_never_keeps_secrets()
    {
        AiBackendSafeLog.Clear();
        AiBackendSafeLog.Info("Authorization: Bearer sk-secretvalue");
        var snap = AiBackendSafeLog.Snapshot();
        Assert.DoesNotContain(snap, l => l.Contains("sk-secretvalue", StringComparison.Ordinal));
    }

    [Fact]
    public void No_api_key_fields_in_backend_public_surface()
    {
        var asm = typeof(AiGenerateOrchestrator).Assembly;
        foreach (var type in asm.GetTypes())
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                // OpenAiOptions.ApiKey is the env-backed holder in backend only — allowed name, no hardcoded value.
                if (type == typeof(OpenAiOptions) && p.Name == nameof(OpenAiOptions.ApiKey))
                    continue;
                Assert.False(
                    p.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                    && type != typeof(OpenAiOptions),
                    type.FullName + "." + p.Name);
            }
        }
    }

    [Fact]
    public void Sample_request_contract_has_no_technical_ids()
    {
        var json = AiResponseSerializer.Serialize(SampleRequest(AiBackendWireActions.GenerateName));
        Assert.DoesNotContain("npcId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mapId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    private static AiGenerateOrchestrator CreateOrchestrator(bool configured, IOpenAiResponsesClient openAi)
    {
        var opts = new OpenAiOptions
        {
            ApiKey = configured ? "test-key-not-real" : null,
            Model = OpenAiOptions.DefaultModel
        };
        var db = RufusMapEditor.Licensing.Sqlite.SqliteLicenseUnitOfWork.CreateInMemory();
        var clock = new RufusMapEditor.Licensing.Abstractions.SystemServerClock();
        var quota = new RufusMapEditor.Licensing.Services.AiQuotaService(db, clock);
        return new AiGenerateOrchestrator(opts, openAi, quota);
    }

    private static AiBackendGenerateRequest SampleRequest(string action)
    {
        var creative = AiCreativeRequestBuilder.Build(
            AiBackendWireActions.TryParse(action, out var a) ? a : AiCreativeAction.GenerarNombre,
            "Minero",
            null,
            "Gruñón",
            null,
            "Trabaja solo en una cueva.",
            null,
            AiTextLength.Corta,
            null);
        var package = AiPromptComposer.Compose(creative);
        return AiBackendRequestBuilder.Build(creative, package);
    }

    private sealed class FakeOpenAi : IOpenAiResponsesClient
    {
        private readonly OpenAiResponsesCallResult _result;
        public int CallCount { get; private set; }

        private FakeOpenAi(OpenAiResponsesCallResult result) => _result = result;

        public static FakeOpenAi Success(string outputJson) =>
            new(OpenAiResponsesCallResult.Ok(outputJson, OpenAiOptions.DefaultModel, 1, 1));

        public static FakeOpenAi Fail(string code, string message) =>
            new(OpenAiResponsesCallResult.Fail(code, message));

        public static FakeOpenAi Unused() =>
            new(OpenAiResponsesCallResult.Fail(AiBackendErrorCodes.InternalError, "no debería llamarse"));

        public Task<OpenAiResponsesCallResult> CreateStructuredAsync(
            string model,
            string inputText,
            string schemaName,
            JsonElement schema,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.DoesNotContain("sk-", inputText, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(schemaName));
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.True(schema.TryGetProperty("additionalProperties", out var ap));
            return Task.FromResult(_result);
        }
    }
}
