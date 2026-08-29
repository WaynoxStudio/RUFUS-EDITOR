namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// FASE 11B.1 — superficie SFTP estrictamente READ-ONLY.
/// No expone Upload / Delete / Rename / WriteRemoteText / Move.
/// </summary>
public interface ILangSftpReadClient : IDisposable
{
    void Connect();
    bool FileExists(string remotePath);
    /// <summary>READ-ONLY existence check for a remote directory (CONT-CONN.1).</summary>
    bool DirectoryExists(string remotePath);
    string ReadAllText(string remotePath);
    byte[] DownloadBytes(string remotePath);
    long GetFileLength(string remotePath);

    /// <summary>Debe permanecer en 0 durante 11B.1.</summary>
    int WriteAttemptCount { get; }
}

public static class LangSftpReadClientFactory
{
    public static ILangSftpReadClient Create(LangSftpSettings settings, string plainPassword) =>
        new SshNetLangSftpReadClient(settings, plainPassword);
}
