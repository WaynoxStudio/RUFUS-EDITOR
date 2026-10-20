using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.AiBackend;

/// <summary>
/// AI.4B — OpenAI configuration for the RUFUS AI backend only.
/// API key and model come from environment variables — never from the WPF editor.
/// </summary>
public sealed class OpenAiOptions
{
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string ModelEnvironmentVariable = "OPENAI_MODEL";
    public const string DefaultModel = "gpt-5-mini";

    /// <summary>Name / single dialogue (bocadillo).</summary>
    public const int DefaultTimeoutSeconds = 60;

    /// <summary>DIALOG.AI.BACKEND_TIMEOUT.DIAGNOSE.2 — full conversation; must stay below ADMIN outer budget.</summary>
    public const int DefaultConversationTimeoutSeconds = 180;

    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 300;

    public string? ApiKey { get; init; }
    public string Model { get; init; } = DefaultModel;
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
    public int ConversationTimeoutSeconds { get; init; } = DefaultConversationTimeoutSeconds;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds));

    public TimeSpan ConversationTimeout =>
        TimeSpan.FromSeconds(Math.Clamp(ConversationTimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds));

    public TimeSpan ResolveTimeout(string? wireAction) =>
        string.Equals(wireAction, AiBackendWireActions.GenerateConversation, StringComparison.Ordinal)
            ? ConversationTimeout
            : Timeout;

    public static OpenAiOptions FromEnvironment()
    {
        var key = NormalizeEnvValue(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));
        var model = NormalizeEnvValue(Environment.GetEnvironmentVariable(ModelEnvironmentVariable));
        return new OpenAiOptions
        {
            ApiKey = key,
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model,
            TimeoutSeconds = DefaultTimeoutSeconds,
            ConversationTimeoutSeconds = DefaultConversationTimeoutSeconds
        };
    }

    /// <summary>Trim and strip a single pair of wrapping quotes from dotenv-style values.</summary>
    internal static string? NormalizeEnvValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var value = raw.Trim();
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
