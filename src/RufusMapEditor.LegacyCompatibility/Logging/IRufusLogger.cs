namespace RufusMapEditor.LegacyCompatibility.Logging;

public interface IRufusLogger
{
    int MaxEntries { get; }
    int Count { get; }
    IReadOnlyList<RufusLogEntry> Snapshot();

    event EventHandler<RufusLogEntry>? EntryAdded;
    event EventHandler? Cleared;

    void Log(RufusLogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Ok(string message);
    void Warn(string message);
    void Error(string message);

    void Clear();
    string ExportText();
    string ExportText(IEnumerable<RufusLogEntry> entries);
}
