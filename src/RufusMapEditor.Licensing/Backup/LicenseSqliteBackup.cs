namespace RufusMapEditor.Licensing.Backup;

/// <summary>
/// Backup design for V1 SQLite (not deployed on VPS in LIC.2).
/// Copy the single DB file while backend is stopped or using SQLite backup API later.
/// Never include OPENAI_API_KEY or RUFUS_ADMIN_API_SECRET in backup archives of env files.
/// </summary>
public static class LicenseSqliteBackup
{
    /// <summary>
    /// File-copy backup of the license database to a destination path.
    /// Safe when no writers are active; production should use online backup API in LIC.3+.
    /// </summary>
    public static void CopyDatabaseFile(string sourceDbPath, string destinationDbPath)
    {
        if (!File.Exists(sourceDbPath))
            throw new FileNotFoundException("License SQLite database not found.", sourceDbPath);
        var dir = Path.GetDirectoryName(destinationDbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.Copy(sourceDbPath, destinationDbPath, overwrite: true);
    }
}
