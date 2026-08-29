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
    public const int DefaultTimeoutSeconds = 60;

    public string? ApiKey { get; init; }
    public string Model { get; init; } = DefaultModel;
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 300));

    public static OpenAiOptions FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var model = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
        return new OpenAiOptions
        {
            ApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
            TimeoutSeconds = DefaultTimeoutSeconds
        };
    }
}
