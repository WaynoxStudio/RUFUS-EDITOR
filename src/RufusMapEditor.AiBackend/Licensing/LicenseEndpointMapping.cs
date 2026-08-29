using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Services;
using RufusMapEditor.Licensing.Sqlite;
using RufusMapEditor.AiBackend;

namespace RufusMapEditor.AiBackend.Licensing;

internal static class LicenseEndpointMapping
{
    public static void MapLicenseAndAdminEndpoints(this WebApplication app)
    {
        app.MapPost(LicenseApiRoutes.Activate, async (ActivateLicenseRequest body, LicenseAuthService auth, CancellationToken ct) =>
            LicenseHttpResults.FromOperation(await auth.ActivateAsync(body, ct)));

        // Validate / renew session (same semantics as heartbeat for V1).
        app.MapPost(LicenseApiRoutes.Validate, async (HeartbeatRequest body, LicenseAuthService auth, CancellationToken ct) =>
            LicenseHttpResults.FromOperation(await auth.HeartbeatAsync(body, ct)));

        app.MapPost(LicenseApiRoutes.Heartbeat, async (HeartbeatRequest body, LicenseAuthService auth, CancellationToken ct) =>
            LicenseHttpResults.FromOperation(await auth.HeartbeatAsync(body, ct)));

        app.MapPost(LicenseApiRoutes.Logout, async (LogoutRequest body, LicenseAuthService auth, CancellationToken ct) =>
        {
            var r = await auth.LogoutAsync(body, ct);
            if (!r.Success)
                return LicenseHttpResults.FromOperation(r);
            return Results.Json(new LogoutResponse { Success = true });
        });

        app.MapPost(AdminApiRoutes.CreateLicense, async (CreateLicenseRequest body, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                var created = await admin.CreateAsync(body, ct);
                return Results.Json(created, statusCode: 201);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet(AdminApiRoutes.ListLicenses, async (AdminLicenseService admin, CancellationToken ct) =>
            Results.Json(await admin.ListAsync(ct)));

        app.MapGet("/v1/admin/licenses/{id:long}", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            var detail = await admin.GetAsync(id, ct);
            return detail is null
                ? Results.Json(new { success = false, errorCode = "LICENSE_NOT_FOUND" }, statusCode: 404)
                : Results.Json(detail);
        });

        app.MapPost("/v1/admin/licenses/{id:long}/extend", async (long id, ExtendLicenseRequest body, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                await admin.ExtendAsync(id, body.ExtraDays, ct);
                return Results.Json(await admin.GetAsync(id, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/v1/admin/licenses/{id:long}/suspend", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            await admin.SuspendAsync(id, ct);
            return Results.Json(await admin.GetAsync(id, ct));
        });

        app.MapPost("/v1/admin/licenses/{id:long}/reactivate", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                await admin.ReactivateAsync(id, ct);
                return Results.Json(await admin.GetAsync(id, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/v1/admin/licenses/{id:long}/revoke", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            await admin.RevokeAsync(id, ct);
            return Results.Json(await admin.GetAsync(id, ct));
        });

        app.MapDelete("/v1/admin/licenses/{id:long}", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                await admin.DeleteAsync(id, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { success = false, errorCode = "LICENSE_NOT_FOUND" }, statusCode: 404);
            }
        });

        app.MapPost("/v1/admin/licenses/{id:long}/display-name", async (long id, UpdateDisplayNameRequest body, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                await admin.UpdateDisplayNameAsync(id, body.DisplayName, ct);
                return Results.Json(await admin.GetAsync(id, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/v1/admin/licenses/{id:long}/reset-device", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            await admin.ResetDevicesAsync(id, ct);
            return Results.Json(await admin.GetAsync(id, ct));
        });

        app.MapPost("/v1/admin/licenses/{id:long}/terminate-session", async (long id, AdminLicenseService admin, CancellationToken ct) =>
        {
            await admin.TerminateSessionsAsync(id, ct);
            return Results.Json(await admin.GetAsync(id, ct));
        });

        app.MapPost("/v1/admin/licenses/{id:long}/ai-settings", async (long id, UpdateAiSettingsRequest body, AdminLicenseService admin, CancellationToken ct) =>
        {
            try
            {
                await admin.UpdateAiSettingsAsync(id, body, ct);
                return Results.Json(await admin.GetAsync(id, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Json(new { success = false, errorCode = "INVALID_REQUEST", message = ex.Message }, statusCode: 400);
            }
        });

        // ADMIN.AI.1 — auth already enforced by RufusAdminAuthMiddleware (no body required).
        app.MapPost(AdminApiRoutes.CreateAiSession, (AdminAiSessionService sessions) =>
        {
            if (!sessions.IsConfigured)
            {
                return Results.Json(new
                {
                    success = false,
                    errorCode = "ADMIN_AI_NOT_CONFIGURED",
                    message = "Admin AI session issuer is not configured."
                }, statusCode: 503);
            }

            var issued = sessions.Issue();
            AiBackendSafeLog.Info("AI AUTH ADMIN → sesión emitida");
            return Results.Json(issued);
        });

        // ADMIN.USAGE.1 — read-only aggregates; auth via RufusAdminAuthMiddleware.
        app.MapGet(AdminApiRoutes.AiUsageStats, async (AdminAiUsageService usage, CancellationToken ct) =>
            Results.Json(await usage.GetStatsAsync(ct)));
    }

    public static void AddRufusLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        var lease = LicenseLeaseOptions.FromEnvironment();
        services.AddSingleton(lease);
        services.AddSingleton(AiLegacyTokenOptions.FromEnvironment());
        services.AddSingleton(AdminAiSessionOptions.FromEnvironment());
        services.AddSingleton<IServerClock, SystemServerClock>();

        var configured = configuration["Licensing:SqlitePath"];
        var dbPath = LicenseSqlitePath.Resolve(configured);
        services.AddSingleton<ILicenseUnitOfWork>(_ => new SqliteLicenseUnitOfWork(dbPath));
        services.AddSingleton<IAdminCredentialVerifier>(_ => new EnvironmentAdminCredentialVerifier());
        services.AddSingleton<AdminAiSessionService>();
        services.AddSingleton<LicenseAuthService>();
        services.AddSingleton<AdminLicenseService>();
        services.AddSingleton<AdminAiUsageService>();
        services.AddSingleton<LicenseAiAuthService>();
        services.AddSingleton<AiQuotaService>();
    }
}
