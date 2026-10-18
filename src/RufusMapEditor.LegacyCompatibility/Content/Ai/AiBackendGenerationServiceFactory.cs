namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// Builds the editor's <see cref="AiBackendGenerationService"/> with BackendUrl
/// and the shared RUFUS_AI_ACCESS_TOKEN provider.
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

        var tokenProvider = new EnvironmentAiBackendAccessTokenProvider();
        if (string.IsNullOrWhiteSpace(tokenProvider.TryGetAccessToken()))
            AiGenerationActivityLog.Backend("access token IA no disponible");
        else
            AiGenerationActivityLog.Backend("IA auth = RUFUS_AI_ACCESS_TOKEN");

        return new AiBackendGenerationService(settings, new AiBackendHttpTransport(), tokenProvider);
    }

    /// <summary>
    /// Content module inside RUFUS ADMIN. Uses Admin AI session tokens only
    /// (never RUFUS_ADMIN_API_SECRET as generate Bearer).
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
}
