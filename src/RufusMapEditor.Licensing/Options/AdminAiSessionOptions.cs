using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// ADMIN.AI.1 — lifetime for opaque Admin AI session tokens (HMAC, no SQLite).
/// Default 60 minutes. Override with <see cref="LifetimeEnvironmentVariable"/>.
/// </summary>
public sealed class AdminAiSessionOptions
{
    public const string LifetimeEnvironmentVariable = "RUFUS_ADMIN_AI_SESSION_MINUTES";
    public const int DefaultLifetimeMinutes = 60;
    public const int MinLifetimeMinutes = 5;
    public const int MaxLifetimeMinutes = 24 * 60;

    /// <summary>Signing material — same secret as Admin API (<c>RUFUS_ADMIN_API_SECRET</c>). Never the AI Bearer itself.</summary>
    public string SigningSecret { get; init; } = "";

    public int LifetimeMinutes { get; init; } = DefaultLifetimeMinutes;

    public bool IsConfigured => SigningSecret.Trim().Length >= 16;

    public TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Clamp(LifetimeMinutes, MinLifetimeMinutes, MaxLifetimeMinutes));

    public static AdminAiSessionOptions FromEnvironment(
        string? adminSecret = null,
        string? lifetimeMinutesRaw = null)
    {
        var secret = (adminSecret
                      ?? Environment.GetEnvironmentVariable(AdminAuthOptions.EnvironmentVariable)
                      ?? "").Trim();
        var raw = lifetimeMinutesRaw
                  ?? Environment.GetEnvironmentVariable(LifetimeEnvironmentVariable);
        var minutes = DefaultLifetimeMinutes;
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var parsed))
            minutes = parsed;

        return new AdminAiSessionOptions
        {
            SigningSecret = secret,
            LifetimeMinutes = minutes,
        };
    }
}
