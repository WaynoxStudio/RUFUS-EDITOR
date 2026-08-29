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
    private bool _editRectDragging;
    private double _contentOffsetX;
    private double _contentOffsetY;
    private const double ContentPadding = 48;
    private const double PanDragThreshold = 4;
    private const string DragPreviewTag = "DragPreview";
    private const string MapCloseBtnTag = "MapCloseBtn";
    private const double GridChromeBtnSize = 26;
    private const double GridChromeGap = 6;
    private const double MapCloseBtnSize = 22;
    private const double MapCloseBtnMargin = 6;

    private static readonly SolidColorBrush SelectionStroke = new(Color.FromArgb(255, 255, 200, 40));
    private static readonly SolidColorBrush SelectionFill = new(Color.FromArgb(40, 255, 200, 40));
    private static readonly SolidColorBrush MarqueeStroke = new(Color.FromArgb(200, 100, 180, 255));
    private static readonly SolidColorBrush MarqueeFill = new(Color.FromArgb(30, 100, 180, 255));
    private static readonly SolidColorBrush CellHoverFill = new(Color.FromArgb(60, 64, 160, 255));
    private static readonly SolidColorBrush CellHoverStroke = new(Color.FromArgb(220, 80, 180, 255));
    private static readonly SolidColorBrush DragPreviewFill = new(Color.FromArgb(50, 64, 160, 255));
    private static readonly SolidColorBrush DragSwapFill = new(Color.FromArgb(50, 255, 160, 40));
    private static readonly SolidColorBrush DragInvalidFill = new(Color.FromArgb(40, 255, 80, 80));
    private static readonly SolidColorBrush DragSwapStroke = new(Color.FromArgb(240, 255, 180, 40));
    private static readonly SolidColorBrush DragInvalidStroke = new(Color.FromArgb(240, 255, 90, 90));

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
    }

    public WorldViewport()
    {
        InitializeComponent();
        Focusable = true;
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

    public void FitAll()
    {
        ComputeContentBounds(out _, out _, out var w, out var h);
        _camera.SetContentSize(w, h);
        _camera.SetViewportSize(ActualWidth, ActualHeight);
        _camera.FitToViewport(padding: 24);
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

        if (_vm.World.HasGrid)
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
        _contentOffsetX = minX - ContentPadding;
        _contentOffsetY = minY - ContentPadding;
        width = maxX - minX + ContentPadding * 2;
        height = maxY - minY + ContentPadding * 2;
    }

    private void RedrawAll()
    {
        ContentCanvas.Children.Clear();
        OverlayCanvas.Children.Clear();
        if (_vm?.World is null) return;

        ComputeContentBounds(out _, out _, out var cw, out var ch);
        _camera.SetContentSize(cw, ch);
        ContentCanvas.Width = cw;
        ContentCanvas.Height = ch;
        OverlayCanvas.Width = cw;
        OverlayCanvas.Height = ch;

        var mosaic = _vm.MosaicMode || _vm.IsMultiMapEditMode;
        var prominentInfo = _vm.ShowInfoOverlay;
        var renderOpts = _vm.IsMultiMapEditMode ? _vm.EditorHost?.GetMapRenderOptions() : null;
        var editable = _vm.MultiMap.EditableKeys;

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
            var isEditable = !_vm.IsMultiMapEditMode || editable.Contains(entry.Key);

            var isDragSource = _draggingMap && _dragKey == entry.Key;
            var img = new Image
            {
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true,
                Tag = entry.Key,
                Opacity = isDragSource ? 0.35 : isEditable ? 1.0 : 0.35,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var thumb = _vm.GetThumbnail(entry.Key, renderOpts);
            if (thumb is not null)
                img.Source = thumb;

            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            ContentCanvas.Children.Add(img);

            if (_vm.IsMultiMapEditMode && (_vm.ShowMapBounds || _vm.ShowSeams))
            {
                var seam = _vm.ShowSeams;
                var border = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Stroke = seam
                        ? new SolidColorBrush(Color.FromArgb(180, 255, 140, 40))
                        : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    StrokeDashArray = seam ? [4.0 / _camera.Zoom, 3.0 / _camera.Zoom] : null,
                    StrokeThickness = (seam ? 1.5 : 1) / _camera.Zoom,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(border, left);
                Canvas.SetTop(border, top);
                OverlayCanvas.Children.Add(border);
            }

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
                IsHitTestVisible = false,
            };
            var panel = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
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

        const bool mosaic = true;
        var mm = _vm.MultiMap;

        foreach (var (p, entry) in _vm.EnumerateAllPlaced())
        {
            if (!mm.EditableKeys.Contains(entry.Key)) continue;
            var tester = mm.GetHitTester(entry.Key);
            if (tester is null) continue;
            var (rx, ry, _, _) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            var ox = rx - _contentOffsetX;
            var oy = ry - _contentOffsetY;

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

        if (mm.HoveredCell is { } hover)
        {
            var tester = mm.GetHitTester(hover.DocumentKey);
            if (tester?.TryGetCellCornersInHitSpace(hover.CellId, out var corners) == true)
            {
                var placement = _vm.World!.Placements.First(p => p.DocumentKey == hover.DocumentKey);
                var doc = mm.GetDocument(hover.DocumentKey)!;
                var (rx, ry, _, _) = WorldGeometry.GetMapRect(placement.WorldX, placement.WorldY, doc, mosaic);
                var shifted = ShiftCorners(corners, rx - _contentOffsetX, ry - _contentOffsetY);

                var isPaintTool = host.Tool is EditorTool.Paint or EditorTool.Erase;
                if (isPaintTool)
                {
                    if (host.Tool == EditorTool.Paint && host.SelectedGfxId is not null)
                        DrawMultiMapBrushPreview(host, hover.DocumentKey, hover.CellId, rx, ry);
                    DrawMultiMapPaintTarget(shifted);
                }
                else
                {
                    OverlayCanvas.Children.Add(CreateDiamondPolygon(shifted, CellHoverFill, CellHoverStroke, 1.5 / _camera.Zoom));
                }
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
            Opacity = 0.55,
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

        if (!_vm.World.Documents.TryGetValue(_dragKey, out var entry))
            return;

        var source = _vm.World.Placements.FirstOrDefault(p => p.DocumentKey == _dragKey);
        if (source is not null && source.WorldX == target.X && source.WorldY == target.Y)
            return;

        var mosaic = _vm.MosaicMode;
        var valid = _vm.CanPlaceAt(target.X, target.Y);
        var occupied = !valid ? false : !_vm.IsCellEmpty(target.X, target.Y);
        var (rx, ry, w, h) = WorldGeometry.GetMapRect(target.X, target.Y, entry.Document, mosaic);
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

        var thumb = _vm.GetThumbnail(_dragKey);
        if (thumb is not null)
        {
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
        if (_vm?.World is null || _vm.IsMultiMapEditMode)
        {
            _hoveredMapKey = null;
            return;
        }

        if (_vm.World.HasGrid)
        {
            var world = _vm.World;
            var mosaic = _vm.MosaicMode;
            var (tlx, tly, _, _) = WorldGeometry.GetSlotRect(world.OriginX, world.OriginY, mosaic);
            var (brx, bry, bw, bh) = WorldGeometry.GetSlotRect(
                world.OriginX + world.GridWidth - 1,
                world.OriginY + world.GridHeight - 1,
                mosaic);

            var (vl, vt) = ContentToViewport(tlx - _contentOffsetX, tly - _contentOffsetY);
            var (vr, vb) = ContentToViewport(brx + bw - _contentOffsetX, bry + bh - _contentOffsetY);
            var midX = (vl + vr) * 0.5;
            var midY = (vt + vb) * 0.5;
            var offset = GridChromeBtnSize + GridChromeGap;

            // North / South → filas; East / West → columnas
            PlaceEdgeChrome(WorldGridEdge.North, midX, vt - offset, horizontal: true);
            PlaceEdgeChrome(WorldGridEdge.South, midX, vb + GridChromeGap, horizontal: true);
            PlaceEdgeChrome(WorldGridEdge.West, vl - offset, midY, horizontal: false);
            PlaceEdgeChrome(WorldGridEdge.East, vr + GridChromeGap, midY, horizontal: false);
        }

        UpdateMapCloseButton();
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
            ToolTip = "Quitar del mundo",
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

    private (double X, double Y) ContentToViewport(double contentX, double contentY) =>
        _camera.ContentToViewport(contentX, contentY);

    private void PlaceEdgeChrome(WorldGridEdge edge, double x, double y, bool horizontal)
    {
        var canShrink = _vm!.CanShrinkGrid(edge);
        var stack = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Background = Brushes.Transparent,
        };

        stack.Children.Add(CreateGridChromeButton(
            "+",
            $"Añadir {(edge is WorldGridEdge.East or WorldGridEdge.West ? "columna" : "fila")}",
            () => _vm.ExpandGrid(edge),
            enabled: true));

        stack.Children.Add(CreateGridChromeButton(
            "−",
            canShrink
                ? $"Quitar {(edge is WorldGridEdge.East or WorldGridEdge.West ? "columna" : "fila")}"
                : "Tamaño mínimo (1)",
            () => _vm.ShrinkGrid(edge),
            enabled: canShrink));

        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = stack.DesiredSize;
        var left = horizontal ? x - sz.Width * 0.5 : x;
        var top = horizontal ? y : y - sz.Height * 0.5;
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
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            IsEnabled = enabled,
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
        if (IsOverMapCloseButton(e.OriginalSource as DependencyObject))
            return;

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && (Keyboard.IsKeyDown(Key.Space) || _spaceDown)))
        {
            SetHoveredMapKey(null);
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
                _vm.SelectKey(key);
                _ = _vm.OpenSelectedMapAsync();
                e.Handled = true;
                return;
            }

            _vm.SelectKey(key, additive: ctrl);
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

    private void BeginPan(Point viewportPos)
    {
        _panning = true;
        _panLast = viewportPos;
        Cursor = Cursors.Hand;
    }

    private static bool ExceededPanDragThreshold(Point from, Point to) =>
        Math.Abs(to.X - from.X) >= PanDragThreshold || Math.Abs(to.Y - from.Y) >= PanDragThreshold;

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            PersistCameraToWorld();
            e.Handled = true;
            return;
        }

        if (_editStroking && e.ChangedButton == MouseButton.Left)
        {
            _vm.DispatchMultiMapFinishStroke();
            _editStroking = false;
            ReleaseMouseCapture();
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

            if (!moved)
            {
                // Left-click → enter / open the map for editing.
                _ = _vm.OpenSelectedMapAsync();
            }
            else
            {
                var (wx, wy) = ToWorldPixel(pos);
                var target = HitWorldCell(wx, wy);
                if (target is not null)
                    _vm.PlaceExistingAt(key, target.Value.X, target.Value.Y);
            }

            RedrawAll();
            e.Handled = true;
            return;
        }

        if (_pendingAddMap && e.ChangedButton == MouseButton.Left)
        {
            _pendingAddMap = false;
            _vm.AddMapFromLibrary(_pendingAddMapX, _pendingAddMapY);
            ReleaseMouseCapture();
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
            if (_editRectDragging)
            {
                _vm.DispatchMultiMapUpdateRectSelect(wx, wy);
                RedrawMultiMapOverlays();
                return;
            }

            _vm.DispatchMultiMapHover(wx, wy);

            if (_editStroking && e.LeftButton == MouseButtonState.Pressed)
            {
                _vm.DispatchMultiMapContinueStroke(wx, wy);
                RedrawAll();
            }
            else
            {
                RedrawMultiMapOverlays();
            }
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
                    : $"World ({hoverPlacement.WorldX},{hoverPlacement.WorldY}) | Map {hoverEntry.Document.Id}";
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
                : $"World ({placement.WorldX},{placement.WorldY}) | Map {entry.Document.Id}";
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
            if (!ctrl) _vm.MultiMap.ClearSelection();
            RedrawMultiMapOverlays();
            return;
        }

        var cell = new WorldCellRef(worldHit.DocumentKey, worldHit.CellId);
        if (tool is EditorTool.Paint or EditorTool.Erase)
        {
            _editStroking = true;
            _vm.DispatchMultiMapBeginStroke();
            CaptureMouse();
        }

        _vm.DispatchMultiMapCellClick(cell, isDrag: false, ctrl);
        RedrawAll();
    }

    private void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            _spaceDown = true;

        if (_vm?.IsMultiMapEditMode == true)
        {
            if (e.Key == Key.Escape)
            {
                _editStroking = false;
                _editRectDragging = false;
                ReleaseMouseCapture();
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

    private (int X, int Y)? HitWorldCell(double worldPixelX, double worldPixelY)
    {
        if (_vm?.World is null) return null;
        var entries = _vm.World.Placements
            .Where(p => _vm.World.Documents.ContainsKey(p.DocumentKey))
            .Select(p => (p.WorldX, p.WorldY, _vm.World.Documents[p.DocumentKey].Document));
        var hit = WorldGeometry.HitTestWorldCell(worldPixelX, worldPixelY, entries, _vm.MosaicMode);
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
            AddMenu(menu, "Abrir mapa", async () => await _vm.OpenSelectedMapAsync());
            AddMenu(menu, "Cambiar coordenadas...", () => _vm.PromptChangeCoordinates(key));
            AddMenu(menu, "Duplicar mapa", () => _vm.DuplicateSelected());
            AddMenu(menu, "Copiar", () => _vm.CopySelected());
            menu.Items.Add(new Separator());
            AddMenu(menu, "Quitar del Mundo", () => _vm.RemoveSelected());
            AddMenu(menu, "Centrar", FitAll);
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
