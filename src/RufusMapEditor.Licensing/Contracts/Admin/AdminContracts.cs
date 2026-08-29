using System.Text.Json.Serialization;

namespace RufusMapEditor.Licensing.Contracts.Admin;

/// <summary>ADMIN API routes — private; never public Editor dist. Not deployed in LIC.2.</summary>
public static class AdminApiRoutes
{
    public const string Prefix = "/v1/admin";
    public const string Login = "/v1/admin/login";
    public const string CreateLicense = "/v1/admin/licenses";
    public const string ListLicenses = "/v1/admin/licenses";
    public const string GetLicense = "/v1/admin/licenses/{id}";
    public const string ExtendLicense = "/v1/admin/licenses/{id}/extend";
    public const string SuspendLicense = "/v1/admin/licenses/{id}/suspend";
    public const string ReactivateLicense = "/v1/admin/licenses/{id}/reactivate";
    public const string RevokeLicense = "/v1/admin/licenses/{id}/revoke";
    public const string DeleteLicense = "/v1/admin/licenses/{id}";
    public const string UpdateDisplayName = "/v1/admin/licenses/{id}/display-name";
    public const string ResetDevice = "/v1/admin/licenses/{id}/reset-device";
    public const string TerminateSession = "/v1/admin/licenses/{id}/terminate-session";
    public const string UpdateAiSettings = "/v1/admin/licenses/{id}/ai-settings";

    /// <summary>ADMIN.AI.1 — issue a short-lived Admin AI session token (not the Admin API secret).</summary>
    public const string CreateAiSession = "/v1/admin/ai-session";

    /// <summary>ADMIN.USAGE.1 — aggregated AI token/generation metrics (read-only).</summary>
    public const string AiUsageStats = "/v1/admin/ai-usage";
}

/// <summary>
/// V1 ADMIN auth: strong secret only on backend (env RUFUS_ADMIN_API_SECRET).
/// Admin tool stores its own protected credential locally — never in Editor dist.
/// Abstraction allows upgrading to multi-admin accounts later without rewriting ops endpoints.
/// </summary>
public static class AdminAuthOptions
{
    public const string EnvironmentVariable = "RUFUS_ADMIN_API_SECRET";
}

public interface IAdminCredentialVerifier
{
    bool IsConfigured { get; }
    bool Verify(string? presentedSecret);
}

public sealed class EnvironmentAdminCredentialVerifier : IAdminCredentialVerifier
{
    private readonly string _expected;

    public EnvironmentAdminCredentialVerifier(string? expectedFromEnv = null)
    {
        _expected = (expectedFromEnv
                     ?? Environment.GetEnvironmentVariable(AdminAuthOptions.EnvironmentVariable)
                     ?? "").Trim();
    }

    public bool IsConfigured => _expected.Length >= 16;

    public bool Verify(string? presentedSecret)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(presentedSecret))
            return false;
        var a = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_expected));
        var b = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presentedSecret.Trim()));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public sealed class CreateLicenseRequest
{
    [JsonPropertyName("durationDays")]
    public int DurationDays { get; set; } = 30;

    [JsonPropertyName("maxDevices")]
    public int MaxDevices { get; set; } = 1;

    [JsonPropertyName("maxConcurrentSessions")]
    public int MaxConcurrentSessions { get; set; } = 1;

    [JsonPropertyName("permissionEditor")]
    public bool PermissionEditor { get; set; } = true;

    [JsonPropertyName("permissionAi")]
    public bool PermissionAi { get; set; }

    [JsonPropertyName("aiDailyLimit")]
    public int? AiDailyLimit { get; set; }

    [JsonPropertyName("aiMonthlyLimit")]
    public int? AiMonthlyLimit { get; set; }

    [JsonPropertyName("adminNotes")]
    public string? AdminNotes { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class CreateLicenseResponse
{
    [JsonPropertyName("licenseId")]
    public long LicenseId { get; set; }

    /// <summary>Plaintext code — shown once; not stored on server.</summary>
    [JsonPropertyName("licenseCode")]
    public string LicenseCode { get; set; } = "";

    [JsonPropertyName("codeDisplayHint")]
    public string CodeDisplayHint { get; set; } = "";
}

public sealed class ExtendLicenseRequest
{
    [JsonPropertyName("extraDays")]
    public int ExtraDays { get; set; }
}

public class AdminLicenseListItemDto
{
    [JsonPropertyName("licenseId")]
    public long LicenseId { get; set; }

    [JsonPropertyName("codeDisplayHint")]
    public string CodeDisplayHint { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("firstActivatedAt")]
    public DateTimeOffset? FirstActivatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("devicesBound")]
    public int DevicesBound { get; set; }

    [JsonPropertyName("maxDevices")]
    public int MaxDevices { get; set; }

    [JsonPropertyName("activeSessions")]
    public int ActiveSessions { get; set; }

    [JsonPropertyName("maxConcurrentSessions")]
    public int MaxConcurrentSessions { get; set; }

    [JsonPropertyName("permissionEditor")]
    public bool PermissionEditor { get; set; }

    [JsonPropertyName("permissionAi")]
    public bool PermissionAi { get; set; }

    /// <summary>Null = unlimited.</summary>
    [JsonPropertyName("aiDailyLimit")]
    public int? AiDailyLimit { get; set; }

    /// <summary>Null = unlimited.</summary>
    [JsonPropertyName("aiMonthlyLimit")]
    public int? AiMonthlyLimit { get; set; }

    [JsonPropertyName("aiUsageToday")]
    public int AiUsageToday { get; set; }

    [JsonPropertyName("aiUsageMonth")]
    public int AiUsageMonth { get; set; }
}

public sealed class AdminLicenseDetailDto : AdminLicenseListItemDto
{
    [JsonPropertyName("durationDays")]
    public int? DurationDays { get; set; }

    [JsonPropertyName("adminNotes")]
    public string? AdminNotes { get; set; }

    [JsonPropertyName("lastActivityAt")]
    public DateTimeOffset? LastActivityAt { get; set; }

    [JsonPropertyName("boundDeviceIds")]
    public List<string> BoundDeviceIds { get; set; } = new();
}

public sealed class UpdateDisplayNameRequest
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class UpdateAiSettingsRequest
{
    [JsonPropertyName("permissionAi")]
    public bool PermissionAi { get; set; }

    /// <summary>Null = unlimited. Negative ignored.</summary>
    [JsonPropertyName("aiDailyLimit")]
    public int? AiDailyLimit { get; set; }

    [JsonPropertyName("aiMonthlyLimit")]
    public int? AiMonthlyLimit { get; set; }
}

public static class AdminAuditActions
{
    public const string LicenseCreated = "license.created";
    public const string LicenseExtended = "license.extended";
    public const string LicenseSuspended = "license.suspended";
    public const string LicenseReactivated = "license.reactivated";
    public const string LicenseRevoked = "license.revoked";
    public const string LicenseDeleted = "license.deleted";
    public const string LicenseDisplayNameChanged = "license.display_name_changed";
    public const string DeviceReset = "device.reset";
    public const string SessionTerminated = "session.terminated";
    public const string AiPermissionChanged = "ai.permission_changed";
    public const string AiLimitChanged = "ai.limit_changed";
    public const string AiSessionIssued = "ai.admin_session_issued";
}

/// <summary>ADMIN.AI.1 — response for POST /v1/admin/ai-session.</summary>
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
/// Wire <c>action</c> values actually stored in <c>rufus_ai_usage_events.action</c>
/// (same strings written by the generate orchestrator). Do not invent other types.
/// </summary>
public static class AiUsageStoredActions
{
    public const string GenerateName = "generate_name";
    public const string GenerateDialogue = "generate_dialogue";
    public const string GenerateConversation = "generate_conversation";
}

/// <summary>ADMIN.USAGE.1 — aggregated metrics only (no prompts, codes, or secrets).</summary>
public sealed class AdminAiUsageStatsDto
{
    [JsonPropertyName("asOfUtc")]
    public DateTimeOffset AsOfUtc { get; set; }

    /// <summary>What the table currently covers (USER license events). ADMIN AI telemetry is separate/pending.</summary>
    [JsonPropertyName("telemetryScope")]
    public string TelemetryScope { get; set; } = "user_license_events_only";

    [JsonPropertyName("telemetryNote")]
    public string TelemetryNote { get; set; } = "";

    [JsonPropertyName("today")]
    public AdminAiUsageBucketDto Today { get; set; } = new();

    [JsonPropertyName("month")]
    public AdminAiUsageBucketDto Month { get; set; } = new();

    [JsonPropertyName("allTime")]
    public AdminAiUsageBucketDto AllTime { get; set; } = new();

    [JsonPropertyName("byAction")]
    public List<AdminAiUsageByActionDto> ByAction { get; set; } = new();

    [JsonPropertyName("daily")]
    public List<AdminAiUsageDayDto> Daily { get; set; } = new();

    [JsonPropertyName("monthly")]
    public List<AdminAiUsageMonthDto> Monthly { get; set; } = new();
}

public sealed class AdminAiUsageBucketDto
{
    [JsonPropertyName("generations")]
    public long Generations { get; set; }

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }

    /// <summary>Null when generations is 0 (avoid divide-by-zero).</summary>
    [JsonPropertyName("avgInputTokens")]
    public double? AvgInputTokens { get; set; }

    [JsonPropertyName("avgOutputTokens")]
    public double? AvgOutputTokens { get; set; }

    [JsonPropertyName("avgTotalTokens")]
    public double? AvgTotalTokens { get; set; }
}

public sealed class AdminAiUsageByActionDto
{
    /// <summary>Stored wire action (e.g. generate_name).</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("generations")]
    public long Generations { get; set; }

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("avgInputTokens")]
    public double? AvgInputTokens { get; set; }

    [JsonPropertyName("avgOutputTokens")]
    public double? AvgOutputTokens { get; set; }

    [JsonPropertyName("avgTotalTokens")]
    public double? AvgTotalTokens { get; set; }
}

public sealed class AdminAiUsageDayDto
{
    /// <summary>UTC calendar day yyyy-MM-dd.</summary>
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("generations")]
    public long Generations { get; set; }

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }
}

public sealed class AdminAiUsageMonthDto
{
    /// <summary>UTC calendar month yyyy-MM.</summary>
    [JsonPropertyName("month")]
    public string Month { get; set; } = "";

    [JsonPropertyName("generations")]
    public long Generations { get; set; }

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }
}
