using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Security;

namespace RufusMapEditor.Licensing.Services;

public sealed class LicenseAiAuthContext
{
    public long LicenseId { get; init; }
    public long SessionId { get; init; }
    public long DeviceRowId { get; init; }
    public bool PermissionAi { get; init; }
    public int? AiDailyLimit { get; init; }
    public int? AiMonthlyLimit { get; init; }
}

public sealed class LicenseAiAuthResult
{
    public bool Success { get; init; }
    public LicenseAiAuthContext? Context { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    public static LicenseAiAuthResult Ok(LicenseAiAuthContext ctx) =>
        new() { Success = true, Context = ctx };

    public static LicenseAiAuthResult Fail(string code, string? message = null) =>
        new() { Success = false, ErrorCode = code, Message = message };
}

/// <summary>
/// LIC.6 — validates session Bearer for AI generate (before body / OpenAI).
/// </summary>
public sealed class LicenseAiAuthService
{
    private readonly ILicenseUnitOfWork _db;
    private readonly IServerClock _clock;

    public LicenseAiAuthService(ILicenseUnitOfWork db, IServerClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Looks up session by token hash. Returns null Result.Context path when token is not a known session
    /// (caller may try legacy). Explicit license failures return Fail with error code.
    /// </summary>
    public async Task<(bool SessionFound, LicenseAiAuthResult Result)> TryAuthorizeSessionAsync(
        string bearerToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return (false, LicenseAiAuthResult.Fail(LicenseErrorCodes.SessionInvalid));

        var hash = SessionTokenGenerator.Hash(bearerToken.Trim());
        var now = _clock.UtcNow;

        LicenseAiAuthResult? result = null;
        var found = false;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var session = await _db.Sessions.GetByTokenHashAsync(hash, innerCt);
            if (session is null)
                return;

            found = true;

            if (session.Status != SessionStatus.Active)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.SessionInvalid, "Sesión inválida.");
                return;
            }

            if (session.LeaseExpiresAtUtc <= now)
            {
                session.Status = SessionStatus.Expired;
                await _db.Sessions.UpdateAsync(session, innerCt);
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.SessionInvalid, "Sesión expirada.");
                return;
            }

            var license = await _db.Licenses.GetByIdAsync(session.LicenseId, innerCt);
            if (license is null)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.LicenseNotFound);
                return;
            }

            if (license.Status == LicenseStatus.Suspended)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.LicenseSuspended, "Licencia suspendida.");
                return;
            }

            if (license.Status == LicenseStatus.Revoked)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.LicenseRevoked, "Licencia revocada.");
                return;
            }

            if (license.ExpiresAtUtc is { } exp && now >= exp)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.LicenseExpired, "Licencia caducada.");
                return;
            }

            if (license.Status is not (LicenseStatus.Active or LicenseStatus.Created))
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.LicenseInactive);
                return;
            }

            var device = (await _db.Devices.ListBoundByLicenseAsync(license.Id, innerCt))
                .FirstOrDefault(d => d.Id == session.DeviceId);
            if (device is null || device.Status != DeviceBindStatus.Bound)
            {
                result = LicenseAiAuthResult.Fail(LicenseErrorCodes.DeviceMismatch, "Dispositivo no autorizado.");
                return;
            }

            if (!license.PermissionAi)
            {
                result = LicenseAiAuthResult.Fail(
                    LicenseErrorCodes.AiNotAllowed,
                    "Tu licencia no incluye acceso al Asistente IA.");
                return;
            }

            result = LicenseAiAuthResult.Ok(new LicenseAiAuthContext
            {
                LicenseId = license.Id,
                SessionId = session.Id,
                DeviceRowId = device.Id,
                PermissionAi = true,
                AiDailyLimit = license.AiDailyLimit,
                AiMonthlyLimit = license.AiMonthlyLimit,
            });
        }, ct);

        if (!found)
            return (false, LicenseAiAuthResult.Fail(LicenseErrorCodes.SessionInvalid));

        return (true, result ?? LicenseAiAuthResult.Fail(LicenseErrorCodes.InvalidRequest));
    }
}
