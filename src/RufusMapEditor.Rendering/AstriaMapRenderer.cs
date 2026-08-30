using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.Rendering;

public sealed class MapRenderOptions
{
    public int SizeCell { get; init; } = IsoGeometry.SizeBaseCell;
    public bool CropToExportBounds { get; init; } = true;
    public string? AstriaLogoPath { get; init; }
    public bool DrawBackground { get; init; } = true;
    public bool DrawGround { get; init; } = true;
    public bool DrawObjectLayer1 { get; init; } = true;
    public bool DrawObjectLayer2 { get; init; } = true;
}

public sealed class MapRenderResult
{
    public required Bitmap Image { get; init; }
    public required MapRenderMetrics Metrics { get; init; }
    public required IReadOnlyList<string> MissingGfx { get; init; }
    public required IReadOnlyList<string> MissingAnchors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class MapRenderMetrics
{
    public TimeSpan DecodeMapData { get; init; }
    public TimeSpan ResolveGfx { get; init; }
    public TimeSpan Render { get; init; }
    public TimeSpan Total { get; init; }
    public int UniqueImagesLoaded { get; init; }
    public int DrawOperations { get; init; }
    public int FullCanvasWidth { get; init; }
    public int FullCanvasHeight { get; init; }
    public int ExportWidth { get; init; }
    public int ExportHeight { get; init; }
}

/// <summary>
/// Astria-compatible isometric map renderer (GDI+), without editor overlays.
/// Port of <c>MapEditor.DrawAll</c> + <c>Cell.Draw_Tile</c> + <c>Save_Img</c> crop/logo.
/// </summary>
public sealed class AstriaMapRenderer
{
    private readonly IGfxCatalog _catalog;
    private readonly CachedBitmapGfxProvider _images;

    public AstriaMapRenderer(IGfxCatalog catalog, CachedBitmapGfxProvider? imageProvider = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _images = imageProvider ?? new CachedBitmapGfxProvider();
    }

    public CachedBitmapGfxProvider ImageCache => _images;

    public MapRenderResult Render(MapDocument map, MapRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        options ??= new MapRenderOptions();
        var totalSw = Stopwatch.StartNew();

        var decodeSw = Stopwatch.StartNew();
        IReadOnlyList<CellData> cells = map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList();
        if (cells.Count == 0)
        {
            if (string.IsNullOrEmpty(map.MapData))
                throw new InvalidOperationException("Map has neither Cells nor MapData.");
            cells = MapDataCodec.DecodeMap(map.MapData);
        }
        decodeSw.Stop();

        var sizeCell = options.SizeCell;
        var (fullW, fullH) = IsoGeometry.FullCanvasSize(map.Width, map.Height, sizeCell);
        var corners = IsoGeometry.BuildCellCorners(map.Width, map.Height, sizeCell);
        var scale = sizeCell / (double)IsoGeometry.SizeBaseCell;

        var missingGfx = new List<string>();
        var missingAnchors = new List<string>();
        var warnings = new List<string>();
        var drawOps = 0;

        var resolveSw = Stopwatch.StartNew();
        GfxResource? background = null;
        if (options.DrawBackground && map.BackgroundId > 0)
        {
            if (_catalog.TryGetBackground(map.BackgroundId, out background) && background is not null)
            {
                if (!_catalog.TryGetAnchor(GfxCategory.Background, map.BackgroundId, out _))
                    warnings.Add($"Background {map.BackgroundId}: no ground-pos anchor (Astria uses Pos default 0,0).");
            }
            else
            {
                missingGfx.Add($"Background:{map.BackgroundId}");
            }
        }

        foreach (var cell in cells)
        {
            ValidateLayer(cell.GroundGfxId, GfxCategory.Ground, missingGfx, missingAnchors);
            ValidateLayer(cell.Object1GfxId, GfxCategory.Object, missingGfx, missingAnchors);
            ValidateLayer(cell.Object2GfxId, GfxCategory.Object, missingGfx, missingAnchors);
        }
        resolveSw.Stop();

        var renderSw = Stopwatch.StartNew();
        var canvas = new Bitmap(fullW, fullH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            // Explicit defaults matching typical WinForms/GDI+ DrawAll (Astria sets none).
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.Default;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.Clear(Color.Black);

            if (options.DrawBackground && background is not null)
            {
                DrawBackground(g, background, map.BackgroundId, sizeCell, scale, fullW, fullH);
                drawOps++;
            }

            if (options.DrawGround)
            {
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell.GroundGfxId <= 0) continue;
                    if (!_catalog.TryGetGround(cell.GroundGfxId, out var res) || res is null) continue;
                    DrawTile(g, corners[i], res, cell.FlipGround, cell.GroundRotation, isObject: false, sizeCell, scale);
                    drawOps++;
                }
            }

            if (options.DrawObjectLayer1)
            {
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell.Object1GfxId <= 0) continue;
                    if (!_catalog.TryGetObject(cell.Object1GfxId, out var res) || res is null) continue;
                    DrawTile(g, corners[i], res, cell.FlipObject1, cell.Object1Rotation, isObject: true, sizeCell, scale);
                    drawOps++;
                }
            }

            if (options.DrawObjectLayer2)
            {
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell.Object2GfxId <= 0) continue;
                    if (!_catalog.TryGetObject(cell.Object2GfxId, out var res) || res is null) continue;
                    DrawTile(g, corners[i], res, cell.FlipObject2, rotation: 0, isObject: true, sizeCell, scale);
                    drawOps++;
                }
            }
        }

        Bitmap output = canvas;
        if (options.CropToExportBounds)
        {
            var crop = IsoGeometry.ExportCrop(map.Width, map.Height, sizeCell);
            output = Crop(canvas, crop.X, crop.Y, crop.Width, crop.Height);
            canvas.Dispose();
        }

        if (!string.IsNullOrWhiteSpace(options.AstriaLogoPath) && File.Exists(options.AstriaLogoPath))
        {
            using var logo = Image.FromFile(options.AstriaLogoPath!);
            using var g = Graphics.FromImage(output);
            g.DrawImage(logo, new Point(output.Width - logo.Width - 5, output.Height - logo.Height - 5));
            drawOps++;
        }

        renderSw.Stop();
        totalSw.Stop();

        return new MapRenderResult
        {
            Image = output,
            MissingGfx = missingGfx.Distinct().OrderBy(x => x).ToList(),
            MissingAnchors = missingAnchors.Distinct().OrderBy(x => x).ToList(),
            Warnings = warnings,
            Metrics = new MapRenderMetrics
            {
                DecodeMapData = decodeSw.Elapsed,
                ResolveGfx = resolveSw.Elapsed,
                Render = renderSw.Elapsed,
                Total = totalSw.Elapsed,
                UniqueImagesLoaded = _images.UniqueImagesLoaded,
                DrawOperations = drawOps,
                FullCanvasWidth = fullW,
                FullCanvasHeight = fullH,
                ExportWidth = output.Width,
                ExportHeight = output.Height,
            },
        };
    }

    private void ValidateLayer(int gfxId, GfxCategory category, List<string> missingGfx, List<string> missingAnchors)
    {
        if (gfxId <= 0) return;
        if (!_catalog.TryGet(category, gfxId, out var res) || res is null)
        {
            missingGfx.Add($"{category}:{gfxId}");
            return;
        }

        if (category is GfxCategory.Ground or GfxCategory.Object && !res.HasAnchor)
            missingAnchors.Add($"{category}:{gfxId}");
    }

    private void DrawBackground(Graphics g, GfxResource background, int backgroundId, int sizeCell, double scale, int fullW, int fullH)
    {
        var anchorX = 0;
        var anchorY = 0;
        // Astria: Tile.Get_Ground_Pos(Background.ID) — XML Pos de grounds (mismo número).
        // Ojo: IDs como 337/338 tienen Pos de suelo grandes; si se aplican tal cual,
        // el rectángulo del fondo se sale del canvas y queda una franja negra abajo.
        if (_catalog.TryGetAnchor(GfxCategory.Background, backgroundId, out var a))
        {
            anchorX = VbInt(a.X * scale);
            anchorY = VbInt(a.Y * scale);
        }

        if (!_images.TryCloneWorkingCopy(background, out var bmp))
            return;

        using (bmp)
        {
            var dx = sizeCell - anchorX;
            var dy = sizeCell / 2 - anchorY;
            var dw = fullW;
            var dh = fullH;

            // Expandir el destino para que cubra TODO el canvas (sin bandas negras).
            if (dx > 0) { dw += dx; dx = 0; }
            if (dy > 0) { dh += dy; dy = 0; }
            if (dx + dw < fullW) dw = fullW - dx;
            if (dy + dh < fullH) dh = fullH - dy;

            g.DrawImage(bmp, new Rectangle(dx, dy, dw, dh));
        }
    }

    private void DrawTile(
        Graphics g,
        IsoGeometry.CellCorners cell,
        GfxResource resource,
        bool flip,
        int rotation,
        bool isObject,
        int sizeCell,
        double scale)
    {
        if (!_images.TryCloneWorkingCopy(resource, out var aImage))
            return;

        try
        {
            var (baseX, baseY) = GfxPlacementMath.ResolveAnchor(
                resource.Anchor?.X, resource.Anchor?.Y, aImage.Width, aImage.Height);
            var dest = GfxPlacementMath.CalculateDrawPlacement(
                cell, aImage.Width, aImage.Height, baseX, baseY, flip, rotation, isObject, sizeCell);

            if (flip)
                aImage.RotateFlip(RotateFlipType.RotateNoneFlipX);

            if (rotation != 0)
            {
                switch (rotation)
                {
                    case 1:
                        aImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        aImage = ResizeImg(aImage, dest.Height, dest.Width);
                        break;
                    case 2:
                        aImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        break;
                    case 3:
                        aImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                        aImage = ResizeImg(aImage, dest.Height, dest.Width);
                        break;
                }
            }

            g.DrawImage(aImage, new Rectangle(dest.X, dest.Y, dest.Width, dest.Height));
        }
        finally
        {
            aImage.Dispose();
        }
    }

    private static Bitmap ResizeImg(Bitmap source, int newWidth, int newHeight)
    {
        var thumb = new Bitmap(newWidth, newHeight);
        using (var gra = Graphics.FromImage(thumb))
        {
            gra.DrawImage(source, new Rectangle(0, 0, newWidth, newHeight), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        source.Dispose();
        return thumb;
    }

    private static Bitmap Crop(Bitmap source, int x, int y, int width, int height)
    {
        var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.DrawImage(source, new Rectangle(0, 0, width, height), x, y, width, height, GraphicsUnit.Pixel);
        return dest;
    }

    private static int VbInt(double value) => Convert.ToInt32(value);
}
