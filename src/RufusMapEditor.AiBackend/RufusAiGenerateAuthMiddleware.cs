using System.Text.Json;
using System.Text.Json.Serialization;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Services;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// LIC.6 / AI.6C.1 / ADMIN.AI.1 — auth gate for POST /v1/ai/generate before any body read.
/// Accepts: USER SessionToken, Admin AI session (rai1.*). Rejects: Admin API secret, legacy unless enabled.
/// </summary>
public sealed class RufusAiGenerateAuthMiddleware
{
    public const string GeneratePath = "/v1/ai/generate";
    public const string HttpContextAuthModeKey = "RufusAi.AuthMode";
    public const string HttpContextLicenseAiKey = "RufusAi.LicenseContext";
    public const string AuthModeSession = "session";
    public const string AuthModeLegacy = "legacy";
    public const string AuthModeAdmin = "admin";

    private readonly RequestDelegate _next;

    public RufusAiGenerateAuthMiddleware(RequestDelegate next) =>
        _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(
        HttpContext context,
        RufusAiAccessOptions accessOptions,
        AiLegacyTokenOptions legacyOptions,
        LicenseAiAuthService licenseAiAuth,
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

        var (sessionFound, sessionResult) = await licenseAiAuth
            .TryAuthorizeSessionAsync(bearer, context.RequestAborted)
            .ConfigureAwait(false);

        if (sessionFound)
        {
            if (!sessionResult.Success)
            {
                AiBackendSafeLog.Info(sessionResult.ErrorCode == LicenseErrorCodes.AiNotAllowed
                    ? "IA AUTH → permiso IA denegado"
                    : "IA AUTH → sesión inválida");
                var (status, code, msg) = MapSessionFailure(sessionResult);
                await WriteAuthFailureAsync(context, status, code, msg).ConfigureAwait(false);
                return;
            }

            context.Items[HttpContextAuthModeKey] = AuthModeSession;
            context.Items[HttpContextLicenseAiKey] = sessionResult.Context!;
            AiBackendSafeLog.Info("IA AUTH → sesión válida");
            await _next(context).ConfigureAwait(false);
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

        // Unknown token — optional legacy shared token (explicit opt-in only).
        if (legacyOptions.Enabled
            && accessOptions.IsConfigured
            && RufusAiAccessAuthenticator.FixedTimeEqualsUtf8(bearer, accessOptions.AccessToken!))
        {
            context.Items[HttpContextAuthModeKey] = AuthModeLegacy;
            AiBackendSafeLog.Info("IA AUTH → OK (legacy)");
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

    public static LicenseAiAuthContext? GetLicenseContext(HttpContext context) =>
        context.Items.TryGetValue(HttpContextLicenseAiKey, out var v) ? v as LicenseAiAuthContext : null;

    public static bool IsLegacyAuth(HttpContext context) =>
        context.Items.TryGetValue(HttpContextAuthModeKey, out var v)
        && string.Equals(v as string, AuthModeLegacy, StringComparison.Ordinal);

    public static bool IsAdminAuth(HttpContext context) =>
        context.Items.TryGetValue(HttpContextAuthModeKey, out var v)
        && string.Equals(v as string, AuthModeAdmin, StringComparison.Ordinal);

    private static (int Status, string Code, string Message) MapSessionFailure(LicenseAiAuthResult r)
    {
        var code = r.ErrorCode ?? LicenseErrorCodes.SessionInvalid;
        var msg = r.Message ?? DefaultMessage(code);
        var status = code switch
        {
            LicenseErrorCodes.SessionInvalid => StatusCodes.Status401Unauthorized,
            LicenseErrorCodes.AiNotAllowed => StatusCodes.Status403Forbidden,
            LicenseErrorCodes.LicenseSuspended => StatusCodes.Status403Forbidden,
            LicenseErrorCodes.LicenseRevoked => StatusCodes.Status403Forbidden,
            LicenseErrorCodes.LicenseExpired => StatusCodes.Status403Forbidden,
            LicenseErrorCodes.DeviceMismatch => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status403Forbidden,
        };

        var wireCode = code switch
        {
            LicenseErrorCodes.SessionInvalid => AiBackendErrorCodes.Unauthorized,
            LicenseErrorCodes.AiNotAllowed => AiBackendErrorCodes.AiNotAllowed,
            _ => code,
        };

        if (code == LicenseErrorCodes.SessionInvalid)
            msg = RufusAiAccessAuthenticator.UnauthorizedUserMessage;

        return (status, wireCode, msg);
    }

    private static string DefaultMessage(string code) => code switch
    {
        LicenseErrorCodes.AiNotAllowed => "Tu licencia no incluye acceso al Asistente IA.",
        LicenseErrorCodes.LicenseSuspended => "Tu licencia está suspendida.",
        LicenseErrorCodes.LicenseRevoked => "Esta licencia ha sido revocada.",
        LicenseErrorCodes.LicenseExpired => "Tu licencia ha caducado.",
        _ => RufusAiAccessAuthenticator.UnauthorizedUserMessage,
    };

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
