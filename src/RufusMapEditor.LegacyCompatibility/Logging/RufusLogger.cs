namespace RufusMapEditor.LegacyCompatibility.Logging;

/// <summary>
/// Thread-safe in-memory logger. UI and services consume via events / snapshots — never write UI controls from here.
/// </summary>
public sealed class RufusLogger : IRufusLogger
{
    public const int DefaultMaxEntries = 5000;

    private readonly object _gate = new();
    private readonly List<RufusLogEntry> _entries;
    private readonly int _maxEntries;

    public RufusLogger(int maxEntries = DefaultMaxEntries)
    {
        if (maxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxEntries = maxEntries;
        _entries = new List<RufusLogEntry>(Math.Min(256, maxEntries));
    }

    public int MaxEntries => _maxEntries;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public event EventHandler<RufusLogEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public IReadOnlyList<RufusLogEntry> Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    public void Debug(string message) => Log(RufusLogLevel.Debug, message);
    public void Info(string message) => Log(RufusLogLevel.Info, message);
    public void Ok(string message) => Log(RufusLogLevel.Ok, message);
    public void Warn(string message) => Log(RufusLogLevel.Warn, message);
    public void Error(string message) => Log(RufusLogLevel.Error, message);

    public void Log(RufusLogLevel level, string message)
    {
        var safe = LogMessageSanitizer.Sanitize(message);
        var entry = new RufusLogEntry(DateTimeOffset.Now, level, safe);

        lock (_gate)
        {
            _entries.Add(entry);
            while (_entries.Count > _maxEntries)
                _entries.RemoveAt(0);
        }

        try
        {
            EntryAdded?.Invoke(this, entry);
        }
        catch
        {
            // Never let subscriber failures break callers.
        }
    }

    public void Clear()
    {
        lock (_gate)
            _entries.Clear();

        try
        {
            Cleared?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // ignore
        }
    }

    public string ExportText() => ExportText(Snapshot());

    public string ExportText(IEnumerable<RufusLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return string.Join(Environment.NewLine, entries.Select(e => e.FormatExport()));
    }
}
