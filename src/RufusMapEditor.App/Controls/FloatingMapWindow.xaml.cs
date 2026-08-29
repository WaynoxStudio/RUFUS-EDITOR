using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class FloatingMapWindow : UserControl
{
    public event EventHandler? LayoutChanged;
    public event EventHandler? ActivatedByUser;

    private Canvas? _host;
    private MainViewModel? _vm;
    private OpenMapDocument? _document;
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

    public MapViewport Viewport => MapViewportControl;
    public OpenMapDocument? BoundDocument => _document;

    public FloatingMapWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        SizeChanged += OnFloatingSizeChanged;
        PreviewMouseDown += (_, _) => ActivateThis();
    }

    public void AttachDocument(MainViewModel vm, OpenMapDocument document)
    {
        if (_document is not null)
            _document.PropertyChanged -= DocumentOnPropertyChanged;
        if (_vm is not null)
            _vm.MapPublishQueue.QueueChanged -= OnQueueChanged;

        _vm = vm;
        _document = document;
        DataContext = vm;
        MapViewportControl.DataContext = vm;
        MapViewportControl.BoundDocument = document;
        document.PropertyChanged += DocumentOnPropertyChanged;
        vm.MapPublishQueue.QueueChanged += OnQueueChanged;
        TitleText.Text = document.WindowTitle;
        ApplyCascadeOffset(document.CascadeIndex);
        RefreshQueueButton();
    }

    private void OnQueueChanged() => Dispatcher.BeginInvoke(RefreshQueueButton);

    private void RefreshQueueButton()
    {
        if (_vm is null || _document is null || QueueAddButton is null) return;
        var mapId = _document.MapId;
        QueueAddButton.Content = _vm.MapPublishQueue.GetHeaderGlyph(mapId);
        QueueAddButton.ToolTip = _vm.MapPublishQueue.GetHeaderTooltip(mapId);
    }

    private async void QueueAdd_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || _document is null) return;
        ActivateThis();
        await _vm.MapPublishQueue.AddMapAsync(_document.MapId).ConfigureAwait(true);
        RefreshQueueButton();
    }

    private void DocumentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenMapDocument.WindowTitle) or nameof(OpenMapDocument.MapImage))
            TitleText.Text = _document?.WindowTitle ?? "Map";
        if (e.PropertyName is nameof(OpenMapDocument.WindowTitle))
            RefreshQueueButton(); // dirty * changes status when queued
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = e.NewValue as MainViewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = FindParent<Canvas>(this);
        if (!_layoutApplied)
            ApplyDefaultLayout();
        FitViewport();
    }

    private void OnFloatingSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_suppressFit || _isMinimized || _document?.MapImage is null) return;
        FitViewport();
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
            if (_document is not null)
                ApplyCascadeOffset(_document.CascadeIndex);
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
            FitViewport();
        }
    }

    public void SaveLayoutToSettings()
    {
        // Per-window layout is in-memory for multi-doc; persist active geometry to shared settings.
        if (_vm is null || _host is null || !_layoutApplied) return;
        if (!ReferenceEquals(_document, _vm.ActiveDocument)) return;

        var layout = _vm.UiLayout;
        layout.MapWindowMaximized = _isMaximized;
        layout.MapWindowMinimized = _isMinimized;

        if (!_isMaximized)
        {
            layout.MapWindowWidth = Width;
            layout.MapWindowHeight = _isMinimized ? _savedHeight : Height;
            layout.MapWindowLeft = GetCanvasLeft();
            layout.MapWindowTop = GetCanvasTop();
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ActivateThis()
    {
        if (_vm is null || _document is null) return;
        BringToFront();
        if (!ReferenceEquals(_vm.ActiveDocument, _document))
            _vm.ActivateDocument(_document);
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
            SaveLayoutToSettings();
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
        SaveLayoutToSettings();
        e.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
            RestoreFromMinimize();
        else
            Minimize();
        SaveLayoutToSettings();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
        SaveLayoutToSettings();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _document is null) return;
        if (_document.IsDirty)
        {
            _vm.ActivateDocument(_document);
            if (!_vm.ConfirmDiscardMapOnly())
                return;
        }
        _vm.CloseDocument(_document);
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
        ApplyMinimizedState(true);
        UpdateChromeState();
    }

    private void RestoreFromMinimize()
    {
        _isMinimized = false;
        ApplyMinimizedState(false);
        UpdateChromeState();
        FitViewport();
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
        FitViewport();
    }

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
            FitViewport();
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

        var isActive = _vm is not null && ReferenceEquals(_document, _vm.ActiveDocument);
        ChromeBorder.BorderBrush = isActive
            ? (Brush)FindResource("BrandAccent")
            : (Brush)FindResource("Border");
        ChromeBorder.BorderThickness = new Thickness(isActive ? 2 : 1);
    }

    public void RefreshActiveChrome()
    {
        UpdateChromeState();
        RefreshQueueButton();
    }

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
        FitViewport();
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizing = false;
        SaveLayoutToSettings();
    }

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

    private void ClampPositionToHost()
    {
        SetPosition(GetCanvasLeft(), GetCanvasTop());
    }

    private void BringToFront()
    {
        if (_host is null) return;
        var maxZ = _host.Children.OfType<UIElement>().Select(Canvas.GetZIndex).DefaultIfEmpty(0).Max();
        Canvas.SetZIndex(this, maxZ + 1);
    }

    private void FitViewport()
    {
        if (_isMinimized || _document?.MapImage is null) return;
        Viewport.FitMap();
    }

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
