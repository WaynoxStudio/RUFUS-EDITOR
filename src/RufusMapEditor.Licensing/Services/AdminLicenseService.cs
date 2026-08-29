using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Security;
using RufusMapEditor.Licensing.Services;

namespace RufusMapEditor.Licensing.Services;

/// <summary>ADMIN operations against repositories (no HTTP host in LIC.2).</summary>
public sealed class AdminLicenseService
{
    private readonly ILicenseUnitOfWork _db;
    private readonly IServerClock _clock;

    public AdminLicenseService(ILicenseUnitOfWork db, IServerClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CreateLicenseResponse> CreateAsync(CreateLicenseRequest request, CancellationToken ct = default)
    {
        if (request.DurationDays < 1)
            throw new ArgumentOutOfRangeException(nameof(request.DurationDays));
        if (request.MaxDevices < 1)
            throw new ArgumentOutOfRangeException(nameof(request.MaxDevices));
        if (request.MaxConcurrentSessions < 1)
            throw new ArgumentOutOfRangeException(nameof(request.MaxConcurrentSessions));

        var code = LicenseCodeGenerator.Generate();
        var normalized = LicenseCodeGenerator.Normalize(code);
        var now = _clock.UtcNow;
        LicenseEntity entity = null!;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            entity = await _db.Licenses.InsertAsync(new LicenseEntity
            {
                CodeHash = LicenseCodeHasher.Hash(normalized),
                CodeDisplayHint = LicenseCodeHasher.DisplayHint(normalized),
                Status = LicenseStatus.Created,
                CreatedAtUtc = now,
                DurationDays = request.DurationDays,
                MaxDevices = request.MaxDevices,
                MaxConcurrentSessions = request.MaxConcurrentSessions,
                PermissionEditor = request.PermissionEditor,
                PermissionAi = request.PermissionAi,
                AiDailyLimit = request.AiDailyLimit is > 0 ? request.AiDailyLimit : null,
                AiMonthlyLimit = request.AiMonthlyLimit is > 0 ? request.AiMonthlyLimit : null,
                AdminNotes = request.AdminNotes,
                DisplayName = NormalizeDisplayName(request.DisplayName),
            }, innerCt);

            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.LicenseCreated,
                LicenseId = entity.Id,
                Detail = $"durationDays={request.DurationDays};devices={request.MaxDevices};sessions={request.MaxConcurrentSessions};ai={request.PermissionAi}",
            }, innerCt);
        }, ct);

        return new CreateLicenseResponse
        {
            LicenseId = entity.Id,
            LicenseCode = normalized,
            CodeDisplayHint = entity.CodeDisplayHint,
        };
    }

    public async Task ExtendAsync(long licenseId, int extraDays, CancellationToken ct = default)
    {
        if (extraDays < 1)
            throw new ArgumentOutOfRangeException(nameof(extraDays));
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");
            var baseExpiry = license.ExpiresAtUtc ?? now;
            if (baseExpiry < now)
                baseExpiry = now;
            license.ExpiresAtUtc = baseExpiry.AddDays(extraDays);
            if (license.Status == LicenseStatus.Created)
            {
                // still created — extending pre-activation just increases duration days
                license.DurationDays = (license.DurationDays ?? 0) + extraDays;
            }

            await _db.Licenses.UpdateAsync(license, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.LicenseExtended,
                LicenseId = licenseId,
                Detail = $"extraDays={extraDays}",
            }, innerCt);
        }, ct);
    }

    public async Task SuspendAsync(long licenseId, CancellationToken ct = default) =>
        // Keep sessions Active so the next heartbeat can return LICENSE_SUSPENDED (LIC.5 UX).
        await SetStatusAsync(licenseId, LicenseStatus.Suspended, AdminAuditActions.LicenseSuspended, terminateSessions: false, ct);

    public async Task ReactivateAsync(long licenseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");
            if (license.Status != LicenseStatus.Suspended)
                throw new InvalidOperationException("only suspended licenses can be reactivated");
            if (license.ExpiresAtUtc is { } exp && now >= exp)
                throw new InvalidOperationException("cannot reactivate expired license");
            license.Status = license.FirstActivatedAtUtc is null ? LicenseStatus.Created : LicenseStatus.Active;
            await _db.Licenses.UpdateAsync(license, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.LicenseReactivated,
                LicenseId = licenseId,
            }, innerCt);
        }, ct);
    }

    public async Task RevokeAsync(long licenseId, CancellationToken ct = default) =>
        await SetStatusAsync(licenseId, LicenseStatus.Revoked, AdminAuditActions.LicenseRevoked, terminateSessions: true, ct);

    public async Task DeleteAsync(long licenseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.LicenseDeleted,
                LicenseId = licenseId,
                Detail = $"hint={license.CodeDisplayHint};name={license.DisplayName ?? ""}",
            }, innerCt);
            await _db.Licenses.DeleteAsync(licenseId, innerCt);
        }, ct);
    }

    public async Task UpdateDisplayNameAsync(long licenseId, string? displayName, CancellationToken ct = default)
    {
        var normalized = NormalizeDisplayName(displayName);
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");
            license.DisplayName = normalized;
            await _db.Licenses.UpdateAsync(license, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.LicenseDisplayNameChanged,
                LicenseId = licenseId,
                Detail = normalized ?? "",
            }, innerCt);
        }, ct);
    }

    public async Task ResetDevicesAsync(long licenseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            await _db.Devices.ResetAllBoundAsync(licenseId, now, innerCt);
            await TerminateActiveSessionsAsync(licenseId, now, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.DeviceReset,
                LicenseId = licenseId,
            }, innerCt);
        }, ct);
    }

    public async Task TerminateSessionsAsync(long licenseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            await TerminateActiveSessionsAsync(licenseId, now, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = AdminAuditActions.SessionTerminated,
                LicenseId = licenseId,
            }, innerCt);
        }, ct);
    }

    public async Task<IReadOnlyList<AdminLicenseListItemDto>> ListAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var licenses = await _db.Licenses.ListAsync(ct);
        var list = new List<AdminLicenseListItemDto>();
        foreach (var lic in licenses)
        {
            await _db.Sessions.ExpireLeasesAsync(lic.Id, now, ct);
            var devices = await _db.Devices.ListBoundByLicenseAsync(lic.Id, ct);
            var sessions = await _db.Sessions.ListActiveByLicenseAsync(lic.Id, ct);
            var status = lic.Status.ToString();
            if (lic.ExpiresAtUtc is { } exp && now >= exp
                && lic.Status is LicenseStatus.Active or LicenseStatus.Created)
                status = "Expired";

            list.Add(new AdminLicenseListItemDto
            {
                LicenseId = lic.Id,
                CodeDisplayHint = lic.CodeDisplayHint,
                DisplayName = lic.DisplayName,
                Status = status,
                CreatedAt = lic.CreatedAtUtc,
                FirstActivatedAt = lic.FirstActivatedAtUtc,
                ExpiresAt = lic.ExpiresAtUtc,
                DevicesBound = devices.Count,
                MaxDevices = lic.MaxDevices,
                ActiveSessions = sessions.Count,
                MaxConcurrentSessions = lic.MaxConcurrentSessions,
                PermissionEditor = lic.PermissionEditor,
                PermissionAi = lic.PermissionAi,
                AiDailyLimit = lic.AiDailyLimit,
                AiMonthlyLimit = lic.AiMonthlyLimit,
            });
        }

        return list;
    }

    public async Task UpdateAiSettingsAsync(long licenseId, UpdateAiSettingsRequest request, CancellationToken ct = default)
    {
        if (request.AiDailyLimit is < 0 || request.AiMonthlyLimit is < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "AI limits cannot be negative.");

        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");

            var permChanged = license.PermissionAi != request.PermissionAi;
            var daily = request.AiDailyLimit is > 0 ? request.AiDailyLimit : null;
            var monthly = request.AiMonthlyLimit is > 0 ? request.AiMonthlyLimit : null;
            var limitsChanged = license.AiDailyLimit != daily || license.AiMonthlyLimit != monthly;

            license.PermissionAi = request.PermissionAi;
            license.AiDailyLimit = daily;
            license.AiMonthlyLimit = monthly;
            await _db.Licenses.UpdateAsync(license, innerCt);

            if (permChanged)
            {
                await _db.Audit.AppendAsync(new AdminAuditEntity
                {
                    AtUtc = now,
                    Action = AdminAuditActions.AiPermissionChanged,
                    LicenseId = licenseId,
                    Detail = $"permissionAi={request.PermissionAi}",
                }, innerCt);
            }

            if (limitsChanged)
            {
                await _db.Audit.AppendAsync(new AdminAuditEntity
                {
                    AtUtc = now,
                    Action = AdminAuditActions.AiLimitChanged,
                    LicenseId = licenseId,
                    Detail = $"daily={daily?.ToString() ?? "unlimited"};monthly={monthly?.ToString() ?? "unlimited"}",
                }, innerCt);
            }
        }, ct);
    }

    public async Task<AdminLicenseDetailDto?> GetAsync(long licenseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var lic = await _db.Licenses.GetByIdAsync(licenseId, ct);
        if (lic is null)
            return null;

        await _db.Sessions.ExpireLeasesAsync(lic.Id, now, ct);
        var devices = await _db.Devices.ListBoundByLicenseAsync(lic.Id, ct);
        var sessions = await _db.Sessions.ListActiveByLicenseAsync(lic.Id, ct);
        var (usageToday, usageMonth) = await _db.AiUsage.GetUsageTotalsAsync(lic.Id, now, ct);
        var status = lic.Status.ToString();
        if (lic.ExpiresAtUtc is { } exp && now >= exp
            && lic.Status is LicenseStatus.Active or LicenseStatus.Created)
            status = "Expired";

        DateTimeOffset? last = null;
        foreach (var d in devices)
        {
            if (last is null || d.LastSeenAtUtc > last)
                last = d.LastSeenAtUtc;
        }

        return new AdminLicenseDetailDto
        {
            LicenseId = lic.Id,
            CodeDisplayHint = lic.CodeDisplayHint,
            DisplayName = lic.DisplayName,
            Status = status,
            CreatedAt = lic.CreatedAtUtc,
            FirstActivatedAt = lic.FirstActivatedAtUtc,
            ExpiresAt = lic.ExpiresAtUtc,
            DurationDays = lic.DurationDays,
            DevicesBound = devices.Count,
            MaxDevices = lic.MaxDevices,
            ActiveSessions = sessions.Count,
            MaxConcurrentSessions = lic.MaxConcurrentSessions,
            PermissionEditor = lic.PermissionEditor,
            PermissionAi = lic.PermissionAi,
            AiDailyLimit = lic.AiDailyLimit,
            AiMonthlyLimit = lic.AiMonthlyLimit,
            AiUsageToday = usageToday,
            AiUsageMonth = usageMonth,
            AdminNotes = lic.AdminNotes,
            LastActivityAt = last,
            BoundDeviceIds = devices.Select(d => d.DeviceId).ToList(),
        };
    }

    private async Task SetStatusAsync(
        long licenseId,
        LicenseStatus status,
        string auditAction,
        bool terminateSessions,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByIdAsync(licenseId, innerCt)
                          ?? throw new InvalidOperationException("license not found");
            license.Status = status;
            await _db.Licenses.UpdateAsync(license, innerCt);
            if (terminateSessions)
                await TerminateActiveSessionsAsync(licenseId, now, innerCt);
            await _db.Audit.AppendAsync(new AdminAuditEntity
            {
                AtUtc = now,
                Action = auditAction,
                LicenseId = licenseId,
            }, innerCt);
        }, ct);
    }

    private async Task TerminateActiveSessionsAsync(long licenseId, DateTimeOffset now, CancellationToken ct)
    {
        await _db.Sessions.ExpireLeasesAsync(licenseId, now, ct);
        var active = await _db.Sessions.ListActiveByLicenseAsync(licenseId, ct);
        foreach (var s in active)
        {
            s.Status = SessionStatus.Terminated;
            await _db.Sessions.UpdateAsync(s, ct);
        }
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;
        var trimmed = displayName.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }
}
