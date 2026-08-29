namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — backend connection settings for the editor.
/// BackendUrl is resolved by AiBackendLocalDevUrl (AI.6B.2 temporary VPS HTTPS; env override).
/// Never stores OpenAI API keys.
/// </summary>
public sealed class AiBackendSettings
{
    public const int DefaultTimeoutSeconds = 60;
    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 300;

    private string? _backendUrl;
    private int _timeoutSeconds = DefaultTimeoutSeconds;

    /// <summary>Absolute base or endpoint URL of the RUFUS AI backend. Empty = not configured.</summary>
    public string? BackendUrl
    {
        get => _backendUrl;
        set => _backendUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = Math.Clamp(value, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BackendUrl);

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}
