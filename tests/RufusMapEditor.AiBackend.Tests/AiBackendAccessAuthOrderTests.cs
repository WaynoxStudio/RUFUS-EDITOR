using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.AiBackend.Tests;

/// <summary>AI.6C.1 — Authorization must run before body deserialize / OpenAI.</summary>
[Collection("AiBackendAuthSerial")]
public sealed class AiBackendAccessAuthOrderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ExpectedToken = "rufus-test-access-token-ai6c1";
    private const string CorruptJson = "{ not-json";
    private readonly WebApplicationFactory<Program> _factory;

    public AiBackendAccessAuthOrderTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Post_no_token_empty_body_returns_401_openai_not_called()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();

        var response = await client.PostAsync(
            "/v1/ai/generate",
            new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        await AssertUnauthorizedBody(response);
    }

    [Fact]
    public async Task Post_no_token_corrupt_json_returns_401_openai_not_called()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();

        var response = await client.PostAsync(
            "/v1/ai/generate",
            new StringContent(CorruptJson, Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        await AssertUnauthorizedBody(response);
    }

    [Fact]
    public async Task Post_false_token_empty_body_returns_401_openai_not_called()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "TOKEN_FALSO");

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        await AssertUnauthorizedBody(response);
    }

    [Fact]
    public async Task Post_false_token_corrupt_json_returns_401_openai_not_called()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(CorruptJson, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "TOKEN_FALSO");

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        await AssertUnauthorizedBody(response);
    }

    [Fact]
    public async Task Post_correct_token_corrupt_json_returns_400_openai_not_called()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(CorruptJson, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ExpectedToken);

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_REQUEST", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ExpectedToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN_FALSO", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_correct_token_valid_request_runs_normal_flow()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(SampleJson(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ExpectedToken);

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.CallCount);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Middleware_matches_generate_post_only()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = "/v1/ai/generate";
        Assert.True(RufusAiGenerateAuthMiddleware.IsGeneratePost(ctx.Request));

        ctx.Request.Method = HttpMethods.Get;
        Assert.False(RufusAiGenerateAuthMiddleware.IsGeneratePost(ctx.Request));

        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = "/health";
        Assert.False(RufusAiGenerateAuthMiddleware.IsGeneratePost(ctx.Request));
    }

    private static async Task AssertUnauthorizedBody(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("UNAUTHORIZED", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(RufusAiAccessAuthenticator.UnauthorizedUserMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ExpectedToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN_FALSO", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> CreateApp(string? accessToken, TrackingOpenAi fake) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<RufusAiAccessOptions>();
                services.AddSingleton(new RufusAiAccessOptions { AccessToken = accessToken });

                services.RemoveAll<AiLegacyTokenOptions>();
                services.AddSingleton(new AiLegacyTokenOptions { Enabled = true });

                services.RemoveAll<IOpenAiResponsesClient>();
                services.AddSingleton<IOpenAiResponsesClient>(fake);

                services.RemoveAll<OpenAiOptions>();
                services.AddSingleton(new OpenAiOptions
                {
                    ApiKey = "test-openai-key-not-real",
                    Model = OpenAiOptions.DefaultModel
                });

                services.RemoveAll<AiGenerateOrchestrator>();
                services.AddSingleton<AiGenerateOrchestrator>();
            });
        });

    private static string SampleJson()
    {
        var creative = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarNombre,
            "Minero", null, "Gruñón", null,
            "Trabaja solo en una cueva.", null, AiTextLength.Corta, null);
        var package = AiPromptComposer.Compose(creative);
        return AiBackendRequestBuilder.Serialize(AiBackendRequestBuilder.Build(creative, package));
    }

    private sealed class TrackingOpenAi : IOpenAiResponsesClient
    {
        private readonly OpenAiResponsesCallResult _result;
        public int CallCount { get; private set; }

        private TrackingOpenAi(OpenAiResponsesCallResult result) => _result = result;

        public static TrackingOpenAi Success(string json) =>
            new(OpenAiResponsesCallResult.Ok(json, OpenAiOptions.DefaultModel, 1, 1));

        public static TrackingOpenAi Unused() =>
            new(OpenAiResponsesCallResult.Fail(AiBackendErrorCodes.InternalError, "no debería llamarse"));

        public Task<OpenAiResponsesCallResult> CreateStructuredAsync(
            string model,
            string inputText,
            string schemaName,
            JsonElement schema,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
