namespace RufusMapEditor.Licensing.Options;

/// <summary>
/// SQLite path for license store. Never hardcode developer absolute paths.
/// Priority: RUFUS_LICENSE_DB_PATH → optional configured path → default under backend data dir.
/// </summary>
public static class LicenseSqlitePath
{
    public const string EnvironmentVariable = "RUFUS_LICENSE_DB_PATH";
    public const string DefaultFileName = "rufus-licenses.db";

    /// <summary>
    /// Resolves DB path. <paramref name="configuredPath"/> may come from appsettings (relative or absolute).
    /// Default: {baseDirectory}/data/rufus-licenses.db — private to backend process, not Editor dist.
    /// </summary>
    public static string Resolve(string? configuredPath = null, string? baseDirectory = null)
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env.Trim());

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var p = configuredPath.Trim();
            if (Path.IsPathRooted(p))
                return Path.GetFullPath(p);
            var root = baseDirectory ?? AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(root, p));
        }

        var dir = baseDirectory ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(dir, "data", DefaultFileName));
    }
}
