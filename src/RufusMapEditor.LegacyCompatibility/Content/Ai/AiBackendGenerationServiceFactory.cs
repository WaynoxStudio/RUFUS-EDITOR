using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// Builds the editor's <see cref="AiBackendGenerationService"/> with BackendUrl
/// and the LIC.6 token provider selection rules.
/// </summary>
public static class AiBackendGenerationServiceFactory
{
    public static AiBackendGenerationService CreateForEditor()
    {
        var settings = new AiBackendSettings();
        if (AiBackendLocalDevUrl.TryResolveGenerateEndpoint(out var endpoint, out var source))
        {
            settings.BackendUrl = endpoint;
            var origin = source.Contains(Path.DirectorySeparatorChar) || source.Contains(Path.AltDirectorySeparatorChar)
                ? Path.GetFileName(source)
                : source;
            AiGenerationActivityLog.Backend($"BackendUrl = {endpoint} (origen: {origin})");
        }
        else
        {
            AiGenerationActivityLog.Backend("BackendUrl no resuelta · IA no configurada");
        }

        var tokenProvider = ResolveTokenProvider();
        if (string.IsNullOrWhiteSpace(tokenProvider.TryGetAccessToken()))
            AiGenerationActivityLog.Backend("access token IA no disponible");

        return new AiBackendGenerationService(settings, new AiBackendHttpTransport(), tokenProvider);
    }

    /// <summary>
    /// ADMIN.AI.1 — Content module inside RUFUS ADMIN. Uses Admin AI session tokens only
    /// (never SessionToken USER, never RUFUS_ADMIN_API_SECRET as generate Bearer, never legacy env token).
    /// </summary>
    public static AiBackendGenerationService CreateForAdmin(IAiBackendAccessTokenProvider adminTokenProvider)
    {
        ArgumentNullException.ThrowIfNull(adminTokenProvider);

        var settings = new AiBackendSettings();
        if (AiBackendLocalDevUrl.TryResolveGenerateEndpoint(out var endpoint, out var source))
        {
            settings.BackendUrl = endpoint;
            var origin = source.Contains(Path.DirectorySeparatorChar) || source.Contains(Path.AltDirectorySeparatorChar)
                ? Path.GetFileName(source)
                : source;
            AiGenerationActivityLog.Backend($"BackendUrl = {endpoint} (origen: {origin})");
        }
        else
        {
            AiGenerationActivityLog.Backend("BackendUrl no resuelta · IA no configurada");
        }

        AiGenerationActivityLog.Backend("IA auth = Admin AI session");
        return new AiBackendGenerationService(settings, new AiBackendHttpTransport(), adminTokenProvider);
    }

    /// <summary>
    /// LIC.7 priority:
    /// 1) USER build or licensing enforced → SessionAccessTokenProvider only.
    /// 2) DEVELOPMENT without enforcement → EnvironmentAiBackendAccessTokenProvider.
    /// </summary>
    public static IAiBackendAccessTokenProvider ResolveTokenProvider(
        bool? licensingEnforced = null,
        ISessionStore? sessionStore = null)
    {
        var enforced = licensingEnforced ?? LicenseEnforcementOptions.UsesSessionTokenForAi;
        if (enforced)
        {
            AiGenerationActivityLog.Backend("IA auth = SessionToken (licensing)");
            return new SessionAccessTokenProvider(sessionStore ?? new DpapiLicenseSessionStore());
        }

        AiGenerationActivityLog.Backend("IA auth = RUFUS_AI_ACCESS_TOKEN (development)");
        return new EnvironmentAiBackendAccessTokenProvider();
    }
}
