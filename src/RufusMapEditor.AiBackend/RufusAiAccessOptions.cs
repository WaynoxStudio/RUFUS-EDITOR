namespace RufusMapEditor.AiBackend;

/// <summary>
/// AI.6C — RUFUS Editor↔Backend access token (never OPENAI_API_KEY).
/// Loaded only from environment; never hardcoded or committed.
/// </summary>
public sealed class RufusAiAccessOptions
{
    public const string EnvironmentVariable = "RUFUS_AI_ACCESS_TOKEN";

    /// <summary>Expected Bearer token for POST /v1/ai/generate. Empty = reject all.</summary>
    public string? AccessToken { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessToken);

    public static RufusAiAccessOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return new RufusAiAccessOptions
        {
            AccessToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim()
        };
    }
}
