using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal sealed class SwfDisplayEntry
{
    public int Depth { get; init; }
    public int CharacterId { get; init; }
    public SwfMatrix Matrix { get; init; } = SwfMatrix.Identity;
    public SwfColorTransform ColorTransform { get; init; } = SwfColorTransform.Identity;
}

internal sealed class SwfTimelineComposer
{
    private readonly SwfMovie _movie;
    private readonly HashSet<(int SpriteId, int Depth)> _recursionGuard = new();

    public SwfTimelineComposer(SwfMovie movie) => _movie = movie;

    public SwfComposeAnalysis Analyze(int spriteId, int frameIndex)
    {
        if (!_movie.Sprites.TryGetValue(spriteId, out var sprite))
            return default;

        frameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, sprite.FrameCount - 1));
        var display = BuildDisplayList(sprite, frameIndex);
        var stats = new ComposeStats();
        var raw = ComputeBounds(display, 0, stats);
        using var bmp = RenderToBitmap(display, raw, stats);
        var visible = bmp is not null
            ? SwfBitmapBounds.CropToVisiblePixels(bmp)
            : Rectangle.Empty;
        return new SwfComposeAnalysis(
            display.Count,
            stats.Shapes,
            stats.NestedSprites,
            stats.Bitmaps,
            stats.Ignored,
            raw,
            visible.IsEmpty
                ? RectangleF.Empty
                : new RectangleF(visible.X, visible.Y, visible.Width, visible.Height));
    }

    public SwfComposeAnalysis AnalyzeRoot(int frameIndex = 0)
    {
        frameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, _movie.FrameCount - 1));
        var display = BuildRootDisplayList(frameIndex);
        var stats = new ComposeStats();
        var raw = ComputeBounds(display, 0, stats);
        using var bmp = RenderToBitmap(display, raw, stats);
        var visible = bmp is not null
            ? SwfBitmapBounds.CropToVisiblePixels(bmp)
            : Rectangle.Empty;
        return new SwfComposeAnalysis(
            display.Count,
            stats.Shapes,
            stats.NestedSprites,
            stats.Bitmaps,
            stats.Ignored,
            raw,
            visible.IsEmpty
                ? RectangleF.Empty
                : new RectangleF(visible.X, visible.Y, visible.Width, visible.Height));
    }

    public Bitmap? ComposeRoot(int frameIndex = 0, int recursionDepth = 0)
    {
        frameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, _movie.FrameCount - 1));
        var display = BuildRootDisplayList(frameIndex);
        if (display.Count == 0)
            return null;

        var stats = new ComposeStats();
        var bounds = ComputeBounds(display, 0, stats);
        if (bounds.IsEmpty)
            return null;

        using var raw = RenderToBitmap(display, bounds, stats);
        if (raw is null)
            return null;

        var crop = SwfBitmapBounds.CropToVisiblePixels(raw);
        return crop.IsEmpty ? raw : SwfBitmapBounds.CropCopy(raw, crop);
    }

    public Bitmap? ComposeSprite(int spriteId, int frameIndex, int recursionDepth = 0)
    {
        if (recursionDepth > SwfSpriteLimits.MaxRecursionDepth)
            return null;
        if (!_movie.Sprites.TryGetValue(spriteId, out var sprite))
            return null;

        frameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, sprite.FrameCount - 1));
        var display = BuildDisplayList(sprite, frameIndex);
        if (display.Count == 0)
            return null;

        var stats = new ComposeStats();
        var bounds = ComputeBounds(display, recursionDepth, stats);
        if (bounds.IsEmpty)
            return null;

        using var raw = RenderToBitmap(display, bounds, stats);
        if (raw is null)
            return null;

        var crop = SwfBitmapBounds.CropToVisiblePixels(raw);
        return crop.IsEmpty ? raw : SwfBitmapBounds.CropCopy(raw, crop);
    }

    private Bitmap? RenderToBitmap(IReadOnlyList<SwfDisplayEntry> display, RectangleF bounds, ComposeStats stats)
    {
        if (bounds.IsEmpty)
            return null;

        var margin = Math.Max(2f, Math.Max(bounds.Width, bounds.Height) * 0.04f);
        var w = Math.Max(1, (int)Math.Ceiling(bounds.Width + margin * 2));
        var h = Math.Max(1, (int)Math.Ceiling(bounds.Height + margin * 2));
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-bounds.Left + margin, -bounds.Top + margin);
            foreach (var entry in display.OrderBy(e => e.Depth))
                DrawEntry(g, entry, 0, stats);
        }

        return bmp;
    }

    private sealed class ComposeStats
    {
        public int Shapes;
        public int Bitmaps;
        public int NestedSprites;
        public int Ignored;
    }

    private List<SwfDisplayEntry> BuildRootDisplayList(int targetFrame)
    {
        var list = new SortedDictionary<int, SwfDisplayEntry>();
        var frame = 0;
        foreach (var tag in SwfStream.EnumerateTags(_movie.Body, _movie.RootTimelineStart, _movie.RootTimelineEnd))
        {
            if (frame > targetFrame)
                break;
            switch (tag.Code)
            {
                case 26 or 70:
                    ApplyPlaceObject(tag.Code, tag.Data, list);
                    break;
                case 28:
                    if (tag.Data.Length >= 4)
                    {
                        var depth = BitConverter.ToUInt16(tag.Data, 2);
                        list.Remove(depth);
                    }
                    break;
                case 71:
                    if (tag.Data.Length >= 2)
                    {
                        var depth = BitConverter.ToUInt16(tag.Data, 0);
                        list.Remove(depth);
                    }
                    break;
                case 1:
                    frame++;
                    break;
            }
        }

        return list.Values.ToList();
    }

    private List<SwfDisplayEntry> BuildDisplayList(SwfSpriteDefinition sprite, int targetFrame)
    {
        var list = new SortedDictionary<int, SwfDisplayEntry>();
        var frame = 0;
        foreach (var tag in SwfStream.EnumerateTags(sprite.TagBuffer, sprite.TagStart, sprite.TagEnd))
        {
            if (frame > targetFrame)
                break;
            switch (tag.Code)
            {
                case 26 or 70:
                    ApplyPlaceObject(tag.Code, tag.Data, list);
                    break;
                case 28:
                    if (tag.Data.Length >= 4)
                    {
                        var depth = BitConverter.ToUInt16(tag.Data, 2);
                        list.Remove(depth);
                    }
                    break;
                case 71:
                    if (tag.Data.Length >= 2)
                    {
                        var depth = BitConverter.ToUInt16(tag.Data, 0);
                        list.Remove(depth);
                    }
                    break;
                case 1:
                    frame++;
                    break;
            }
        }

        return list.Values.ToList();
    }

    private static void ApplyPlaceObject(int code, byte[] data, SortedDictionary<int, SwfDisplayEntry> list)
    {
        if (data.Length == 0) return;
        try
        {
            ApplyPlaceObjectCore(code, data, list);
        }
        catch (EndOfStreamException)
        {
        }
        catch (IndexOutOfRangeException)
        {
        }
    }

    private static void ApplyPlaceObjectCore(int code, byte[] data, SortedDictionary<int, SwfDisplayEntry> list)
    {
        var br = new SwfBitReader(data);
        var flags = br.ReadUi8();
        if (code == 70 && (flags & 0x80) != 0 && br.BytePosition < data.Length)
            _ = br.ReadUi8();
        if (br.BytePosition + 2 > data.Length)
            return;

        var depth = br.ReadUi16();
        // Dofus Retro SWFs use this flag layout consistently (verified against sprite staticR pipeline).
        var hasChar = (flags & 0x02) != 0;
        var hasMatrix = (flags & 0x08) != 0;
        var hasCx = (flags & 0x10) != 0;
        var hasRatio = (flags & 0x04) != 0;
        var hasName = (flags & 0x20) != 0;
        var hasClipDepth = (flags & 0x40) != 0;

        var charId = 0;
        if (hasChar)
            charId = br.ReadUi16();

        var matrix = SwfMatrix.Identity;
        if (hasMatrix)
            matrix = SwfMatrix.Read(br);

        var cx = SwfColorTransform.Identity;
        if (hasCx)
            cx = SwfColorTransform.Read(br, withAlpha: true);

        if (hasRatio) _ = br.ReadUi16();
        if (hasName) _ = br.ReadString();
        if (hasClipDepth) _ = br.ReadUi16();

        if (!hasChar)
        {
            if (list.TryGetValue(depth, out var existing))
            {
                list[depth] = new SwfDisplayEntry
                {
                    Depth = depth,
                    CharacterId = existing.CharacterId,
                    Matrix = hasMatrix ? matrix : existing.Matrix,
                    ColorTransform = hasCx ? cx : existing.ColorTransform,
                };
            }

            return;
        }

        list[depth] = new SwfDisplayEntry
        {
            Depth = depth,
            CharacterId = charId,
            Matrix = matrix,
            ColorTransform = cx,
        };
    }

    private RectangleF ComputeBounds(IReadOnlyList<SwfDisplayEntry> display, int recursionDepth, ComposeStats stats)
    {
        var boxes = new List<RectangleF>();
        foreach (var entry in display)
        {
            var b = MeasureEntry(entry, recursionDepth, stats);
            if (!b.IsEmpty && b.Width > 0.5f && b.Height > 0.5f)
                boxes.Add(b);
        }

        if (boxes.Count == 0)
            return RectangleF.Empty;

        boxes = TrimOutlierBounds(boxes);
        RectangleF? union = null;
        foreach (var b in boxes)
            union = union is null ? b : RectangleF.Union(union.Value, b);
        return union ?? RectangleF.Empty;
    }

    private static List<RectangleF> TrimOutlierBounds(List<RectangleF> boxes)
    {
        if (boxes.Count <= 2)
            return boxes;

        var areas = boxes.Select(b => b.Width * b.Height).OrderBy(a => a).ToList();
        var median = areas[areas.Count / 2];
        if (median <= 0)
            return boxes;

        var maxArea = median * 25f;
        return boxes.Where(b => b.Width * b.Height <= maxArea).ToList();
    }

    private RectangleF MeasureEntry(SwfDisplayEntry entry, int recursionDepth, ComposeStats stats)
    {
        if (_movie.Shapes.TryGetValue(entry.CharacterId, out var shape))
        {
            stats.Shapes++;
            return MeasureShapePaths(shape, entry.Matrix);
        }

        if (_movie.Bitmaps.TryGetValue(entry.CharacterId, out var bmp))
        {
            stats.Bitmaps++;
            return MeasureBitmap(bmp.Bitmap, entry.Matrix);
        }

        if (_movie.Sprites.TryGetValue(entry.CharacterId, out var nested))
        {
            stats.NestedSprites++;
            var key = (nested.CharacterId, entry.Depth);
            if (!_recursionGuard.Add(key))
                return RectangleF.Empty;
            try
            {
                using var sub = ComposeSprite(nested.CharacterId, 0, recursionDepth + 1);
                if (sub is null)
                {
                    stats.Ignored++;
                    return RectangleF.Empty;
                }

                return MeasureBitmap(sub, entry.Matrix);
            }
            finally
            {
                _recursionGuard.Remove(key);
            }
        }

        stats.Ignored++;
        return RectangleF.Empty;
    }

    private void DrawEntry(Graphics g, SwfDisplayEntry entry, int recursionDepth, ComposeStats stats)
    {
        if (_movie.Shapes.TryGetValue(entry.CharacterId, out var shape))
        {
            stats.Shapes++;
            DrawShape(g, shape, entry.Matrix, entry.ColorTransform);
            return;
        }

        if (_movie.Bitmaps.TryGetValue(entry.CharacterId, out var bmpDef))
        {
            stats.Bitmaps++;
            DrawBitmap(g, bmpDef.Bitmap, entry.Matrix, entry.ColorTransform);
            return;
        }

        if (!_movie.Sprites.TryGetValue(entry.CharacterId, out var nested))
        {
            stats.Ignored++;
            return;
        }

        stats.NestedSprites++;
        var key = (nested.CharacterId, entry.Depth);
        if (!_recursionGuard.Add(key))
            return;
        try
        {
            using var sub = ComposeSprite(nested.CharacterId, 0, recursionDepth + 1);
            if (sub is null)
            {
                stats.Ignored++;
                return;
            }

            var state = g.Save();
            try
            {
                g.MultiplyTransform(new Matrix(
                    entry.Matrix.A, entry.Matrix.B,
                    entry.Matrix.C, entry.Matrix.D,
                    entry.Matrix.Tx, entry.Matrix.Ty));
                DrawBitmap(g, sub, SwfMatrix.Identity, entry.ColorTransform);
            }
            finally
            {
                g.Restore(state);
            }
        }
        finally
        {
            _recursionGuard.Remove(key);
        }
    }

    private static RectangleF MeasureShapePaths(SwfShapeDefinition shape, SwfMatrix matrix)
    {
        RectangleF? union = null;
        foreach (var path in shape.Paths)
        {
            if (path.Points.Count < 2) continue;
            var fi = path.Fill1 != 0 ? path.Fill1 : path.Fill0;
            if (fi <= 0 || fi > path.Fills.Count) continue;
            var fill = path.Fills[fi - 1];
            if (fill.Kind != SwfFillKind.Solid || fill.Color.A == 0) continue;

            var pts = path.Points.ToArray();
            matrix.TransformPoints(pts);
            var b = BoundsFromPoints(pts);
            if (b.IsEmpty) continue;
            union = union is null ? b : RectangleF.Union(union.Value, b);
        }

        return union ?? RectangleF.Empty;
    }

    private static RectangleF MeasureBitmap(Bitmap bmp, SwfMatrix matrix)
    {
        var pts = new[]
        {
            matrix.Transform(0, 0),
            matrix.Transform(bmp.Width, 0),
            matrix.Transform(0, bmp.Height),
            matrix.Transform(bmp.Width, bmp.Height),
        };
        return BoundsFromPoints(pts);
    }

    private static void DrawShape(Graphics g, SwfShapeDefinition shape, SwfMatrix matrix, SwfColorTransform cx)
    {
        foreach (var path in shape.Paths)
        {
            if (path.Points.Count < 3) continue;
            var fi = path.Fill1 != 0 ? path.Fill1 : path.Fill0;
            if (fi <= 0 || fi > path.Fills.Count) continue;
            var fill = path.Fills[fi - 1];
            if (fill.Kind != SwfFillKind.Solid || fill.Color.A == 0) continue;

            var pts = path.Points.ToArray();
            matrix.TransformPoints(pts);
            var color = cx.Apply(fill.Color);
            if (color.A == 0) continue;
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, pts);
        }
    }

    private static void DrawBitmap(Graphics g, Bitmap bmp, SwfMatrix matrix, SwfColorTransform cx)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0) return;
        var state = g.Save();
        try
        {
            g.MultiplyTransform(new Matrix(matrix.A, matrix.B, matrix.C, matrix.D, matrix.Tx, matrix.Ty));
            if (cx.MulR == 1f && cx.MulG == 1f && cx.MulB == 1f && cx.MulA == 1f &&
                cx.AddR == 0 && cx.AddG == 0 && cx.AddB == 0 && cx.AddA == 0)
            {
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
            }
            else
            {
                using var tinted = ApplyColorTransform(bmp, cx);
                g.DrawImage(tinted, 0, 0, tinted.Width, tinted.Height);
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static Bitmap ApplyColorTransform(Bitmap source, SwfColorTransform cx)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            copy.SetPixel(x, y, cx.Apply(source.GetPixel(x, y)));
        return copy;
    }

    private static RectangleF BoundsFromPoints(IReadOnlyList<PointF> pts)
    {
        if (pts.Count == 0) return RectangleF.Empty;
        var minX = pts.Min(p => p.X);
        var maxX = pts.Max(p => p.X);
        var minY = pts.Min(p => p.Y);
        var maxY = pts.Max(p => p.Y);
        if (maxX <= minX && maxY <= minY)
            return new RectangleF(minX, minY, Math.Max(1f, maxX - minX), Math.Max(1f, maxY - minY));
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }
}
