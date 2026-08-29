using System.Text.Json;
using System.Text.Json.Serialization;
using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.Licensing;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Options;

var builder = WebApplication.CreateBuilder(args);

var openAiOptions = OpenAiOptions.FromEnvironment();
builder.Services.AddSingleton(openAiOptions);

var accessOptions = RufusAiAccessOptions.FromEnvironment();
builder.Services.AddSingleton(accessOptions);

builder.Services.AddHttpClient<IOpenAiResponsesClient, OpenAiResponsesClient>((sp, http) =>
{
    http.Timeout = Timeout.InfiniteTimeSpan;
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());

builder.Services.AddSingleton<AiGenerateOrchestrator>();
builder.Services.AddRufusLicensing(builder.Configuration);

var app = builder.Build();

const string GenerateRoute = RufusAiGenerateAuthMiddleware.GeneratePath;

// AI.6C.1 — Authorization BEFORE any body deserialize / orchestrator / OpenAI.
app.UseMiddleware<RufusAiGenerateAuthMiddleware>();
// LIC.3 — Admin auth BEFORE body for /v1/admin/*
app.UseMiddleware<RufusAdminAuthMiddleware>();

app.MapGet("/health", (OpenAiOptions opts, RufusAiAccessOptions access, AiLegacyTokenOptions legacy, IAdminCredentialVerifier admin) => Results.Json(new
{
    status = "ok",
    service = "RufusMapEditor.AiBackend",
    openaiConfigured = opts.IsConfigured,
    rufusAccessConfigured = access.IsConfigured,
    legacyAiTokenEnabled = legacy.Enabled,
    adminAuthConfigured = admin.IsConfigured,
    licenseDb = LicenseSqlitePath.Resolve(app.Configuration["Licensing:SqlitePath"]),
    model = opts.Model
}));

app.MapPost(GenerateRoute, async (
    HttpRequest httpRequest,
    HttpContext httpContext,
    AiGenerateOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    // Auth already enforced by RufusAiGenerateAuthMiddleware (do not re-check / do not read body before that).
    AiBackendGenerateRequest? body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<AiBackendGenerateRequest>(
            httpRequest.Body,
            AiResponseSerializer.Options,
            cancellationToken).ConfigureAwait(false);
    }
    catch (JsonException)
    {
        return Results.Json(new AiBackendHttpResponse
        {
            Success = false,
            Error = new AiBackendErrorBody
            {
                Code = AiBackendErrorCodes.InvalidRequest,
                Message = "JSON de request inválido."
            }
        }, statusCode: 400);
    }

    var licenseAi = RufusAiGenerateAuthMiddleware.GetLicenseContext(httpContext);
    var legacy = RufusAiGenerateAuthMiddleware.IsLegacyAuth(httpContext);
    var adminAi = RufusAiGenerateAuthMiddleware.IsAdminAuth(httpContext);
    var response = await orchestrator.GenerateAsync(body, licenseAi, legacy, adminAi, cancellationToken).ConfigureAwait(false);
    var status = response.Success ? 200 : MapErrorStatus(response.Error?.Code);
    return Results.Json(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }, statusCode: status);
});

app.MapLicenseAndAdminEndpoints();

app.Run();

static int MapErrorStatus(string? code) => code switch
{
    AiBackendErrorCodes.AiNotConfigured => 503,
    AiBackendErrorCodes.Unauthorized => 401,
    AiBackendErrorCodes.AiNotAllowed => 403,
    AiBackendErrorCodes.AiQuotaExceeded => 403,
    AiBackendErrorCodes.AiQuotaDailyExceeded => 403,
    AiBackendErrorCodes.AiQuotaMonthlyExceeded => 403,
    LicenseErrorCodes.LicenseSuspended => 403,
    LicenseErrorCodes.LicenseRevoked => 403,
    LicenseErrorCodes.LicenseExpired => 403,
    AiBackendErrorCodes.InvalidRequest => 400,
    AiBackendErrorCodes.InvalidAction => 400,
    AiBackendErrorCodes.OpenAiTimeout => 504,
    AiBackendErrorCodes.InvalidAiResponse => 502,
    AiBackendErrorCodes.OpenAiError => 502,
    _ => 500
};

/// <summary>Exposes entry assembly for WebApplicationFactory / probes.</summary>
public partial class Program;
