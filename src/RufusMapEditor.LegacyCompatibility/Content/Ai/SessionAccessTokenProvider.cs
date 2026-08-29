using RufusMapEditor.Licensing.Client;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// LIC.2 / LIC.5 — prepared provider that exposes the license session token as Bearer for AiBackend.
/// Reads from <see cref="ISessionStore"/> (same DPAPI session saved after Activate/Heartbeat).
/// NOT wired as the default in LIC.5; EnvironmentAiBackendAccessTokenProvider remains active.
/// LIC.6: Editor factory will select this when license auth replaces the shared AI token.
/// </summary>
public sealed class SessionAccessTokenProvider : IAiBackendAccessTokenProvider
{
    private readonly ISessionStore _sessionStore;

    public SessionAccessTokenProvider(ISessionStore sessionStore) =>
        _sessionStore = sessionStore;

    /// <summary>Convenience for tests / LIC.6 wiring with the same store the Editor uses.</summary>
    public static SessionAccessTokenProvider FromDefaultStore() =>
        new(new DpapiLicenseSessionStore());

    public string? TryGetAccessToken()
    {
        // Sync wrapper for existing interface; store is local/DPAPI and fast.
        var state = _sessionStore.LoadAsync().GetAwaiter().GetResult();
        if (state is null || string.IsNullOrWhiteSpace(state.SessionToken))
            return null;
        if (!state.PermissionAi)
            return null;
        return state.SessionToken.Trim();
    }
}
