using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class FloatingWorldWindow : UserControl
{
    public event EventHandler? ActivatedByUser;
    public event EventHandler? ChromeStateChanged;

    private Canvas? _host;
    private MainViewModel? _vm;
    private OpenWorldSession? _session;
    private bool _dragging;
    private bool _resizing;
    private bool _suppressFit;
    private Point _dragStart;
    private double _startLeft;
    private double _startTop;
    private bool _isMaximized;
    private bool _isMinimized;
    private double _savedWidth = 720;
    private double _savedHeight = 520;
    private double _savedLeft;
    private double _savedTop;
    private bool _layoutApplied;

    private const double TitleBarHeight = 28;
    private const double HostMargin = 8;

    public WorldViewport Viewport => WorldViewportControl;
    public OpenWorldSession? BoundSession => _session;
    public bool IsMinimized => _isMinimized;
    public string TaskbarTitle => _session?.WindowTitle ?? "Mundo";

    public FloatingWorldWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        SizeChanged += OnFloatingSizeChanged;
        PreviewMouseDown += (_, _) => ActivateThis();
    }

    public void AttachSession(MainViewModel vm, OpenWorldSession session)
    {
        if (_session is not null)
            _session.PropertyChanged -= SessionOnPropertyChanged;

        _vm = vm;
        _session = session;
        DataContext = vm;
        WorldViewportControl.DataContext = session.Vm;
        session.PropertyChanged += SessionOnPropertyChanged;
        TitleText.Text = session.WindowTitle;
        ApplyCascadeOffset(session.CascadeIndex);
    }

    private void SessionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenWorldSession.WindowTitle))
        {
            TitleText.Text = _session?.WindowTitle ?? "Mundo";
            ChromeStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = FindParent<Canvas>(this);
        if (!_layoutApplied)
            ApplyDefaultLayout();
        Dispatcher.BeginInvoke(() => FitViewport(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnFloatingSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_suppressFit || _isMinimized) return;
        // Keep camera; only note size via viewport SizeChanged handler.
    }

    private void ApplyCascadeOffset(int index)
    {
        if (_host is null) return;
        var left = HostMargin + 24 + index * 28;
        var top = HostMargin + 24 + index * 28;
        Width = 720;
        Height = 520;
        SetPosition(left, top);
        _savedWidth = Width;
        _savedHeight = Height;
        _savedLeft = left;
        _savedTop = top;
        _layoutApplied = true;
    }

    public void ApplyDefaultLayout()
    {
        if (_host is null) return;
        var hostW = _host.ActualWidth;
        var hostH = _host.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;

        _suppressFit = true;
        try
        {
            if (_session is not null)
                ApplyCascadeOffset(_session.CascadeIndex);
            else
            {
                Width = Math.Clamp(720, MinWidth, Math.Max(MinWidth, hostW - HostMargin * 2));
                Height = Math.Clamp(520, MinHeight, Math.Max(MinHeight, hostH - HostMargin * 2));
                SetPosition(Math.Max(HostMargin, (hostW - Width) / 2), Math.Max(HostMargin, (hostH - Height) / 2));
            }
            UpdateChromeState();
            _layoutApplied = true;
        }
        finally
        {
            _suppressFit = false;
            FitViewport();
        }
    }

    public void OnHostSizeChanged()
    {
        if (_host is null || _dragging || _resizing) return;

        var hostW = _host.ActualWidth;
        var hostH = _host.ActualHeight;
        if (hostW <= 0 || hostH <= 0) return;

        _suppressFit = true;
        try
        {
            if (_isMaximized)
            {
                ApplyMaximized(hostW, hostH);
                Dispatcher.BeginInvoke(() => FitViewport(fillToEdges: true),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            if (Width > hostW - HostMargin * 2)
                Width = Math.Max(MinWidth, hostW - HostMargin * 2);
            if (Height > hostH - HostMargin * 2)
                Height = Math.Max(MinHeight, hostH - HostMargin * 2);

            ClampPositionToHost();
            _savedWidth = Width;
            _savedHeight = Height;
            _savedLeft = GetCanvasLeft();
            _savedTop = GetCanvasTop();
        }
        finally
        {
            _suppressFit = false;
        }
    }

    public void ActivateFromTaskbar() => ActivateThis();

    private void ActivateThis()
    {
        if (_vm is null || _session is null) return;
        BringToFront();
        if (!ReferenceEquals(_vm.World, _session.Vm))
            _vm.ActivateWorldSession(_session);
        ActivatedByUser?.Invoke(this, EventArgs.Empty);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveChrome(e.OriginalSource as DependencyObject))
            return;

        ActivateThis();

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (_isMaximized || _host is null) return;

        _dragging = true;
        _dragStart = e.GetPosition(_host);
        _startLeft = GetCanvasLeft();
        _startTop = GetCanvasTop();
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _host is null) return;

        var pos = e.GetPosition(_host);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        SetPosition(_startLeft + dx, _startTop + dy);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        ClampPositionToHost();
        e.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
            RestoreFromMinimize();
        else
            Minimize();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _session is null) return;
        _vm.CloseWorldSession(_session);
    }

    private void Minimize()
    {
        if (_isMinimized) return;
        if (!_isMaximized)
        {
            _savedWidth = Width;
            _savedHeight = Height;
            _savedLeft = GetCanvasLeft();
            _savedTop = GetCanvasTop();
        }

        _isMinimized = true;
        Visibility = Visibility.Collapsed;
        ChromeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreFromMinimize()
    {
        if (!_isMinimized)
        {
            ActivateThis();
            return;
        }

        _isMinimized = false;
        Visibility = Visibility.Visible;
        ApplyMinimizedState(false);
        UpdateChromeState();
        FitViewport();
        ActivateThis();
        ChromeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMinimizedState(bool minimized)
    {
        ContentHost.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
        Height = minimized ? TitleBarHeight + 2 : _savedHeight;
        MinHeight = minimized ? TitleBarHeight + 2 : 200;
        ResizeGrip.IsEnabled = !minimized && !_isMaximized;
    }

    private void ToggleMaximize()
    {
        if (_isMaximized)
            RestoreFromMaximize();
        else
            Maximize();
    }

    private void Maximize()
    {
        if (_host is null) return;
        if (!_isMaximized)
        {
            _savedWidth = Width;
            _savedHeight = Height;
            _savedLeft = GetCanvasLeft();
            _savedTop = GetCanvasTop();
        }

        _isMaximized = true;
        _isMinimized = false;
        ApplyMaximized(_host.ActualWidth, _host.ActualHeight);
        UpdateChromeState();
        Dispatcher.BeginInvoke(() => FitViewport(fillToEdges: true),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Abre el mundo a pantalla completa del área de trabajo.</summary>
    public void MaximizeToHost() => Maximize();

    private void RestoreFromMaximize()
    {
        if (_host is null) return;

        _suppressFit = true;
        try
        {
            _isMaximized = false;
            Width = _savedWidth;
            Height = _savedHeight;
            SetPosition(_savedLeft, _savedTop);
            ContentHost.Visibility = Visibility.Visible;
            MinHeight = 200;
            ResizeGrip.IsEnabled = true;
            UpdateChromeState();
            ClampPositionToHost();
        }
        finally
        {
            _suppressFit = false;
            FitViewport(fillToEdges: false);
        }
    }

    private void ApplyMaximized(double hostW, double hostH)
    {
        Width = Math.Max(MinWidth, hostW - HostMargin * 2);
        Height = Math.Max(MinHeight, hostH - HostMargin * 2);
        Canvas.SetLeft(this, HostMargin);
        Canvas.SetTop(this, HostMargin);
        ContentHost.Visibility = Visibility.Visible;
        MinHeight = 200;
        ResizeGrip.IsEnabled = false;
    }

    private void UpdateChromeState()
    {
        MinimizeButton.Content = _isMinimized ? "▢" : "—";
        MinimizeButton.ToolTip = _isMinimized ? "Restaurar" : "Minimizar";
        MaximizeButton.Content = _isMaximized ? "❐" : "□";
        MaximizeButton.ToolTip = _isMaximized ? "Restaurar tamaño" : "Maximizar";
        TitleBar.Cursor = _isMaximized ? Cursors.Arrow : Cursors.SizeAll;

        var isActive = _vm is not null && _session is not null
                       && ReferenceEquals(_vm.World, _session.Vm);
        ChromeBorder.BorderBrush = isActive
            ? (Brush)FindResource("BrandAccent")
            : (Brush)FindResource("Border");
        ChromeBorder.BorderThickness = new Thickness(isActive ? 2 : 1);
    }

    public void RefreshActiveChrome() => UpdateChromeState();

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _resizing = true;
        ActivateThis();
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_host is null || _isMaximized || _isMinimized) return;

        var newWidth = Math.Max(MinWidth, Width + e.HorizontalChange);
        var newHeight = Math.Max(MinHeight, Height + e.VerticalChange);
        var left = GetCanvasLeft();
        var top = GetCanvasTop();

        newWidth = Math.Min(newWidth, _host.ActualWidth - left - HostMargin);
        newHeight = Math.Min(newHeight, _host.ActualHeight - top - HostMargin);

        Width = newWidth;
        Height = newHeight;
        _savedWidth = newWidth;
        _savedHeight = newHeight;
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e) =>
        _resizing = false;

    private void SetPosition(double left, double top)
    {
        if (_host is null)
        {
            Canvas.SetLeft(this, left);
            Canvas.SetTop(this, top);
            return;
        }

        var maxLeft = Math.Max(0, _host.ActualWidth - ActualWidth);
        var maxTop = Math.Max(0, _host.ActualHeight - ActualHeight);
        Canvas.SetLeft(this, Math.Clamp(left, 0, maxLeft));
        Canvas.SetTop(this, Math.Clamp(top, 0, maxTop));
    }

    private void ClampPositionToHost() => SetPosition(GetCanvasLeft(), GetCanvasTop());

    private void BringToFront()
    {
        if (_host is null) return;
        var maxZ = _host.Children.OfType<UIElement>().Select(Canvas.GetZIndex).DefaultIfEmpty(0).Max();
        Canvas.SetZIndex(this, maxZ + 1);
    }

    private void FitViewport(bool fillToEdges)
    {
        if (_isMinimized || _session?.Vm.World is null) return;
        WorldViewportControl.SetFillToEdges(fillToEdges);
    }

    private void FitViewport() => FitViewport(fillToEdges: _isMaximized);

    private double GetCanvasLeft()
    {
        var left = Canvas.GetLeft(this);
        return double.IsNaN(left) ? 0 : left;
    }

    private double GetCanvasTop()
    {
        var top = Canvas.GetTop(this);
        return double.IsNaN(top) ? 0 : top;
    }

    private static bool IsInteractiveChrome(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or Thumb)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T match)
                return match;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}
