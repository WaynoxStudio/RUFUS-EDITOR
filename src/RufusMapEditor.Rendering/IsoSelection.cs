namespace RufusMapEditor.Rendering;

/// <summary>
/// Selection helpers on top of isometric cell diamonds (export-cropped or full canvas).
/// </summary>
public static class IsoSelection
{
    public static HashSet<int> CellsIntersectingRect(
        IsoHitTester tester,
        double x0,
        double y0,
        double x1,
        double y1)
    {
        var minX = Math.Min(x0, x1);
        var maxX = Math.Max(x0, x1);
        var minY = Math.Min(y0, y1);
        var maxY = Math.Max(y0, y1);
        var result = new HashSet<int>();

        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c))
                continue;
            if (DiamondIntersectsAabb(c, minX, minY, maxX, maxY))
                result.Add(id);
        }

        return result;
    }

    public static bool DiamondIntersectsAabb(
        IsoGeometry.CellCorners c,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        var dMinX = Math.Min(Math.Min(c.A.X, c.B.X), Math.Min(c.C.X, c.D.X));
        var dMaxX = Math.Max(Math.Max(c.A.X, c.B.X), Math.Max(c.C.X, c.D.X));
        var dMinY = Math.Min(Math.Min(c.A.Y, c.B.Y), Math.Min(c.C.Y, c.D.Y));
        var dMaxY = Math.Max(Math.Max(c.A.Y, c.B.Y), Math.Max(c.C.Y, c.D.Y));
        if (dMaxX < minX || dMinX > maxX || dMaxY < minY || dMinY > maxY)
            return false;

        // Center inside rect → included.
        var (cx, cy) = ((c.A.X + c.C.X) / 2.0, (c.B.Y + c.D.Y) / 2.0);
        if (cx >= minX && cx <= maxX && cy >= minY && cy <= maxY)
            return true;

        // Any diamond vertex inside rect.
        if (PointInRect(c.A.X, c.A.Y, minX, minY, maxX, maxY) ||
            PointInRect(c.B.X, c.B.Y, minX, minY, maxX, maxY) ||
            PointInRect(c.C.X, c.C.Y, minX, minY, maxX, maxY) ||
            PointInRect(c.D.X, c.D.Y, minX, minY, maxX, maxY))
            return true;

        // Any rect corner inside diamond (export/hit space coords).
        if (IsoHitTester.PointInDiamond(minX, minY, c) ||
            IsoHitTester.PointInDiamond(maxX, minY, c) ||
            IsoHitTester.PointInDiamond(minX, maxY, c) ||
            IsoHitTester.PointInDiamond(maxX, maxY, c))
            return true;

        return false;
    }

    private static bool PointInRect(double x, double y, double minX, double minY, double maxX, double maxY) =>
        x >= minX && x <= maxX && y >= minY && y <= maxY;

    /// <summary>
    /// Nearest cell whose diamond contains the point, else nearest center within maxDist.
    /// </summary>
    public static int? ResolvePasteTarget(IsoHitTester tester, double contentX, double contentY, double maxDist = 40)
    {
        var hit = tester.HitTest(contentX, contentY);
        if (hit is not null)
            return hit;

        int? best = null;
        var bestDist = maxDist;
        for (var id = 0; id < tester.Corners.Count; id++)
        {
            if (!tester.TryGetCellCornersInHitSpace(id, out var c))
                continue;
            var (cx, cy) = ((c.A.X + c.C.X) / 2.0, (c.B.Y + c.D.Y) / 2.0);
            var d = Math.Sqrt((cx - contentX) * (cx - contentX) + (cy - contentY) * (cy - contentY));
            if (d < bestDist)
            {
                bestDist = d;
                best = id;
            }
        }

        return best;
    }
}
