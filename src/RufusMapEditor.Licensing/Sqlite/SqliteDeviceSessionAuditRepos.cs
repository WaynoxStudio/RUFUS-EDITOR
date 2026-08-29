using Microsoft.Data.Sqlite;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Sqlite;

internal sealed class SqliteDeviceRepository : SqliteRepoBase, IDeviceRepository
{
    public SqliteDeviceRepository(SqliteConnection connection) : base(connection) { }

    public async Task<IReadOnlyList<DeviceEntity>> ListBoundByLicenseAsync(long licenseId, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand(
            "SELECT * FROM rufus_devices WHERE license_id=$lid AND status='Bound'");
        cmd.Parameters.AddWithValue("$lid", licenseId);
        var list = new List<DeviceEntity>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(Map(r));
        return list;
    }

    public async Task<DeviceEntity?> GetBoundAsync(long licenseId, string deviceId, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand(
            "SELECT * FROM rufus_devices WHERE license_id=$lid AND device_id=$did AND status='Bound'");
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$did", deviceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return Map(r);
    }

    public async Task<DeviceEntity?> GetAnyAsync(long licenseId, string deviceId, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand(
            "SELECT * FROM rufus_devices WHERE license_id=$lid AND device_id=$did LIMIT 1");
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$did", deviceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return Map(r);
    }

    public async Task<DeviceEntity> InsertAsync(DeviceEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_devices (license_id, device_id, bound_at_utc, last_seen_at_utc, status)
            VALUES ($lid, $did, $bound, $seen, $status);
            SELECT last_insert_rowid();
            """);
        cmd.Parameters.AddWithValue("$lid", entity.LicenseId);
        cmd.Parameters.AddWithValue("$did", entity.DeviceId);
        cmd.Parameters.AddWithValue("$bound", Fmt(entity.BoundAtUtc));
        cmd.Parameters.AddWithValue("$seen", Fmt(entity.LastSeenAtUtc));
        cmd.Parameters.AddWithValue("$status", entity.Status.ToString());
        entity.Id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return entity;
    }

    public async Task UpdateAsync(DeviceEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            UPDATE rufus_devices SET license_id=$lid, device_id=$did, bound_at_utc=$bound,
              last_seen_at_utc=$seen, status=$status WHERE id=$id
            """);
        cmd.Parameters.AddWithValue("$lid", entity.LicenseId);
        cmd.Parameters.AddWithValue("$did", entity.DeviceId);
        cmd.Parameters.AddWithValue("$bound", Fmt(entity.BoundAtUtc));
        cmd.Parameters.AddWithValue("$seen", Fmt(entity.LastSeenAtUtc));
        cmd.Parameters.AddWithValue("$status", entity.Status.ToString());
        cmd.Parameters.AddWithValue("$id", entity.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResetAllBoundAsync(long licenseId, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            UPDATE rufus_devices SET status='Reset', last_seen_at_utc=$seen
            WHERE license_id=$lid AND status='Bound'
            """);
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$seen", Fmt(atUtc));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DeviceEntity Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        LicenseId = r.GetInt64(r.GetOrdinal("license_id")),
        DeviceId = r.GetString(r.GetOrdinal("device_id")),
        BoundAtUtc = ParseDto(r.GetString(r.GetOrdinal("bound_at_utc"))),
        LastSeenAtUtc = ParseDto(r.GetString(r.GetOrdinal("last_seen_at_utc"))),
        Status = Enum.Parse<DeviceBindStatus>(r.GetString(r.GetOrdinal("status"))),
    };
}

internal sealed class SqliteSessionRepository : SqliteRepoBase, ISessionRepository
{
    public SqliteSessionRepository(SqliteConnection connection) : base(connection) { }

    public async Task<SessionEntity?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("SELECT * FROM rufus_sessions WHERE token_hash=$h");
        cmd.Parameters.AddWithValue("$h", tokenHash);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return Map(r);
    }

    public async Task<IReadOnlyList<SessionEntity>> ListActiveByLicenseAsync(long licenseId, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand(
            "SELECT * FROM rufus_sessions WHERE license_id=$lid AND status='Active'");
        cmd.Parameters.AddWithValue("$lid", licenseId);
        var list = new List<SessionEntity>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(Map(r));
        return list;
    }

    public async Task<SessionEntity> InsertAsync(SessionEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_sessions (
              license_id, device_row_id, token_hash, created_at_utc, last_renewed_at_utc, lease_expires_at_utc, status)
            VALUES ($lid, $did, $th, $c, $r, $e, $s);
            SELECT last_insert_rowid();
            """);
        cmd.Parameters.AddWithValue("$lid", entity.LicenseId);
        cmd.Parameters.AddWithValue("$did", entity.DeviceId);
        cmd.Parameters.AddWithValue("$th", entity.TokenHash);
        cmd.Parameters.AddWithValue("$c", Fmt(entity.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$r", Fmt(entity.LastRenewedAtUtc));
        cmd.Parameters.AddWithValue("$e", Fmt(entity.LeaseExpiresAtUtc));
        cmd.Parameters.AddWithValue("$s", entity.Status.ToString());
        entity.Id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return entity;
    }

    public async Task UpdateAsync(SessionEntity entity, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            UPDATE rufus_sessions SET license_id=$lid, device_row_id=$did, token_hash=$th,
              created_at_utc=$c, last_renewed_at_utc=$r, lease_expires_at_utc=$e, status=$s
            WHERE id=$id
            """);
        cmd.Parameters.AddWithValue("$lid", entity.LicenseId);
        cmd.Parameters.AddWithValue("$did", entity.DeviceId);
        cmd.Parameters.AddWithValue("$th", entity.TokenHash);
        cmd.Parameters.AddWithValue("$c", Fmt(entity.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$r", Fmt(entity.LastRenewedAtUtc));
        cmd.Parameters.AddWithValue("$e", Fmt(entity.LeaseExpiresAtUtc));
        cmd.Parameters.AddWithValue("$s", entity.Status.ToString());
        cmd.Parameters.AddWithValue("$id", entity.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExpireLeasesAsync(long licenseId, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            UPDATE rufus_sessions SET status='Expired'
            WHERE license_id=$lid AND status='Active' AND lease_expires_at_utc <= $now
            """);
        cmd.Parameters.AddWithValue("$lid", licenseId);
        cmd.Parameters.AddWithValue("$now", Fmt(nowUtc));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SessionEntity Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        LicenseId = r.GetInt64(r.GetOrdinal("license_id")),
        DeviceId = r.GetInt64(r.GetOrdinal("device_row_id")),
        TokenHash = r.GetString(r.GetOrdinal("token_hash")),
        CreatedAtUtc = ParseDto(r.GetString(r.GetOrdinal("created_at_utc"))),
        LastRenewedAtUtc = ParseDto(r.GetString(r.GetOrdinal("last_renewed_at_utc"))),
        LeaseExpiresAtUtc = ParseDto(r.GetString(r.GetOrdinal("lease_expires_at_utc"))),
        Status = Enum.Parse<SessionStatus>(r.GetString(r.GetOrdinal("status"))),
    };
}

internal sealed class SqliteAdminAuditRepository : SqliteRepoBase, IAdminAuditRepository
{
    public SqliteAdminAuditRepository(SqliteConnection connection) : base(connection) { }

    public async Task AppendAsync(AdminAuditEntity entry, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand("""
            INSERT INTO rufus_admin_audit (at_utc, action, license_id, detail)
            VALUES ($at, $action, $lid, $detail)
            """);
        cmd.Parameters.AddWithValue("$at", Fmt(entry.AtUtc));
        cmd.Parameters.AddWithValue("$action", entry.Action);
        cmd.Parameters.AddWithValue("$lid", (object?)entry.LicenseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$detail", (object?)entry.Detail ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AdminAuditEntity>> ListRecentAsync(int take, CancellationToken ct = default)
    {
        await using var cmd = CreateCommand(
            "SELECT * FROM rufus_admin_audit ORDER BY id DESC LIMIT $n");
        cmd.Parameters.AddWithValue("$n", take);
        var list = new List<AdminAuditEntity>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AdminAuditEntity
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                AtUtc = ParseDto(r.GetString(r.GetOrdinal("at_utc"))),
                Action = r.GetString(r.GetOrdinal("action")),
                LicenseId = r.IsDBNull(r.GetOrdinal("license_id")) ? null : r.GetInt64(r.GetOrdinal("license_id")),
                Detail = r.IsDBNull(r.GetOrdinal("detail")) ? null : r.GetString(r.GetOrdinal("detail")),
            });
        }
        return list;
    }
}
