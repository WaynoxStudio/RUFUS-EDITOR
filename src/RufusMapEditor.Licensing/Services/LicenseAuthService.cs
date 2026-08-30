using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Security;

namespace RufusMapEditor.Licensing.Services;

public sealed class LicenseOperationResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public SessionSuccessResponse? Session { get; init; }

    public static LicenseOperationResult Ok(SessionSuccessResponse session) =>
        new() { Success = true, Session = session };

    public static LicenseOperationResult Fail(string code, string? message = null) =>
        new() { Success = false, ErrorCode = code, Message = message };
}

/// <summary>
/// Core license auth logic (Activate / Heartbeat / Logout). Uses IServerClock only — never client clock.
/// </summary>
public sealed class LicenseAuthService
{
    private readonly ILicenseUnitOfWork _db;
    private readonly IServerClock _clock;
    private readonly LicenseLeaseOptions _lease;

    public LicenseAuthService(ILicenseUnitOfWork db, IServerClock clock, LicenseLeaseOptions? lease = null)
    {
        _db = db;
        _clock = clock;
        _lease = lease ?? LicenseLeaseOptions.CreateDefault();
    }

    public async Task<LicenseOperationResult> ActivateAsync(ActivateLicenseRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseCode) || string.IsNullOrWhiteSpace(request.DeviceId))
            return LicenseOperationResult.Fail(LicenseErrorCodes.InvalidRequest, "licenseCode and deviceId required");

        var normalized = LicenseCodeGenerator.Normalize(request.LicenseCode);
        var codeHash = LicenseCodeHasher.Hash(normalized);
        var deviceId = request.DeviceId.Trim();
        var now = _clock.UtcNow;

        LicenseOperationResult? result = null;
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var license = await _db.Licenses.GetByCodeHashAsync(codeHash, innerCt);
            if (license is null)
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.LicenseNotFound);
                return;
            }

            var gate = EvaluateLicenseGate(license, now);
            if (gate is not null)
            {
                result = gate;
                return;
            }

            if (!license.PermissionEditor)
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.EditorNotAllowed);
                return;
            }

            // First activation: Created → Active + expiresAt
            if (license.Status == LicenseStatus.Created)
            {
                license.Status = LicenseStatus.Active;
                license.FirstActivatedAtUtc = now;
                if (license.DurationDays is int days && days > 0)
                    license.ExpiresAtUtc = now.AddDays(days);
                await _db.Licenses.UpdateAsync(license, innerCt);
            }

            // Re-check expiry after activation calc
            gate = EvaluateLicenseGate(license, now);
            if (gate is not null)
            {
                result = gate;
                return;
            }

            var bound = await _db.Devices.ListBoundByLicenseAsync(license.Id, innerCt);
            var existingBound = bound.FirstOrDefault(d =>
                string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

            DeviceEntity existing;
            if (existingBound is not null)
            {
                existing = existingBound;
                existing.LastSeenAtUtc = now;
                await _db.Devices.UpdateAsync(existing, innerCt);
            }
            else
            {
                // After admin reset the row remains (status=Reset) due to UNIQUE(license_id, device_id).
                var prior = await _db.Devices.GetAnyAsync(license.Id, deviceId, innerCt);
                if (prior is not null)
                {
                    if (bound.Count >= license.MaxDevices)
                    {
                        result = LicenseOperationResult.Fail(LicenseErrorCodes.DeviceLimitReached);
                        return;
                    }

                    prior.Status = DeviceBindStatus.Bound;
                    prior.BoundAtUtc = now;
                    prior.LastSeenAtUtc = now;
                    await _db.Devices.UpdateAsync(prior, innerCt);
                    existing = prior;
                }
                else
                {
                    if (bound.Count >= license.MaxDevices)
                    {
                        result = LicenseOperationResult.Fail(LicenseErrorCodes.DeviceLimitReached);
                        return;
                    }

                    existing = await _db.Devices.InsertAsync(new DeviceEntity
                    {
                        LicenseId = license.Id,
                        DeviceId = deviceId,
                        BoundAtUtc = now,
                        LastSeenAtUtc = now,
                        Status = DeviceBindStatus.Bound,
                    }, innerCt);
                }
            }

            await _db.Sessions.ExpireLeasesAsync(license.Id, now, innerCt);
            var active = await _db.Sessions.ListActiveByLicenseAsync(license.Id, innerCt);

            // Same device with an active session: rotate (close old, create new) — counts as one seat.
            foreach (var s in active.Where(s => s.DeviceId == existing.Id))
            {
                s.Status = SessionStatus.Closed;
                await _db.Sessions.UpdateAsync(s, innerCt);
            }

            active = await _db.Sessions.ListActiveByLicenseAsync(license.Id, innerCt);
            if (active.Count >= license.MaxConcurrentSessions)
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.SessionLimitReached);
                return;
            }

            result = LicenseOperationResult.Ok(await IssueSessionAsync(license, existing, now, innerCt));
        }, ct);

        return result ?? LicenseOperationResult.Fail(LicenseErrorCodes.InvalidRequest);
    }

    public async Task<LicenseOperationResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionToken) || string.IsNullOrWhiteSpace(request.DeviceId))
            return LicenseOperationResult.Fail(LicenseErrorCodes.InvalidRequest);

        var now = _clock.UtcNow;
        var tokenHash = SessionTokenGenerator.Hash(request.SessionToken.Trim());
        LicenseOperationResult? result = null;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var session = await _db.Sessions.GetByTokenHashAsync(tokenHash, innerCt);
            // Closed / Terminated = explicit end. Active or soft-Expired can be renewed for the same device.
            if (session is null
                || session.Status is SessionStatus.Closed or SessionStatus.Terminated)
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.SessionInvalid);
                return;
            }

            if (session.Status is not (SessionStatus.Active or SessionStatus.Expired))
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.SessionInvalid);
                return;
            }

            var license = await _db.Licenses.GetByIdAsync(session.LicenseId, innerCt);
            if (license is null)
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.LicenseNotFound);
                return;
            }

            var gate = EvaluateLicenseGate(license, now);
            if (gate is not null)
            {
                session.Status = SessionStatus.Terminated;
                await _db.Sessions.UpdateAsync(session, innerCt);
                result = gate;
                return;
            }

            var device = (await _db.Devices.ListBoundByLicenseAsync(license.Id, innerCt))
                .FirstOrDefault(d => d.Id == session.DeviceId);
            if (device is null
                || !string.Equals(device.DeviceId, request.DeviceId.Trim(), StringComparison.Ordinal))
            {
                result = LicenseOperationResult.Fail(LicenseErrorCodes.DeviceMismatch);
                return;
            }

            // Soft lease expiry (app closed > lease window): renew instead of forcing re-type of license code.
            session.Status = SessionStatus.Active;
            session.LastRenewedAtUtc = now;
            session.LeaseExpiresAtUtc = now.AddSeconds(_lease.LeaseSeconds);
            await _db.Sessions.UpdateAsync(session, innerCt);
            device.LastSeenAtUtc = now;
            await _db.Devices.UpdateAsync(device, innerCt);

            // Return same opaque token (client already has it)
            var sessionResponse = new SessionSuccessResponse
            {
                SessionToken = request.SessionToken.Trim(),
                ExpiresAt = session.LeaseExpiresAtUtc,
                Permissions = new LicensePermissionsDto
                {
                    Editor = license.PermissionEditor,
                    Ai = license.PermissionAi,
                },
                LicenseExpiresAt = license.ExpiresAtUtc,
                HeartbeatSeconds = _lease.HeartbeatSeconds,
            };
            await ApplyAiUsageSnapshotAsync(sessionResponse, license, innerCt);
            result = LicenseOperationResult.Ok(sessionResponse);
        }, ct);

        return result ?? LicenseOperationResult.Fail(LicenseErrorCodes.InvalidRequest);
    }

    public async Task<LicenseOperationResult> LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionToken) || string.IsNullOrWhiteSpace(request.DeviceId))
            return LicenseOperationResult.Fail(LicenseErrorCodes.InvalidRequest);

        var tokenHash = SessionTokenGenerator.Hash(request.SessionToken.Trim());
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var session = await _db.Sessions.GetByTokenHashAsync(tokenHash, innerCt);
            if (session is null)
                return;

            var devices = await _db.Devices.ListBoundByLicenseAsync(session.LicenseId, innerCt);
            var device = devices.FirstOrDefault(d => d.Id == session.DeviceId);
            if (device is null
                || !string.Equals(device.DeviceId, request.DeviceId.Trim(), StringComparison.Ordinal))
                return;

            if (session.Status == SessionStatus.Active)
            {
                session.Status = SessionStatus.Closed;
                await _db.Sessions.UpdateAsync(session, innerCt);
            }
        }, ct);

        return LicenseOperationResult.Ok(new SessionSuccessResponse
        {
            SessionToken = "",
            ExpiresAt = _clock.UtcNow,
            Permissions = new LicensePermissionsDto(),
        });
    }

    /// <summary>Effective gate including derived expiry (no cron required).</summary>
    public static LicenseOperationResult? EvaluateLicenseGate(LicenseEntity license, DateTimeOffset nowUtc)
    {
        if (license.Status == LicenseStatus.Suspended)
            return LicenseOperationResult.Fail(LicenseErrorCodes.LicenseSuspended);
        if (license.Status == LicenseStatus.Revoked)
            return LicenseOperationResult.Fail(LicenseErrorCodes.LicenseRevoked);

        if (license.ExpiresAtUtc is { } exp && nowUtc >= exp)
            return LicenseOperationResult.Fail(LicenseErrorCodes.LicenseExpired);

        if (license.Status is not (LicenseStatus.Created or LicenseStatus.Active))
            return LicenseOperationResult.Fail(LicenseErrorCodes.LicenseInactive);

        return null;
    }

    private async Task<SessionSuccessResponse> IssueSessionAsync(
        LicenseEntity license,
        DeviceEntity device,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var plaintext = SessionTokenGenerator.Generate();
        var leaseUntil = now.AddSeconds(_lease.LeaseSeconds);
        await _db.Sessions.InsertAsync(new SessionEntity
        {
            LicenseId = license.Id,
            DeviceId = device.Id,
            TokenHash = SessionTokenGenerator.Hash(plaintext),
            CreatedAtUtc = now,
            LastRenewedAtUtc = now,
            LeaseExpiresAtUtc = leaseUntil,
            Status = SessionStatus.Active,
        }, ct);

        var response = new SessionSuccessResponse
        {
            SessionToken = plaintext,
            ExpiresAt = leaseUntil,
            Permissions = new LicensePermissionsDto
            {
                Editor = license.PermissionEditor,
                Ai = license.PermissionAi,
            },
            LicenseExpiresAt = license.ExpiresAtUtc,
            HeartbeatSeconds = _lease.HeartbeatSeconds,
        };
        await ApplyAiUsageSnapshotAsync(response, license, ct);
        return response;
    }

    private async Task ApplyAiUsageSnapshotAsync(
        SessionSuccessResponse response,
        LicenseEntity license,
        CancellationToken ct)
    {
        response.AiDailyLimit = license.AiDailyLimit;
        response.AiMonthlyLimit = license.AiMonthlyLimit;
        var (today, month) = await _db.AiUsage.GetUsageTotalsAsync(license.Id, _clock.UtcNow, ct);
        response.AiUsageToday = today;
        response.AiUsageMonth = month;
    }
}
