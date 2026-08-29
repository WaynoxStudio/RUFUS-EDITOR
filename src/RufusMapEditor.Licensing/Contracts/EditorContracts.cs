using System.Text.Json.Serialization;

namespace RufusMapEditor.Licensing.Contracts;

/// <summary>API error codes for Editor ↔ License backend (v1).</summary>
public static class LicenseErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string LicenseNotFound = "LICENSE_NOT_FOUND";
    public const string LicenseSuspended = "LICENSE_SUSPENDED";
    public const string LicenseRevoked = "LICENSE_REVOKED";
    public const string LicenseExpired = "LICENSE_EXPIRED";
    public const string LicenseInactive = "LICENSE_INACTIVE";
    public const string DeviceLimitReached = "DEVICE_LIMIT_REACHED";
    public const string SessionLimitReached = "SESSION_LIMIT_REACHED";
    public const string SessionInvalid = "SESSION_INVALID";
    public const string DeviceMismatch = "DEVICE_MISMATCH";
    public const string EditorNotAllowed = "EDITOR_NOT_ALLOWED";
    public const string AiNotAllowed = "AI_NOT_ALLOWED";
    public const string AiQuotaExceeded = "AI_QUOTA_EXCEEDED";
    public const string AiQuotaDailyExceeded = "AI_QUOTA_DAILY_EXCEEDED";
    public const string AiQuotaMonthlyExceeded = "AI_QUOTA_MONTHLY_EXCEEDED";
    /// <summary>Client-side transient network/timeout — not an authoritative backend rejection.</summary>
    public const string NetworkUnavailable = "NETWORK_UNAVAILABLE";
}

public sealed class LicensePermissionsDto
{
    [JsonPropertyName("editor")]
    public bool Editor { get; set; }

    [JsonPropertyName("ai")]
    public bool Ai { get; set; }
}

public sealed class ActivateLicenseRequest
{
    [JsonPropertyName("licenseCode")]
    public string LicenseCode { get; set; } = "";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }
}

public sealed class SessionSuccessResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("permissions")]
    public LicensePermissionsDto Permissions { get; set; } = new();

    [JsonPropertyName("licenseExpiresAt")]
    public DateTimeOffset? LicenseExpiresAt { get; set; }

    [JsonPropertyName("heartbeatSeconds")]
    public int HeartbeatSeconds { get; set; }

    [JsonPropertyName("aiDailyLimit")]
    public int? AiDailyLimit { get; set; }

    [JsonPropertyName("aiMonthlyLimit")]
    public int? AiMonthlyLimit { get; set; }

    [JsonPropertyName("aiUsageToday")]
    public int? AiUsageToday { get; set; }

    [JsonPropertyName("aiUsageMonth")]
    public int? AiUsageMonth { get; set; }
}

public sealed class LicenseErrorResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class HeartbeatRequest
{
    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = "";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";
}

public sealed class LogoutRequest
{
    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = "";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";
}

public sealed class LogoutResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;
}

/// <summary>Conceptual routes (not deployed in LIC.2).</summary>
public static class LicenseApiRoutes
{
    public const string VersionPrefix = "/v1/license";
    public const string Activate = "/v1/license/activate";
    public const string Validate = "/v1/license/session";
    public const string Heartbeat = "/v1/license/heartbeat";
    public const string Logout = "/v1/license/logout";
}
