namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.6C — supplies the RUFUS Editor↔Backend access token (never OPENAI_API_KEY).
/// Designed so a future license/installation provider can replace the env-based one
/// without changing <see cref="AiBackendGenerationService"/>.
/// </summary>
public interface IAiBackendAccessTokenProvider
{
    /// <summary>
    /// Current RUFUS access token for Authorization: Bearer.
    /// Null or whitespace = not configured for this installation.
    /// </summary>
    string? TryGetAccessToken();
}

/// <summary>
/// ADMIN.AI.1 — optional one-shot refresh after generate returns 401 (expired Admin AI session).
/// Prefer <see cref="IAiBackendAccessTokenAsync"/> from async callers to avoid UI deadlocks.
/// </summary>
public interface IAiBackendAccessTokenRefresh
{
    /// <summary>Invalidate cache and obtain a new token. True if a new token is available.</summary>
    bool TryRefreshAfterUnauthorized();
}

/// <summary>
/// ADMIN.UI.3.1 — async token prepare/refresh for providers that must call the network
/// (Admin AI session). Call from async paths only — never block the WPF UI thread.
/// </summary>
public interface IAiBackendAccessTokenAsync
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<bool> RefreshAfterUnauthorizedAsync(CancellationToken cancellationToken = default);
}

/// <summary>AI.6C — shared env name for Editor and Backend (separate secrets from OpenAI).</summary>
public static class AiBackendAccessTokenEnv
{
    public const string VariableName = "RUFUS_AI_ACCESS_TOKEN";
}

/// <summary>
/// AI.6C — reads RUFUS_AI_ACCESS_TOKEN from the process environment.
/// Phase: shared authorized-install token. Future: license/installation providers.
/// </summary>
public sealed class EnvironmentAiBackendAccessTokenProvider : IAiBackendAccessTokenProvider
{
    public string? TryGetAccessToken()
    {
        var token = Environment.GetEnvironmentVariable(AiBackendAccessTokenEnv.VariableName);
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }
}

/// <summary>AI.6C — fixed token for tests or future injection (e.g. license store).</summary>
public sealed class StaticAiBackendAccessTokenProvider : IAiBackendAccessTokenProvider
{
    private readonly string? _token;

    public StaticAiBackendAccessTokenProvider(string? token) =>
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    public string? TryGetAccessToken() => _token;
}
