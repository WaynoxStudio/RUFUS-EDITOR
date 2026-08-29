using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Abstractions;

public interface IAiUsageRepository
{
    /// <summary>
    /// Atomically checks daily/monthly limits and increments counters if allowed.
    /// Uses the ambient UoW transaction (BEGIN IMMEDIATE via ExecuteInTransaction).
    /// </summary>
    Task<(bool Allowed, string? DenyCode)> TryConsumeAsync(
        long licenseId,
        int? dailyLimit,
        int? monthlyLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    Task AppendEventAsync(AiUsageEventEntity entry, CancellationToken ct = default);

    Task<int> CountInPeriodAsync(long licenseId, DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcExclusive, CancellationToken ct = default);

    Task<(int Today, int Month)> GetUsageTotalsAsync(long licenseId, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>
    /// ADMIN.USAGE.1 — read-only SQL aggregate over real columns only.
    /// Null bounds = unbounded. Does not load row payloads to the client.
    /// </summary>
    Task<AiUsageTokenTotals> AggregateTokensAsync(
        DateTimeOffset? fromUtcInclusive,
        DateTimeOffset? toUtcExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<AiUsageActionTotals>> AggregateByActionAsync(
        DateTimeOffset? fromUtcInclusive,
        DateTimeOffset? toUtcExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<AiUsageDayTotals>> AggregateByDayAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<AiUsageMonthTotals>> AggregateByMonthAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default);
}
