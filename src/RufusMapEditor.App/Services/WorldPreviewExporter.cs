using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Astria-style Géopositions previews next to the .rufworld:
/// {Name}.png (mosaic) and {Name}_Mode.png (mosaic + "X,Y (MapID)" labels).
/// </summary>
public static class WorldPreviewExporter
{
    private static readonly Color EmptyFill = Color.Black;
    private static readonly Color GridLine = Color.FromArgb(255, 70, 70, 70);
    private static readonly Color LabelFill = Color.White;
    private static readonly Color LabelShadow = Color.FromArgb(220, 0, 0, 0);

    public static void Export(WorldDocument world, AstriaLibraryService library, string rufworldPath)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(rufworldPath))
            throw new ArgumentException("Path required", nameof(rufworldPath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(rufworldPath));
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException("No se pudo resolver la carpeta del proyecto.");
        Directory.CreateDirectory(dir);

        var name = Path.GetFileNameWithoutExtension(rufworldPath);
        var pngPath = Path.Combine(dir, name + ".png");
        var modePath = Path.Combine(dir, name + "_Mode.png");

        using var mosaic = RenderMosaic(world, library);
        SavePngAtomic(mosaic, pngPath);
        DrawModeLabels(mosaic, world);
        SavePngAtomic(mosaic, modePath);
    }

    private static Bitmap RenderMosaic(WorldDocument world, AstriaLibraryService library)
    {
        var cells = EnumeratePreviewCells(world);
        if (cells.Count == 0)
            throw new InvalidOperationException("El mundo no tiene cuadrícula ni mapas para previsualizar.");

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var cell in cells)
        {
            IncludeRect(cell.X, cell.Y, cell.Width, cell.Height, ref minX, ref minY, ref maxX, ref maxY);
        }

        var width = Math.Max(1, (int)Math.Ceiling(maxX - minX));
        var height = Math.Max(1, (int)Math.Ceiling(maxY - minY));

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(EmptyFill);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.SmoothingMode = SmoothingMode.None;

        using var gridPen = new Pen(GridLine, 1f);

        foreach (var cell in cells)
        {
            var dest = new Rectangle(
                (int)Math.Round(cell.X - minX),
                (int)Math.Round(cell.Y - minY),
                cell.Width,
                cell.Height);

            if (cell.Map is not null && library.IsLoaded)
            {
                try
                {
                    var result = library.Render(cell.Map);
                    try
                    {
                        g.DrawImage(result.Image, dest);
                    }
                    finally
                    {
                        result.Image.Dispose();
                    }
                }
                catch
                {
                    using var brush = new SolidBrush(Color.FromArgb(255, 28, 28, 28));
                    g.FillRectangle(brush, dest);
                }
            }
            else
            {
                g.FillRectangle(Brushes.Black, dest);
            }

            g.DrawRectangle(gridPen, dest.X, dest.Y, Math.Max(0, dest.Width - 1), Math.Max(0, dest.Height - 1));
        }

        return bmp;
    }

    private static void DrawModeLabels(Bitmap mosaic, WorldDocument world)
    {
        var cells = EnumeratePreviewCells(world);
        if (cells.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var cell in cells)
            IncludeRect(cell.X, cell.Y, cell.Width, cell.Height, ref minX, ref minY, ref maxX, ref maxY);

        using var g = Graphics.FromImage(mosaic);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var cell in cells)
        {
            var dest = new Rectangle(
                (int)Math.Round(cell.X - minX),
                (int)Math.Round(cell.Y - minY),
                cell.Width,
                cell.Height);

            var mapId = cell.Map?.Id ?? 0;
            var text = $"{cell.WorldX},{cell.WorldY} ({mapId})";
            var fontPx = Math.Clamp(dest.Height * 0.045, 12, 22);
            using var font = new Font("Consolas", (float)fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var pad = Math.Max(4, dest.Width * 0.01f);
            var origin = new PointF(dest.X + pad, dest.Y + pad);

            using var shadow = new SolidBrush(LabelShadow);
            using var fill = new SolidBrush(LabelFill);
            g.DrawString(text, font, shadow, origin.X + 1, origin.Y + 1);
            g.DrawString(text, font, fill, origin);
        }
    }

    private static List<PreviewCell> EnumeratePreviewCells(WorldDocument world)
    {
        var byPos = new Dictionary<(int X, int Y), MapDocument>();
        foreach (var p in world.Placements)
        {
            if (world.Documents.TryGetValue(p.DocumentKey, out var entry))
                byPos[(p.WorldX, p.WorldY)] = entry.Document;
        }

        var cells = new List<PreviewCell>();
        const bool mosaic = true;

        if (world.HasGrid)
        {
            foreach (var (x, y) in WorldGeometry.EnumerateGridCells(world))
            {
                byPos.TryGetValue((x, y), out var map);
                var (rx, ry, w, h) = map is not null
                    ? WorldGeometry.GetMapRect(x, y, map, mosaic)
                    : WorldGeometry.GetSlotRect(x, y, mosaic);
                cells.Add(new PreviewCell(x, y, rx, ry, w, h, map));
            }

            // Maps placed outside the declared grid still appear on the preview.
            foreach (var ((x, y), map) in byPos)
            {
                if (WorldGeometry.IsInGrid(world, x, y)) continue;
                var (rx, ry, w, h) = WorldGeometry.GetMapRect(x, y, map, mosaic);
                cells.Add(new PreviewCell(x, y, rx, ry, w, h, map));
            }

            return cells;
        }

        foreach (var ((x, y), map) in byPos)
        {
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(x, y, map, mosaic);
            cells.Add(new PreviewCell(x, y, rx, ry, w, h, map));
        }

        return cells;
    }

    private static void IncludeRect(
        double x, double y, int w, int h,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x + w);
        maxY = Math.Max(maxY, y + h);
    }

    private static void SavePngAtomic(Bitmap bitmap, string destinationPath)
    {
        var full = Path.GetFullPath(destinationPath);
        var temp = full + ".tmp";
        bitmap.Save(temp, ImageFormat.Png);
        if (File.Exists(full))
            File.Replace(temp, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(temp, full);
    }

    private readonly record struct PreviewCell(
        int WorldX,
        int WorldY,
        double X,
        double Y,
        int Width,
        int Height,
        MapDocument? Map);
}
