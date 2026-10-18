using System.Text.Json;
using System.Text.Json.Serialization;
using RufusMapEditor.LegacyCompatibility.AdminAuth;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// Auth gate for POST /v1/ai/generate before any body read.
/// Accepts: Admin AI session (rai1.*), shared RUFUS_AI_ACCESS_TOKEN when configured.
/// Rejects: Admin API secret as generate Bearer.
/// </summary>
public sealed class RufusAiGenerateAuthMiddleware
{
    public const string GeneratePath = "/v1/ai/generate";
    public const string HttpContextAuthModeKey = "RufusAi.AuthMode";
    public const string AuthModeAccessToken = "access_token";
    public const string AuthModeAdmin = "admin";

    private readonly RequestDelegate _next;

    public RufusAiGenerateAuthMiddleware(RequestDelegate next) =>
        _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(
        HttpContext context,
        RufusAiAccessOptions accessOptions,
        AdminAiSessionService adminAiSessions,
        IAdminCredentialVerifier adminVerifier)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsGeneratePost(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!RufusAiAccessAuthenticator.TryExtractBearerToken(context.Request, out var bearer)
            || string.IsNullOrEmpty(bearer))
        {
            AiBackendSafeLog.Info("IA AUTH → DENEGADA");
            await WriteAuthFailureAsync(context, StatusCodes.Status401Unauthorized,
                AiBackendErrorCodes.Unauthorized, RufusAiAccessAuthenticator.UnauthorizedUserMessage)
                .ConfigureAwait(false);
            return;
        }

        // Never accept the Admin API secret as an AI generate Bearer.
        if (adminVerifier.Verify(bearer))
        {
            AiBackendSafeLog.Info("IA AUTH → DENEGADA (admin secret)");
            await WriteAuthFailureAsync(context, StatusCodes.Status401Unauthorized,
                AiBackendErrorCodes.Unauthorized, RufusAiAccessAuthenticator.UnauthorizedUserMessage)
                .ConfigureAwait(false);
            return;
        }

        if (adminAiSessions.TryValidate(bearer, out _))
        {
            context.Items[HttpContextAuthModeKey] = AuthModeAdmin;
            AiBackendSafeLog.Info("AI AUTH ADMIN → sesión válida");
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (AdminAiSessionService.LooksLikeAdminAiToken(bearer))
        {
            AiBackendSafeLog.Info("AI AUTH ADMIN → expirada");
            await WriteAuthFailureAsync(context, StatusCodes.Status401Unauthorized,
                AiBackendErrorCodes.Unauthorized, RufusAiAccessAuthenticator.UnauthorizedUserMessage)
                .ConfigureAwait(false);
            return;
        }

        // Shared private AI access token (always when configured).
        if (accessOptions.IsConfigured
            && RufusAiAccessAuthenticator.FixedTimeEqualsUtf8(bearer, accessOptions.AccessToken!))
        {
            context.Items[HttpContextAuthModeKey] = AuthModeAccessToken;
            AiBackendSafeLog.Info("IA AUTH → OK (access token)");
            await _next(context).ConfigureAwait(false);
            return;
        }

        AiBackendSafeLog.Info("IA AUTH → DENEGADA");
        await WriteAuthFailureAsync(context, StatusCodes.Status401Unauthorized,
            AiBackendErrorCodes.Unauthorized, RufusAiAccessAuthenticator.UnauthorizedUserMessage)
            .ConfigureAwait(false);
    }

    public static bool IsGeneratePost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals(GeneratePath, StringComparison.OrdinalIgnoreCase);

    public static bool IsAccessTokenAuth(HttpContext context) =>
        context.Items.TryGetValue(HttpContextAuthModeKey, out var v)
        && string.Equals(v as string, AuthModeAccessToken, StringComparison.Ordinal);

    public static bool IsAdminAuth(HttpContext context) =>
        context.Items.TryGetValue(HttpContextAuthModeKey, out var v)
        && string.Equals(v as string, AuthModeAdmin, StringComparison.Ordinal);

    private static Task WriteAuthFailureAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(
            new AiBackendHttpResponse
            {
                Success = false,
                Error = new AiBackendErrorBody { Code = code, Message = message }
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }
}
