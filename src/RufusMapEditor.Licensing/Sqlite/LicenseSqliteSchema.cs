using Microsoft.Data.Sqlite;

namespace RufusMapEditor.Licensing.Sqlite;

/// <summary>
/// Safe additive schema evolution for production SQLite (never DROP/recreate license data).
/// </summary>
internal static class LicenseSqliteSchema
{
    public const int CurrentVersion = 3;

    public static void Apply(SqliteConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS rufus_schema_meta (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS rufus_licenses (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  code_hash TEXT NOT NULL UNIQUE,
                  code_display_hint TEXT NOT NULL,
                  status TEXT NOT NULL,
                  created_at_utc TEXT NOT NULL,
                  first_activated_at_utc TEXT NULL,
                  expires_at_utc TEXT NULL,
                  duration_days INTEGER NULL,
                  max_devices INTEGER NOT NULL DEFAULT 1,
                  max_concurrent_sessions INTEGER NOT NULL DEFAULT 1,
                  permission_editor INTEGER NOT NULL DEFAULT 1,
                  permission_ai INTEGER NOT NULL DEFAULT 0,
                  admin_notes TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS rufus_devices (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  license_id INTEGER NOT NULL,
                  device_id TEXT NOT NULL,
                  bound_at_utc TEXT NOT NULL,
                  last_seen_at_utc TEXT NOT NULL,
                  status TEXT NOT NULL,
                  UNIQUE(license_id, device_id),
                  FOREIGN KEY(license_id) REFERENCES rufus_licenses(id)
                );

                CREATE TABLE IF NOT EXISTS rufus_sessions (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  license_id INTEGER NOT NULL,
                  device_row_id INTEGER NOT NULL,
                  token_hash TEXT NOT NULL UNIQUE,
                  created_at_utc TEXT NOT NULL,
                  last_renewed_at_utc TEXT NOT NULL,
                  lease_expires_at_utc TEXT NOT NULL,
                  status TEXT NOT NULL,
                  FOREIGN KEY(license_id) REFERENCES rufus_licenses(id),
                  FOREIGN KEY(device_row_id) REFERENCES rufus_devices(id)
                );

                CREATE TABLE IF NOT EXISTS rufus_admin_audit (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  at_utc TEXT NOT NULL,
                  action TEXT NOT NULL,
                  license_id INTEGER NULL,
                  detail TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_rufus_devices_license ON rufus_devices(license_id);
                CREATE INDEX IF NOT EXISTS ix_rufus_sessions_license ON rufus_sessions(license_id);
                """;
            cmd.ExecuteNonQuery();
        }

        var version = ReadVersion(connection);
        if (version < 1)
            SetVersion(connection, 1);

        version = ReadVersion(connection);
        if (version < 2)
        {
            EnsureColumn(connection, "rufus_licenses", "ai_daily_limit", "INTEGER NULL");
            EnsureColumn(connection, "rufus_licenses", "ai_monthly_limit", "INTEGER NULL");
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS rufus_ai_usage_events (
                      id INTEGER PRIMARY KEY AUTOINCREMENT,
                      license_id INTEGER NOT NULL,
                      session_id INTEGER NULL,
                      at_utc TEXT NOT NULL,
                      action TEXT NOT NULL,
                      model TEXT NULL,
                      input_tokens INTEGER NULL,
                      output_tokens INTEGER NULL,
                      openai_succeeded INTEGER NOT NULL DEFAULT 0,
                      FOREIGN KEY(license_id) REFERENCES rufus_licenses(id)
                    );

                    CREATE TABLE IF NOT EXISTS rufus_ai_quota_counters (
                      license_id INTEGER NOT NULL,
                      period_type TEXT NOT NULL,
                      period_key TEXT NOT NULL,
                      count INTEGER NOT NULL DEFAULT 0,
                      PRIMARY KEY (license_id, period_type, period_key),
                      FOREIGN KEY(license_id) REFERENCES rufus_licenses(id)
                    );

                    CREATE INDEX IF NOT EXISTS ix_rufus_ai_usage_license_at
                      ON rufus_ai_usage_events(license_id, at_utc);
                    """;
                cmd.ExecuteNonQuery();
            }

            SetVersion(connection, 2);
        }

        version = ReadVersion(connection);
        if (version < 3)
        {
            EnsureColumn(connection, "rufus_licenses", "display_name", "TEXT NULL");
            SetVersion(connection, 3);
        }
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM rufus_schema_meta WHERE key='schema_version'";
        var v = cmd.ExecuteScalar()?.ToString();
        return int.TryParse(v, out var n) ? n : 0;
    }

    private static void SetVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rufus_schema_meta(key, value) VALUES('schema_version', $v)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """;
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string sqlType)
    {
        using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var r = info.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType}";
        alter.ExecuteNonQuery();
    }
}
