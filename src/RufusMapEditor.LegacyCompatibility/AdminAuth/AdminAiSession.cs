using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.AdminAuth;

/// <summary>Response for POST /v1/admin/ai-session.</summary>
public sealed class AdminAiSessionResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>
/// Lifetime for opaque Admin AI session tokens (HMAC, no SQLite).
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

/// <summary>
/// Issues / validates opaque Admin AI session tokens.
/// Format: rai1.{expUnix}.{jti}.{macBase64Url} — HMAC-SHA256, no SQLite persistence.
/// Signing key is derived from the Admin API secret; rotating the secret invalidates all sessions.
/// </summary>
public sealed class AdminAiSessionService
{
    public const string TokenPrefix = "rai1.";

    private readonly AdminAiSessionOptions _options;
    private readonly IServerClock _clock;
    private readonly byte[] _hmacKey;

    public AdminAiSessionService(AdminAiSessionOptions options, IServerClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _hmacKey = DeriveKey(_options.SigningSecret);
    }

    public bool IsConfigured => _options.IsConfigured && _hmacKey.Length > 0;

    public AdminAiSessionResponse Issue()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Admin AI session issuer is not configured.");

        var now = _clock.UtcNow;
        var expires = now.Add(_options.Lifetime);
        var jti = Guid.NewGuid().ToString("N");
        var expUnix = expires.ToUnixTimeSeconds();
        var payload = $"{expUnix}.{jti}";
        var mac = ComputeMac(payload);
        var token = $"{TokenPrefix}{payload}.{Base64UrlEncode(mac)}";

        return new AdminAiSessionResponse
        {
            AccessToken = token,
            ExpiresAt = expires,
            TokenType = "Bearer",
        };
    }

    /// <summary>True when the bearer is a well-formed Admin AI token and MAC + expiry are valid.</summary>
    public bool TryValidate(string? bearer, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!IsConfigured || string.IsNullOrWhiteSpace(bearer))
            return false;

        var token = bearer.Trim();
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return false;

        var body = token[TokenPrefix.Length..];
        var parts = body.Split('.', 3);
        if (parts.Length != 3)
            return false;

        if (!long.TryParse(parts[0], out var expUnix))
            return false;
        var jti = parts[1];
        if (string.IsNullOrWhiteSpace(jti) || jti.Length is < 8 or > 64)
            return false;
        if (!TryBase64UrlDecode(parts[2], out var presentedMac))
            return false;

        var payload = $"{parts[0]}.{jti}";
        var expectedMac = ComputeMac(payload);
        if (!CryptographicOperations.FixedTimeEquals(presentedMac, expectedMac))
            return false;

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        if (_clock.UtcNow >= expiresAt)
            return false;

        return true;
    }

    /// <summary>True when the string looks like an Admin AI token (prefix), regardless of validity.</summary>
    public static bool LooksLikeAdminAiToken(string? bearer) =>
        !string.IsNullOrWhiteSpace(bearer)
        && bearer.TrimStart().StartsWith(TokenPrefix, StringComparison.Ordinal);

    private byte[] ComputeMac(string payload)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        return HMACSHA256.HashData(_hmacKey, data);
    }

    private static byte[] DeriveKey(string adminSecret)
    {
        if (string.IsNullOrWhiteSpace(adminSecret))
            return Array.Empty<byte>();
        // Domain-separated key so the raw Admin secret is never accepted as an AI Bearer.
        var material = Encoding.UTF8.GetBytes("RUFUS-ADMIN-AI-SESSION-V1|" + adminSecret.Trim());
        return SHA256.HashData(material);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: return false;
        }

        try
        {
            bytes = Convert.FromBase64String(padded);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
