using System.Security.Cryptography;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>FASE 11B.1 — configuración SFTP LANG (contraseña DPAPI, nunca embebida).</summary>
public sealed class LangSftpSettings
{
    public const string DefaultLangRemotePath = "/var/www/html/data/lang/";
    public const string DefaultSwfRemotePath = "/var/www/html/data/lang/swf/";
    public const string VersionsFileName = "versions_es.txt";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    /// <summary>Contraseña protegida con DPAPI (Base64). Nunca registrar en logs.</summary>
    public string? PasswordProtectedBase64 { get; set; }
    public string LangRemotePath { get; set; } = DefaultLangRemotePath;
    public string SwfRemotePath { get; set; } = DefaultSwfRemotePath;

    /// <summary>Snapshot de la última sincronización READ-ONLY (para 11B.2).</summary>
    public LangRemoteSyncSnapshot? LastSync { get; set; }
}

public sealed class LangRemoteSyncSnapshot
{
    public int MapsVersion { get; set; }
    public string SwfFileName { get; set; } = "";
    public string SwfSha256 { get; set; } = "";
    public string VersionsEsSha256 { get; set; } = "";
    public string VersionsEsRelevantLine { get; set; } = "";
    public DateTimeOffset SyncedUtc { get; set; }
    public string? LocalCachePath { get; set; }
}

public static class LangSftpPasswordProtector
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
