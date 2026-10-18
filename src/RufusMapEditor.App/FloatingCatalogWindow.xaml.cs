using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class FloatingCatalogWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _vm;
    private bool _dockRequested;
    private bool _suppressZoomEvent;
    private double _zoomPercent = DefaultZoomPercent;

    /// <summary>0% = same density as current full-screen (~21/row). Higher = larger tiles.</summary>
    private const double MinScale = 1.0;
    private const double MaxScale = 2.6;
    /// <summary>~15 columns on a typical maximized catalog (~21 densest → scale ≈ 1.4).</summary>
    private const double DefaultZoomPercent = 28;
    private const double BaseTileWidth = 72;
    private const double BaseTileHeight = 88;
    private const double TileMargin = 8; // Margin="4" × 2

    public FloatingCatalogWindow(MainViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        Owner = Application.Current?.MainWindow;
        ThemeService.ApplyCatalogPanelTheme(this);
        ThemeService.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
        Loaded += OnLoaded;
        ApplyZoomToTiles();
    }

    public event Action? DockRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public double TileWidth { get; private set; } = BaseTileWidth;
    public double TileHeight { get; private set; } = BaseTileHeight;

    public double ZoomPercent
    {
        get => _zoomPercent;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            if (Math.Abs(v - _zoomPercent) < 0.01) return;
            _zoomPercent = v;
            ApplyZoomToTiles();
            UpdateCatalogLayout(forceRefresh: true);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TileWidth));
            OnPropertyChanged(nameof(TileHeight));
        }
    }

    private double ZoomScale =>
        MinScale + (MaxScale - MinScale) * (_zoomPercent / 100.0);

    private double TileOuterWidth => TileWidth + TileMargin;

    private int ThumbPixelSize =>
        Math.Clamp((int)Math.Round(56 * ZoomScale), 56, 192);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressZoomEvent = true;
        ZoomSlider.Value = DefaultZoomPercent;
        _suppressZoomEvent = false;
        ZoomPercent = DefaultZoomPercent;
        UpdateCatalogLayout(forceRefresh: true);
    }

    private void OnThemeChanged() => ThemeService.ApplyCatalogPanelTheme(this);

    private void Dock_Click(object sender, RoutedEventArgs e)
    {
        _dockRequested = true;
        DockRequested?.Invoke();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        if (!_dockRequested)
            DockRequested?.Invoke();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressZoomEvent || !IsLoaded) return;
        ZoomPercent = e.NewValue;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;

        ZoomPercent += e.Delta > 0 ? 6 : -6;
        _suppressZoomEvent = true;
        ZoomSlider.Value = ZoomPercent;
        _suppressZoomEvent = false;
        e.Handled = true;
    }

    private void ApplyZoomToTiles()
    {
        var scale = ZoomScale;
        TileWidth = Math.Round(BaseTileWidth * scale, 1);
        TileHeight = Math.Round(BaseTileHeight * scale, 1);
        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{(int)Math.Round(ZoomPercent)}%";
    }

    private void FolderTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderNodeVm node) return;
        _vm.SelectFolderNode(node.Children.Count > 0 ? null : node);
    }

    private void GfxCatalogList_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCatalogLayout(forceRefresh: false);

    private void UpdateCatalogLayout(bool forceRefresh)
    {
        if (GfxCatalogList is null || GfxCatalogList.ActualWidth <= 0) return;
        _vm.SetCatalogPanelLayout(GfxCatalogList.ActualWidth, TileOuterWidth, forceRefresh);
    }

    private void GfxItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsFavoriteStarSource(e.OriginalSource as DependencyObject))
            return;

        if (sender is FrameworkElement { DataContext: GfxItemVm item })
            _vm.SelectGfx(item);
    }

    private void GfxFavoriteStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GfxItemVm item })
            _vm.ToggleFavorite(item);
        e.Handled = true;
    }

    private void GfxItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GfxItemVm item })
        {
            _vm.SelectGfx(item);
            _vm.ToggleFavorite(item);
            e.Handled = true;
        }
    }

    private void GfxThumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GfxItemVm item })
            _vm.EnsureThumbnail(item, ThumbPixelSize);
    }

    private void GfxItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not GfxItemVm item)
            return;

        _vm.EnsureThumbnail(item, ThumbPixelSize);
        HoverPreviewImage.Source = _vm.GetCatalogHoverPreview(item);
        HoverPreviewId.Text = $"GfxID {item.Id}";
        var dims = _vm.FormatCatalogHoverDetails(item);
        HoverPreviewDims.Text = dims;
        HoverPreviewDims.Visibility = string.IsNullOrEmpty(dims) ? Visibility.Collapsed : Visibility.Visible;
        GfxHoverPopup.PlacementTarget = border;
        GfxHoverPopup.IsOpen = true;
    }

    private void GfxItem_MouseLeave(object sender, MouseEventArgs e) =>
        GfxHoverPopup.IsOpen = false;

    private static bool IsFavoriteStarSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "FavoriteStar" })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
