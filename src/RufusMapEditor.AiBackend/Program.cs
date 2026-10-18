using System.Text.Json;
using System.Text.Json.Serialization;
using RufusMapEditor.AiBackend;
using RufusMapEditor.AiBackend.Admin;
using RufusMapEditor.AiBackend.OpenAi;
using RufusMapEditor.LegacyCompatibility.AdminAuth;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

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
builder.Services.AddRufusAdminServices();

var app = builder.Build();

const string GenerateRoute = RufusAiGenerateAuthMiddleware.GeneratePath;

// Authorization BEFORE any body deserialize / orchestrator / OpenAI.
app.UseMiddleware<RufusAiGenerateAuthMiddleware>();
// Admin auth BEFORE body for /v1/admin/*
app.UseMiddleware<RufusAdminAuthMiddleware>();

app.MapGet("/health", (OpenAiOptions opts, RufusAiAccessOptions access, IAdminCredentialVerifier admin) => Results.Json(new
{
    status = "ok",
    service = "RufusMapEditor.AiBackend",
    openaiConfigured = opts.IsConfigured,
    rufusAccessConfigured = access.IsConfigured,
    adminAuthConfigured = admin.IsConfigured,
    model = opts.Model,
    openAiTimeoutSeconds = opts.TimeoutSeconds,
    openAiConversationTimeoutSeconds = opts.ConversationTimeoutSeconds
}));

// Proxied under /v1/ai/ on VPS (Apache only forwards that prefix).
app.MapGet("/v1/ai/health", (OpenAiOptions opts) => Results.Json(new
{
    status = "ok",
    model = opts.Model,
    openaiConfigured = opts.IsConfigured,
    openAiTimeoutSeconds = opts.TimeoutSeconds,
    openAiConversationTimeoutSeconds = opts.ConversationTimeoutSeconds
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

    var adminAi = RufusAiGenerateAuthMiddleware.IsAdminAuth(httpContext);
    var response = await orchestrator.GenerateAsync(body, adminAi, cancellationToken).ConfigureAwait(false);
    var status = response.Success ? 200 : MapErrorStatus(response.Error?.Code);
    return Results.Json(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    }, statusCode: status);
});

app.MapAdminEndpoints();

app.Run();

static int MapErrorStatus(string? code) => code switch
{
    AiBackendErrorCodes.AiNotConfigured => 503,
    AiBackendErrorCodes.Unauthorized => 401,
    AiBackendErrorCodes.AiNotAllowed => 403,
    AiBackendErrorCodes.InvalidRequest => 400,
    AiBackendErrorCodes.InvalidAction => 400,
    AiBackendErrorCodes.OpenAiTimeout => 504,
    AiBackendErrorCodes.InvalidAiResponse => 502,
    AiBackendErrorCodes.OpenAiResponseInvalid => 502,
    AiBackendErrorCodes.OpenAiError => 502,
    AiBackendErrorCodes.OpenAiAuthError => 502,
    AiBackendErrorCodes.OpenAiRateLimit => 502,
    AiBackendErrorCodes.OpenAiBadRequest => 502,
    AiBackendErrorCodes.OpenAiConnectionError => 502,
    AiBackendErrorCodes.OpenAiClientError => 502,
    _ => 500
};

/// <summary>Exposes entry assembly for WebApplicationFactory / probes.</summary>
public partial class Program;
