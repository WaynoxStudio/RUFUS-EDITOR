using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.Licensing.Client;

public enum LicenseGateOutcome
{
    Authorized,
    NeedsActivation,
    Denied,
    TransientNetwork,
}

public sealed class LicenseGateResult
{
    public LicenseGateOutcome Outcome { get; init; }
    public LicenseSessionLocalState? Session { get; init; }
    public string? ErrorCode { get; init; }
    public string UserMessage { get; init; } = "";
}

/// <summary>
/// Editor-side license orchestration (no UI). Backend remains authority; local store is a cache only.
/// </summary>
public sealed class EditorLicenseSessionService
{
    private readonly ILicenseClient _client;
    private readonly ISessionStore _store;
    private readonly IDeviceIdProvider _deviceId;
    private readonly LicenseLeaseOptions _lease;
    private readonly string _clientVersion;
    private readonly Func<DateTimeOffset> _utcNow;

    public EditorLicenseSessionService(
        ILicenseClient client,
        ISessionStore store,
        IDeviceIdProvider deviceId,
        LicenseLeaseOptions? lease = null,
        string? clientVersion = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _client = client;
        _store = store;
        _deviceId = deviceId;
        _lease = lease ?? LicenseLeaseOptions.CreateDefault();
        _clientVersion = string.IsNullOrWhiteSpace(clientVersion) ? "1.0" : clientVersion.Trim();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public int HeartbeatSeconds =>
        _lease.HeartbeatSeconds > 10 ? _lease.HeartbeatSeconds : LicenseLeaseOptions.CreateDefault().HeartbeatSeconds;

    public string DeviceId => _deviceId.GetDeviceId();

    public async Task<LicenseSessionLocalState?> LoadLocalAsync(CancellationToken ct = default) =>
        await _store.LoadAsync(ct);

    /// <summary>Resume from DPAPI store + validate against backend.</summary>
    public async Task<LicenseGateResult> TryResumeAsync(CancellationToken ct = default)
    {
        var local = await _store.LoadAsync(ct);
        if (local is null || string.IsNullOrWhiteSpace(local.SessionToken))
        {
            // No live session token — still try silent activate if we kept the license code.
            if (!string.IsNullOrWhiteSpace(local?.LicenseCode))
                return await TrySilentReactivateAsync(local!.LicenseCode!, ct);
            return NeedActivation();
        }

        var deviceId = _deviceId.GetDeviceId();
        if (!string.IsNullOrWhiteSpace(local.DeviceId)
            && !string.Equals(local.DeviceId, deviceId, StringComparison.Ordinal))
        {
            await _store.ClearAsync(ct);
            return Denied(LicenseErrorCodes.DeviceMismatch);
        }

        var result = await _client.ValidateSessionAsync(new HeartbeatRequest
        {
            SessionToken = local.SessionToken,
            DeviceId = deviceId,
        }, ct);

        var applied = await ApplyRemoteResultAsync(
            result, deviceId, clearOnExplicitFailure: true, ct, preserveLicenseCode: local.LicenseCode);

        if (applied.Outcome == LicenseGateOutcome.Authorized)
            return applied;

        // Soft-expired / wiped server session: re-activate with the saved code (same device).
        if (CanSilentReactivate(applied.ErrorCode) && !string.IsNullOrWhiteSpace(local.LicenseCode))
            return await TrySilentReactivateAsync(local.LicenseCode!, ct);

        return applied;
    }

    public async Task<LicenseGateResult> ActivateAsync(string licenseCode, CancellationToken ct = default)
    {
        var deviceId = _deviceId.GetDeviceId();
        var normalizedCode = licenseCode.Trim();
        var result = await _client.ActivateAsync(new ActivateLicenseRequest
        {
            LicenseCode = normalizedCode,
            DeviceId = deviceId,
            ClientVersion = _clientVersion,
        }, ct);

        if (!result.Success)
        {
            if (result.IsTransientNetworkError)
                return Transient(result.ErrorCode);
            return Denied(result.ErrorCode);
        }

        if (result.Session is null)
            return Denied(LicenseErrorCodes.InvalidRequest);

        if (!result.Session.Permissions.Editor)
        {
            await _store.ClearAsync(ct);
            return Denied(LicenseErrorCodes.EditorNotAllowed);
        }

        var state = LicenseSessionMapper.FromSuccess(result.Session, deviceId);
        state.LicenseCode = normalizedCode;
        await _store.SaveAsync(state, ct);
        return new LicenseGateResult
        {
            Outcome = LicenseGateOutcome.Authorized,
            Session = state,
        };
    }

    /// <summary>
    /// Periodic heartbeat. Transient network → keep session while server-issued lease not past.
    /// Explicit backend rejection → clear store.
    /// </summary>
    public async Task<LicenseGateResult> HeartbeatAsync(CancellationToken ct = default)
    {
        var local = await _store.LoadAsync(ct);
        if (local is null || string.IsNullOrWhiteSpace(local.SessionToken))
            return NeedActivation();

        var deviceId = string.IsNullOrWhiteSpace(local.DeviceId) ? _deviceId.GetDeviceId() : local.DeviceId;
        var result = await _client.HeartbeatAsync(new HeartbeatRequest
        {
            SessionToken = local.SessionToken,
            DeviceId = deviceId,
        }, ct);

        if (result.IsTransientNetworkError)
        {
            // Tolerance window uses last server-stamped lease expiry (not license end date).
            if (local.LeaseExpiresAt > _utcNow())
            {
                return new LicenseGateResult
                {
                    Outcome = LicenseGateOutcome.TransientNetwork,
                    Session = local,
                    ErrorCode = LicenseErrorCodes.NetworkUnavailable,
                    UserMessage = LicenseUserMessages.NetworkLost,
                };
            }

            await PreserveLicenseCodeOnlyAsync(local, ct);
            return Denied(LicenseErrorCodes.SessionInvalid);
        }

        return await ApplyRemoteResultAsync(
            result, deviceId, clearOnExplicitFailure: true, ct, preserveLicenseCode: local.LicenseCode);
    }

    public async Task LogoutBestEffortAsync(CancellationToken ct = default)
    {
        var local = await _store.LoadAsync(ct);
        if (local is null || string.IsNullOrWhiteSpace(local.SessionToken))
            return;

        var deviceId = string.IsNullOrWhiteSpace(local.DeviceId) ? _deviceId.GetDeviceId() : local.DeviceId;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(3));
            await _client.LogoutAsync(new LogoutRequest
            {
                SessionToken = local.SessionToken,
                DeviceId = deviceId,
            }, linked.Token);
        }
        catch
        {
            // best-effort
        }
    }

    public Task ClearLocalAsync(CancellationToken ct = default) => _store.ClearAsync(ct);

    private async Task<LicenseGateResult> TrySilentReactivateAsync(string licenseCode, CancellationToken ct)
    {
        var reactivated = await ActivateAsync(licenseCode, ct);
        if (reactivated.Outcome == LicenseGateOutcome.Authorized)
            return reactivated;

        // Keep the code in store for the next attempt unless the license itself is dead.
        if (reactivated.Outcome == LicenseGateOutcome.Denied
            && IsFatalLicenseRejection(reactivated.ErrorCode))
            await _store.ClearAsync(ct);

        return reactivated;
    }

    private static bool CanSilentReactivate(string? errorCode) =>
        string.IsNullOrWhiteSpace(errorCode)
        || string.Equals(errorCode, LicenseErrorCodes.SessionInvalid, StringComparison.Ordinal);

    private static bool IsFatalLicenseRejection(string? code) =>
        code is LicenseErrorCodes.LicenseExpired
            or LicenseErrorCodes.LicenseRevoked
            or LicenseErrorCodes.LicenseSuspended
            or LicenseErrorCodes.LicenseNotFound
            or LicenseErrorCodes.EditorNotAllowed
            or LicenseErrorCodes.DeviceMismatch
            or LicenseErrorCodes.DeviceLimitReached;

    private async Task PreserveLicenseCodeOnlyAsync(LicenseSessionLocalState previous, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(previous.LicenseCode))
        {
            await _store.SaveAsync(new LicenseSessionLocalState
            {
                LicenseCode = previous.LicenseCode,
                DeviceId = previous.DeviceId,
            }, ct);
        }
        else
        {
            await _store.ClearAsync(ct);
        }
    }

    private async Task<LicenseGateResult> ApplyRemoteResultAsync(
        LicenseOperationClientResult result,
        string deviceId,
        bool clearOnExplicitFailure,
        CancellationToken ct,
        string? preserveLicenseCode = null)
    {
        if (result.Success && result.Session is not null)
        {
            if (!result.Session.Permissions.Editor)
            {
                if (clearOnExplicitFailure)
                    await _store.ClearAsync(ct);
                return Denied(LicenseErrorCodes.EditorNotAllowed);
            }

            var state = LicenseSessionMapper.FromSuccess(result.Session, deviceId);
            state.LicenseCode = preserveLicenseCode
                ?? (await _store.LoadAsync(ct))?.LicenseCode;
            await _store.SaveAsync(state, ct);
            return new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.Authorized,
                Session = state,
            };
        }

        if (result.IsTransientNetworkError)
            return Transient(result.ErrorCode);

        if (clearOnExplicitFailure && LicenseUserMessages.IsExplicitRejection(result.ErrorCode))
        {
            // Fatal license states wipe everything. Soft session loss keeps the code for silent re-activate.
            if (IsFatalLicenseRejection(result.ErrorCode))
            {
                await _store.ClearAsync(ct);
            }
            else
            {
                var previous = await _store.LoadAsync(ct);
                var code = preserveLicenseCode ?? previous?.LicenseCode;
                if (!string.IsNullOrWhiteSpace(code))
                {
                    await _store.SaveAsync(new LicenseSessionLocalState
                    {
                        LicenseCode = code,
                        DeviceId = previous?.DeviceId ?? deviceId,
                    }, ct);
                }
                else
                {
                    await _store.ClearAsync(ct);
                }
            }
        }

        return Denied(result.ErrorCode);
    }

    private static LicenseGateResult NeedActivation() => new()
    {
        Outcome = LicenseGateOutcome.NeedsActivation,
        UserMessage = "",
    };

    private static LicenseGateResult Denied(string? code) => new()
    {
        Outcome = LicenseGateOutcome.Denied,
        ErrorCode = code,
        UserMessage = LicenseUserMessages.ForErrorCode(code),
    };

    private static LicenseGateResult Transient(string? code) => new()
    {
        Outcome = LicenseGateOutcome.TransientNetwork,
        ErrorCode = code ?? LicenseErrorCodes.NetworkUnavailable,
        UserMessage = LicenseUserMessages.NetworkLost,
    };
}
