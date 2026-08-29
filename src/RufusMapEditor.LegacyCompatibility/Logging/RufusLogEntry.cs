namespace RufusMapEditor.LegacyCompatibility.Logging;

public sealed class RufusLogEntry
{
    public RufusLogEntry(DateTimeOffset timestamp, RufusLogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message ?? "";
    }

    public DateTimeOffset Timestamp { get; }
    public RufusLogLevel Level { get; }
    public string Message { get; }

    public string LevelLabel => Level switch
    {
        RufusLogLevel.Debug => "DEBUG",
        RufusLogLevel.Info => "INFO",
        RufusLogLevel.Ok => "OK",
        RufusLogLevel.Warn => "WARN",
        RufusLogLevel.Error => "ERROR",
        _ => "INFO",
    };

    /// <summary>Display line: HH:mm:ss  LEVEL  message</summary>
    public string FormatDisplay() =>
        $"{Timestamp:HH:mm:ss}  {LevelLabel,-5}  {Message}";

    /// <summary>Export line: [HH:mm:ss] [LEVEL] message</summary>
    public string FormatExport() =>
        $"[{Timestamp:HH:mm:ss}] [{LevelLabel}] {Message}";
}
