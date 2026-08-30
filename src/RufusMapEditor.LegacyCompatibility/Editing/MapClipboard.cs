namespace RufusMapEditor.LegacyCompatibility.Editing;

/// <summary>
/// Clipboard payload: full cell snapshots + content-space offsets from the anchor cell center.
/// </summary>
public sealed class MapClipboard
{
    public required int AnchorCellId { get; init; }
    public required IReadOnlyList<ClipboardEntry> Entries { get; init; }

    public sealed class ClipboardEntry
    {
        public required CellSnapshot Snapshot { get; init; }
        public required double OffsetX { get; init; }
        public required double OffsetY { get; init; }
    }

    public static MapClipboard? Capture(
        IReadOnlyList<int> cellIds,
        Func<int, CellSnapshot> capture,
        Func<int, (double X, double Y)> centerOf,
        int? anchorCellId = null)
    {
        var ordered = cellIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList();
        if (ordered.Count == 0)
            return null;

        var anchorId = anchorCellId is int a && ordered.Contains(a) ? a : ordered[0];
        var (ax, ay) = centerOf(anchorId);
        var entries = new List<ClipboardEntry>();
        foreach (var id in ordered)
        {
            var (cx, cy) = centerOf(id);
            entries.Add(new ClipboardEntry
            {
                Snapshot = capture(id),
                OffsetX = cx - ax,
                OffsetY = cy - ay,
            });
        }

        return new MapClipboard { AnchorCellId = anchorId, Entries = entries };
    }
}
