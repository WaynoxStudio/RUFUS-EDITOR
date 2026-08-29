using System.Text.Json;
using System.Text.Json.Serialization;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.AiBackend.Licensing;

/// <summary>
/// LIC.3 — Admin auth BEFORE any body read for /v1/admin/* (same lesson as AI.6C.1).
/// Expects Authorization: Bearer &lt;RUFUS_ADMIN_API_SECRET&gt;.
/// </summary>
public sealed class RufusAdminAuthMiddleware
{
    private readonly RequestDelegate _next;

    public RufusAdminAuthMiddleware(RequestDelegate next) =>
        _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(HttpContext context, IAdminCredentialVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(verifier);

        if (IsAdminPath(context.Request) && !TryAuthorize(context.Request, verifier))
        {
            await WriteUnauthorizedAsync(context).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    public static bool IsAdminPath(HttpRequest request) =>
        request.Path.StartsWithSegments(AdminApiRoutes.Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool TryAuthorize(HttpRequest request, IAdminCredentialVerifier verifier)
    {
        if (!request.Headers.TryGetValue("Authorization", out var header))
            return false;
        var raw = header.ToString();
        const string prefix = "Bearer ";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return verifier.Verify(raw[prefix.Length..].Trim());
    }

    public static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(
            new
            {
                success = false,
                errorCode = "UNAUTHORIZED",
                message = "Admin authentication required."
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }
}
