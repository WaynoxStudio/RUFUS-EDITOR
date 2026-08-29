using Microsoft.Data.Sqlite;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Sqlite;

internal sealed class SqliteAiUsageRepository : SqliteRepoBase, IAiUsageRepository
{
    public SqliteAiUsageRepository(SqliteConnection connection) : base(connection) { }

    public async Task<(bool Allowed, string? DenyCode)> TryConsumeAsync(
        long licenseId,
        int? dailyLimit,
        int? monthlyLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        var dayKey = nowUtc.UtcDateTime.ToString("yyyy-MM-dd");
        var monthKey = nowUtc.UtcDateTime.ToString("yyyy-MM");

        var dayCount = await GetCounterAsync(licenseId, "day", dayKey, ct);
        if (dailyLimit is int dLim && dayCount >= dLim)
            return (false, LicenseErrorCodes.AiQuotaDailyExceeded);

        var monthCount = await GetCounterAsync(licenseId, "month", monthKey, ct);
        if (monthlyLimit is int mLim && monthCount >= mLim)
            return (false, LicenseErrorCodes.AiQuotaMonthlyExceeded);

        await UpsertIncrementAsync(licenseId, "day", dayKey, ct);
        await UpsertIncrementAsync(licenseId, "month", monthKey, ct);
        return (true, null);
    }

    public async Task AppendEventAsync(AiUsageEventEntity entry, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_ai_usage_events (
              license_id, session_id, at_utc, action, model, input_tokens, output_tokens, openai_succeeded)
            VALUES ($lid, $sid, $at, $action, $model, $tin, $tout, $ok)
            """);
        cmd.Parameters.AddWithValue("$lid", entry.LicenseId);
        cmd.Parameters.AddWithValue("$sid", (object?)entry.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", Fmt(entry.AtUtc));
        cmd.Parameters.AddWithValue("$action", entry.Action);
        cmd.Parameters.AddWithValue("$model", (object?)entry.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tin", (object?)entry.InputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tout", (object?)entry.OutputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ok", entry.OpenAiSucceeded ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountInPeriodAsync(
        long licenseId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            SELECT COUNT(*) FROM rufus_ai_usage_events
            WHERE license_id=$lid AND at_utc >= $from AND at_utc < $to
            """);
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$from", Fmt(fromUtcInclusive));
        cmd.Parameters.AddWithValue("$to", Fmt(toUtcExclusive));
        var n = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(n);
    }

    public async Task<(int Today, int Month)> GetUsageTotalsAsync(long licenseId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var dayKey = nowUtc.UtcDateTime.ToString("yyyy-MM-dd");
        var monthKey = nowUtc.UtcDateTime.ToString("yyyy-MM");
        var today = await GetCounterAsync(licenseId, "day", dayKey, ct);
        var month = await GetCounterAsync(licenseId, "month", monthKey, ct);
        return (today, month);
    }

    private async Task<int> GetCounterAsync(long licenseId, string periodType, string periodKey, CancellationToken ct)
    {
        await using var cmd = CreateCommand("""
            SELECT count FROM rufus_ai_quota_counters
            WHERE license_id=$lid AND period_type=$pt AND period_key=$pk
            """);
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$pt", periodType);
        cmd.Parameters.AddWithValue("$pk", periodKey);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
    }

    private async Task UpsertIncrementAsync(long licenseId, string periodType, string periodKey, CancellationToken ct)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_ai_quota_counters (license_id, period_type, period_key, count)
            VALUES ($lid, $pt, $pk, 1)
            ON CONFLICT(license_id, period_type, period_key)
            DO UPDATE SET count = count + 1
            """);
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$pt", periodType);
        cmd.Parameters.AddWithValue("$pk", periodKey);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<AiUsageTokenTotals> AggregateTokensAsync(
        DateTimeOffset? fromUtcInclusive,
        DateTimeOffset? toUtcExclusive,
        CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            SELECT COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0)
            FROM rufus_ai_usage_events
            WHERE ($from IS NULL OR at_utc >= $from)
              AND ($to IS NULL OR at_utc < $to)
            """);
        BindRange(cmd, fromUtcInclusive, toUtcExclusive);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return default;
        return new AiUsageTokenTotals(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2));
    }

    public async Task<IReadOnlyList<AiUsageActionTotals>> AggregateByActionAsync(
        DateTimeOffset? fromUtcInclusive,
        DateTimeOffset? toUtcExclusive,
        CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            SELECT action,
                   COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0)
            FROM rufus_ai_usage_events
            WHERE ($from IS NULL OR at_utc >= $from)
              AND ($to IS NULL OR at_utc < $to)
            GROUP BY action
            ORDER BY action
            """);
        BindRange(cmd, fromUtcInclusive, toUtcExclusive);
        var list = new List<AiUsageActionTotals>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AiUsageActionTotals(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return list;
    }

    public async Task<IReadOnlyList<AiUsageDayTotals>> AggregateByDayAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default)
    {
        // at_utc is stored as ISO-8601 ("O"); substr(1,10) = yyyy-MM-dd UTC date.
        await using var cmd = CreateCommand("""
            SELECT substr(at_utc, 1, 10),
                   COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0)
            FROM rufus_ai_usage_events
            WHERE at_utc >= $from AND at_utc < $to
            GROUP BY substr(at_utc, 1, 10)
            ORDER BY 1
            """);
        cmd.Parameters.AddWithValue("$from", Fmt(fromUtcInclusive));
        cmd.Parameters.AddWithValue("$to", Fmt(toUtcExclusive));
        var list = new List<AiUsageDayTotals>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AiUsageDayTotals(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return list;
    }

    public async Task<IReadOnlyList<AiUsageMonthTotals>> AggregateByMonthAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            SELECT substr(at_utc, 1, 7),
                   COUNT(*),
                   COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0)
            FROM rufus_ai_usage_events
            WHERE at_utc >= $from AND at_utc < $to
            GROUP BY substr(at_utc, 1, 7)
            ORDER BY 1
            """);
        cmd.Parameters.AddWithValue("$from", Fmt(fromUtcInclusive));
        cmd.Parameters.AddWithValue("$to", Fmt(toUtcExclusive));
        var list = new List<AiUsageMonthTotals>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AiUsageMonthTotals(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return list;
    }

    private static void BindRange(SqliteCommand cmd, DateTimeOffset? fromUtcInclusive, DateTimeOffset? toUtcExclusive)
    {
        cmd.Parameters.AddWithValue("$from", fromUtcInclusive is null ? DBNull.Value : Fmt(fromUtcInclusive.Value));
        cmd.Parameters.AddWithValue("$to", toUtcExclusive is null ? DBNull.Value : Fmt(toUtcExclusive.Value));
    }
}
