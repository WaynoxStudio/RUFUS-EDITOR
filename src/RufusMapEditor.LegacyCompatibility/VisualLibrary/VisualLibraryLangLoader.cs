using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.2 — READ-ONLY download of active monsters_es / items_es via versions_es.txt.</summary>
public static class VisualLibraryLangLoader
{
    public static string DefaultCacheDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "lang-cache");

    public sealed class LangArtifact
    {
        public required int Version { get; init; }
        public required string FileName { get; init; }
        public required string LocalPath { get; init; }
        public required byte[] Bytes { get; init; }
        public required string Sha256 { get; init; }
    }

    public static (string versionsText, LangArtifact? monsters, LangArtifact? items, string? error) LoadActive(
        LangSftpSettings settings,
        string plainPassword,
        string? cacheDirectory = null,
        Func<LangSftpSettings, string, ILangSftpReadClient>? clientFactory = null,
        bool loadMonsters = true,
        bool loadItems = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var factory = clientFactory ?? LangSftpReadClientFactory.Create;
        var cacheRoot = string.IsNullOrWhiteSpace(cacheDirectory) ? DefaultCacheDirectory : cacheDirectory!;

        using var client = factory(settings, plainPassword);
        client.Connect();

        var langPath = NormalizeDir(string.IsNullOrWhiteSpace(settings.LangRemotePath)
            ? LangSftpSettings.DefaultLangRemotePath
            : settings.LangRemotePath);
        var swfDir = NormalizeDir(string.IsNullOrWhiteSpace(settings.SwfRemotePath)
            ? LangSftpSettings.DefaultSwfRemotePath
            : settings.SwfRemotePath);

        var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);
        if (!client.FileExists(versionsRemote))
            return ("", null, null, "versions_es.txt inexistente en la ruta LANG remota.");

        var versionsText = client.ReadAllText(versionsRemote);
        Directory.CreateDirectory(cacheRoot);
        File.WriteAllText(Path.Combine(cacheRoot, LangSftpSettings.VersionsFileName), versionsText, Encoding.UTF8);

        LangArtifact? monsters = null;
        LangArtifact? items = null;

        if (loadMonsters)
        {
            if (!VersionsEsParser.TryParseMonstersVersion(versionsText, out var ver, out var err))
                return (versionsText, null, null, err);
            monsters = Download(client, swfDir, cacheRoot, VersionsEsParser.BuildMonstersSwfFileName(ver), ver);
        }

        if (loadItems)
        {
            if (!VersionsEsParser.TryParseItemsVersion(versionsText, out var ver, out var err))
                return (versionsText, monsters, null, err);
            items = Download(client, swfDir, cacheRoot, VersionsEsParser.BuildItemsSwfFileName(ver), ver);
        }

        if (client.WriteAttemptCount != 0)
            return (versionsText, monsters, items, "Cliente SFTP realizó escrituras (no permitido en LIB.2).");

        return (versionsText, monsters, items, null);
    }

    /// <summary>Load from local cache/fixture without SFTP (tests / offline).</summary>
    public static LangArtifact? TryLoadLocal(string directory, string fileName, int version)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return null;
        var bytes = File.ReadAllBytes(path);
        return new LangArtifact
        {
            Version = version,
            FileName = fileName,
            LocalPath = path,
            Bytes = bytes,
            Sha256 = Sha256Hex(bytes),
        };
    }

    private static LangArtifact Download(
        ILangSftpReadClient client,
        string swfDir,
        string cacheRoot,
        string fileName,
        int version)
    {
        var remote = Combine(swfDir, fileName);
        if (!client.FileExists(remote))
            throw new InvalidOperationException($"SWF activo inexistente en remoto: {fileName}");
        var bytes = client.DownloadBytes(remote);
        var local = Path.Combine(cacheRoot, fileName);
        File.WriteAllBytes(local, bytes);
        return new LangArtifact
        {
            Version = version,
            FileName = fileName,
            LocalPath = local,
            Bytes = bytes,
            Sha256 = Sha256Hex(bytes),
        };
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeDir(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (!p.StartsWith('/')) p = "/" + p;
        return p.EndsWith('/') ? p : p + "/";
    }

    private static string Combine(string dir, string name)
    {
        var d = NormalizeDir(dir);
        return d + name.TrimStart('/');
    }
}
