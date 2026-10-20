namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — backend connection settings for the editor.
/// BackendUrl is resolved by AiBackendLocalDevUrl (AI.6B.2 temporary VPS HTTPS; env override).
/// Never stores OpenAI API keys.
/// </summary>
public sealed class AiBackendSettings
{
    /// <summary>Default per-request timeout for name/dialogue (bocadillo).</summary>
    public const int DefaultTimeoutSeconds = 60;

    /// <summary>
    /// DIALOG.AI.BACKEND_TIMEOUT.DIAGNOSE.2 — outer HTTP budget for full conversation.
    /// Must exceed backend OpenAI conversation timeout (180s) to avoid cancel race.
    /// </summary>
    public const int DefaultConversationTimeoutSeconds = 210;

    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 300;

    private string? _backendUrl;
    private int _timeoutSeconds = DefaultTimeoutSeconds;
    private int _conversationTimeoutSeconds = DefaultConversationTimeoutSeconds;

    /// <summary>Absolute base or endpoint URL of the RUFUS AI backend. Empty = not configured.</summary>
    public string? BackendUrl
    {
        get => _backendUrl;
        set => _backendUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Timeout for GenerarNombre / GenerarDialogo (seconds).</summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = Math.Clamp(value, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    /// <summary>Timeout for GenerarConversacion (seconds).</summary>
    public int ConversationTimeoutSeconds
    {
        get => _conversationTimeoutSeconds;
        set => _conversationTimeoutSeconds = Math.Clamp(value, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BackendUrl);

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public TimeSpan ConversationTimeout => TimeSpan.FromSeconds(ConversationTimeoutSeconds);

    /// <summary>Per-action HTTP timeout for the editor → RUFUS AI backend call.</summary>
    public TimeSpan ResolveTimeout(AiCreativeAction action) =>
        action == AiCreativeAction.GenerarConversacion ? ConversationTimeout : Timeout;
}
