using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.Rendering.Package;

/// <summary>
/// Deterministic GDI+ ModeCell overlays on a full-canvas bitmap (Astria Save_Img ModeCell concept).
/// Does not draw selection, hover, paint preview, or theme-dependent brushes.
/// </summary>
public static class ModeCellOverlayPainter
{
    public static void Paint(
        Graphics g,
        MapDocument map,
        IsoGeometry.CellCorners[] corners,
        bool showCellIds = true,
        int sizeCell = IsoGeometry.SizeBaseCell)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(corners);

        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        using var gridPen = new Pen(ModeCellExportPalette.GridStroke, 1f);
        using var unwalkFill = new SolidBrush(ModeCellExportPalette.UnwalkableFill);
        using var unwalkPen = new Pen(ModeCellExportPalette.UnwalkableStroke, 1.5f);
        using var losFill = new SolidBrush(ModeCellExportPalette.LosBlockFill);
        using var losPen = new Pen(ModeCellExportPalette.LosBlockStroke, 1.4f);
        using var fight1Fill = new SolidBrush(ModeCellExportPalette.Fight1Fill);
        using var fight1Pen = new Pen(ModeCellExportPalette.Fight1Stroke, 1.5f);
        using var fight2Fill = new SolidBrush(ModeCellExportPalette.Fight2Fill);
        using var fight2Pen = new Pen(ModeCellExportPalette.Fight2Stroke, 1.5f);
        using var labelBrush = new SolidBrush(ModeCellExportPalette.FightLabel);
        using var idBrush = new SolidBrush(ModeCellExportPalette.CellIdFill);
        using var idShadow = new SolidBrush(ModeCellExportPalette.CellIdShadow);
        using var limitPen = new Pen(ModeCellExportPalette.ExportLimitStroke, 2f);
        using var font = new Font("Consolas", Math.Max(7f, sizeCell / 3.2f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var idFont = new Font("Consolas", Math.Max(6f, sizeCell / 3.6f), FontStyle.Regular, GraphicsUnit.Pixel);

        var cells = map.Cells;
        var n = Math.Min(cells.Count, corners.Length);

        for (var i = 0; i < n; i++)
            g.DrawPolygon(gridPen, ToPoints(corners[i]));

        for (var i = 0; i < n; i++)
        {
            var cell = cells[i];
            var c = corners[i];
            var pts = ToPoints(c);
            var (cx, cy) = IsoGeometry.GetCellCenter(c);

            if (cell.Movement == MovementType.Unwalkable)
            {
                g.FillPolygon(unwalkFill, pts);
                g.DrawPolygon(unwalkPen, pts);
                var half = Math.Max(4, (c.B.X - c.A.X) / 6.0);
                g.DrawLine(unwalkPen, (float)(cx - half), (float)(cy - half / 2), (float)(cx + half), (float)(cy + half / 2));
                g.DrawLine(unwalkPen, (float)(cx + half), (float)(cy - half / 2), (float)(cx - half), (float)(cy + half / 2));
            }

            if (!cell.LineOfSight)
            {
                g.FillPolygon(losFill, pts);
                g.DrawPolygon(losPen, pts);
                var w = Math.Max(3, (c.B.X - c.A.X) / 5.0);
                var h = Math.Max(2, (c.C.Y - c.A.Y) / 6.0);
                PointF[] inner =
                [
                    new((float)cx, (float)(cy - h)),
                    new((float)(cx + w), (float)cy),
                    new((float)cx, (float)(cy + h)),
                    new((float)(cx - w), (float)cy),
                ];
                g.DrawPolygon(losPen, inner);
            }

            if (cell.FightCell == 1)
            {
                g.FillPolygon(fight1Fill, pts);
                g.DrawPolygon(fight1Pen, pts);
                DrawCenteredLabel(g, "1", cx, cy, font, labelBrush);
            }
            else if (cell.FightCell == 2)
            {
                g.FillPolygon(fight2Fill, pts);
                g.DrawPolygon(fight2Pen, pts);
                DrawCenteredLabel(g, "2", cx, cy, font, labelBrush);
            }

            if (showCellIds)
            {
                var text = i.ToString();
                var size = g.MeasureString(text, idFont);
                var x = (float)(cx - size.Width / 2);
                var y = (float)(cy - size.Height / 2);
                g.DrawString(text, idFont, idShadow, x + 1, y + 1);
                g.DrawString(text, idFont, idBrush, x, y);
            }
        }

        var crop = IsoGeometry.ExportCrop(map.Width, map.Height, sizeCell);
        g.DrawRectangle(limitPen, crop.X, crop.Y, crop.Width - 1, crop.Height - 1);
    }

    private static PointF[] ToPoints(IsoGeometry.CellCorners c) =>
    [
        new(c.A.X, c.A.Y),
        new(c.B.X, c.B.Y),
        new(c.C.X, c.C.Y),
        new(c.D.X, c.D.Y),
    ];

    private static void DrawCenteredLabel(Graphics g, string text, double cx, double cy, Font font, Brush brush)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (float)(cx - size.Width / 2), (float)(cy - size.Height / 2));
    }
}
