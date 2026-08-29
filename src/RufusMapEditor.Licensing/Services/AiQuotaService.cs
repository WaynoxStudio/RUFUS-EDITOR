using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Services;

/// <summary>
/// LIC.6 — AI generation quota (server clock). Counts when entering the OpenAI path.
/// OpenAI errors still consume a unit (documented policy) to prevent retry abuse.
/// </summary>
public sealed class AiQuotaService
{
    private readonly ILicenseUnitOfWork _db;
    private readonly IServerClock _clock;

    public AiQuotaService(ILicenseUnitOfWork db, IServerClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<(bool Allowed, string? DenyCode)> TryConsumeForGenerationAsync(
        LicenseAiAuthContext ctx,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var allowed = false;
        string? deny = null;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            // Re-read limits under lock for concurrency safety.
            var license = await _db.Licenses.GetByIdAsync(ctx.LicenseId, innerCt);
            if (license is null)
            {
                deny = LicenseErrorCodes.LicenseNotFound;
                return;
            }

            var (ok, code) = await _db.AiUsage.TryConsumeAsync(
                ctx.LicenseId,
                license.AiDailyLimit,
                license.AiMonthlyLimit,
                now,
                innerCt);
            allowed = ok;
            deny = code;
        }, ct);

        return (allowed, deny);
    }

    public async Task RecordUsageEventAsync(
        LicenseAiAuthContext ctx,
        string action,
        string? model,
        int? inputTokens,
        int? outputTokens,
        bool openAiSucceeded,
        CancellationToken ct = default)
    {
        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            await _db.AiUsage.AppendEventAsync(new AiUsageEventEntity
            {
                LicenseId = ctx.LicenseId,
                SessionId = ctx.SessionId,
                AtUtc = _clock.UtcNow,
                Action = action,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                OpenAiSucceeded = openAiSucceeded,
            }, innerCt);
        }, ct);
    }

    public Task<(int Today, int Month)> GetUsageAsync(long licenseId, CancellationToken ct = default) =>
        _db.AiUsage.GetUsageTotalsAsync(licenseId, _clock.UtcNow, ct);
}
