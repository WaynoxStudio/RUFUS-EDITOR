using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Controls;

public partial class WorldViewport : UserControl
{
    private readonly ViewportCamera _camera = new();
    private WorldViewModel? _vm;
    private bool _panning;
    private bool _draggingMap;
    private bool _marquee;
    private bool _pendingAddMap;
    private bool _viewPanPending;
    private int _pendingAddMapX;
    private int _pendingAddMapY;
    private Point _panLast;
    private Point _marqueeStart;
    private Point _leftPressPos;
    private string? _dragKey;
    private string? _hoveredMapKey;
    private Point _mapPressPos;
    private bool _mapDragMoved;
    private (int X, int Y)? _dragTargetCell;
    private bool _spaceDown;
    private bool _editStroking;
    private bool _editErasing;
    private bool _editRectDragging;
    private bool _combinedSelectPending;
    private WorldCellHit? _combinedPendingCell;
    private bool _combinedAltMapPending;
    private string? _combinedAltMapKey;
    private bool _movePending;
    private bool _movingSelection;
    private int? _moveGrabCellId;
    private string? _moveDocKey;
    private Point _strokeOriginViewport;
    private bool _strokeDragArmed;
    private double _contentOffsetX;
    private double _contentOffsetY;
    private const double ContentPadding = 80;
    private const double PanDragThreshold = 4;
    private const string DragPreviewTag = "DragPreview";
    private const string MapCloseBtnTag = "MapCloseBtn";
    private const double GridChromeBtnSize = 18;
    private const double GridChromeGap = 4;
    private const string GridLineChromeTag = "GridLineChrome";
    private const double MapCloseBtnSize = 22;
    private const double MapCloseBtnMargin = 6;

    /// <summary>Only the MAPA combinado viewport. World floating windows must stay independent.</summary>
    public bool IsCombinedMapsSurface { get; set; }

    private bool IsCombinedMapsInteraction =>
        IsCombinedMapsSurface && _vm?.EditorHost?.IsMapCombinedMode == true;

    private static readonly SolidColorBrush SelectionStroke = new(Color.FromArgb(255, 255, 200, 40));
    // Stroke-only selection — do not wash/dim the thumbnail.
    private static readonly SolidColorBrush SelectionFill = new(Color.FromArgb(0, 0, 0, 0));
    private static readonly SolidColorBrush MarqueeStroke = new(Color.FromArgb(200, 100, 180, 255));
    private static readonly SolidColorBrush MarqueeFill = new(Color.FromArgb(30, 100, 180, 255));
    private static readonly SolidColorBrush CellHoverFill = new(Color.FromArgb(60, 64, 160, 255));
    private static readonly SolidColorBrush CellHoverStroke = new(Color.FromArgb(220, 80, 180, 255));
    private static readonly SolidColorBrush DragPreviewFill = new(Color.FromArgb(50, 64, 160, 255));
    private static readonly SolidColorBrush DragSwapFill = new(Color.FromArgb(50, 255, 160, 40));
    private static readonly SolidColorBrush DragInvalidFill = new(Color.FromArgb(40, 255, 80, 80));
    private static readonly SolidColorBrush DragSwapStroke = new(Color.FromArgb(240, 255, 180, 40));
    private static readonly SolidColorBrush DragInvalidStroke = new(Color.FromArgb(240, 255, 90, 90));
    private static readonly SolidColorBrush MoveValidFill = new(Color.FromArgb(90, 70, 200, 120));
    private static readonly SolidColorBrush MoveValidStroke = new(Color.FromArgb(230, 80, 220, 140));
    private static readonly SolidColorBrush MoveOutsideFill = new(Color.FromArgb(110, 220, 50, 50));
    private static readonly SolidColorBrush MoveOutsideStroke = new(Color.FromArgb(255, 255, 70, 70));

    static WorldViewport()
    {
        SelectionStroke.Freeze();
        SelectionFill.Freeze();
        MarqueeStroke.Freeze();
        MarqueeFill.Freeze();
        CellHoverFill.Freeze();
        CellHoverStroke.Freeze();
        DragPreviewFill.Freeze();
        DragSwapFill.Freeze();
        DragInvalidFill.Freeze();
        DragSwapStroke.Freeze();
        DragInvalidStroke.Freeze();
        MoveValidFill.Freeze();
        MoveValidStroke.Freeze();
        MoveOutsideFill.Freeze();
        MoveOutsideStroke.Freeze();
    }

    public WorldViewport()
    {
        InitializeComponent();
        Focusable = true;
        AllowDrop = true;
        DragOver += Viewport_DragOver;
        Drop += Viewport_Drop;
        DataContextChanged += (_, _) => BindVm(DataContext as WorldViewModel);
        SizeChanged += (_, _) =>
        {
            _camera.SetViewportSize(ActualWidth, ActualHeight);
            ApplyTransform();
        };
        Loaded += (_, _) =>
        {
            _camera.SetViewportSize(ActualWidth, ActualHeight);
            RestoreCameraFromWorld();
            ApplyTransform();
            RedrawAll();
        };
        LostMouseCapture += (_, _) => CancelPointerStroke(finish: true);
    }

    private void BindVm(WorldViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.RequestRedraw -= RedrawAll;
            _vm.RequestFitAll -= FitAll;
            _vm.WorldChanged -= OnWorldChanged;
            _vm.RequestOverlayRedraw -= RedrawMultiMapOverlays;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.RequestRedraw += RedrawAll;
            _vm.RequestFitAll += FitAll;
            _vm.WorldChanged += OnWorldChanged;
            _vm.RequestOverlayRedraw += RedrawMultiMapOverlays;
            RestoreCameraFromWorld();
            RedrawAll();
        }
    }

    private void OnWorldChanged()
    {
        RestoreCameraFromWorld();
        RedrawAll();
    }

    private void RestoreCameraFromWorld()
    {
        if (_vm?.World is null) return;
        var view = _vm.World.View;
        _camera.SetZoom(view.Zoom);
        _camera.SetPan(view.PanX, view.PanY);
    }

    private void PersistCameraToWorld()
    {
        if (_vm?.World is null) return;
        _vm.World.View.Zoom = _camera.Zoom;
        _vm.World.View.PanX = _camera.OffsetX;
        _vm.World.View.PanY = _camera.OffsetY;
        _vm.MarkDirtyFromView();
    }

    /// <summary>When true, encajar pone el mosaico a los bordes del viewport (maximizar).</summary>
    private bool _fillToEdges;

    public void SetFillToEdges(bool fill)
    {
        _fillToEdges = fill;
        FitAll();
    }

    public void FitAll()
    {
        ComputeContentBounds(out _, out _, out var w, out var h);
        _camera.SetContentSize(w, h);
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        _camera.FitToViewport(padding: _fillToEdges ? 0 : 24);
        ApplyTransform();
        PersistCameraToWorld();
        RedrawOverlays();
    }

    private void ComputeContentBounds(out double minX, out double minY, out double width, out double height)
    {
        minX = 0;
        minY = 0;
        width = 800;
        height = 600;
        if (_vm?.World is null) return;

        var mosaic = _vm.MosaicMode;
        var any = false;
        var boundsMinX = 0.0;
        var boundsMinY = 0.0;
        var maxX = 0.0;
        var maxY = 0.0;

        void IncludeRect(double rx, double ry, double w, double h)
        {
            if (!any)
            {
                boundsMinX = rx;
                boundsMinY = ry;
                maxX = rx + w;
                maxY = ry + h;
                any = true;
            }
            else
            {
                boundsMinX = Math.Min(boundsMinX, rx);
                boundsMinY = Math.Min(boundsMinY, ry);
                maxX = Math.Max(maxX, rx + w);
                maxY = Math.Max(maxY, ry + h);
            }
        }

        foreach (var (p, entry) in _vm.EnumeratePlaced())
        {
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            IncludeRect(rx, ry, w, h);
        }

        if (_vm.IsScratchCombined)
        {
            // Solo casillas "+" vecinas — no toda la cuadrícula (evita desplazar el mosaico a la izquierda).
            foreach (var (gx, gy) in _vm.EnumerateCombinedAddSlots())
            {
                var (rx, ry, w, h) = WorldGeometry.GetSlotRect(gx, gy, mosaic);
                IncludeRect(rx, ry, w, h);
            }
        }
        else if (_vm.World.HasGrid)
        {
            foreach (var (gx, gy) in WorldGeometry.EnumerateGridCells(_vm.World))
            {
                var (rx, ry, w, h) = WorldGeometry.GetSlotRect(gx, gy, mosaic);
                IncludeRect(rx, ry, w, h);
            }
        }

        if (!any) return;
        minX = boundsMinX;
        minY = boundsMinY;
        var pad = _fillToEdges ? 0 : ContentPadding;
        _contentOffsetX = minX - pad;
        _contentOffsetY = minY - pad;
        width = maxX - minX + pad * 2;
        height = maxY - minY + pad * 2;
    }

    private void RedrawAll()
    {
        ContentCanvas.Children.Clear();
        OverlayCanvas.Children.Clear();
        if (_vm?.World is null) return;

        var prevOx = _contentOffsetX;
        var prevOy = _contentOffsetY;
        ComputeContentBounds(out _, out _, out var cw, out var ch);
        // Si el origen del contenido se mueve (casillas +), compensar el pan para no saltar la vista.
        var dOx = _contentOffsetX - prevOx;
        var dOy = _contentOffsetY - prevOy;
        if (Math.Abs(dOx) > 0.01 || Math.Abs(dOy) > 0.01)
        {
            _camera.SetPan(_camera.OffsetX + dOx * _camera.Zoom, _camera.OffsetY + dOy * _camera.Zoom);
            PersistCameraToWorld();
            ApplyTransform();
        }

        _camera.SetContentSize(cw, ch);
        ContentCanvas.Width = cw;
        ContentCanvas.Height = ch;
        OverlayCanvas.Width = cw;
        OverlayCanvas.Height = ch;

        var mosaic = _vm.MosaicMode || _vm.IsMultiMapEditMode;
        var prominentInfo = _vm.ShowInfoOverlay;
        var renderOpts = _vm.IsMultiMapEditMode ? _vm.EditorHost?.GetMapRenderOptions() : null;

        if (_vm.World.HasGrid && !_vm.IsMultiMapEditMode)
        {
            var occupied = _vm.World.Placements.Select(p => (p.WorldX, p.WorldY)).ToHashSet();
            foreach (var (gx, gy) in WorldGeometry.EnumerateGridCells(_vm.World))
            {
                if (occupied.Contains((gx, gy))) continue;
                DrawEmptyGridSlot(gx, gy, mosaic, prominentInfo);
            }
        }

        foreach (var (p, entry) in _vm.EnumerateAllPlaced())
        {
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            var left = rx - _contentOffsetX;
            var top = ry - _contentOffsetY;

            var img = new Image
            {
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                Tag = entry.Key,
                Opacity = 1.0,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var thumb = _vm.GetThumbnail(entry.Key, renderOpts);
            if (thumb is not null)
                img.Source = thumb;

            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            ContentCanvas.Children.Add(img);

            DrawPlacedMapChrome(p, entry, left, top, w, h);

            DrawMapInfoLabel(
                left, top, w, h,
                $"{entry.Document.Id}\n({p.WorldX},{p.WorldY})",
                prominentInfo);
        }

        RedrawOverlays();
        if (_vm.IsMultiMapEditMode)
            RedrawMultiMapOverlays();
        else if (_draggingMap)
            RedrawDragPreview();
        UpdateGridChrome();
    }

    private void DrawPlacedMapChrome(
        WorldMapPlacement p,
        WorldMapEntry entry,
        double left,
        double top,
        double w,
        double h)
    {
        if (_vm is null || !_vm.IsMultiMapEditMode) return;
        if (!(_vm.ShowMapBounds || _vm.ShowSeams || _vm.SelectedKeys.Contains(entry.Key)))
            return;

        var selected = _vm.SelectedKeys.Contains(entry.Key);
        var seam = _vm.ShowSeams && !selected;
        var border = new Rectangle
        {
            Width = w,
            Height = h,
            Stroke = selected
                ? SelectionStroke
                : seam
                    ? new SolidColorBrush(Color.FromArgb(180, 255, 140, 40))
                    : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            StrokeDashArray = seam ? [4.0 / _camera.Zoom, 3.0 / _camera.Zoom] : null,
            StrokeThickness = (selected ? 3 : seam ? 1.5 : 1) / _camera.Zoom,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        OverlayCanvas.Children.Add(border);
    }

    private void DrawEmptyGridSlot(int gx, int gy, bool mosaic, bool prominentInfo)
    {
        var (rx, ry, w, h) = WorldGeometry.GetSlotRect(gx, gy, mosaic);
        var left = rx - _contentOffsetX;
        var top = ry - _contentOffsetY;

        var slot = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = ThemeService.GetBrush("PreviewEmpty"),
            Stroke = ThemeService.GetBrush("PreviewEmptyBorder"),
            StrokeThickness = 1 / Math.Max(_camera.Zoom, 0.1),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(slot, left);
        Canvas.SetTop(slot, top);
        ContentCanvas.Children.Add(slot);

        // Empty slots stay discreet (corner only); large centered labels are for placed maps.
        DrawMapInfoLabel(left, top, w, h, $"{gx}, {gy}", prominent: false);
    }

    /// <summary>
    /// Prominent (checkbox on): large centered ID/coords. Off: small label on the top-left edge.
    /// </summary>
    private void DrawMapInfoLabel(double left, double top, double w, double h, string text, bool prominent)
    {
        var zoom = Math.Max(_camera.Zoom, 0.05);
        if (zoom < 0.08) return;

        if (prominent)
        {
            var fontSize = Math.Clamp(28.0 / zoom, 16, Math.Min(72, h * 0.32));
            var label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 0,
                    Opacity = 0.85,
                },
                IsHitTestVisible = false,
            };
            // No full-tile dark panel — map stays full color; ID stays readable via shadow.
            var panel = new Border
            {
                Width = w,
                Height = h,
                Background = Brushes.Transparent,
                Child = label,
                IsHitTestVisible = false,
            };
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            Canvas.SetLeft(panel, left);
            Canvas.SetTop(panel, top);
            OverlayCanvas.Children.Add(panel);
            return;
        }

        var smallSize = Math.Clamp(10.0 / zoom, 8, 13);
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 1, 3, 1),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text.Replace('\n', ' '),
                Foreground = new SolidColorBrush(Color.FromArgb(220, 230, 230, 230)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = smallSize,
                IsHitTestVisible = false,
            },
        };
        var inset = 4 / zoom;
        Canvas.SetLeft(chip, left + inset);
        Canvas.SetTop(chip, top + inset);
        OverlayCanvas.Children.Add(chip);
    }

    private void RedrawMultiMapOverlays()
    {
        if (_vm is null || !_vm.IsMultiMapEditMode) return;
        var host = _vm.EditorHost;
        if (host is null) return;

        // Must clear first: hover/paint-target diamonds used to accumulate into a pink trail.
        OverlayCanvas.Children.Clear();

        const bool mosaic = true;
        var mm = _vm.MultiMap;

        foreach (var (p, entry) in _vm.EnumerateAllPlaced())
        {
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            var left = rx - _contentOffsetX;
            var top = ry - _contentOffsetY;
            DrawPlacedMapChrome(p, entry, left, top, w, h);

            if (!mm.EditableKeys.Contains(entry.Key)) continue;
            var tester = mm.GetHitTester(entry.Key);
            if (tester is null) continue;
            var ox = left;
            var oy = top;

            if (host.ShowGrid)
                DrawMapGrid(tester, ox, oy);

            if (host.ShowCellIds && host.ShowCellIdsEffective)
                DrawMapCellIds(tester, ox, oy);

            if (_vm.ShowMapIds && _camera.Zoom >= 0.2)
            {
                var label = new TextBlock
                {
                    Text = $"Map {entry.Document.Id}",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                };
                Canvas.SetLeft(label, ox + 4);
                Canvas.SetTop(label, oy + 4);
                OverlayCanvas.Children.Add(label);
            }
        }

        if (host.Tool is not (EditorTool.Paint or EditorTool.Erase))
        {
            foreach (var sel in mm.Selection)
            {
                var tester = mm.GetHitTester(sel.DocumentKey);
                if (tester?.TryGetCellCornersInHitSpace(sel.CellId, out var corners) != true) continue;
                var placement = _vm.World!.Placements.FirstOrDefault(x => x.DocumentKey == sel.DocumentKey);
                if (placement is null) continue;
                var doc = mm.GetDocument(sel.DocumentKey)!;
                var (rx, ry, _, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, doc, mosaic);
                var shifted = ShiftCorners(corners, rx - _contentOffsetX, ry - _contentOffsetY);
                OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, SelectionFill, SelectionStroke, 2.0 / _camera.Zoom));
            }
        }

        // Single hover target only (never a trail).
        if (mm.HoveredCell is { } hover &&
            host.Tool is EditorTool.Paint or EditorTool.Erase)
        {
            var tester = mm.GetHitTester(hover.DocumentKey);
            if (tester?.TryGetCellCornersInHitSpace(hover.CellId, out var corners) == true)
            {
                var placement = _vm.World!.Placements.First(p => p.DocumentKey == hover.DocumentKey);
                var doc = mm.GetDocument(hover.DocumentKey)!;
                var (rx, ry, _, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, doc, mosaic);
                var shifted = ShiftCorners(corners, rx - _contentOffsetX, ry - _contentOffsetY);

                if (host.Tool == EditorTool.Paint && host.SelectedGfxId is not null)
                    DrawMultiMapBrushPreview(host, hover.DocumentKey, hover.CellId, rx, ry);
                DrawMultiMapPaintTarget(shifted);
            }
        }

        if (mm.IsRectSelecting)
        {
            var b = mm.RectSelectBounds;
            var rect = new Rectangle
            {
                Width = Math.Abs(b.X1 - b.X0),
                Height = Math.Abs(b.Y1 - b.Y0),
                Stroke = MarqueeStroke,
                StrokeThickness = 1 / _camera.Zoom,
                Fill = MarqueeFill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, Math.Min(b.X0, b.X1) - _contentOffsetX);
            Canvas.SetTop(rect, Math.Min(b.Y0, b.Y1) - _contentOffsetY);
            OverlayCanvas.Children.Add(rect);
        }

        if (_draggingMap)
            RedrawDragPreview();

        if (host.IsMovingSelection)
            DrawMultiMapSelectionMovePreview(host);
    }

    private void DrawMultiMapSelectionMovePreview(MainViewModel host)
    {
        if (host.MovePreviewItems.Count == 0 || _moveDocKey is null || _vm?.World is null)
            return;

        var tester = _vm.MultiMap.GetHitTester(_moveDocKey);
        if (tester is null) return;

        var placement = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == _moveDocKey);
        var doc = _vm.MultiMap.GetDocument(_moveDocKey);
        if (placement is null || doc is null) return;

        var (rx, ry, _, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, doc, mosaicMode: true);
        var ox = rx - _contentOffsetX;
        var oy = ry - _contentOffsetY;

        var templateId = _moveGrabCellId
            ?? host.PrimarySelectedCellId
            ?? (host.SelectedCellIds.Count > 0 ? host.SelectedCellIds[0] : (int?)null);
        if (templateId is null || !tester.TryGetCellCornersInHitSpace(templateId.Value, out var template))
            return;

        var tox = (template.A.X + template.C.X) / 2.0;
        var toy = (template.B.Y + template.D.Y) / 2.0;
        var thickness = 2.2 / _camera.Zoom;

        foreach (var item in host.MovePreviewItems)
        {
            var dx = item.CenterX - tox;
            var dy = item.CenterY - toy;
            var shifted = ShiftCorners(template, ox + dx, oy + dy);

            if (item.IsOutside)
            {
                OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, MoveOutsideFill, MoveOutsideStroke, thickness));
            }
            else if (item.TargetCellId is int tid &&
                     tester.TryGetCellCornersInHitSpace(tid, out var realCorners))
            {
                OverlayCanvas.Children.Add(CreateDiamondPolygon(
                    ShiftCorners(realCorners, ox, oy), MoveValidFill, MoveValidStroke, thickness));
            }
            else
            {
                OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, MoveValidFill, MoveValidStroke, thickness));
            }
        }
    }

    private void DrawMultiMapPaintTarget(IsoGeometry.CellCorners shifted)
    {
        var fill = ThemeService.GetBrush("OverlayPaintTargetFill");
        var stroke = ThemeService.GetBrush("OverlayPaintTargetStroke");
        var thickness = 2.8 / _camera.Zoom;
        OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, fill, stroke, thickness));
        OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, Brushes.Transparent, stroke, thickness * 0.45));
    }

    private void DrawMultiMapBrushPreview(MainViewModel host, string documentKey, int cellId, double mapRx, double mapRy)
    {
        if (host.Tool != EditorTool.Paint || host.SelectedGfxId is null)
            return;
        if (!host.TryGetMultiMapBrushPreviewVisual(documentKey, cellId, out var visual))
            return;

        var ox = mapRx - _contentOffsetX + visual.Bounds.X;
        var oy = mapRy - _contentOffsetY + visual.Bounds.Y;
        var img = new Image
        {
            Source = visual.Image,
            Width = visual.Bounds.Width,
            Height = visual.Bounds.Height,
            Stretch = Stretch.Fill,
            Opacity = 0.82,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, ox);
        Canvas.SetTop(img, oy);
        OverlayCanvas.Children.Add(img);
    }

    private void DrawMapGrid(IsoHitTester tester, double offsetX, double offsetY)
    {
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c)) continue;
            OverlayCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry
                {
                    Figures =
                    {
                        new PathFigure(new Point(c.A.X + offsetX, c.A.Y + offsetY), new[]
                        {
                            new LineSegment(new Point(c.B.X + offsetX, c.B.Y + offsetY), true),
                            new LineSegment(new Point(c.C.X + offsetX, c.C.Y + offsetY), true),
                            new LineSegment(new Point(c.D.X + offsetX, c.D.Y + offsetY), true),
                        }, true),
                    },
                },
                Stroke = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200)),
                StrokeThickness = 1 / Math.Max(_camera.Zoom, 0.1),
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            });
        }
    }

    private void DrawMapCellIds(IsoHitTester tester, double offsetX, double offsetY)
    {
        if (_camera.Zoom < 0.35) return;
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c)) continue;
            var (cx, cy) = IsoGeometry.GetCellCenter(c);
            cx += offsetX;
            cy += offsetY;
            var tb = new TextBlock
            {
                Text = id.ToString(),
                FontSize = 9,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
            };
            Canvas.SetLeft(tb, cx - 8);
            Canvas.SetTop(tb, cy - 6);
            OverlayCanvas.Children.Add(tb);
        }
    }

    private static IsoGeometry.CellCorners ShiftCorners(IsoGeometry.CellCorners c, double dx, double dy) => new()
    {
        A = new IsoGeometry.Point((int)Math.Round(c.A.X + dx), (int)Math.Round(c.A.Y + dy)),
        B = new IsoGeometry.Point((int)Math.Round(c.B.X + dx), (int)Math.Round(c.B.Y + dy)),
        C = new IsoGeometry.Point((int)Math.Round(c.C.X + dx), (int)Math.Round(c.C.Y + dy)),
        D = new IsoGeometry.Point((int)Math.Round(c.D.X + dx), (int)Math.Round(c.D.Y + dy)),
    };

    private static Polygon CreateDiamondPolygon(
        IsoGeometry.CellCorners c,
        SolidColorBrush fill,
        SolidColorBrush stroke,
        double thickness) =>
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

    private void RedrawOverlays()
    {
        if (_vm?.IsMultiMapEditMode == true) return;
        OverlayCanvas.Children.RemoveWhere(c =>
            (c is Rectangle or System.Windows.Shapes.Path) && !IsDragPreview(c));
        if (_vm?.World is null) return;

        var mosaic = _vm.MosaicMode;
        foreach (var (p, entry) in _vm.EnumeratePlaced())
        {
            if (!_vm.SelectedKeys.Contains(entry.Key)) continue;
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            var rect = new Rectangle
            {
                Width = w,
                Height = h,
                Stroke = SelectionStroke,
                StrokeThickness = 2 / _camera.Zoom,
                Fill = SelectionFill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, rx - _contentOffsetX);
            Canvas.SetTop(rect, ry - _contentOffsetY);
            OverlayCanvas.Children.Add(rect);
        }
    }

    private static bool IsDragPreview(UIElement element) =>
        element is FrameworkElement { Tag: DragPreviewTag };

    private void ClearDragPreview()
    {
        _dragTargetCell = null;
        OverlayCanvas.Children.RemoveWhere(IsDragPreview);
    }

    private void RedrawDragPreview()
    {
        OverlayCanvas.Children.RemoveWhere(IsDragPreview);
        if (!_draggingMap || _dragKey is null || _vm?.World is null || _dragTargetCell is not { } target)
            return;

        if (!_vm.TryGetPlacementCoords(_dragKey, out var ax, out var ay))
            return;

        var dx = target.X - ax;
        var dy = target.Y - ay;
        if (dx == 0 && dy == 0) return;

        var mosaic = _vm.MosaicMode || _vm.IsMultiMapEditMode;
        var keys = _vm.SelectedKeys.Contains(_dragKey)
            ? _vm.SelectedKeys.ToList()
            : new List<string> { _dragKey };

        foreach (var key in keys)
        {
            if (!_vm.TryGetPlacementCoords(key, out var sx, out var sy)) continue;
            if (!_vm.World.Documents.TryGetValue(key, out var entry)) continue;

            var nx = sx + dx;
            var ny = sy + dy;
            var valid = _vm.CanPlaceAt(nx, ny);
            var occupied = valid && !_vm.IsCellEmpty(nx, ny);
            if (occupied)
            {
                var occKey = _vm.World.Placements
                    .FirstOrDefault(p => p.WorldX == nx && p.WorldY == ny)?.DocumentKey;
                if (occKey is not null &&
                    (occKey == _dragKey || _vm.SelectedKeys.Contains(occKey)))
                    occupied = false;
            }

            var (rx, ry, w, h) = WorldGeometry.GetMapRect(nx, ny, entry.Document, mosaic);
            var left = rx - _contentOffsetX;
            var top = ry - _contentOffsetY;

            var fill = !valid ? DragInvalidFill : occupied ? DragSwapFill : DragPreviewFill;
            var stroke = !valid ? DragInvalidStroke : occupied ? DragSwapStroke : CellHoverStroke;

            var slot = new Rectangle
            {
                Width = w,
                Height = h,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 2 / Math.Max(_camera.Zoom, 0.1),
                StrokeDashArray = occupied ? [6.0 / _camera.Zoom, 4.0 / _camera.Zoom] : null,
                IsHitTestVisible = false,
                Tag = DragPreviewTag,
            };
            Canvas.SetLeft(slot, left);
            Canvas.SetTop(slot, top);
            OverlayCanvas.Children.Add(slot);

            var thumb = _vm.GetThumbnail(key);
            if (thumb is null) continue;
            var img = new Image
            {
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                Source = thumb,
                Opacity = valid ? 0.55 : 0.3,
                IsHitTestVisible = false,
                Tag = DragPreviewTag,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            OverlayCanvas.Children.Add(img);
        }

        var n = keys.Count;
        _vm.HoverText = n > 1
            ? $"Mover {n} mapas → Δ({dx},{dy})"
            : _vm.CanPlaceAt(target.X, target.Y)
                ? $"Mover → ({target.X},{target.Y})"
                : $"Fuera de cuadrícula ({target.X},{target.Y})";
    }

    private void ApplyTransform()
    {
        WorldScale.ScaleX = _camera.Zoom;
        WorldScale.ScaleY = _camera.Zoom;
        WorldTranslate.X = _camera.OffsetX;
        WorldTranslate.Y = _camera.OffsetY;
        UpdateGridChrome();
    }

    private void UpdateGridChrome()
    {
        GridChromeCanvas.Children.Clear();
        if (_vm?.World is null)
        {
            _hoveredMapKey = null;
            return;
        }

        if (_vm.IsScratchCombined && _vm.IsMultiMapEditMode)
        {
            UpdateCombinedAddSlots();
            return;
        }

        if (_vm.IsMultiMapEditMode)
        {
            _hoveredMapKey = null;
            return;
        }

        if (_vm.World.HasGrid)
        {
            var world = _vm.World;
            var mosaic = _vm.MosaicMode;
            var x0 = world.OriginX;
            var y0 = world.OriginY;
            var x1 = x0 + world.GridWidth - 1;
            var y1 = y0 + world.GridHeight - 1;

            for (var x = x0; x <= x1; x++)
            {
                var colX = x;
                var (l, t, r, _) = SlotViewportRect(colX, y0, mosaic);
                PlaceAxisChrome(
                    midX: (l + r) * 0.5,
                    midY: t - GridChromeGap,
                    horizontal: true,
                    plusTip: $"Insertar columna vacía en X={colX}",
                    minusTip: world.GridWidth > 1
                        ? $"Eliminar columna X={colX}"
                        : "Tamaño mínimo (1 columna)",
                    onPlus: () => _vm.InsertGridColumnAt(colX),
                    onMinus: () => _vm.DeleteGridColumnAt(colX),
                    canMinus: world.GridWidth > 1,
                    above: true);
            }

            for (var y = y0; y <= y1; y++)
            {
                var rowY = y;
                var (l, t, _, b) = SlotViewportRect(x0, rowY, mosaic);
                PlaceAxisChrome(
                    midX: l - GridChromeGap,
                    midY: (t + b) * 0.5,
                    horizontal: false,
                    plusTip: $"Insertar fila vacía en Y={rowY}",
                    minusTip: world.GridHeight > 1
                        ? $"Eliminar fila Y={rowY}"
                        : "Tamaño mínimo (1 fila)",
                    onPlus: () => _vm.InsertGridRowAt(rowY),
                    onMinus: () => _vm.DeleteGridRowAt(rowY),
                    canMinus: world.GridHeight > 1,
                    above: false);
            }
        }

        UpdateMapCloseButton();
    }

    private const string CombinedAddSlotTag = "CombinedAddSlot";

    private void UpdateCombinedAddSlots()
    {
        if (_vm is null) return;

        var mosaic = true;
        foreach (var (gx, gy) in _vm.EnumerateCombinedAddSlots())
        {
            var (l, t, r, b) = SlotViewportRect(gx, gy, mosaic);
            var w = Math.Max(24, r - l);
            var h = Math.Max(24, b - t);
            var x = gx;
            var y = gy;

            var slot = new Border
            {
                Width = w,
                Height = h,
                Cursor = Cursors.Hand,
                Focusable = false,
                AllowDrop = true,
                Tag = CombinedAddSlotTag,
                ToolTip = $"Clic = añadir mapa · Arrastrar = mover vista · o suelta un mapa de MAPAS",
                Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 200, 200, 200)),
                BorderThickness = new Thickness(1),
                Opacity = 0.9,
                Child = new TextBlock
                {
                    Text = "+",
                    FontSize = Math.Clamp(Math.Min(w, h) * 0.28, 18, 42),
                    FontWeight = FontWeights.Light,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 230, 230, 230)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                },
            };
            Canvas.SetLeft(slot, l);
            Canvas.SetTop(slot, t);
            // Clic corto = añadir; arrastrar = pan (misma lógica que el mosaico vía _pendingAddMap).
            slot.PreviewMouseLeftButtonDown += (_, e) =>
            {
                Focus();
                _pendingAddMap = true;
                _pendingAddMapX = x;
                _pendingAddMapY = y;
                _leftPressPos = e.GetPosition(this);
                _mapPressPos = _leftPressPos;
                _combinedSelectPending = false;
                _combinedPendingCell = null;
                CaptureMouse();
                e.Handled = true;
            };
            slot.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent(MainViewModel.MapIdDragFormat))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            };
            slot.Drop += (_, e) =>
            {
                if (!TryGetDraggedMapId(e.Data, out var mapId))
                    return;
                _ = _vm.EditorHost?.AddMapToCombinedAtAsync(mapId, x, y);
                e.Handled = true;
            };
            GridChromeCanvas.Children.Add(slot);
        }
    }

    private static bool TryGetDraggedMapId(IDataObject data, out int mapId)
    {
        mapId = 0;
        if (data.GetDataPresent(MainViewModel.MapIdDragFormat) &&
            data.GetData(MainViewModel.MapIdDragFormat) is int id)
        {
            mapId = id;
            return true;
        }

        return false;
    }

    private void Viewport_DragOver(object sender, DragEventArgs e)
    {
        // Combinado usa casillas "+"; el drop directo es para MUNDO.
        if (IsCombinedMapsSurface)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (_vm?.World is not null && TryGetDraggedMapId(e.Data, out _))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Viewport_Drop(object sender, DragEventArgs e)
    {
        if (IsCombinedMapsSurface || _vm?.World is null)
            return;
        if (!TryGetDraggedMapId(e.Data, out var mapId))
            return;

        var pos = e.GetPosition(this);
        var (wx, wy) = ToWorldPixel(pos);
        var cell = HitWorldCell(wx, wy);
        if (cell is { } c)
            _vm.PlaceLibraryMapAt(mapId, c.X, c.Y);
        else
            _vm.PlaceLibraryMapAt(mapId);

        e.Handled = true;
    }

    private void SetHoveredMapKey(string? key)
    {
        if (_hoveredMapKey == key) return;
        _hoveredMapKey = key;
        UpdateMapCloseButton();
    }

    private void UpdateMapCloseButton()
    {
        GridChromeCanvas.Children.RemoveWhere(IsMapCloseButton);

        if (_hoveredMapKey is null ||
            _vm?.World is null ||
            _vm.IsMultiMapEditMode ||
            _draggingMap ||
            _panning)
            return;

        var placement = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == _hoveredMapKey);
        if (placement is null || !_vm.World.Documents.TryGetValue(_hoveredMapKey, out var entry))
            return;

        var mosaic = _vm.MosaicMode;
        var (rx, ry, w, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, entry.Document, mosaic);
        var (right, top) = ContentToViewport(rx + w - _contentOffsetX, ry - _contentOffsetY);

        var btn = CreateMapCloseButton(_hoveredMapKey);
        Canvas.SetLeft(btn, right - MapCloseBtnSize - MapCloseBtnMargin);
        Canvas.SetTop(btn, top + MapCloseBtnMargin);
        GridChromeCanvas.Children.Add(btn);
    }

    private Button CreateMapCloseButton(string documentKey)
    {
        var btn = new Button
        {
            Content = "×",
            Width = MapCloseBtnSize,
            Height = MapCloseBtnSize,
            Padding = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            ToolTip = _vm?.IsScratchCombined == true ? "Quitar del combinado" : "Quitar del mundo",
            Cursor = Cursors.Hand,
            Focusable = false,
            Tag = MapCloseBtnTag,
            Background = ThemeService.GetBrush("ElevatedSurface"),
            Foreground = ThemeService.GetBrush("TextPrimary"),
            BorderBrush = ThemeService.GetBrush("Border"),
            BorderThickness = new Thickness(1),
        };
        btn.Click += (_, e) =>
        {
            _vm?.RemoveMap(documentKey);
            SetHoveredMapKey(null);
            e.Handled = true;
        };
        return btn;
    }

    private static bool IsOverGridLineChrome(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: GridLineChromeTag })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsMapCloseButton(UIElement element) =>
        element is FrameworkElement { Tag: MapCloseBtnTag };

    private static bool IsOverMapCloseButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: MapCloseBtnTag })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private (double Left, double Top, double Right, double Bottom) SlotViewportRect(int gx, int gy, bool mosaic)
    {
        var (rx, ry, w, h) = WorldGeometry.GetSlotRect(gx, gy, mosaic);
        var (l, t) = ContentToViewport(rx - _contentOffsetX, ry - _contentOffsetY);
        var (r, b) = ContentToViewport(rx + w - _contentOffsetX, ry + h - _contentOffsetY);
        return (l, t, r, b);
    }

    private (double X, double Y) ContentToViewport(double contentX, double contentY) =>
        _camera.ContentToViewport(contentX, contentY);

    private void PlaceAxisChrome(
        double midX,
        double midY,
        bool horizontal,
        string plusTip,
        string minusTip,
        Action onPlus,
        Action onMinus,
        bool canMinus,
        bool above)
    {
        var stack = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Background = Brushes.Transparent,
            Tag = GridLineChromeTag,
        };
        stack.Children.Add(CreateGridChromeButton("+", plusTip, onPlus, enabled: true));
        stack.Children.Add(CreateGridChromeButton("−", minusTip, onMinus, enabled: canMinus));
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = stack.DesiredSize;

        double left;
        double top;
        if (horizontal && above)
        {
            left = midX - sz.Width * 0.5;
            top = midY - sz.Height;
        }
        else if (!horizontal && !above)
        {
            left = midX - sz.Width;
            top = midY - sz.Height * 0.5;
        }
        else
        {
            left = midX;
            top = midY;
        }

        Canvas.SetLeft(stack, left);
        Canvas.SetTop(stack, top);
        GridChromeCanvas.Children.Add(stack);
    }

    private Button CreateGridChromeButton(string text, string tip, Action action, bool enabled)
    {
        var btn = new Button
        {
            Content = text,
            Width = GridChromeBtnSize,
            Height = GridChromeBtnSize,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            IsEnabled = enabled,
            Tag = GridLineChromeTag,
            Background = ThemeService.GetBrush("ElevatedSurface"),
            Foreground = ThemeService.GetBrush("TextPrimary"),
            BorderBrush = ThemeService.GetBrush("Border"),
            BorderThickness = new Thickness(1),
            Focusable = false,
        };
        btn.Click += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        return btn;
    }

    private (double X, double Y) ToWorldPixel(Point viewportPos)
    {
        var (cx, cy) = _camera.ViewportToContent(viewportPos.X, viewportPos.Y);
        return (cx + _contentOffsetX, cy + _contentOffsetY);
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm?.World is null) return;
        var pos = e.GetPosition(this);
        _camera.ZoomByFactorAt(pos.X, pos.Y, e.Delta > 0 ? 1.1 : 1.0 / 1.1);
        ApplyTransform();
        PersistCameraToWorld();
        RedrawAll();
        e.Handled = true;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.World is null) return;
        Focus();

        // Close button handles its own click; don't start drag/pan/select.
        if (IsOverMapCloseButton(e.OriginalSource as DependencyObject)
            || IsOverGridLineChrome(e.OriginalSource as DependencyObject))
            return;

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && WantsImmediateViewPan()))
        {
            SetHoveredMapKey(null);
            _combinedSelectPending = false;
            _panning = true;
            _panLast = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var rightPos = e.GetPosition(this);
            var (rwx, rwy) = ToWorldPixel(rightPos);

            if (_vm.IsMultiMapEditMode && TryBeginCombinedErase(rightPos, rwx, rwy))
            {
                e.Handled = true;
                return;
            }

            var rightKey = _vm.HitTestDocumentKey(rwx, rwy);
            if (rightKey is not null && !_vm.IsMultiMapEditMode)
            {
                // Right-click on a placed map → edit world coordinates.
                _vm.SelectKey(rightKey);
                _vm.PromptChangeCoordinates(rightKey);
            }
            else
            {
                ShowContextMenu(rightPos);
            }

            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(this);
        var (wx, wy) = ToWorldPixel(pos);
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (_vm.IsMultiMapEditMode)
        {
            HandleMultiMapMouseDown(wx, wy, ctrl);
            e.Handled = true;
            return;
        }

        var key = _vm.HitTestDocumentKey(wx, wy);

        if (key is not null)
        {
            if (e.ClickCount >= 2)
            {
                // Combinado activo: el clic (mouseup) pregunta izquierda/derecha.
                // Sin combinado: doble clic abre el mapa en MAPA.
                if (_vm.EditorHost?.IsMapCombinedMode != true || _vm.IsScratchCombined)
                {
                    _vm.SelectKey(key);
                    _ = _vm.OpenSelectedMapAsync();
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+arrastrar = mover / intercambiar el mapa. Sin Ctrl = seleccionar y pan de vista.
            if (ctrl)
            {
                if (!_vm.SelectedKeys.Contains(key))
                    _vm.SelectKey(key);
                SetHoveredMapKey(null);
                _dragKey = key;
                _draggingMap = true;
                _mapDragMoved = false;
                _mapPressPos = pos;
                _dragTargetCell = null;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (!_vm.SelectedKeys.Contains(key))
                _vm.SelectKey(key, additive: false);
            else
                _vm.EnsureKeySelected(key);

            SetHoveredMapKey(null);
            _viewPanPending = true;
            _mapPressPos = pos;
            _leftPressPos = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var cell = HitWorldCell(wx, wy);
        if (ctrl)
        {
            _marquee = true;
            _marqueeStart = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (cell is { } emptyCell && _vm.IsCellEmpty(emptyCell.X, emptyCell.Y) && _vm.CanPlaceAt(emptyCell.X, emptyCell.Y))
        {
            _pendingAddMap = true;
            _pendingAddMapX = emptyCell.X;
            _pendingAddMapY = emptyCell.Y;
            _leftPressPos = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (!ctrl)
            _vm.ClearSelection();

        BeginPan(pos);
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Space / middle / hand tool always pan. Alt pans except in combinado+Seleccionar,
    /// where Alt+arrastrar mueve el mapa en el mosaico.
    /// </summary>
    private bool WantsImmediateViewPan()
    {
        if (Keyboard.IsKeyDown(Key.Space) || _spaceDown)
            return true;
        var tool = _vm?.EditorHost?.Tool ?? EditorTool.Select;
        if (tool == EditorTool.Pan)
            return true;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
            return false;
        if (!IsCombinedMapsInteraction)
            return true;
        return tool is not (EditorTool.Select or EditorTool.RectSelect);
    }

    private void BeginPan(Point viewportPos)
    {
        _viewPanPending = false;
        _panning = true;
        _panLast = viewportPos;
        Cursor = Cursors.Hand;
    }

    private static bool ExceededPanDragThreshold(Point from, Point to) =>
        Math.Abs(to.X - from.X) >= PanDragThreshold || Math.Abs(to.Y - from.Y) >= PanDragThreshold;

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_editErasing && e.ChangedButton == MouseButton.Right)
        {
            CancelPointerStroke(finish: true);
            RedrawMultiMapOverlays();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _viewPanPending)
        {
            var clicked = !ExceededPanDragThreshold(_mapPressPos, e.GetPosition(this));
            _viewPanPending = false;
            ReleaseMouseCapture();
            if (clicked)
                _ = _vm?.EditorHost?.TryOfferAddWorldSelectionToCombinedAsync(_vm);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _movingSelection)
        {
            _movingSelection = false;
            _movePending = false;
            _moveGrabCellId = null;
            _moveDocKey = null;
            _vm?.EditorHost?.CommitSelectionMove();
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            RedrawAll();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _movePending)
        {
            var grabId = _moveGrabCellId;
            var grabKey = _moveDocKey;
            _movePending = false;
            _moveGrabCellId = null;
            _moveDocKey = null;
            ReleaseMouseCapture();
            if (grabId is int id && grabKey is not null)
            {
                var pos = e.GetPosition(this);
                var (wx, wy) = ToWorldPixel(pos);
                double? lx = null;
                double? ly = null;
                if (TryWorldToMapLocal(grabKey, wx, wy, out var localX, out var localY))
                {
                    lx = localX;
                    ly = localY;
                }

                _vm?.EditorHost?.FocusOpenMapFromWorldDocumentKey(grabKey);
                _vm?.EditorHost?.InspectCellGfx(id, lx, ly);
                RedrawAll();
            }

            e.Handled = true;
            return;
        }

        if (_panning)
        {
            _panning = false;
            _combinedSelectPending = false;
            _combinedAltMapPending = false;
            _combinedAltMapKey = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            PersistCameraToWorld();
            e.Handled = true;
            return;
        }

        if (_combinedAltMapPending && e.ChangedButton == MouseButton.Left)
        {
            var mapKey = _combinedAltMapKey;
            _combinedAltMapPending = false;
            _combinedAltMapKey = null;
            ReleaseMouseCapture();

            if (mapKey is not null && !ExceededPanDragThreshold(_mapPressPos, e.GetPosition(this)))
            {
                var host = _vm.EditorHost;
                if (host?.IsCombinedMapsMultiSelect == true)
                    _vm.SelectKey(mapKey, additive: true);
                else
                    _vm.SelectKey(mapKey);

                if (_vm.SelectedKeys.Contains(mapKey))
                    host?.FocusOpenMapFromWorldDocumentKey(mapKey);
                host?.NotifyFocusGfxUi();
                RedrawAll();
            }

            e.Handled = true;
            return;
        }

        if (_combinedSelectPending && e.ChangedButton == MouseButton.Left)
        {
            var moved = ExceededPanDragThreshold(_mapPressPos, e.GetPosition(this));
            var pending = _combinedPendingCell;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _combinedSelectPending = false;
            _combinedPendingCell = null;
            ReleaseMouseCapture();

            if (!moved)
            {
                if (pending is WorldCellHit cellHit)
                {
                    _vm.DispatchMultiMapCellClick(
                        new WorldCellRef(cellHit.DocumentKey, cellHit.CellId),
                        isDrag: false,
                        ctrl,
                        cellHit.LocalX,
                        cellHit.LocalY);
                    RedrawAll();
                }
                else if ((_vm.EditorHost?.Tool ?? EditorTool.Select) is EditorTool.Select or EditorTool.RectSelect)
                {
                    _vm.MultiMap.ClearSelection();
                    _vm.EditorHost?.SyncUiFromMultiMapSelection();
                    RedrawMultiMapOverlays();
                }
            }

            e.Handled = true;
            return;
        }

        if (_editStroking && e.ChangedButton == MouseButton.Left)
        {
            CancelPointerStroke(finish: true);
            RedrawMultiMapOverlays();
            e.Handled = true;
            return;
        }

        if (_editRectDragging && e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            var (wx, wy) = ToWorldPixel(pos);
            _vm.DispatchMultiMapEndRectSelect(wx, wy);
            _editRectDragging = false;
            ReleaseMouseCapture();
            RedrawMultiMapOverlays();
            e.Handled = true;
            return;
        }

        if (_draggingMap && _dragKey is not null && e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            var key = _dragKey;
            var moved = _mapDragMoved || ExceededPanDragThreshold(_mapPressPos, pos);
            _draggingMap = false;
            _dragKey = null;
            ClearDragPreview();
            ReleaseMouseCapture();

            if (moved)
            {
                var (wx, wy) = ToWorldPixel(pos);
                var target = HitWorldCell(wx, wy);
                if (target is not null)
                    _vm.MoveSelectionAnchoredAt(key, target.Value.X, target.Value.Y);
            }
            // Clic simple: solo selección (abrir mapa = doble clic / menú / Enter).

            RedrawAll();
            e.Handled = true;
            return;
        }

        if (_pendingAddMap && e.ChangedButton == MouseButton.Left)
        {
            _pendingAddMap = false;
            ReleaseMouseCapture();
            if (_vm?.IsScratchCombined == true && _vm.EditorHost?.IsMapCombinedMode == true)
                _ = _vm.EditorHost.PromptAddMapToCombinedAtAsync(_pendingAddMapX, _pendingAddMapY);
            else
                _vm?.AddMapFromLibrary(_pendingAddMapX, _pendingAddMapY);
            e.Handled = true;
            return;
        }

        if (_marquee && e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            var (x0, y0) = ToWorldPixel(_marqueeStart);
            var (x1, y1) = ToWorldPixel(pos);
            _vm.SelectInRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            _marquee = false;
            ReleaseMouseCapture();
            RedrawOverlays();
            e.Handled = true;
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_pendingAddMap && e.LeftButton == MouseButtonState.Pressed)
        {
            if (ExceededPanDragThreshold(_leftPressPos, pos))
            {
                _pendingAddMap = false;
                BeginPan(pos);
            }
            else
            {
                return;
            }
        }

        if (_viewPanPending && e.LeftButton == MouseButtonState.Pressed)
        {
            if (ExceededPanDragThreshold(_mapPressPos, pos))
            {
                _viewPanPending = false;
                BeginPan(_mapPressPos);
                _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
                _panLast = pos;
                ApplyTransform();
            }

            return;
        }

        if (_panning)
        {
            _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
            _panLast = pos;
            ApplyTransform();
            return;
        }

        if (_vm?.World is null) return;
        var (wx, wy) = ToWorldPixel(pos);

        if (_vm.IsMultiMapEditMode)
        {
            if (_movingSelection && e.LeftButton == MouseButtonState.Pressed && _vm.EditorHost is { } moveHost)
            {
                if (TryWorldToMapLocal(_moveDocKey, wx, wy, out var mlx, out var mly))
                    moveHost.UpdateSelectionMove(mlx, mly);
                RedrawMultiMapOverlays();
                return;
            }

            if (_movePending && e.LeftButton == MouseButtonState.Pressed)
            {
                if (ExceededPanDragThreshold(_mapPressPos, pos) &&
                    _moveGrabCellId is int grabId &&
                    _moveDocKey is not null &&
                    _vm.EditorHost is { } host)
                {
                    host.FocusOpenMapFromWorldDocumentKey(_moveDocKey);
                    host.SyncUiFromMultiMapSelection(_moveDocKey);
                    if (host.TryBeginSelectionMove(grabId))
                    {
                        _movePending = false;
                        _movingSelection = true;
                        Cursor = Cursors.SizeAll;
                        if (TryWorldToMapLocal(_moveDocKey, wx, wy, out var lx, out var ly))
                            host.UpdateSelectionMove(lx, ly);
                        RedrawMultiMapOverlays();
                    }
                    else
                    {
                        _movePending = false;
                        _moveGrabCellId = null;
                        _moveDocKey = null;
                        BeginPan(_mapPressPos);
                        _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
                        _panLast = pos;
                        ApplyTransform();
                    }
                }

                return;
            }

            if (_combinedAltMapPending && e.LeftButton == MouseButtonState.Pressed)
            {
                if (ExceededPanDragThreshold(_mapPressPos, pos))
                {
                    var mapKey = _combinedAltMapKey;
                    _combinedAltMapPending = false;
                    _combinedAltMapKey = null;
                    if (mapKey is null)
                    {
                        BeginPan(_mapPressPos);
                        _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
                        _panLast = pos;
                        ApplyTransform();
                    }
                    else
                    {
                        _vm.EnsureKeySelected(mapKey);
                        _vm.EditorHost?.FocusOpenMapFromWorldDocumentKey(mapKey);
                        SetHoveredMapKey(null);
                        _dragKey = mapKey;
                        _draggingMap = true;
                        _mapDragMoved = true;
                        _dragTargetCell = HitWorldCell(wx, wy);
                        RedrawAll();
                        RedrawDragPreview();
                    }
                }

                return;
            }

            if (_combinedSelectPending && e.LeftButton == MouseButtonState.Pressed)
            {
                if (ExceededPanDragThreshold(_mapPressPos, pos))
                {
                    _combinedSelectPending = false;
                    BeginPan(_mapPressPos);
                    _camera.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y);
                    _panLast = pos;
                    ApplyTransform();
                }
                return;
            }

            if (_draggingMap && e.LeftButton == MouseButtonState.Pressed)
            {
                if (ExceededPanDragThreshold(_mapPressPos, pos))
                    _mapDragMoved = true;
                _dragTargetCell = HitWorldCell(wx, wy);
                RedrawAll();
                RedrawDragPreview();
                return;
            }

            if (_editRectDragging)
            {
                _vm.DispatchMultiMapUpdateRectSelect(wx, wy);
                RedrawMultiMapOverlays();
                return;
            }

            // Paint/Erase only while the left button is held (same as floating MAPA).
            // If the stroke is stuck without a press, cancel — never paint on bare hover.
            if (_editErasing)
            {
                if (Mouse.RightButton != MouseButtonState.Pressed)
                {
                    CancelPointerStroke(finish: true);
                    RedrawMultiMapOverlays();
                    return;
                }

                if (!_strokeDragArmed)
                {
                    if (ExceededPanDragThreshold(_strokeOriginViewport, pos))
                        _strokeDragArmed = true;
                    else
                    {
                        _vm.DispatchMultiMapHover(wx, wy);
                        RedrawMultiMapOverlays();
                        return;
                    }
                }

                _vm.DispatchMultiMapContinueStroke(wx, wy);
                RedrawAll();
                return;
            }

            if (_editStroking)
            {
                if (!IsLeftStrokeActive())
                {
                    CancelPointerStroke(finish: true);
                    RedrawMultiMapOverlays();
                    return;
                }

                if (!_strokeDragArmed)
                {
                    if (ExceededPanDragThreshold(_strokeOriginViewport, pos))
                        _strokeDragArmed = true;
                    else
                    {
                        _vm.DispatchMultiMapHover(wx, wy);
                        RedrawMultiMapOverlays();
                        return;
                    }
                }

                _vm.DispatchMultiMapContinueStroke(wx, wy);
                RedrawAll();
                return;
            }

            var hostTool = _vm.EditorHost?.Tool ?? EditorTool.Select;
            var skipCellHover = IsCombinedMapsInteraction && hostTool == EditorTool.Select;
            if (!skipCellHover)
                _vm.DispatchMultiMapHover(wx, wy);
            else
                _vm.DispatchMultiMapClearHover();

            RedrawMultiMapOverlays();
            return;
        }

        if (_draggingMap && e.LeftButton == MouseButtonState.Pressed)
        {
            if (ExceededPanDragThreshold(_mapPressPos, pos))
                _mapDragMoved = true;
            _dragTargetCell = HitWorldCell(wx, wy);
            RedrawDragPreview();
            if (_dragTargetCell is { } cell)
            {
                var valid = _vm.CanPlaceAt(cell.X, cell.Y);
                var occupied = valid && !_vm.IsCellEmpty(cell.X, cell.Y);
                _vm.HoverText = !valid
                    ? $"Fuera de cuadrícula ({cell.X},{cell.Y})"
                    : occupied
                        ? $"Intercambiar → ({cell.X},{cell.Y})"
                        : $"Mover → ({cell.X},{cell.Y})";
            }
            else
            {
                _vm.HoverText = "";
            }
            return;
        }

        // Keep close button visible while the pointer is over it.
        if (IsOverMapCloseButton(e.OriginalSource as DependencyObject) && _hoveredMapKey is not null)
        {
            var hoverKey = _hoveredMapKey;
            if (_vm.World.Documents.TryGetValue(hoverKey, out var hoverEntry))
            {
                var hoverPlacement = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == hoverKey);
                _vm.HoverText = hoverPlacement is null
                    ? $"Map {hoverEntry.Document.Id}"
                    : $"World ({hoverPlacement.WorldX},{hoverPlacement.WorldY}) | Map {hoverEntry.Document.Id} · Arrastrar = vista · Ctrl = mover";
            }
            return;
        }

        var key = _vm.HitTestDocumentKey(wx, wy);
        SetHoveredMapKey(key);
        if (key is not null && _vm.World.Documents.TryGetValue(key, out var entry))
        {
            var placement = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == key);
            _vm.HoverText = placement is null
                ? $"Map {entry.Document.Id}"
                : $"World ({placement.WorldX},{placement.WorldY}) | Map {entry.Document.Id} · Arrastrar = vista · Ctrl = mover";
        }
        else
        {
            var cell = HitWorldCell(wx, wy);
            _vm.HoverText = cell is null ? "" : $"Celda ({cell.Value.X},{cell.Value.Y})";
        }
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_vm?.IsMultiMapEditMode == true)
            _vm.DispatchMultiMapClearHover();
        else
        {
            if (_draggingMap)
            {
                _dragTargetCell = null;
                OverlayCanvas.Children.RemoveWhere(IsDragPreview);
            }
            SetHoveredMapKey(null);
            _vm!.HoverText = "";
        }
    }

    private void HandleMultiMapMouseDown(double wx, double wy, bool ctrl)
    {
        if (_vm is null) return;
        var host = _vm.EditorHost;
        var tool = host?.Tool ?? EditorTool.Select;
        var mapCombined = IsCombinedMapsInteraction;

        // In MAPA combinado:
        // - Seleccionar: clic = celda · arrastrar selección = mover GFX · arrastrar vacío = pan
        // - Alt+clic = añadir/quitar mapa del alcance · Alt+arrastrar = mover mapa
        // - Pintar / resto: igual que mapa suelto
        if (mapCombined && tool == EditorTool.Select)
        {
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            _mapPressPos = Mouse.GetPosition(this);

            if (alt)
            {
                var mapKey = _vm.HitTestDocumentKey(wx, wy);
                _combinedAltMapPending = true;
                _combinedAltMapKey = mapKey;
                _combinedSelectPending = false;
                _combinedPendingCell = null;
                _movePending = false;
                _moveGrabCellId = null;
                _moveDocKey = null;
                _draggingMap = false;
                _dragKey = null;
                _mapDragMoved = false;
                CaptureMouse();
                return;
            }

            var cellHit = _vm.MultiMap.HitTest(wx, wy, mosaicMode: true);

            // Same as floating MAPA: drag on selection moves GFX instead of panning.
            if (!ctrl &&
                cellHit is WorldCellHit selectedHit &&
                _vm.MultiMap.Selection.Any(s =>
                    s.DocumentKey == selectedHit.DocumentKey && s.CellId == selectedHit.CellId))
            {
                _movePending = true;
                _moveGrabCellId = selectedHit.CellId;
                _moveDocKey = selectedHit.DocumentKey;
                _combinedSelectPending = false;
                _combinedPendingCell = null;
                _combinedAltMapPending = false;
                _combinedAltMapKey = null;
                _draggingMap = false;
                _dragKey = null;
                CaptureMouse();
                return;
            }

            // Casilla "+" (sin mapa): clic = añadir · arrastrar = pan.
            if (cellHit is null)
            {
                var gridCell = HitWorldCell(wx, wy);
                if (gridCell is { } slot && _vm.IsCombinedAddSlot(slot.X, slot.Y))
                {
                    _pendingAddMap = true;
                    _pendingAddMapX = slot.X;
                    _pendingAddMapY = slot.Y;
                    _leftPressPos = Mouse.GetPosition(this);
                    _mapPressPos = _leftPressPos;
                    _combinedSelectPending = false;
                    _combinedPendingCell = null;
                    CaptureMouse();
                    return;
                }
            }

            _combinedPendingCell = cellHit;
            _combinedSelectPending = true;
            _combinedAltMapPending = false;
            _combinedAltMapKey = null;
            _movePending = false;
            _moveGrabCellId = null;
            _moveDocKey = null;
            _draggingMap = false;
            _dragKey = null;
            CaptureMouse();
            return;
        }

        if (tool == EditorTool.RectSelect)
        {
            _editRectDragging = true;
            _vm.DispatchMultiMapBeginRectSelect(wx, wy);
            CaptureMouse();
            return;
        }

        var hit = _vm.MultiMap.HitTest(wx, wy, mosaicMode: true);
        if (hit is not WorldCellHit worldHit)
        {
            // Black margins / outside maps: drag pans the mosaic (never paint).
            _mapPressPos = Mouse.GetPosition(this);
            _combinedPendingCell = null;
            _combinedSelectPending = true;
            _combinedAltMapPending = false;
            _combinedAltMapKey = null;
            _movePending = false;
            _editStroking = false;
            CaptureMouse();
            return;
        }

        var cell = new WorldCellRef(worldHit.DocumentKey, worldHit.CellId);
        if (tool is EditorTool.Paint or EditorTool.Erase)
        {
            // Paint requires an active brush — never start a stroke that could keep painting on move.
            if (tool == EditorTool.Paint && host?.SelectedGfxId is null)
                return;

            _editStroking = true;
            _strokeDragArmed = false;
            _strokeOriginViewport = Mouse.GetPosition(this);
            _vm.DispatchMultiMapBeginStroke();
            CaptureMouse();
            // Single click paints exactly one cell; drag (past threshold) continues the stroke.
            _vm.DispatchMultiMapCellClick(cell, isDrag: false, ctrl);
            RedrawAll();
            return;
        }

        // Cell tools / Select outside the combined-map special case.
        _vm.DispatchMultiMapCellClick(cell, isDrag: false, ctrl);
        RedrawAll();
    }

    private bool IsLeftStrokeActive() =>
        _editStroking &&
        IsMouseCaptured &&
        Mouse.LeftButton == MouseButtonState.Pressed;

    private bool TryWorldToMapLocal(string? documentKey, double worldX, double worldY, out double localX, out double localY)
    {
        localX = 0;
        localY = 0;
        if (documentKey is null || _vm?.World is null) return false;

        var placement = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == documentKey);
        var doc = _vm.MultiMap.GetDocument(documentKey);
        if (placement is null || doc is null) return false;

        var (rx, ry, _, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, doc, mosaicMode: true);
        localX = worldX - rx;
        localY = worldY - ry;
        return true;
    }

    private void CancelPointerStroke(bool finish)
    {
        if (_viewPanPending)
        {
            _viewPanPending = false;
            Cursor = Cursors.Arrow;
        }

        if (_movingSelection)
        {
            _movingSelection = false;
            _movePending = false;
            _moveGrabCellId = null;
            _moveDocKey = null;
            if (finish)
                _vm?.EditorHost?.CommitSelectionMove();
            else
                _vm?.EditorHost?.CancelSelectionMove();
            Cursor = Cursors.Arrow;
        }
        else if (_movePending)
        {
            _movePending = false;
            _moveGrabCellId = null;
            _moveDocKey = null;
        }

        if (!_editStroking && !_editErasing && !_editRectDragging && !_combinedSelectPending && !_movingSelection && !_movePending)
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            return;
        }

        if (_editStroking || _editErasing)
        {
            if (finish)
                _vm?.DispatchMultiMapFinishStroke();
            _editStroking = false;
            _editErasing = false;
            _strokeDragArmed = false;
        }

        if (_editRectDragging)
            _editRectDragging = false;

        _combinedSelectPending = false;
        _combinedPendingCell = null;
        _combinedAltMapPending = false;
        _combinedAltMapKey = null;

        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            _spaceDown = true;

        if (e.Key == Key.Escape && _vm?.EditorHost?.IsPasteArmed == true)
        {
            _vm.EditorHost.CancelPasteArmed();
            e.Handled = true;
            return;
        }

        if (_vm?.IsMultiMapEditMode == true)
        {
            if (e.Key == Key.Escape)
            {
                if (_movingSelection || _movePending)
                {
                    _movingSelection = false;
                    _movePending = false;
                    _moveGrabCellId = null;
                    _moveDocKey = null;
                    _vm.EditorHost?.CancelSelectionMove();
                    if (IsMouseCaptured)
                        ReleaseMouseCapture();
                    Cursor = Cursors.Arrow;
                    RedrawAll();
                    e.Handled = true;
                    return;
                }

                CancelPointerStroke(finish: true);
                RedrawAll();
                e.Handled = true;
            }
            if (e.Key == Key.Delete)
            {
                _vm.DispatchMultiMapDelete();
                RedrawAll();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Delete && _vm is not null)
        {
            _vm.RemoveCommand.Execute(null);
            e.Handled = true;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None)
        {
            FitAll();
            e.Handled = true;
        }
        if (e.Key == Key.Enter && _vm?.HasSingleSelection == true)
        {
            _ = _vm.OpenSelectedMapAsync();
            e.Handled = true;
        }
    }

    private void Viewport_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            _spaceDown = false;
    }

    /// <summary>
    /// Combinado / edición multimapa + Construir/Borrar: right-click removes GFX instead of the world context menu.
    /// </summary>
    private bool TryBeginCombinedErase(Point viewportPos, double wx, double wy)
    {
        var host = _vm?.EditorHost;
        var tool = host?.Tool ?? EditorTool.Select;
        if (tool is not (EditorTool.Paint or EditorTool.Erase))
            return false;

        var hit = _vm!.MultiMap.HitTest(wx, wy, mosaicMode: true);
        if (hit is not WorldCellHit worldHit)
            return true; // swallow the menu on empty margins

        var cell = new WorldCellRef(worldHit.DocumentKey, worldHit.CellId);
        if (tool == EditorTool.Paint)
        {
            if (host is null || !host.TryEraseActiveBrushAtWorldCell(cell))
                return true;
        }
        else if (host is not null)
        {
            host.BeginMultiMapEraseStroke(matchBrushOnly: false);
            host.HandleMultiMapEraseClick(cell, matchBrushOnly: host.EraseOnlySelectedGfx);
        }

        _editErasing = true;
        _strokeDragArmed = false;
        _strokeOriginViewport = viewportPos;
        CaptureMouse();
        RedrawAll();
        return true;
    }

    private (int X, int Y)? HitWorldCell(double worldPixelX, double worldPixelY)
    {
        if (_vm?.World is null) return null;
        var mosaic = _vm.MosaicMode || _vm.IsMultiMapEditMode;
        var entries = _vm.World.Placements
            .Where(p => _vm.World.Documents.ContainsKey(p.DocumentKey))
            .Select(p => (p.WorldX, p.WorldY, _vm.World.Documents[p.DocumentKey].Document));
        var hit = WorldGeometry.HitTestWorldCell(worldPixelX, worldPixelY, entries, mosaic);
        if (hit is not null) return hit;
        return _vm.HitTestGridCell(worldPixelX, worldPixelY);
    }

    private void ShowContextMenu(Point viewportPos)
    {
        if (_vm is null) return;
        var (wx, wy) = ToWorldPixel(viewportPos);
        var key = _vm.HitTestDocumentKey(wx, wy);
        var menu = new ContextMenu();

        if (key is not null)
        {
            _vm.SelectKey(key);
            if (_vm.IsScratchCombined)
            {
                AddMenu(menu, "Quitar del combinado", () => _vm.RemoveSelected());
                AddMenu(menu, "Centrar", FitAll);
            }
            else
            {
                AddMenu(menu, "Abrir mapa", async () => await _vm.OpenSelectedMapAsync());
                AddMenu(menu, "Cambiar coordenadas...", () => _vm.PromptChangeCoordinates(key));
                AddMenu(menu, "Duplicar mapa", () => _vm.DuplicateSelected());
                AddMenu(menu, "Copiar", () => _vm.CopySelected());
                menu.Items.Add(new Separator());
                AddMenu(menu, "Quitar del Mundo", () => _vm.RemoveSelected());
                AddMenu(menu, "Centrar", FitAll);
            }
        }
        else
        {
            AddMenu(menu, "Pegar", () => _vm.PasteAtWorldCell(HitWorldCell(wx, wy)));
        }

        menu.IsOpen = true;
    }

    private static void AddMenu(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void AddMenu(ContextMenu menu, string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        menu.Items.Add(item);
    }
}

internal static class CanvasChildrenExtensions
{
    public static void RemoveWhere(this UIElementCollection children, Func<UIElement, bool> predicate)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is UIElement el && predicate(el))
                children.RemoveAt(i);
        }
    }
}
