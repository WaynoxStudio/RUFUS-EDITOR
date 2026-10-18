using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.AdminAuth;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.AiBackend.Tests;

[Collection("AiBackendAuthSerial")]
public sealed class AdminAiSessionAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminSecret = "test-admin-secret-32chars!!";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminAiSessionAuthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Valid_admin_secret_issues_ai_session()
    {
        await using var app = CreateApp(TrackingOpenAi.Unused());
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminSecret);

        using var res = await client.PostAsync("/v1/admin/ai-session", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AdminAiSessionResponse>();
        Assert.NotNull(body);
        Assert.StartsWith(AdminAiSessionService.TokenPrefix, body.AccessToken, StringComparison.Ordinal);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Invalid_admin_secret_does_not_issue_token()
    {
        await using var app = CreateApp(TrackingOpenAi.Unused());
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret-xxxxxxxx");

        using var res = await client.PostAsync("/v1/admin/ai-session", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Missing_admin_secret_does_not_issue_token()
    {
        await using var app = CreateApp(TrackingOpenAi.Unused());
        var client = app.CreateClient();
        using var res = await client.PostAsync("/v1/admin/ai-session", content: null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Auth_before_body_rejects_without_credentials()
    {
        await using var app = CreateApp(TrackingOpenAi.Unused());
        var client = app.CreateClient();
        using var content = new StringContent("{\"should\":\"not-matter\"}", Encoding.UTF8, "application/json");
        using var res = await client.PostAsync("/v1/admin/ai-session", content);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Admin_ai_token_allows_generate()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        await using var app = CreateApp(fake);
        var adminToken = await IssueAdminAiAsync(app);

        var client = app.CreateClient();
        using var adminReq = Authorized(adminToken, SampleJson());
        var adminRes = await client.SendAsync(adminReq);
        Assert.Equal(System.Net.HttpStatusCode.OK, adminRes.StatusCode);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Shared_access_token_allows_generate()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        const string shared = "shared-access-token-for-tests";
        await using var app = CreateApp(fake, sharedToken: shared);
        var client = app.CreateClient();
        using var req = Authorized(shared, SampleJson());
        var res = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Fake_admin_ai_token_rejected()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(fake);
        var client = app.CreateClient();
        using var req = Authorized("rai1.9999999999.deadbeef.notarealmacvaluexxxxx", SampleJson());
        var res = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Expired_admin_ai_token_rejected()
    {
        var fake = TrackingOpenAi.Unused();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        await using var app = CreateApp(fake, clock: clock);
        var token = await IssueAdminAiAsync(app);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        var client = app.CreateClient();
        using var req = Authorized(token, SampleJson());
        var res = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Admin_secret_direct_on_generate_rejected()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(fake);
        var client = app.CreateClient();
        using var req = Authorized(AdminSecret, SampleJson());
        var res = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    private WebApplicationFactory<Program> CreateApp(
        TrackingOpenAi fake,
        string? sharedToken = null,
        FixedClock? clock = null)
    {
        var resolvedClock = clock ?? new FixedClock(DateTimeOffset.UtcNow);
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IServerClock>();
                services.AddSingleton<IServerClock>(resolvedClock);

                services.RemoveAll<RufusAiAccessOptions>();
                services.AddSingleton(new RufusAiAccessOptions { AccessToken = sharedToken });

                services.RemoveAll<IAdminCredentialVerifier>();
                services.AddSingleton<IAdminCredentialVerifier>(
                    new EnvironmentAdminCredentialVerifier(AdminSecret));

                services.RemoveAll<AdminAiSessionOptions>();
                services.AddSingleton(new AdminAiSessionOptions
                {
                    SigningSecret = AdminSecret,
                    LifetimeMinutes = 60,
                });
                services.RemoveAll<AdminAiSessionService>();
                services.AddSingleton<AdminAiSessionService>();

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
    }

    private static async Task<string> IssueAdminAiAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminSecret);
        using var res = await client.PostAsync("/v1/admin/ai-session", content: null);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AdminAiSessionResponse>();
        Assert.NotNull(body);
        return body.AccessToken;
    }

    private static HttpRequestMessage Authorized(string token, string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
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

    private sealed class FixedClock : IServerClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class TrackingOpenAi : IOpenAiResponsesClient
    {
        private readonly OpenAiResponsesCallResult _result;
        private int _calls;
        public int CallCount => _calls;

        private TrackingOpenAi(OpenAiResponsesCallResult result) => _result = result;
        public static TrackingOpenAi Success(string json) =>
            new(OpenAiResponsesCallResult.Ok(json, OpenAiOptions.DefaultModel, 3, 4));
        public static TrackingOpenAi Unused() =>
            new(OpenAiResponsesCallResult.Fail(AiBackendErrorCodes.InternalError, "no"));

        public Task<OpenAiResponsesCallResult> CreateStructuredAsync(
            string model, string inputText, string schemaName, JsonElement schema, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_result);
        }
    }
}
