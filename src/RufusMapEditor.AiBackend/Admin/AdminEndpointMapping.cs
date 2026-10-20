using RufusMapEditor.LegacyCompatibility.AdminAuth;
using RufusMapEditor.AiBackend;

namespace RufusMapEditor.AiBackend.Admin;

internal static class AdminEndpointMapping
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
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
    }

    public static void AddRufusAdminServices(this IServiceCollection services)
    {
        services.AddSingleton(AdminAiSessionOptions.FromEnvironment());
        services.AddSingleton<IServerClock, SystemServerClock>();
        services.AddSingleton<IAdminCredentialVerifier>(_ => new EnvironmentAdminCredentialVerifier());
        services.AddSingleton<AdminAiSessionService>();
    }
}
