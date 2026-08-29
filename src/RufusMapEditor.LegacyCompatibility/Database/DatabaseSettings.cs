using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace RufusMapEditor.LegacyCompatibility.Database;

public sealed class DatabaseSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "";
    public string Database { get; set; } = MapasColumns.DefaultDatabase;
    public string Table { get; set; } = MapasColumns.DefaultTable;
    /// <summary>DPAPI-protected password (Base64). Never log or put in reports.</summary>
    public string? PasswordProtectedBase64 { get; set; }
    public NewMapDefaultsSettings NewMapDefaults { get; set; } = new();

    public string PublicEndpoint => $"{User}@{Host}:{Port}/{Database}.{Table}";

    public string BuildConnectionString(string plainPassword)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim(),
            Port = (uint)Math.Clamp(Port <= 0 ? 3306 : Port, 1, 65535),
            UserID = User ?? "",
            Password = plainPassword ?? "",
            Database = string.IsNullOrWhiteSpace(Database) ? MapasColumns.DefaultDatabase : Database.Trim(),
            ConnectionTimeout = 8,
            DefaultCommandTimeout = 45,
            SslMode = MySqlSslMode.Preferred,
        };
        return b.ConnectionString;
    }
}

public static class DatabasePasswordProtector
{
    public static string Protect(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain ?? "");
        return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
            return "";
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
