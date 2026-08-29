using Microsoft.Data.Sqlite;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Sqlite;

internal abstract class SqliteRepoBase
{
    protected readonly SqliteConnection Connection;
    internal SqliteTransaction? CurrentTransaction { get; set; }

    protected SqliteRepoBase(SqliteConnection connection) => Connection = connection;

    protected SqliteCommand CreateCommand(string sql)
    {
        var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        if (CurrentTransaction is not null)
            cmd.Transaction = CurrentTransaction;
        return cmd;
    }

    protected static string Fmt(DateTimeOffset dto) => dto.UtcDateTime.ToString("O");

    protected static DateTimeOffset ParseDto(string s) => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

    protected static DateTimeOffset? ParseDtoNullable(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : ParseDto(s);
}

internal sealed class SqliteLicenseRepository : SqliteRepoBase, ILicenseRepository
{
    public SqliteLicenseRepository(SqliteConnection connection) : base(connection) { }

    public async Task<LicenseEntity?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("SELECT * FROM rufus_licenses WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        return await ReadOneAsync(cmd, ct);
    }

    public async Task<LicenseEntity?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("SELECT * FROM rufus_licenses WHERE code_hash = $h");
        cmd.Parameters.AddWithValue("$h", codeHash);
        return await ReadOneAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<LicenseEntity>> ListAsync(CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("SELECT * FROM rufus_licenses ORDER BY id DESC");
        var list = new List<LicenseEntity>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(Map(r));
        return list;
    }

    public async Task<LicenseEntity> InsertAsync(LicenseEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_licenses (
              code_hash, code_display_hint, status, created_at_utc, first_activated_at_utc, expires_at_utc,
              duration_days, max_devices, max_concurrent_sessions, permission_editor, permission_ai,
              ai_daily_limit, ai_monthly_limit, admin_notes, display_name)
            VALUES ($code_hash, $hint, $status, $created, $activated, $expires, $duration, $max_dev, $max_sess, $pe, $pai,
              $ai_day, $ai_month, $notes, $display_name);
            SELECT last_insert_rowid();
            """);
        Bind(cmd, entity);
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        entity.Id = id;
        return entity;
    }

    public async Task UpdateAsync(LicenseEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            UPDATE rufus_licenses SET
              code_hash=$code_hash, code_display_hint=$hint, status=$status, created_at_utc=$created,
              first_activated_at_utc=$activated, expires_at_utc=$expires, duration_days=$duration,
              max_devices=$max_dev, max_concurrent_sessions=$max_sess, permission_editor=$pe, permission_ai=$pai,
              ai_daily_limit=$ai_day, ai_monthly_limit=$ai_month,
              admin_notes=$notes, display_name=$display_name
            WHERE id=$id
            """);
        Bind(cmd, entity);
        cmd.Parameters.AddWithValue("$id", entity.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var cmdSessions = CreateCommand("DELETE FROM rufus_sessions WHERE license_id = $id");
        cmdSessions.Parameters.AddWithValue("$id", id);
        await cmdSessions.ExecuteNonQueryAsync(ct);

        await using var cmdDevices = CreateCommand("DELETE FROM rufus_devices WHERE license_id = $id");
        cmdDevices.Parameters.AddWithValue("$id", id);
        await cmdDevices.ExecuteNonQueryAsync(ct);

        await using var cmdAiEvents = CreateCommand("DELETE FROM rufus_ai_usage_events WHERE license_id = $id");
        cmdAiEvents.Parameters.AddWithValue("$id", id);
        await cmdAiEvents.ExecuteNonQueryAsync(ct);

        await using var cmdAiQuota = CreateCommand("DELETE FROM rufus_ai_quota_counters WHERE license_id = $id");
        cmdAiQuota.Parameters.AddWithValue("$id", id);
        await cmdAiQuota.ExecuteNonQueryAsync(ct);

        await using var cmdLicense = CreateCommand("DELETE FROM rufus_licenses WHERE id = $id");
        cmdLicense.Parameters.AddWithValue("$id", id);
        var rows = await cmdLicense.ExecuteNonQueryAsync(ct);
        if (rows == 0)
            throw new InvalidOperationException("license not found");
    }

    private static void Bind(SqliteCommand cmd, LicenseEntity e)
    {
        cmd.Parameters.AddWithValue("$code_hash", e.CodeHash);
        cmd.Parameters.AddWithValue("$hint", e.CodeDisplayHint);
        cmd.Parameters.AddWithValue("$status", e.Status.ToString());
        cmd.Parameters.AddWithValue("$created", Fmt(e.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$activated", (object?)FmtNullable(e.FirstActivatedAtUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$expires", (object?)FmtNullable(e.ExpiresAtUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$duration", (object?)e.DurationDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$max_dev", e.MaxDevices);
        cmd.Parameters.AddWithValue("$max_sess", e.MaxConcurrentSessions);
        cmd.Parameters.AddWithValue("$pe", e.PermissionEditor ? 1 : 0);
        cmd.Parameters.AddWithValue("$pai", e.PermissionAi ? 1 : 0);
        cmd.Parameters.AddWithValue("$ai_day", (object?)e.AiDailyLimit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ai_month", (object?)e.AiMonthlyLimit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)e.AdminNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$display_name", (object?)e.DisplayName ?? DBNull.Value);
    }

    private static string? FmtNullable(DateTimeOffset? dto) => dto is null ? null : Fmt(dto.Value);

    private async Task<LicenseEntity?> ReadOneAsync(SqliteCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return Map(r);
    }

    private static LicenseEntity Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        CodeHash = r.GetString(r.GetOrdinal("code_hash")),
        CodeDisplayHint = r.GetString(r.GetOrdinal("code_display_hint")),
        Status = Enum.Parse<LicenseStatus>(r.GetString(r.GetOrdinal("status"))),
        CreatedAtUtc = ParseDto(r.GetString(r.GetOrdinal("created_at_utc"))),
        FirstActivatedAtUtc = ParseDtoNullable(r.IsDBNull(r.GetOrdinal("first_activated_at_utc")) ? null : r.GetString(r.GetOrdinal("first_activated_at_utc"))),
        ExpiresAtUtc = ParseDtoNullable(r.IsDBNull(r.GetOrdinal("expires_at_utc")) ? null : r.GetString(r.GetOrdinal("expires_at_utc"))),
        DurationDays = r.IsDBNull(r.GetOrdinal("duration_days")) ? null : r.GetInt32(r.GetOrdinal("duration_days")),
        MaxDevices = r.GetInt32(r.GetOrdinal("max_devices")),
        MaxConcurrentSessions = r.GetInt32(r.GetOrdinal("max_concurrent_sessions")),
        PermissionEditor = r.GetInt32(r.GetOrdinal("permission_editor")) != 0,
        PermissionAi = r.GetInt32(r.GetOrdinal("permission_ai")) != 0,
        AiDailyLimit = ReadNullableInt(r, "ai_daily_limit"),
        AiMonthlyLimit = ReadNullableInt(r, "ai_monthly_limit"),
        AdminNotes = r.IsDBNull(r.GetOrdinal("admin_notes")) ? null : r.GetString(r.GetOrdinal("admin_notes")),
        DisplayName = ReadOptionalString(r, "display_name"),
    };

    private static string? ReadOptionalString(SqliteDataReader r, string column)
    {
        try
        {
            var o = r.GetOrdinal(column);
            return r.IsDBNull(o) ? null : r.GetString(o);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static int? ReadNullableInt(SqliteDataReader r, string column)
    {
        try
        {
            var o = r.GetOrdinal(column);
            return r.IsDBNull(o) ? null : r.GetInt32(o);
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }
}
