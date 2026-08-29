using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Controls;

public partial class MapViewport : UserControl
{
    private bool _hasFittedContent;
    private readonly ViewportCamera _camera = new();
    private MainViewModel? _vm;
    private OpenMapDocument? _boundDocument;
    private bool _panning;
    private bool _stroking;
    private bool _erasing;
    private bool _rectDragging;
    private bool _leftPressPending;
    private Point _panLast;
    private Point _leftPressPos;
    private bool _spaceDown;
    private double? _lastHitContentX;
    private double? _lastHitContentY;
    private const double PanDragThreshold = 3;

    private static readonly SolidColorBrush HoverDiamondFill =
        new(Color.FromArgb(60, 64, 160, 255));
    private static readonly SolidColorBrush HoverDiamondStroke =
        new(Color.FromArgb(220, 80, 180, 255));
    private static readonly SolidColorBrush SelectionFill =
        new(Color.FromArgb(90, 255, 200, 40));
    private static readonly SolidColorBrush SelectionStroke =
        new(Color.FromArgb(255, 255, 180, 40));
    private static readonly SolidColorBrush SecondarySelectionFill =
        new(Color.FromArgb(50, 255, 200, 40));
    private static readonly SolidColorBrush GfxBoundsStroke =
        new(Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush GfxHighlightStroke =
        new(Color.FromArgb(255, 255, 220, 80));
    private static readonly SolidColorBrush GroundHighlightStroke =
        new(Color.FromArgb(255, 120, 200, 255));
    static MapViewport()
    {
        HoverDiamondFill.Freeze();
        HoverDiamondStroke.Freeze();
        SelectionFill.Freeze();
        SelectionStroke.Freeze();
        SecondarySelectionFill.Freeze();
        GfxBoundsStroke.Freeze();
        GfxHighlightStroke.Freeze();
        GroundHighlightStroke.Freeze();
    }

    private static Brush OverlayBrush(string key) => ThemeService.GetBrush(key);

    public MapViewport()
    {
        InitializeComponent();
        Focusable = true;
        ThemeService.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) =>
        {
            _camera.SetViewportSize(ActualWidth, ActualHeight);
            TryFitContentIfNeeded();
            ApplyTransform();
        };
        Loaded += (_, _) =>
        {
            _camera.SetViewportSize(ActualWidth, ActualHeight);
            TryFitContentIfNeeded();
            ApplyTransform();
        };
    }

    public OpenMapDocument? BoundDocument
    {
        get => _boundDocument;
        set
        {
            if (_boundDocument is not null)
                _boundDocument.PropertyChanged -= BoundDocumentOnPropertyChanged;
            _boundDocument = value;
            _hasFittedContent = false;
            if (_boundDocument is not null)
                _boundDocument.PropertyChanged += BoundDocumentOnPropertyChanged;
            SyncFromViewModel();
            TryFitContentIfNeeded();
            ApplyTransform();
        }
    }

    private void BoundDocumentOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenMapDocument.MapImage))
        {
            _hasFittedContent = false;
            SyncFromViewModel();
            TryFitContentIfNeeded();
            ApplyTransform();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
            _vm.RequestFitMap -= OnRequestFitMap;
            _vm.RequestZoom100 -= OnRequestZoom100;
        }

        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += VmOnPropertyChanged;
            _vm.RequestFitMap += OnRequestFitMap;
            _vm.RequestZoom100 += OnRequestZoom100;
            SyncFromViewModel();
        }
    }

    private void OnRequestFitMap()
    {
        if (_boundDocument is not null && _vm is not null &&
            !ReferenceEquals(_boundDocument, _vm.ActiveDocument))
            return;
        FitMap();
    }

    private void OnRequestZoom100()
    {
        if (_boundDocument is not null && _vm is not null &&
            !ReferenceEquals(_boundDocument, _vm.ActiveDocument))
            return;
        Zoom100();
    }

    private bool IsBoundActive =>
        _boundDocument is null || (_vm is not null && ReferenceEquals(_boundDocument, _vm.ActiveDocument));

    private IsoHitTester? ResolveHitTester() => _boundDocument?.HitTester ?? _vm?.HitTester;

    private MapDocument? ResolveMap() => _boundDocument?.Map ?? _vm?.CurrentMap;

    private ImageSource? ResolveMapImage() => _boundDocument?.MapImage ?? _vm?.MapImage;

    private void VmOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.MapImage):
            case nameof(MainViewModel.HitTester):
                // Bound windows keep their own image/hit-tester; only unbound/active sync from VM.
                if (_boundDocument is not null && !IsBoundActive)
                    return;
                SyncFromViewModel();
                break;
            case nameof(MainViewModel.ShowGrid):
            case nameof(MainViewModel.ShowCellIds):
            case nameof(MainViewModel.ShowCellIdsEffective):
            case nameof(MainViewModel.ShowDebugInfo):
            case nameof(MainViewModel.ShowUnwalkableMarkers):
            case nameof(MainViewModel.ShowLosBlockMarkers):
            case nameof(MainViewModel.ShowFightMarkers):
            case nameof(MainViewModel.ShowMapExportLimit):
            case nameof(MainViewModel.CellModeOverlayRevision):
            case nameof(MainViewModel.FixedMobsOverlayRevision):
                // Visibility toggles apply to every open floating map.
                RedrawOverlays();
                break;
            case nameof(MainViewModel.HoveredCellId):
            case nameof(MainViewModel.SelectedCellIds):
            case nameof(MainViewModel.PrimarySelectedCellId):
            case nameof(MainViewModel.ViewportZoom):
            case nameof(MainViewModel.IsRectSelecting):
            case nameof(MainViewModel.RectSelectBounds):
            case nameof(MainViewModel.SelectedGfxId):
            case nameof(MainViewModel.BrushFlip):
            case nameof(MainViewModel.BrushRotation):
            case nameof(MainViewModel.PaintLayer):
            case nameof(MainViewModel.BrushPreview):
            case nameof(MainViewModel.HighlightedInspectorLayer):
            case nameof(MainViewModel.Tool):
            case nameof(MainViewModel.CurrentMap):
                if (!IsBoundActive)
                    return;
                RedrawOverlays();
                break;
        }
    }

    private void SyncFromViewModel()
    {
        if (_vm is null && _boundDocument is null) return;
        var image = ResolveMapImage();
        MapImage.Source = image;
        if (image is BitmapSource bmp)
        {
            _camera.SetContentSize(bmp.PixelWidth, bmp.PixelHeight);
            MapImage.Width = bmp.PixelWidth;
            MapImage.Height = bmp.PixelHeight;
            OverlayCanvas.Width = bmp.PixelWidth;
            OverlayCanvas.Height = bmp.PixelHeight;
        }
        else
        {
            _camera.SetContentSize(0, 0);
            OverlayCanvas.Children.Clear();
        }

        ApplyTransform();
        // Always redraw this window's own overlays (fight/LoS/…) from BoundDocument.
        RedrawOverlays();
        UpdateZoomLabel();
    }

    public void FitMap()
    {
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        if (ActualWidth > 1 && ActualHeight > 1 && _camera.ContentWidth > 0 && _camera.ContentHeight > 0)
        {
            _camera.FitToViewport();
            _hasFittedContent = true;
        }
        ApplyTransform();
        UpdateZoomLabel();
    }

    /// <summary>Keep zoom/pan; only refresh camera viewport size after host resize.</summary>
    public void NotifyViewportSizeChanged()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        TryFitContentIfNeeded();
        ApplyTransform();
        UpdateZoomLabel();
    }

    private void TryFitContentIfNeeded()
    {
        if (_hasFittedContent) return;
        if (ActualWidth <= 1 || ActualHeight <= 1) return;
        if (ResolveMapImage() is null || _camera.ContentWidth <= 0 || _camera.ContentHeight <= 0)
            return;
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        _camera.FitToViewport();
        _hasFittedContent = true;
        UpdateZoomLabel();
    }

    public void Zoom100()
    {
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        _camera.SetActualSizeCentered();
        ApplyTransform();
        UpdateZoomLabel();
    }

    private void ApplyTransform()
    {
        WorldScale.ScaleX = _camera.Zoom;
        WorldScale.ScaleY = _camera.Zoom;
        WorldTranslate.X = _camera.OffsetX;
        WorldTranslate.Y = _camera.OffsetY;
    }

    private void UpdateZoomLabel()
    {
        if (_vm is null) return;
        _vm.ZoomText = $"{_camera.ZoomPercent}%";
        _vm.ViewportZoom = _camera.Zoom;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((_boundDocument?.MapImage ?? _vm?.MapImage) is null) return;
        var pos = e.GetPosition(this);
        _camera.ZoomByFactorAt(pos.X, pos.Y, e.Delta > 0 ? 1.1 : 1.0 / 1.1);
        ApplyTransform();
        UpdateZoomLabel();
        e.Handled = true;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_vm is not null && _boundDocument is not null &&
            !ReferenceEquals(_boundDocument, _vm.ActiveDocument))
        {
            _vm.ActivateDocument(_boundDocument);
        }

        if ((_boundDocument?.MapImage ?? _vm?.MapImage) is null) return;

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left &&
             (Keyboard.IsKeyDown(Key.Space) ||
              _spaceDown ||
              (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt ||
              _vm?.Tool == EditorTool.Pan)))
        {
            BeginPan(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var rightPos = e.GetPosition(this);
            var eraseCell = HitCell(rightPos);
            if (eraseCell is int eraseId)
            {
                if (_vm.IsCellModeTool)
                {
                    _erasing = true;
                    _vm.BeginCellModeEraseStroke();
                    _vm.PaintCellMode(eraseId, isDrag: false, erase: true);
                    CaptureMouse();
                }
                else if (_vm.Tool == EditorTool.Paint && _vm.SelectedGfxId is int)
                {
                    // MAP-PAINT.1 — right-click in paint with active brush:
                    // only remove the active GFX on the active layer (mistaken stamp).
                    // Never delete unrelated GFX on other cells/layers.
                    if (_vm.TryEraseActiveBrushAtCell(eraseId))
                    {
                        _erasing = true;
                        CaptureMouse();
                    }
                    // else: safe no-op
                }
                else if (_vm.Tool == EditorTool.Erase)
                {
                    _erasing = true;
                    _vm.BeginEraseStroke();
                    _vm.EraseCell(eraseId, isDrag: false);
                    CaptureMouse();
                }
                else
                {
                    // Select / no brush: pick up GFX into paint brush (existing behaviour).
                    _erasing = true;
                    _vm.DeleteGfxAndEnterBuildMode(eraseId);
                    CaptureMouse();
                }
            }

            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(this);
        var (cx, cy) = _camera.ViewportToContent(pos.X, pos.Y);

        if (cx >= 0 && cy >= 0 && cx < _camera.ContentWidth && cy < _camera.ContentHeight)
            _vm.UpdateHover(cx, cy);
        _lastHitContentX = cx;
        _lastHitContentY = cy;

        // Rect select needs drag for the marquee — keep immediate behaviour.
        if (_vm.Tool == EditorTool.RectSelect)
        {
            _rectDragging = true;
            _vm.BeginRectSelect(cx, cy);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Hold + drag anywhere = pan. Short click (release without dragging) = tool action.
        _leftPressPending = true;
        _leftPressPos = pos;
        CaptureMouse();
        e.Handled = true;
    }

    private void BeginPan(Point viewportPos)
    {
        _leftPressPending = false;
        _panning = true;
        _panLast = viewportPos;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private static bool ExceededPanDragThreshold(Point from, Point to) =>
        Math.Abs(to.X - from.X) >= PanDragThreshold || Math.Abs(to.Y - from.Y) >= PanDragThreshold;

    /// <summary>Apply select/paint/etc. for a short left click (no pan drag).</summary>
    private void PerformLeftClickAction(Point pos)
    {
        if (_vm is null) return;

        var (cx, cy) = _camera.ViewportToContent(pos.X, pos.Y);
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (cx >= 0 && cy >= 0 && cx < _camera.ContentWidth && cy < _camera.ContentHeight)
            _vm.UpdateHover(cx, cy);
        _lastHitContentX = cx;
        _lastHitContentY = cy;

        var cell = _vm.HoveredCellId ?? HitCell(pos);
        if (cell is not int id)
            return;

        if (_vm.IsCellModeTool)
        {
            _vm.BeginCellModeStroke();
            _vm.PaintCellMode(id, isDrag: false, erase: false);
            _vm.FinishStroke();
        }
        else
        {
            var paintWithGfx = _vm.SelectedGfxId is int
                && _vm.Tool is not EditorTool.RectSelect
                && _vm.Tool is not EditorTool.Eyedropper
                && _vm.Tool is not EditorTool.Pan
                && !(_vm.Tool == EditorTool.Select && ctrl);

            if (paintWithGfx)
            {
                _vm.BeginPaintStroke();
                _vm.PaintCell(id, isDrag: false);
                _vm.FinishStroke();
            }
            else if (_vm.Tool is EditorTool.Paint or EditorTool.Erase)
            {
                _vm.BeginStroke();
                _vm.HandleCellClick(id, isDrag: false, ctrl);
                _vm.FinishStroke();
            }
            else
            {
                _vm.HandleCellClick(id, isDrag: false, ctrl);
            }
        }

        RedrawOverlays();
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _leftPressPending)
        {
            _leftPressPending = false;
            var clickPos = _leftPressPos;
            ReleaseMouseCapture();
            PerformLeftClickAction(clickPos);
            if (!_stroking && !_erasing && !_rectDragging && !_panning)
                ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            _leftPressPending = false;
            ReleaseMouseCapture();
            Cursor = _vm?.Tool == EditorTool.Pan ? Cursors.Hand : Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (_erasing && e.ChangedButton == MouseButton.Right)
        {
            _erasing = false;
            _vm?.FinishStroke();
            ReleaseMouseCapture();
            RedrawOverlays();
            e.Handled = true;
            return;
        }

        if (_rectDragging && e.ChangedButton == MouseButton.Left)
        {
            var upPos = e.GetPosition(this);
            var (ux, uy) = _camera.ViewportToContent(upPos.X, upPos.Y);
            _vm?.EndRectSelect(ux, uy);
            _rectDragging = false;
            ReleaseMouseCapture();
            RedrawOverlays();
            e.Handled = true;
            return;
        }

        if (_stroking && e.ChangedButton == MouseButton.Left)
        {
            _stroking = false;
            _vm?.FinishStroke();
            ReleaseMouseCapture();
            RedrawOverlays();
            e.Handled = true;
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_leftPressPending && e.LeftButton == MouseButtonState.Pressed &&
            ExceededPanDragThreshold(_leftPressPos, pos))
        {
            BeginPan(_leftPressPos);
            _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
            _panLast = pos;
            ApplyTransform();
            return;
        }

        if (_panning)
        {
            _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
            _panLast = pos;
            ApplyTransform();
            return;
        }

        // Inactive floating windows must not hijack shared hover / paint state.
        if (!IsBoundActive)
            return;

        if (ResolveMapImage() is null)
        {
            _vm?.ClearHover();
            return;
        }

        var (cx, cy) = _camera.ViewportToContent(pos.X, pos.Y);

        if (_rectDragging)
        {
            _vm!.UpdateRectSelect(cx, cy);
            RedrawOverlays();
            return;
        }

        if (cx < 0 || cy < 0 || cx >= _camera.ContentWidth || cy >= _camera.ContentHeight)
        {
            _vm!.ClearHover();
            RedrawOverlays();
            return;
        }

        _vm!.UpdateHover(cx, cy);
        _lastHitContentX = cx;
        _lastHitContentY = cy;

        if (_stroking && e.LeftButton == MouseButtonState.Pressed)
            _vm.ContinueStroke(cx, cy);

        if (_erasing && e.RightButton == MouseButtonState.Pressed)
            _vm.ContinueStroke(cx, cy);

        RedrawOverlays();
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_panning || _stroking || _erasing || _rectDragging || _leftPressPending) return;
        if (!IsBoundActive) return;
        _vm?.ClearHover();
        RedrawOverlays();
    }

    private void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceDown = true;
            if (!_panning && !_stroking && !_erasing && !_rectDragging)
                Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
        if (IsTextInputFocused()) return;

        if (e.Key == Key.Delete)
        {
            _vm?.ClearActiveLayerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Viewport_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            if (!_panning)
                Cursor = _vm?.Tool == EditorTool.Pan ? Cursors.Hand : Cursors.Arrow;
            e.Handled = true;
        }
    }

    private static bool IsTextInputFocused() =>
        Keyboard.FocusedElement is TextBox;

    private int? HitCell(Point viewportPos)
    {
        var tester = ResolveHitTester();
        if (tester is null) return null;
        var (cx, cy) = _camera.ViewportToContent(viewportPos.X, viewportPos.Y);
        return tester.HitTest(cx, cy);
    }

    private void RedrawOverlays()
    {
        OverlayCanvas.Children.Clear();
        var tester = ResolveHitTester();
        var mapImage = ResolveMapImage();
        if (tester is null || mapImage is null || _vm is null) return;

        if (_vm.ShowGrid)
            DrawGrid(tester);

        // Always from this window's map — never from another floating document.
        DrawCellModeMarkers(tester);
        DrawFixedMobMarkers(tester);
        DrawMobTargetCell(tester);

        if (_vm.ShowMapExportLimit && mapImage is BitmapSource bmp)
            DrawMapExportLimit(bmp.PixelWidth, bmp.PixelHeight);

        if (_vm.ShowCellIdsEffective)
            DrawCellIds(tester);

        // Interactive chrome only on the active floating window.
        if (!IsBoundActive)
            return;

        foreach (var sel in _vm.SelectedCellIds)
        {
            if (!tester.TryGetCellCornersInHitSpace(sel, out var corners)) continue;
            var isPrimary = sel == _vm.PrimarySelectedCellId;
            OverlayCanvas.Children.Add(CreateDiamond(corners,
                isPrimary ? SelectionFill : SecondarySelectionFill,
                SelectionStroke, isPrimary ? 2.0 : 1.2));

            DrawSelectedCellGfxBounds(sel, isPrimary);
        }

        var paintTarget = _vm.Tool is EditorTool.Paint or EditorTool.Erase or EditorTool.Unwalkable
            or EditorTool.LineOfSight or EditorTool.FightCell1 or EditorTool.FightCell2
            or EditorTool.MobCell
            ? _vm.HoveredCellId : null;

        if (paintTarget is int targetCell &&
            tester.TryGetCellCornersInHitSpace(targetCell, out var targetCorners))
        {
            if (_vm.Tool == EditorTool.Paint && _vm.SelectedGfxId is not null)
            {
                DrawBrushPreview(targetCell);
                DrawBrushPreviewBounds(targetCell);
            }
            else if (_vm.Tool == EditorTool.Erase)
            {
                DrawBrushPreviewBounds(targetCell);
            }
            else if (_vm.IsCellModeTool)
            {
                DrawCellModeHoverPreview(targetCell, targetCorners);
            }
            else if (_vm.Tool == EditorTool.MobCell)
            {
                OverlayCanvas.Children.Add(CreateDiamond(targetCorners,
                    OverlayBrush("OverlayFixedMobFill"), OverlayBrush("OverlayFixedMobStroke"), 2.2));
                DrawCellModeLabel(targetCorners, "M", OverlayBrush("OverlayFixedMobLabel"));
            }

            DrawPaintTargetDiamond(targetCorners);
        }
        else if (_vm.HoveredCellId is int hover &&
                 !_vm.SelectedCellIds.Contains(hover) &&
                 tester.TryGetCellCornersInHitSpace(hover, out var hoverCorners))
        {
            OverlayCanvas.Children.Add(CreateDiamond(hoverCorners,
                HoverDiamondFill, HoverDiamondStroke, 1.5));

            DrawHoverGfxBounds(hover);

            if (_vm.ShowDebugInfo)
            {
                var label = new TextBlock
                {
                    Text = $"Cell {hover}",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 11,
                };
                Canvas.SetLeft(label, hoverCorners.A.X + 4);
                Canvas.SetTop(label, hoverCorners.A.Y - 18);
                OverlayCanvas.Children.Add(label);
            }
        }
        else if (_vm.HoveredCellId is int hoverSelected &&
                 _vm.SelectedCellIds.Contains(hoverSelected) &&
                 _vm.Tool is EditorTool.Paint or EditorTool.Eyedropper)
        {
            DrawBrushPreview(hoverSelected);
        }

        if (_vm.IsRectSelecting && _vm.RectSelectBounds is { } b)
        {
            var rect = new Rectangle
            {
                Width = Math.Abs(b.X1 - b.X0),
                Height = Math.Abs(b.Y1 - b.Y0),
                Stroke = new SolidColorBrush(Color.FromArgb(220, 100, 200, 255)),
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromArgb(40, 100, 180, 255)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, Math.Min(b.X0, b.X1));
            Canvas.SetTop(rect, Math.Min(b.Y0, b.Y1));
            OverlayCanvas.Children.Add(rect);
        }

        if (_vm.ShowDebugInfo && _vm.HoveredCellId is int dbgCell)
            DrawCellDebugOverlay(dbgCell, tester, _lastHitContentX, _lastHitContentY);
    }

    private void DrawSelectedCellGfxBounds(int cellId, bool isPrimary)
    {
        if (_vm is null || !isPrimary) return;

        if (_vm.Tool == EditorTool.Paint && _vm.SelectedGfxId is not null &&
            cellId == _vm.PrimarySelectedCellId)
        {
            DrawBrushPreviewBounds(cellId);
            return;
        }

        var highlight = _vm.HighlightedInspectorLayer;
        if (highlight != InspectorLayerHighlight.None)
        {
            var layer = highlight switch
            {
                InspectorLayerHighlight.Ground => PaintLayer.Ground,
                InspectorLayerHighlight.Object1 => PaintLayer.Object1,
                _ => PaintLayer.Object2,
            };
            DrawLayerBounds(cellId, layer, highlighted: true);
            return;
        }

        DrawLayerBounds(cellId, PaintLayer.Ground, highlighted: false);
        DrawLayerBounds(cellId, PaintLayer.Object1, highlighted: false);
        DrawLayerBounds(cellId, PaintLayer.Object2, highlighted: false);
    }

    private void DrawHoverGfxBounds(int cellId)
    {
        if (_vm is null) return;

        if (_vm.Tool == EditorTool.Select || _vm.Tool == EditorTool.RectSelect)
        {
            DrawLayerBounds(cellId, PaintLayer.Ground, highlighted: false);
            DrawLayerBounds(cellId, PaintLayer.Object1, highlighted: false);
            DrawLayerBounds(cellId, PaintLayer.Object2, highlighted: false);
            return;
        }

        if (_vm.Tool == EditorTool.Paint && _vm.SelectedGfxId is not null)
        {
            DrawBrushPreviewBounds(cellId);
            return;
        }
    }

    private void DrawPaintTargetDiamond(IsoGeometry.CellCorners corners)
    {
        var fill = ThemeService.GetBrush("OverlayPaintTargetFill");
        var stroke = ThemeService.GetBrush("OverlayPaintTargetStroke");
        OverlayCanvas.Children.Add(CreateDiamond(corners, fill, stroke, 2.8));
        OverlayCanvas.Children.Add(CreateDiamond(corners, Brushes.Transparent, stroke, 1.2));
    }

    private void DrawBrushPreviewBounds(int cellId)
    {
        if (_vm is null || !_vm.TryGetBrushPreviewVisual(cellId, out var visual))
            return;

        var rect = CreateBoundsRect(visual.Bounds, GfxBoundsStroke, 1.0);
        Canvas.SetLeft(rect, visual.Bounds.X);
        Canvas.SetTop(rect, visual.Bounds.Y);
        OverlayCanvas.Children.Add(rect);
    }

    private void DrawLayerBounds(int cellId, PaintLayer layer, bool highlighted)
    {
        if (_vm is null) return;
        if (!_vm.TryGetCellLayerVisual(cellId, layer, out var visual))
            return;

        var stroke = highlighted
            ? layer == PaintLayer.Ground ? GroundHighlightStroke : GfxHighlightStroke
            : GfxBoundsStroke;
        var thickness = highlighted ? 2.0 : 1.0;
        var rect = CreateBoundsRect(visual.Bounds, stroke, thickness);
        Canvas.SetLeft(rect, visual.Bounds.X);
        Canvas.SetTop(rect, visual.Bounds.Y);
        OverlayCanvas.Children.Add(rect);
    }

    private void DrawBrushPreview(int cellId)
    {
        if (_vm is null || _vm.Tool != EditorTool.Paint || _vm.SelectedGfxId is null)
            return;

        if (!_vm.TryGetBrushPreviewVisual(cellId, out var visual))
            return;

        var img = new Image
        {
            Source = visual.Image,
            Width = visual.Bounds.Width,
            Height = visual.Bounds.Height,
            Stretch = Stretch.Fill,
            Opacity = 0.55,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, visual.Bounds.X);
        Canvas.SetTop(img, visual.Bounds.Y);
        OverlayCanvas.Children.Add(img);

        if (_vm.ShowDebugInfo)
            DrawPlacementDebugLabel(cellId, visual.Bounds, isPreview: true);
    }

    private void DrawPlacementDebugLabel(int cellId, GfxPlacementMath.PlacementRect bounds, bool isPreview)
    {
        if (ResolveHitTester() is null) return;
        var prefix = isPreview ? "Preview" : "Final";
        var label = new TextBlock
        {
            Text = $"{prefix} c{cellId} @ {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}",
            Foreground = Brushes.Lime,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            Padding = new Thickness(3, 1, 3, 1),
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
        };
        Canvas.SetLeft(label, bounds.X);
        Canvas.SetTop(label, Math.Max(0, bounds.Y - 14));
        OverlayCanvas.Children.Add(label);
    }

    private void DrawCellModeMarkers(IsoHitTester tester)
    {
        if (_vm is null) return;
        var map = ResolveMap();
        if (map is null) return;
        var cells = map.Cells;
        for (var id = 0; id < tester.Corners.Count && id < cells.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var corners)) continue;
            var cell = cells[id];

            if (cell.Movement == MovementType.Unwalkable && _vm.ShowUnwalkableMarkers)
            {
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayUnwalkableFill"), OverlayBrush("OverlayUnwalkableStroke"), 1.5));
                DrawUnwalkableCross(corners);
            }

            if (!cell.LineOfSight && _vm.ShowLosBlockMarkers)
            {
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayLosBlockFill"), OverlayBrush("OverlayLosBlockStroke"), 1.2));
                DrawLosInnerDiamond(corners);
            }

            if (_vm.ShowFightMarkers && cell.FightCell == 1)
            {
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayFight1Fill"), OverlayBrush("OverlayFight1Stroke"), 1.5));
                DrawCellModeLabel(corners, "1", OverlayBrush("OverlayFightLabel"));
            }
            else if (_vm.ShowFightMarkers && cell.FightCell == 2)
            {
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayFight2Fill"), OverlayBrush("OverlayFight2Stroke"), 1.5));
                DrawCellModeLabel(corners, "2", OverlayBrush("OverlayFightLabel"));
            }
        }
    }

    /// <summary>LIB.4 — visual marks for mobs_fix cells on the open map (does not alter cell flags).</summary>
    private void DrawFixedMobMarkers(IsoHitTester tester)
    {
        if (_vm is null) return;
        var map = ResolveMap();
        if (map is null) return;
        // Only draw markers that belong to this floating map's id.
        if (map.Id != (_vm.CurrentMap?.Id ?? map.Id))
            return;
        if (_vm.MapMonsters.FixedMobCellIds.Count == 0)
            return;

        var fill = OverlayBrush("OverlayFixedMobFill");
        var stroke = OverlayBrush("OverlayFixedMobStroke");
        var labelBrush = OverlayBrush("OverlayFixedMobLabel");
        foreach (var cellId in _vm.MapMonsters.FixedMobCellIds)
        {
            if (!tester.TryGetCellCornersInHitSpace(cellId, out var corners)) continue;
            OverlayCanvas.Children.Add(CreateDiamond(corners, fill, stroke, 1.6));
            DrawCellModeLabel(corners, "M", labelBrush);
        }
    }

    private void DrawMobTargetCell(IsoHitTester tester)
    {
        if (_vm is null) return;
        if (_vm.MapMonsters.MobTargetCellId is not int cellId) return;
        if (!tester.TryGetCellCornersInHitSpace(cellId, out var corners)) return;
        OverlayCanvas.Children.Add(CreateDiamond(corners,
            OverlayBrush("OverlayMobTargetFill"), OverlayBrush("OverlayMobTargetStroke"), 2.4));
        DrawCellModeLabel(corners, "●", OverlayBrush("OverlayFixedMobLabel"));
    }

    private void DrawCellModeHoverPreview(int cellId, IsoGeometry.CellCorners corners)
    {
        if (_vm is null) return;
        switch (_vm.Tool)
        {
            case EditorTool.Unwalkable:
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayUnwalkableFill"), OverlayBrush("OverlayUnwalkableStroke"), 2.0));
                DrawUnwalkableCross(corners);
                break;
            case EditorTool.LineOfSight:
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayLosBlockFill"), OverlayBrush("OverlayLosBlockStroke"), 2.0));
                DrawLosInnerDiamond(corners);
                break;
            case EditorTool.FightCell1:
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayFight1Fill"), OverlayBrush("OverlayFight1Stroke"), 2.0));
                DrawCellModeLabel(corners, "1", OverlayBrush("OverlayFightLabel"));
                break;
            case EditorTool.FightCell2:
                OverlayCanvas.Children.Add(CreateDiamond(corners,
                    OverlayBrush("OverlayFight2Fill"), OverlayBrush("OverlayFight2Stroke"), 2.0));
                DrawCellModeLabel(corners, "2", OverlayBrush("OverlayFightLabel"));
                break;
        }
    }

    private void DrawMapExportLimit(int width, int height)
    {
        var brush = Application.Current.TryFindResource("MapBoundaryBrush") as Brush
            ?? new SolidColorBrush(Color.FromArgb(220, 200, 120, 48));
        var rect = new Rectangle
        {
            Width = Math.Max(0, width - 1),
            Height = Math.Max(0, height - 1),
            Stroke = brush,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, 0.5);
        Canvas.SetTop(rect, 0.5);
        OverlayCanvas.Children.Add(rect);
    }

    private void DrawUnwalkableCross(IsoGeometry.CellCorners c)
    {
        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        var half = Math.Max(4, (c.B.X - c.A.X) / 6);
        OverlayCanvas.Children.Add(new Line
        {
            X1 = cx - half, Y1 = cy - half / 2,
            X2 = cx + half, Y2 = cy + half / 2,
            Stroke = OverlayBrush("OverlayUnwalkableStroke"), StrokeThickness = 1.5, IsHitTestVisible = false,
        });
        OverlayCanvas.Children.Add(new Line
        {
            X1 = cx + half, Y1 = cy - half / 2,
            X2 = cx - half, Y2 = cy + half / 2,
            Stroke = OverlayBrush("OverlayUnwalkableStroke"), StrokeThickness = 1.5, IsHitTestVisible = false,
        });
    }

    private void DrawLosInnerDiamond(IsoGeometry.CellCorners c)
    {
        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        var w = Math.Max(3, (c.B.X - c.A.X) / 5);
        var h = Math.Max(2, (c.D.Y - c.A.Y) / 6);
        var losStroke = OverlayBrush("OverlayLosBlockStroke");
        OverlayCanvas.Children.Add(new Polygon
        {
            Points = [new Point(cx, cy - h), new Point(cx + w, cy), new Point(cx, cy + h), new Point(cx - w, cy)],
            Fill = Brushes.Transparent,
            Stroke = losStroke,
            StrokeThickness = 1.4,
            IsHitTestVisible = false,
        });
    }

    private void OnThemeChanged() => RedrawOverlays();

    private void DrawCellModeLabel(IsoGeometry.CellCorners c, string text, Brush foreground)
    {
        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        var label = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, cx - 5);
        Canvas.SetTop(label, cy - 8);
        OverlayCanvas.Children.Add(label);
    }

    private void DrawGrid(IsoHitTester tester)
    {
        var stroke = ThemeService.GetBrush("OverlayGrid");
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c)) continue;
            OverlayCanvas.Children.Add(CreateDiamond(c, Brushes.Transparent, stroke, 0.8));
        }
    }

    private void DrawCellIds(IsoHitTester tester)
    {
        var fg = ThemeService.GetBrush("OverlayCellId");
        var shadow = ThemeService.GetBrush("OverlayCellIdShadow");
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c)) continue;
            var (cx, cy) = IsoGeometry.GetCellCenter(c);
            var label = new TextBlock
            {
                Text = id.ToString(),
                Foreground = fg,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
            };
            label.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = shadow.Color,
                BlurRadius = 2,
                ShadowDepth = 0,
                Opacity = 0.9,
            };
            Canvas.SetLeft(label, cx - 8);
            Canvas.SetTop(label, cy - 6);
            OverlayCanvas.Children.Add(label);
        }
    }

    private void DrawCellDebugOverlay(int cellId, IsoHitTester tester, double? hitX = null, double? hitY = null)
    {
        if (_vm is null || !_vm.ShowDebugInfo || !tester.TryGetCellCornersInHitSpace(cellId, out var c))
            return;

        var (cx, cy) = IsoGeometry.GetCellCenter(c);
        foreach (var (px, py, name) in new[]
                 {
                     (c.A.X, c.A.Y, "A"),
                     (c.B.X, c.B.Y, "B"),
                     (c.C.X, c.C.Y, "C"),
                     (c.D.X, c.D.Y, "D"),
                 })
        {
            OverlayCanvas.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Brushes.Lime,
                IsHitTestVisible = false,
            });
            Canvas.SetLeft(OverlayCanvas.Children[^1], px - 3);
            Canvas.SetTop(OverlayCanvas.Children[^1], py - 3);
            var vtx = new TextBlock
            {
                Text = name,
                Foreground = Brushes.Lime,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
            };
            Canvas.SetLeft(vtx, px + 4);
            Canvas.SetTop(vtx, py - 6);
            OverlayCanvas.Children.Add(vtx);
        }

        OverlayCanvas.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.Cyan,
            IsHitTestVisible = false,
        });
        Canvas.SetLeft(OverlayCanvas.Children[^1], cx - 4);
        Canvas.SetTop(OverlayCanvas.Children[^1], cy - 4);

        if (hitX is double hx && hitY is double hy)
        {
            OverlayCanvas.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            });
            Canvas.SetLeft(OverlayCanvas.Children[^1], hx - 5);
            Canvas.SetTop(OverlayCanvas.Children[^1], hy - 5);
        }

        var minX = Math.Min(Math.Min(c.A.X, c.B.X), Math.Min(c.C.X, c.D.X));
        var minY = Math.Min(Math.Min(c.A.Y, c.B.Y), Math.Min(c.C.Y, c.D.Y));
        var maxX = Math.Max(Math.Max(c.A.X, c.B.X), Math.Max(c.C.X, c.D.X));
        var maxY = Math.Max(Math.Max(c.A.Y, c.B.Y), Math.Max(c.C.Y, c.D.Y));
        var info = new TextBlock
        {
            Text = $"Cell {cellId}  center ({cx:F0},{cy:F0})  bounds {minX},{minY}-{maxX},{maxY}",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
        };
        Canvas.SetLeft(info, minX);
        Canvas.SetTop(info, Math.Max(0, minY - 22));
        OverlayCanvas.Children.Add(info);
    }

    private static Rectangle CreateBoundsRect(GfxPlacementMath.PlacementRect r, Brush stroke, double thickness) =>
        new()
        {
            Width = Math.Max(1, r.Width),
            Height = Math.Max(1, r.Height),
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };

    private static Polygon CreateDiamond(IsoGeometry.CellCorners c, Brush fill, Brush stroke, double thickness) =>
        new()
        {
            Points =
            [
                new Point(c.A.X, c.A.Y),
                new Point(c.B.X, c.B.Y),
                new Point(c.C.X, c.C.Y),
                new Point(c.D.X, c.D.Y),
            ],
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        };
}
