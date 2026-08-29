using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.Controls;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.App;

public partial class MapsEditorView : UserControl
{
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, FloatingMapWindow> _mapWindows = new(StringComparer.Ordinal);
    private int _hoverMapPreviewId = -1;
    private bool _catalogCollapsed;
    private bool _mapsCollapsed;
    private const double MapsCollapsedWidth = 22;

    public MapsEditorView() : this(deferLibraryLoad: false)
    {
    }

    public MapsEditorView(bool deferLibraryLoad)
    {
        Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        InitializeComponent();
        _vm = new MainViewModel(deferLibraryLoad);
        DataContext = _vm;
        _vm.DocumentOpened += OnDocumentOpened;
        _vm.DocumentClosed += OnDocumentClosed;
        _vm.DocumentActivated += OnDocumentActivated;
        _vm.MapMonsters.RequestFocusPanel += OnMonstersFocusRequested;
        _vm.RequestResetPanels += ResetPanels;
        _vm.RequestApplyLayout += ApplyLayoutFromSettings;
        _vm.ScrollCatalogToGfxId += ScrollCatalogToGfx;
        _vm.PropertyChanged += VmOnPropertyChanged;
        _vm.Logs.PropertyChanged += LogsOnPropertyChanged;
        PreviewKeyDown += Window_PreviewKeyDown;
        SizeChanged += Host_SizeChanged;
        Loaded += (_, _) =>
        {
            var hostWindow = Window.GetWindow(this);
            if (hostWindow is not null)
                ThemeService.ApplyToWindow(hostWindow);
            ThemeService.ThemeChanged += OnThemeChanged;
            if (deferLibraryLoad)
            {
                Dispatcher.BeginInvoke(
                    () => _vm.EnsureLibraryLoaded(),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            _vm.CheckRecoverableAutosavesOnStartup();
            _catalogCollapsed = _vm.UiLayout.CatalogCollapsed;
            _mapsCollapsed = _vm.UiLayout.MapsCollapsed;
            ApplyLayoutFromSettings();
            ApplyLogsLayout();
            UpdateCatalogColumns();
            VisualLibraryBootstrap.ConfigurePreviewFromSettings();
            RufusLog.Info("Aplicación iniciada");
        };
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private async void MonstersExpander_Expanded(object sender, RoutedEventArgs e)
    {
        await _vm.MapMonsters.EnsureCatalogAsync(refreshDb: true);
        await _vm.MapMonsters.LoadNaturalMobsForCurrentMapAsync();
        _vm.MapMonsters.RefreshContextStatus();
    }

    private void OnMonstersFocusRequested()
    {
        _vm.ShowInspectorPanel = true;
        _vm.MapMonsters.PanelExpanded = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (MonstersExpander is not null)
                MonstersExpander.BringIntoView();
            InspectorScrollViewer?.ScrollToHome();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnDocumentOpened(OpenMapDocument doc)
    {
        if (_mapWindows.ContainsKey(doc.DocumentId)) return;

        var window = new FloatingMapWindow();
        window.AttachDocument(_vm, doc);
        window.LayoutChanged += (_, _) => _vm.PersistUiLayout();
        MapWorkspaceCanvas.Children.Add(window);
        _mapWindows[doc.DocumentId] = window;
        RefreshAllWindowChrome();
    }

    private void OnDocumentClosed(OpenMapDocument doc)
    {
        if (!_mapWindows.Remove(doc.DocumentId, out var window)) return;
        MapWorkspaceCanvas.Children.Remove(window);
        RefreshAllWindowChrome();
    }

    private void OnDocumentActivated(OpenMapDocument doc)
    {
        if (_mapWindows.TryGetValue(doc.DocumentId, out var window))
        {
            var maxZ = MapWorkspaceCanvas.Children.OfType<UIElement>()
                .Select(Canvas.GetZIndex).DefaultIfEmpty(0).Max();
            Canvas.SetZIndex(window, maxZ + 1);
            window.RefreshActiveChrome();
            window.Viewport.FitMap();
        }
        RefreshAllWindowChrome();
    }

    private void RefreshAllWindowChrome()
    {
        foreach (var w in _mapWindows.Values)
            w.RefreshActiveChrome();
    }

    private void ForEachMapWindow(Action<FloatingMapWindow> action)
    {
        foreach (var w in _mapWindows.Values)
            action(w);
    }

    private void OnThemeChanged()
    {
        var hostWindow = Window.GetWindow(this);
        if (hostWindow is not null)
            ThemeService.ApplyToWindow(hostWindow);
        UpdateCatalogColumns();
    }

    private void GfxCatalogList_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCatalogColumns();

    private void UpdateCatalogColumns()
    {
        if (GfxCatalogList is null) return;
        _vm.SetCatalogPanelWidth(GfxCatalogList.ActualWidth);
    }

    private void ApplyLayoutFromSettings()
    {
        var layout = _vm.UiLayout;
        layout.Clamp();

        var workspaceWidth = WorkspaceGrid.ActualWidth > 0 ? WorkspaceGrid.ActualWidth : ActualWidth;

        var leftWidth = layout.ResolveLeftPanelWidth(workspaceWidth, _vm.ShowMapsPanel && !(_mapsCollapsed || layout.MapsCollapsed));
        var rightWidth = layout.ResolveRightPanelWidth(workspaceWidth, _vm.ShowInspectorPanel);
        layout.LeftPanelWidth = leftWidth;
        layout.RightPanelWidth = rightWidth;

        if (!_vm.ShowMapsPanel)
        {
            LeftCol.Width = new GridLength(0);
            LeftCol.MinWidth = 0;
            MapsSplitterCol.Width = new GridLength(0);
            MapsSplitter.Visibility = Visibility.Collapsed;
        }
        else if (_mapsCollapsed || layout.MapsCollapsed)
        {
            LeftCol.MinWidth = 0;
            LeftCol.MaxWidth = MapsCollapsedWidth;
            LeftCol.Width = new GridLength(MapsCollapsedWidth);
            MapsSplitterCol.Width = new GridLength(0);
            MapsSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            LeftCol.MinWidth = 120;
            LeftCol.MaxWidth = 480;
            LeftCol.Width = new GridLength(leftWidth);
            MapsSplitterCol.Width = new GridLength(4);
            MapsSplitter.Visibility = Visibility.Visible;
        }

        RightCol.Width = _vm.ShowInspectorPanel ? new GridLength(rightWidth) : new GridLength(0);
        InspectorSplitter.Visibility = _vm.ShowInspectorPanel ? Visibility.Visible : Visibility.Collapsed;

        var showCategoriesHost = _vm.ShowCategoriesPanel || _vm.ShowCatalogPanel;
        var bottomWidth = BottomSideBySideGrid.ActualWidth > 0
            ? BottomSideBySideGrid.ActualWidth
            : (LogsDockHost.ActualWidth > 0 ? LogsDockHost.ActualWidth : workspaceWidth);
        var categoriesWidth = layout.ResolveCategoriesWidth(bottomWidth, showCategoriesHost);
        CategoriesCol.Width = categoriesWidth > 0
            ? new GridLength(categoriesWidth)
            : new GridLength(0);
        CategoriesLogsSplitterCol.Width = categoriesWidth > 0 ? new GridLength(4) : new GridLength(0);
        CategoriesPanelHost.Visibility = showCategoriesHost ? Visibility.Visible : Visibility.Collapsed;
        CategoriesSplitter.Visibility = showCategoriesHost ? Visibility.Visible : Visibility.Collapsed;

        var treeCollapsed = _catalogCollapsed || layout.CatalogCollapsed;
        var showTree = _vm.ShowCategoriesPanel && !treeCollapsed;
        CategoriesTreeCol.Width = showTree ? new GridLength(148) : new GridLength(0);
        CategoriesTreeCol.MinWidth = showTree ? 100 : 0;
        CategoriesTreeSplitterCol.Width = showTree ? new GridLength(3) : new GridLength(0);
        FolderTreeView.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;

        CatalogGrid.Visibility = _vm.ShowCatalogPanel ? Visibility.Visible : Visibility.Collapsed;

        MapToolBar.Visibility = _vm.ShowToolBar ? Visibility.Visible : Visibility.Collapsed;
        MapToolSidebar.Visibility = Visibility.Visible;
        MainStatusBar.Visibility = _vm.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;

        UpdateCollapseCatalogButton();
        UpdateCollapseMapsButton();
        ApplyLogsLayout();
        if (_vm.HasMap)
            ForEachMapWindow(w => w.OnHostSizeChanged());
    }

    private void MapWorkspaceHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MapWorkspaceCanvas is null) return;
        MapWorkspaceCanvas.Width = e.NewSize.Width;
        MapWorkspaceCanvas.Height = e.NewSize.Height;
        if (_vm.HasMap)
            ForEachMapWindow(w => w.OnHostSizeChanged());
    }

    private void Host_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyLayoutFromSettings();
        UpdateCatalogColumns();
    }

    private void ResetPanels()
    {
        _catalogCollapsed = false;
        _mapsCollapsed = false;
        _vm.UiLayout.ResetToDefaults();
        _vm.Logs.IsExpanded = false;
        _vm.Logs.PanelHeight = UiLayoutSettings.DefaultLogsPanelHeight;
        ApplyLayoutFromSettings();
        ApplyLogsLayout();
        _vm.PersistUiLayout();
    }

    private void CollapseMaps_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.ShowMapsPanel) return;

        _mapsCollapsed = !_mapsCollapsed;
        _vm.UiLayout.MapsCollapsed = _mapsCollapsed;
        ApplyLayoutFromSettings();
        UpdateCollapseMapsButton();
        _vm.PersistUiLayout();
    }

    private void UpdateCollapseMapsButton()
    {
        if (MapsPanelExpanded is null || MapsPanelCollapsed is null) return;

        var collapsed = _mapsCollapsed || _vm.UiLayout.MapsCollapsed;
        MapsPanelExpanded.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        MapsPanelCollapsed.Visibility = collapsed && _vm.ShowMapsPanel ? Visibility.Visible : Visibility.Collapsed;

        if (CollapseMapsButton is not null)
        {
            CollapseMapsButton.Content = collapsed ? "▸" : "◂";
            CollapseMapsButton.ToolTip = collapsed ? "Mostrar mapas" : "Ocultar mapas";
        }
    }

    private void CollapseCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.ShowCategoriesPanel) return;

        _catalogCollapsed = !_catalogCollapsed;
        _vm.UiLayout.CatalogCollapsed = _catalogCollapsed;
        ApplyLayoutFromSettings();
        _vm.PersistUiLayout();
    }

    private void UpdateCollapseCatalogButton()
    {
        if (CollapseCatalogButton is null) return;
        var treeHidden = _catalogCollapsed || _vm.UiLayout.CatalogCollapsed || !_vm.ShowCategoriesPanel;
        CollapseCatalogButton.Content = treeHidden ? "▸" : "◂";
        CollapseCatalogButton.ToolTip = treeHidden ? "Mostrar árbol de categorías" : "Ocultar árbol de categorías";
        CollapseCatalogButton.Visibility = _vm.ShowCategoriesPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var layout = _vm.UiLayout;
        var workspaceWidth = WorkspaceGrid.ActualWidth > 0 ? WorkspaceGrid.ActualWidth : ActualWidth;

        if (LeftCol.Width.Value > MapsCollapsedWidth + 8)
        {
            layout.LeftPanelWidth = LeftCol.Width.Value;
            if (workspaceWidth > 0)
                layout.LeftPanelRatio = LeftCol.Width.Value / workspaceWidth;
            if (_mapsCollapsed)
            {
                _mapsCollapsed = false;
                layout.MapsCollapsed = false;
                UpdateCollapseMapsButton();
            }
        }

        if (RightCol.Width.Value > 0)
        {
            layout.RightPanelWidth = RightCol.Width.Value;
            if (workspaceWidth > 0)
                layout.RightPanelRatio = RightCol.Width.Value / workspaceWidth;
        }

        if (CategoriesCol.Width.Value > 0 && BottomSideBySideGrid.ActualWidth > 0)
            layout.CategoriesPanelRatio = CategoriesCol.Width.Value / BottomSideBySideGrid.ActualWidth;

        layout.Clamp();
        _vm.PersistUiLayout();
    }

    private void LogsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogConsoleViewModel.IsExpanded))
        {
            ApplyLogsLayout();
            PersistLogsLayout();
        }
        else if (e.PropertyName == nameof(LogConsoleViewModel.PanelHeight)
                 && LogsContentRow is not null
                 && Math.Abs(LogsContentRow.Height.Value - _vm.Logs.PanelHeight) > 0.5)
        {
            LogsContentRow.Height = new GridLength(_vm.Logs.PanelHeight);
        }
    }

    private void ApplyLogsLayout()
    {
        if (LogsContentRow is null || LogsSplitterRow is null || LogsResizeThumb is null)
            return;

        var logs = _vm.Logs;
        var categoriesHostVisible = _vm.ShowCategoriesPanel || _vm.ShowCatalogPanel;

        // Categorías (+catálogo) y Logs comparten altura
        if (logs.IsExpanded || categoriesHostVisible)
        {
            var h = logs.PanelHeight > 0 ? logs.PanelHeight : UiLayoutSettings.DefaultLogsPanelHeight;
            if (categoriesHostVisible && h < 160)
                h = 160;
            LogsSplitterRow.Height = new GridLength(4);
            LogsContentRow.Height = new GridLength(h);
            LogsResizeThumb.Visibility = Visibility.Visible;
        }
        else
        {
            LogsSplitterRow.Height = new GridLength(0);
            LogsContentRow.Height = new GridLength(UiLayoutSettings.LogsCollapsedHeight);
            LogsResizeThumb.Visibility = Visibility.Collapsed;
        }
    }

    private void PersistLogsLayout()
    {
        _vm.UiLayout.LogsExpanded = _vm.Logs.IsExpanded;
        if (_vm.Logs.IsExpanded && LogsContentRow.Height.Value >= 80)
            _vm.UiLayout.LogsPanelHeight = LogsContentRow.Height.Value;
        _vm.PersistUiLayout();
    }

    private void LogsThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_vm.Logs.IsExpanded && !(_vm.ShowCategoriesPanel || _vm.ShowCatalogPanel))
            return;
        var h = Math.Clamp(_vm.Logs.PanelHeight - e.VerticalChange, 80, 480);
        _vm.Logs.PanelHeight = h;
        LogsContentRow.Height = new GridLength(h);
    }

    private void LogsSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_vm.Logs.IsExpanded) return;
        PersistLogsLayout();
    }

    private void WorldTray_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: WorldViewModel.WorldTrayItemVm item }) return;
        _vm.World.PlaceTrayItem(item.Key);
    }

    public MainViewModel ViewModel => _vm;

    public void SetEmbeddedHost(bool embedded) => _vm.IsEmbeddedHost = embedded;

    public bool TryConfirmClose() => _vm.ConfirmDiscardIfDirty();

    private bool _disposed;

    public void DisposeWorkspace()
    {
        if (_disposed) return;
        _disposed = true;
        ThemeService.ThemeChanged -= OnThemeChanged;
        _vm.DocumentOpened -= OnDocumentOpened;
        _vm.DocumentClosed -= OnDocumentClosed;
        _vm.DocumentActivated -= OnDocumentActivated;
        _vm.MapMonsters.RequestFocusPanel -= OnMonstersFocusRequested;
        _vm.Logs.PropertyChanged -= LogsOnPropertyChanged;
        _vm.PropertyChanged -= VmOnPropertyChanged;
        _vm.RequestResetPanels -= ResetPanels;
        _vm.RequestApplyLayout -= ApplyLayoutFromSettings;
        _vm.ScrollCatalogToGfxId -= ScrollCatalogToGfx;
        _vm.Dispose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        switch (e.Key)
        {
            case Key.V when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.Select;
                e.Handled = true;
                break;
            case Key.R when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.RectSelect;
                e.Handled = true;
                break;
            case Key.B when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.Paint;
                e.Handled = true;
                break;
            case Key.E when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.Erase;
                e.Handled = true;
                break;
            case Key.I when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.Eyedropper;
                e.Handled = true;
                break;
            case Key.H when (Keyboard.Modifiers & ModifierKeys.Control) == 0:
                _vm.Tool = EditorTool.Pan;
                e.Handled = true;
                break;
            case Key.Space:
                // Keep Space available for map pan (MapViewport tracks Space while focused).
                break;
            case Key.Escape:
                if (_vm.HasSelection && _vm.ClearMapSelectionCommand.CanExecute(null))
                {
                    _vm.ClearMapSelectionCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    private async void MapList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedMapId is int id)
            await _vm.LoadMapAsync(id);
    }

    private async void MapList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.IsLoading) return;
        if (_vm.SelectedMapId is int id && _vm.HasLibrary)
            await _vm.LoadMapAsync(id);
    }

    private void FolderTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderNodeVm node) return;
        _vm.SelectFolderNode(node.Children.Count > 0 ? null : node);
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
            _vm.EnsureThumbnail(item);
    }

    private async void MapThumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MapPickerItemVm item })
            await _vm.EnsureMapThumbnailAsync(item);
    }

    private async void MapListItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not MapPickerItemVm item)
            return;

        var mapId = item.MapId;
        _hoverMapPreviewId = mapId;
        MapHoverPreviewId.Text = $"Mapa {mapId}";

        var cached = _vm.TryGetMapHoverPreview(mapId);
        if (cached is not null)
        {
            MapHoverPreviewDims.Text = $"{cached.Width} × {cached.Height} celdas";
            MapHoverPreviewDims.Visibility = Visibility.Visible;
            MapHoverPreviewImage.Source = cached.Image;
            MapHoverLoading.Visibility = Visibility.Collapsed;
        }
        else
        {
            MapHoverPreviewDims.Visibility = Visibility.Collapsed;
            MapHoverPreviewImage.Source = null;
            MapHoverLoading.Text = "Cargando…";
            MapHoverLoading.Visibility = Visibility.Visible;
        }

        MapHoverPopup.PlacementTarget = border;
        MapHoverPopup.IsOpen = true;

        if (cached is not null)
            return;

        var preview = await _vm.GetMapHoverPreviewAsync(mapId);
        if (_hoverMapPreviewId != mapId)
            return;

        if (preview is null)
        {
            MapHoverLoading.Text = "Sin vista previa";
            MapHoverLoading.Visibility = Visibility.Visible;
            return;
        }

        MapHoverPreviewDims.Text = $"{preview.Width} × {preview.Height} celdas";
        MapHoverPreviewDims.Visibility = Visibility.Visible;
        MapHoverLoading.Visibility = Visibility.Collapsed;
        MapHoverPreviewImage.Source = preview.Image;

        if (!item.HasThumbnail)
        {
            item.Thumbnail = preview.Image;
            item.IsLoading = false;
        }
    }

    private void MapListItem_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverMapPreviewId = -1;
        MapHoverPopup.IsOpen = false;
        MapHoverLoading.Text = "Cargando…";
    }

    private void GfxItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not GfxItemVm item)
            return;

        _vm.EnsureThumbnail(item);
        HoverPreviewImage.Source = _vm.GetCatalogHoverPreview(item);
        HoverPreviewId.Text = $"GfxID {item.Id}";
        var dims = _vm.FormatCatalogHoverDetails(item);
        HoverPreviewDims.Text = dims;
        HoverPreviewDims.Visibility = string.IsNullOrEmpty(dims) ? Visibility.Collapsed : Visibility.Visible;
        GfxHoverPopup.PlacementTarget = border;
        GfxHoverPopup.IsOpen = true;
    }

    private void GfxItem_MouseLeave(object sender, MouseEventArgs e)
    {
        GfxHoverPopup.IsOpen = false;
    }

    private void ReplaceGfx_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.World.IsMultiMapEditMode)
        {
            if (_vm.MultiMap.Selection.Count == 0)
            {
                MessageBox.Show("Selecciona celdas en multimap primero.", "Reemplazar GFX");
                return;
            }

            var mmDlg = new ReplaceGfxWindow(_vm.PaintLayer.ToString(), _vm.SelectedGfxId) { Owner = Window.GetWindow(this) };
            if (mmDlg.ShowDialog() != true) return;
            var mmCount = _vm.MultiMap.CountReplace(mmDlg.FindId, _vm.PaintLayer);
            if (mmCount == 0)
            {
                MessageBox.Show("Ninguna celda coincide.", "Reemplazar GFX");
                return;
            }

            if (MessageBox.Show($"{mmCount} celdas serán modificadas.\n\n¿Aplicar?", "Reemplazar GFX",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            _vm.ApplyMultiMapReplace(mmDlg.FindId, mmDlg.ReplaceId);
            return;
        }

        if (!_vm.HasSelection)
        {
            MessageBox.Show("Selecciona celdas primero.", "Reemplazar GFX");
            return;
        }

        var dlg = new ReplaceGfxWindow(_vm.PaintLayer.ToString(), _vm.SelectedGfxId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        var count = _vm.ReplaceGfx(dlg.FindId, dlg.ReplaceId);
        if (count == 0)
        {
            MessageBox.Show("Ninguna celda de la selección coincide.", "Reemplazar GFX");
            return;
        }

        if (MessageBox.Show($"{count} celdas serán modificadas.\n\n¿Aplicar?", "Reemplazar GFX",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _vm.ApplyReplace(dlg.FindId, dlg.ReplaceId);
    }

    private void ApplyToSelection_Click(object sender, RoutedEventArgs e) =>
        _vm.ApplyBrushToSelectionCommand.Execute(null);

    private async void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Header: string path })
            await _vm.OpenRecentProjectAsync(path);
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HighlightedInspectorLayer)
            or nameof(MainViewModel.IsInspectorGroundHighlighted)
            or nameof(MainViewModel.IsInspectorObject1Highlighted)
            or nameof(MainViewModel.IsInspectorObject2Highlighted))
        {
            UpdateInspectorHighlights();
        }

        if (e.PropertyName is nameof(MainViewModel.ShowMapsPanel)
            or nameof(MainViewModel.ShowInspectorPanel)
            or nameof(MainViewModel.ShowCatalogPanel)
            or nameof(MainViewModel.ShowCategoriesPanel)
            or nameof(MainViewModel.ShowBrushPanel)
            or nameof(MainViewModel.ShowToolBar)
            or nameof(MainViewModel.ShowStatusBar))
        {
            ApplyLayoutFromSettings();
        }
    }

    private void UpdateInspectorHighlights()
    {
        InspectorGroundBorder.BorderThickness = new Thickness(_vm.IsInspectorGroundHighlighted ? 3 : 2);
        InspectorObject1Border.BorderThickness = new Thickness(_vm.IsInspectorObject1Highlighted ? 3 : 2);
        InspectorObject2Border.BorderThickness = new Thickness(_vm.IsInspectorObject2Highlighted ? 3 : 2);
    }

    private void ScrollCatalogToGfx(int gfxId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            for (var rowIndex = 0; rowIndex < _vm.VisibleGfxRows.Count; rowIndex++)
            {
                var row = _vm.VisibleGfxRows[rowIndex];
                var colIndex = -1;
                for (var i = 0; i < row.Items.Count; i++)
                {
                    if (row.Items[i].Id == gfxId) { colIndex = i; break; }
                }

                if (colIndex < 0) continue;
                GfxCatalogList.SelectedIndex = rowIndex;
                GfxCatalogList.ScrollIntoView(row.Items[colIndex]);
                _vm.SelectGfx(row.Items[colIndex]);
                return;
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void InspectorGround_Click(object sender, MouseButtonEventArgs e) =>
        _vm.SelectInspectorGroundCommand.Execute(null);

    private void InspectorObject1_Click(object sender, MouseButtonEventArgs e) =>
        _vm.SelectInspectorObject1Command.Execute(null);

    private void InspectorObject2_Click(object sender, MouseButtonEventArgs e) =>
        _vm.SelectInspectorObject2Command.Execute(null);
}
