namespace RufusMapEditor.Rendering;

/// <summary>
/// Samples real iso hit-test cells along a pointer segment so fast drags do not skip diamonds.
/// Uses <see cref="IsoHitTester.HitTest"/> — not arbitrary pixel painting.
/// </summary>
public static class IsoStrokeInterpolation
{
    public static IReadOnlyList<int> CellsAlongSegment(
        IsoHitTester tester,
        double x0,
        double y0,
        double x1,
        double y1,
        double stepPixels = 10)
    {
        ArgumentNullException.ThrowIfNull(tester);
        var results = new List<int>();
        var seen = new HashSet<int>();

        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
        {
            TryAdd(tester.HitTest(x1, y1));
            return results;
        }

        stepPixels = Math.Max(4, stepPixels);
        var steps = Math.Max(1, (int)Math.Ceiling(len / stepPixels));
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            TryAdd(tester.HitTest(x0 + dx * t, y0 + dy * t));
        }

        return results;

        void TryAdd(int? cellId)
        {
            if (cellId is int id && seen.Add(id))
                results.Add(id);
        }
    }
}
