using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.App.ViewModels;

public enum LogFilterMode
{
    All,
    Info,
    Warn,
    Error,
    Debug,
}

public sealed class LogConsoleViewModel : ViewModelBase, IDisposable
{
    private readonly IRufusLogger _logger;
    private readonly Dispatcher _dispatcher;
    private bool _isExpanded;
    private bool _autoScroll = true;
    private double _panelHeight = 160;
    private LogFilterMode _filter = LogFilterMode.All;
    private string _autoScrollLabel = "Auto-scroll ON";
    private bool _disposed;

    public LogConsoleViewModel(IRufusLogger logger, Dispatcher? dispatcher = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

        Entries = new ObservableCollection<RufusLogEntry>();
        VisibleEntries = new ObservableCollection<RufusLogEntry>();

        foreach (var e in _logger.Snapshot())
            Entries.Add(e);
        RebuildVisible();

        _logger.EntryAdded += OnEntryAdded;
        _logger.Cleared += OnCleared;

        ToggleExpandedCommand = new RelayCommand(ToggleExpanded);
        ClearCommand = new RelayCommand(Clear);
        CopyCommand = new RelayCommand(Copy);
        SaveCommand = new RelayCommand(Save);
        ToggleAutoScrollCommand = new RelayCommand(ToggleAutoScroll);
        SetFilterAllCommand = new RelayCommand(() => SetFilter(LogFilterMode.All));
        SetFilterInfoCommand = new RelayCommand(() => SetFilter(LogFilterMode.Info));
        SetFilterWarnCommand = new RelayCommand(() => SetFilter(LogFilterMode.Warn));
        SetFilterErrorCommand = new RelayCommand(() => SetFilter(LogFilterMode.Error));
        SetFilterDebugCommand = new RelayCommand(() => SetFilter(LogFilterMode.Debug));
    }

    public ObservableCollection<RufusLogEntry> Entries { get; }
    public ObservableCollection<RufusLogEntry> VisibleEntries { get; }

    public RelayCommand ToggleExpandedCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ToggleAutoScrollCommand { get; }
    public RelayCommand SetFilterAllCommand { get; }
    public RelayCommand SetFilterInfoCommand { get; }
    public RelayCommand SetFilterWarnCommand { get; }
    public RelayCommand SetFilterErrorCommand { get; }
    public RelayCommand SetFilterDebugCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(CollapsedBarVisible));
            OnPropertyChanged(nameof(ExpandedPanelVisible));
        }
    }

    public bool CollapsedBarVisible => !_isExpanded;
    public bool ExpandedPanelVisible => _isExpanded;

    public bool AutoScroll
    {
        get => _autoScroll;
        set
        {
            if (!SetProperty(ref _autoScroll, value)) return;
            AutoScrollLabel = value ? "Auto-scroll ON" : "Auto-scroll OFF";
        }
    }

    public string AutoScrollLabel
    {
        get => _autoScrollLabel;
        private set => SetProperty(ref _autoScrollLabel, value);
    }

    public double PanelHeight
    {
        get => _panelHeight;
        set => SetProperty(ref _panelHeight, Math.Clamp(value, 80, 480));
    }

    public LogFilterMode Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;
            RebuildVisible();
        }
    }

    /// <summary>Raised on UI thread when a new visible entry is appended and auto-scroll is on.</summary>
    public event Action? RequestScrollToEnd;

    public void ToggleExpanded() => IsExpanded = !IsExpanded;
    public void Clear() => _logger.Clear();
    public void ToggleAutoScroll() => AutoScroll = !AutoScroll;
    public void SetFilter(LogFilterMode mode) => Filter = mode;

    public void Copy()
    {
        var text = _logger.ExportText(VisibleEntries);
        try
        {
            Clipboard.SetText(text ?? "");
        }
        catch
        {
            // Clipboard can fail if another process holds it.
        }
    }

    public void Save()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Guardar log",
            Filter = "Texto UTF-8 (*.txt)|*.txt|Todos (*.*)|*.*",
            FileName = $"rufus-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExt = ".txt",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, _logger.ExportText(VisibleEntries), System.Text.Encoding.UTF8);
    }

    private void OnEntryAdded(object? sender, RufusLogEntry entry)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess())
            AppendEntry(entry);
        else
            _dispatcher.BeginInvoke(() => AppendEntry(entry), DispatcherPriority.Background);
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess())
            ResetCollections();
        else
            _dispatcher.BeginInvoke(ResetCollections, DispatcherPriority.Background);
    }

    private void AppendEntry(RufusLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > _logger.MaxEntries && Entries.Count > 0)
            Entries.RemoveAt(0);

        if (PassesFilter(entry))
        {
            VisibleEntries.Add(entry);
            while (VisibleEntries.Count > _logger.MaxEntries && VisibleEntries.Count > 0)
                VisibleEntries.RemoveAt(0);

            if (AutoScroll)
                RequestScrollToEnd?.Invoke();
        }
    }

    private void ResetCollections()
    {
        Entries.Clear();
        VisibleEntries.Clear();
    }

    private void RebuildVisible()
    {
        VisibleEntries.Clear();
        foreach (var e in Entries)
        {
            if (PassesFilter(e))
                VisibleEntries.Add(e);
        }

        if (AutoScroll && VisibleEntries.Count > 0)
            RequestScrollToEnd?.Invoke();
    }

    private bool PassesFilter(RufusLogEntry entry) => Filter switch
    {
        LogFilterMode.Info => entry.Level is RufusLogLevel.Info or RufusLogLevel.Ok,
        LogFilterMode.Warn => entry.Level == RufusLogLevel.Warn,
        LogFilterMode.Error => entry.Level == RufusLogLevel.Error,
        LogFilterMode.Debug => entry.Level == RufusLogLevel.Debug,
        _ => true,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.EntryAdded -= OnEntryAdded;
        _logger.Cleared -= OnCleared;
    }
}
