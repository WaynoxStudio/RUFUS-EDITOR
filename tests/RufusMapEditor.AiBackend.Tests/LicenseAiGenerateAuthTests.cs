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
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Services;
using RufusMapEditor.Licensing.Sqlite;

namespace RufusMapEditor.AiBackend.Tests;

[Collection("AiBackendAuthSerial")]
public sealed class LicenseAiGenerateAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LicenseAiGenerateAuthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Session_ai_true_allows_generate_and_counts_usage()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        await using var app = CreateApp(fake, out var db);
        var (token, licenseId) = await ActivateAsync(app, ai: true, daily: null);

        var client = app.CreateClient();
        using var req = Authorized(token, SampleJson());
        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.CallCount);

        var (_, month) = await db.AiUsage.GetUsageTotalsAsync(licenseId, DateTimeOffset.UtcNow);
        Assert.Equal(1, (await db.AiUsage.GetUsageTotalsAsync(licenseId, DateTimeOffset.UtcNow)).Today);
        Assert.True(month >= 1);
    }

    [Fact]
    public async Task Session_ai_false_denies_without_openai()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(fake, out _);
        var (token, _) = await ActivateAsync(app, ai: false, daily: null);

        var client = app.CreateClient();
        using var req = Authorized(token, SampleJson());
        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AI_NOT_ALLOWED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suspended_license_denies_ai()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(fake, out _);
        var (token, licenseId) = await ActivateAsync(app, ai: true, daily: null);
        await AdminPost(app, $"/v1/admin/licenses/{licenseId}/suspend");

        var client = app.CreateClient();
        using var req = Authorized(token, SampleJson());
        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Daily_quota_blocks_second_call()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        await using var app = CreateApp(fake, out _);
        var (token, _) = await ActivateAsync(app, ai: true, daily: 1);

        var client = app.CreateClient();
        using var ok = Authorized(token, SampleJson());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.SendAsync(ok)).StatusCode);

        using var denied = Authorized(token, SampleJson());
        var response = await client.SendAsync(denied);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, fake.CallCount);
        Assert.Contains("AI_QUOTA_DAILY", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_last_slot_does_not_exceed_daily_limit()
    {
        var fake = TrackingOpenAi.Success(AiMockResponses.NamesJson);
        await using var app = CreateApp(fake, out var db);
        var (token, licenseId) = await ActivateAsync(app, ai: true, daily: 1);
        var client = app.CreateClient();

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var req = Authorized(token, SampleJson());
            return await client.SendAsync(req);
        });
        var results = await Task.WhenAll(tasks);
        var ok = results.Count(r => r.IsSuccessStatusCode);
        Assert.Equal(1, ok);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(1, (await db.AiUsage.GetUsageTotalsAsync(licenseId, DateTimeOffset.UtcNow)).Today);
    }

    [Fact]
    public async Task Legacy_disabled_rejects_shared_token()
    {
        var fake = TrackingOpenAi.Unused();
        await using var app = CreateApp(fake, out _, legacyEnabled: false, sharedToken: "legacy-only-token");
        var client = app.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/ai/generate")
        {
            Content = new StringContent(SampleJson(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "legacy-only-token");
        var response = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    private WebApplicationFactory<Program> CreateApp(
        TrackingOpenAi fake,
        out SqliteLicenseUnitOfWork db,
        bool legacyEnabled = false,
        string? sharedToken = null)
    {
        var unit = SqliteLicenseUnitOfWork.CreateInMemory();
        db = unit;
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILicenseUnitOfWork>();
                services.AddSingleton<ILicenseUnitOfWork>(unit);

                services.RemoveAll<RufusAiAccessOptions>();
                services.AddSingleton(new RufusAiAccessOptions { AccessToken = sharedToken });

                services.RemoveAll<AiLegacyTokenOptions>();
                services.AddSingleton(new AiLegacyTokenOptions { Enabled = legacyEnabled });

                services.RemoveAll<IAdminCredentialVerifier>();
                services.AddSingleton<IAdminCredentialVerifier>(
                    new EnvironmentAdminCredentialVerifier("test-admin-secret-32chars!!"));

                services.RemoveAll<AdminAiSessionOptions>();
                services.AddSingleton(new AdminAiSessionOptions
                {
                    SigningSecret = "test-admin-secret-32chars!!",
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
                services.RemoveAll<LicenseAiAuthService>();
                services.AddSingleton<LicenseAiAuthService>();
                services.RemoveAll<AiQuotaService>();
                services.AddSingleton<AiQuotaService>();
                services.RemoveAll<AdminLicenseService>();
                services.AddSingleton<AdminLicenseService>();
                services.RemoveAll<LicenseAuthService>();
                services.AddSingleton<LicenseAuthService>();
            });
        });
    }

    private static async Task<(string Token, long LicenseId)> ActivateAsync(
        WebApplicationFactory<Program> app, bool ai, int? daily)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-admin-secret-32chars!!");
        var created = await client.PostAsJsonAsync("/v1/admin/licenses", new CreateLicenseRequest
        {
            DurationDays = 7,
            MaxDevices = 1,
            MaxConcurrentSessions = 1,
            PermissionEditor = true,
            PermissionAi = ai,
            AiDailyLimit = daily,
        });
        created.EnsureSuccessStatusCode();
        var createdBody = await created.Content.ReadFromJsonAsync<CreateLicenseResponse>();
        Assert.NotNull(createdBody);

        using var act = await client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = createdBody.LicenseCode,
            DeviceId = "a" + new string('b', 63),
            ClientVersion = "lic6-test",
        });
        act.EnsureSuccessStatusCode();
        var session = await act.Content.ReadFromJsonAsync<SessionSuccessResponse>();
        Assert.NotNull(session);
        return (session.SessionToken, createdBody.LicenseId);
    }

    private static async Task AdminPost(WebApplicationFactory<Program> app, string path)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-admin-secret-32chars!!");
        using var res = await client.PostAsync(path, content: null);
        res.EnsureSuccessStatusCode();
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
            string model, string inputText, string schemaName, JsonElement schema, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_result);
        }
    }
}
