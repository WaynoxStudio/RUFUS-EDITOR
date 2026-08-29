namespace RufusMapEditor.Rendering;

/// <summary>
/// Screen / export-image → Cell ID using Astria diamond geometry.
/// Prefer candidate estimation over scanning every cell on each mouse move.
/// </summary>
public sealed class IsoHitTester
{
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly int _sizeCell;
    private readonly IsoGeometry.CellCorners[] _corners;
    private readonly (int X, int Y, int Width, int Height) _crop;
    private readonly bool _useCropSpace;

    public IsoHitTester(int mapWidth, int mapHeight, int sizeCell = IsoGeometry.SizeBaseCell, bool exportCroppedSpace = true)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _sizeCell = sizeCell;
        _corners = IsoGeometry.BuildCellCorners(mapWidth, mapHeight, sizeCell);
        _crop = IsoGeometry.ExportCrop(mapWidth, mapHeight, sizeCell);
        _useCropSpace = exportCroppedSpace;
    }

    public int MapWidth => _mapWidth;
    public int MapHeight => _mapHeight;
    public IReadOnlyList<IsoGeometry.CellCorners> Corners => _corners;

    /// <summary>
    /// Hit-test a point in export-image coordinates (cropped PNG space) or full-canvas space.
    /// Returns Astria / MapData Cell ID, or null if outside any cell diamond.
    /// </summary>
    public int? HitTest(double x, double y)
    {
        var fullX = x;
        var fullY = y;
        if (_useCropSpace)
        {
            fullX = x + _crop.X;
            fullY = y + _crop.Y;
        }

        foreach (var id in EnumerateCandidates(fullX, fullY))
        {
            if (id < 0 || id >= _corners.Length)
                continue;
            if (PointInDiamond(fullX, fullY, _corners[id]))
                return id;
        }

        // Safety net for edge estimation misses: AABB-filtered full pass.
        for (var id = 0; id < _corners.Length; id++)
        {
            if (!AabbContains(fullX, fullY, _corners[id]))
                continue;
            if (PointInDiamond(fullX, fullY, _corners[id]))
                return id;
        }

        return null;
    }

    /// <summary>
    /// Convert a Cell ID to its diamond corners in the same coordinate space as <see cref="HitTest"/>.
    /// </summary>
    public bool TryGetCellCornersInHitSpace(int cellId, out IsoGeometry.CellCorners corners)
    {
        corners = default!;
        if (cellId < 0 || cellId >= _corners.Length)
            return false;

        var c = _corners[cellId];
        if (!_useCropSpace)
        {
            corners = c;
            return true;
        }

        corners = new IsoGeometry.CellCorners
        {
            A = new IsoGeometry.Point(c.A.X - _crop.X, c.A.Y - _crop.Y),
            B = new IsoGeometry.Point(c.B.X - _crop.X, c.B.Y - _crop.Y),
            C = new IsoGeometry.Point(c.C.X - _crop.X, c.C.Y - _crop.Y),
            D = new IsoGeometry.Point(c.D.X - _crop.X, c.D.Y - _crop.Y),
        };
        return true;
    }

    private IEnumerable<int> EnumerateCandidates(double fullX, double fullY)
    {
        // Half-row step is sizeCell/2. Estimate nearby half-rows then map to cell IDs.
        var half = _sizeCell / 2.0;
        if (half <= 0)
            yield break;

        var approxHalfRow = (int)Math.Floor(fullY / half);
        var approxCol = (int)Math.Floor(fullX / (double)_sizeCell);

        var yielded = new HashSet<int>();

        void YieldId(int id)
        {
            if (id >= 0 && id < _corners.Length)
                yielded.Add(id);
        }

        for (var dr = -2; dr <= 2; dr++)
        {
            for (var dc = -2; dc <= 2; dc++)
            {
                var hr = approxHalfRow + dr;
                var col = approxCol + dc;
                if (hr < 0)
                    continue;

                if (hr % 2 == 0)
                {
                    // First GenerateGrid pass: id = i + (n * mapWidth * 2) - n
                    var n = hr / 2;
                    var i = col / 2;
                    if (n >= 0 && n < _mapHeight && i >= 0 && i <= _mapWidth)
                        YieldId(i + (n * _mapWidth * 2) - n);
                }
                else
                {
                    // Second pass: id = i + (n * (mapWidth * 2) + mapWidth) - n
                    var n = (hr - 1) / 2;
                    var i = (col - 1) / 2;
                    if (n >= 0 && n <= _mapHeight - 2 && i >= 0 && i <= _mapWidth - 2)
                        YieldId(i + (n * (_mapWidth * 2) + _mapWidth) - n);
                }
            }
        }

        foreach (var id in yielded)
            yield return id;

        // Fallback: if estimation missed (edge / floating error), scan AABBs near the point.
        if (yielded.Count == 0)
        {
            for (var id = 0; id < _corners.Length; id++)
            {
                if (AabbContains(fullX, fullY, _corners[id]))
                    yield return id;
            }
        }
    }

    private static bool AabbContains(double x, double y, IsoGeometry.CellCorners c)
    {
        var minX = Math.Min(Math.Min(c.A.X, c.B.X), Math.Min(c.C.X, c.D.X));
        var maxX = Math.Max(Math.Max(c.A.X, c.B.X), Math.Max(c.C.X, c.D.X));
        var minY = Math.Min(Math.Min(c.A.Y, c.B.Y), Math.Min(c.C.Y, c.D.Y));
        var maxY = Math.Max(Math.Max(c.A.Y, c.B.Y), Math.Max(c.C.Y, c.D.Y));
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>
    /// Same edge-cross product test as Astria <c>MapEditor.Get_IdCell</c>.
    /// Location(0..3) = A,B,C,D.
    /// </summary>
    public static bool PointInDiamond(double x, double y, IsoGeometry.CellCorners cell)
    {
        var num6 = ((y - cell.A.Y) * (cell.B.X - cell.A.X)) - ((x - cell.A.X) * (cell.B.Y - cell.A.Y));
        var num7 = ((y - cell.B.Y) * (cell.C.X - cell.B.X)) - ((x - cell.B.X) * (cell.C.Y - cell.B.Y));
        var num4 = ((y - cell.C.Y) * (cell.D.X - cell.C.X)) - ((x - cell.C.X) * (cell.D.Y - cell.C.Y));
        var num5 = ((y - cell.D.Y) * (cell.A.X - cell.D.X)) - ((x - cell.D.X) * (cell.A.Y - cell.D.Y));
        return num6 >= 0 && num7 >= 0 && num4 >= 0 && num5 >= 0;
    }
}
