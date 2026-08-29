namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.6C — optional RUFUS Bearer token for Editor → Backend (never OpenAI).</summary>
public readonly struct AiBackendRequestAuth
{
    public AiBackendRequestAuth(string? bearerToken) =>
        BearerToken = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();

    public string? BearerToken { get; }

    public bool HasBearer => !string.IsNullOrWhiteSpace(BearerToken);

    public static AiBackendRequestAuth None => new(null);

    public static AiBackendRequestAuth Bearer(string token) => new(token);
}
