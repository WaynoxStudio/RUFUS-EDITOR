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

/// <summary>AI.6C — RUFUS access token auth on POST /v1/ai/generate.</summary>
[Collection("AiBackendAuthSerial")]
public sealed class AiBackendAccessAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ExpectedToken = "rufus-test-access-token-ai6c";
    private readonly WebApplicationFactory<Program> _factory;

    public AiBackendAccessAuthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void Authenticator_denies_when_access_token_not_configured()
    {
        AiBackendSafeLog.Clear();
        var opts = new RufusAiAccessOptions { AccessToken = null };
        Assert.False(RufusAiAccessAuthenticator.TryAuthorize(RequestWith("Bearer anything"), opts));
        Assert.Contains(AiBackendSafeLog.Snapshot(), l => l.Contains("IA AUTH → DENEGADA", StringComparison.Ordinal));
    }

    [Fact]
    public void Authenticator_denies_missing_authorization()
    {
        AiBackendSafeLog.Clear();
        var opts = new RufusAiAccessOptions { AccessToken = ExpectedToken };
        Assert.False(RufusAiAccessAuthenticator.TryAuthorize(RequestWith(null), opts));
        Assert.Contains(AiBackendSafeLog.Snapshot(), l => l.Contains("IA AUTH → DENEGADA", StringComparison.Ordinal));
    }

    [Fact]
    public void Authenticator_denies_empty_bearer()
    {
        AiBackendSafeLog.Clear();
        var opts = new RufusAiAccessOptions { AccessToken = ExpectedToken };
        Assert.False(RufusAiAccessAuthenticator.TryAuthorize(RequestWith("Bearer "), opts));
        Assert.Contains(AiBackendSafeLog.Snapshot(), l => l.Contains("IA AUTH → DENEGADA", StringComparison.Ordinal));
    }

    [Fact]
    public void Authenticator_denies_wrong_token_without_logging_secrets()
    {
        AiBackendSafeLog.Clear();
        var opts = new RufusAiAccessOptions { AccessToken = ExpectedToken };
        Assert.False(RufusAiAccessAuthenticator.TryAuthorize(RequestWith("Bearer wrong-token"), opts));
        var snap = AiBackendSafeLog.Snapshot();
        Assert.Contains(snap, l => l.Contains("IA AUTH → DENEGADA", StringComparison.Ordinal));
        Assert.DoesNotContain(snap, l => l.Contains(ExpectedToken, StringComparison.Ordinal));
        Assert.DoesNotContain(snap, l => l.Contains("wrong-token", StringComparison.Ordinal));
    }

    [Fact]
    public void Authenticator_accepts_correct_token()
    {
        AiBackendSafeLog.Clear();
        var opts = new RufusAiAccessOptions { AccessToken = ExpectedToken };
        Assert.True(RufusAiAccessAuthenticator.TryAuthorize(RequestWith("Bearer " + ExpectedToken), opts));
        Assert.Contains(AiBackendSafeLog.Snapshot(), l => l.Contains("IA AUTH → OK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_without_authorization_returns_401_and_does_not_call_openai()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();

        var response = await client.PostAsync(
            "/v1/ai/generate",
            new StringContent(SampleJson(), Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("UNAUTHORIZED", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ExpectedToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_empty_bearer_returns_401_and_does_not_call_openai()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(SampleJson(), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer ");

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Post_wrong_token_returns_401_and_does_not_call_openai()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(ExpectedToken, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(SampleJson(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "incorrect");

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Post_correct_token_processes_and_calls_openai()
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
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Post_when_backend_token_unset_returns_401()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(accessToken: null, fake);
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(SampleJson(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "anything");

        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
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

    private static HttpRequest RequestWith(string? authorization)
    {
        var ctx = new DefaultHttpContext();
        if (authorization is not null)
            ctx.Request.Headers.Authorization = authorization;
        return ctx.Request;
    }

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
