namespace RufusMapEditor.LegacyCompatibility.Logging;

/// <summary>
/// Process-wide facade so static services (BD / LANG / SFTP) can log without DI or UI coupling.
/// </summary>
public static class RufusLog
{
    private static IRufusLogger _current = new RufusLogger();

    public static IRufusLogger Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static void Debug(string message) => Current.Debug(message);
    public static void Info(string message) => Current.Info(message);
    public static void Ok(string message) => Current.Ok(message);
    public static void Warn(string message) => Current.Warn(message);
    public static void Error(string message) => Current.Error(message);
    public static void Log(RufusLogLevel level, string message) => Current.Log(level, message);
}
