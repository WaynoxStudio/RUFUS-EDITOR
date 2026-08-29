namespace RufusMapEditor.Licensing.Model;

public sealed class LicenseEntity
{
    public long Id { get; set; }
    public string CodeHash { get; set; } = "";
    /// <summary>Non-secret hint for ADMIN lists (e.g. last 4 of code). Full code only returned at create.</summary>
    public string CodeDisplayHint { get; set; } = "";
    public LicenseStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? FirstActivatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    /// <summary>Duration in whole days from first activation. Null if ADMIN set absolute dates later.</summary>
    public int? DurationDays { get; set; }
    public int MaxDevices { get; set; } = 1;
    public int MaxConcurrentSessions { get; set; } = 1;
    public bool PermissionEditor { get; set; } = true;
    public bool PermissionAi { get; set; }
    /// <summary>Null = unlimited daily AI generations.</summary>
    public int? AiDailyLimit { get; set; }
    /// <summary>Null = unlimited monthly AI generations.</summary>
    public int? AiMonthlyLimit { get; set; }
    public string? AdminNotes { get; set; }
    /// <summary>Human-readable label for ADMIN lists (e.g. customer name). Not secret.</summary>
    public string? DisplayName { get; set; }
}

public sealed class AiUsageEventEntity
{
    public long Id { get; set; }
    public long LicenseId { get; set; }
    public long? SessionId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Action { get; set; } = "";
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public bool OpenAiSucceeded { get; set; }
}

public sealed class DeviceEntity
{
    public long Id { get; set; }
    public long LicenseId { get; set; }
    public string DeviceId { get; set; } = "";
    public DateTimeOffset BoundAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DeviceBindStatus Status { get; set; }
}

public sealed class SessionEntity
{
    public long Id { get; set; }
    public long LicenseId { get; set; }
    public long DeviceId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastRenewedAtUtc { get; set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public SessionStatus Status { get; set; }
}

public sealed class AdminAuditEntity
{
    public long Id { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Action { get; set; } = "";
    public long? LicenseId { get; set; }
    public string? Detail { get; set; }
}
