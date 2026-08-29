using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.App.Controls;

public partial class LogConsolePanel : UserControl
{
    public LogConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            if (DataContext is LogConsoleViewModel vm)
                Hook(vm);
        };
        Unloaded += (_, _) => Unhook();
    }

    private LogConsoleViewModel? _vm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unhook();
        if (e.NewValue is LogConsoleViewModel vm)
            Hook(vm);
    }

    private void Hook(LogConsoleViewModel vm)
    {
        _vm = vm;
        vm.RequestScrollToEnd += ScrollToEnd;
    }

    private void Unhook()
    {
        if (_vm is null) return;
        _vm.RequestScrollToEnd -= ScrollToEnd;
        _vm = null;
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count == 0) return;
        var last = LogList.Items[^1];
        LogList.ScrollIntoView(last);
    }

    private void LogList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm?.AutoScroll == true)
            ScrollToEnd();
    }
}

/// <summary>Maps RufusLogLevel to themed brushes.</summary>
public sealed class LogLevelToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not RufusLogLevel level)
            return Brushes.Gray;

        var key = level switch
        {
            RufusLogLevel.Debug => "LogLevelDebug",
            RufusLogLevel.Info => "LogLevelInfo",
            RufusLogLevel.Ok => "LogLevelOk",
            RufusLogLevel.Warn => "LogLevelWarn",
            RufusLogLevel.Error => "LogLevelError",
            _ => "TextSecondary",
        };

        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        if (Application.Current?.TryFindResource("TextSecondary") is Brush fallback)
            return fallback;
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
