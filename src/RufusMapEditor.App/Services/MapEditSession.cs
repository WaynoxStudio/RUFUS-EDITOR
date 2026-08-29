using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Per-document edit session: history, selection, stroke batching, clipboard.
/// </summary>
public sealed class MapEditSession
{
    private readonly Dictionary<int, CellSnapshot> _strokeBefore = new();
    private bool _strokeOpen;
    private string _strokeName = "";

    public MapEditSession(MapDocument document, IsoHitTester hitTester)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        HitTester = hitTester ?? throw new ArgumentNullException(nameof(hitTester));
        History = new EditHistory();
        History.MarkClean();
        Selection = new HashSet<int>();
    }

    public MapDocument Document { get; }
    public EditHistory History { get; }
    public IsoHitTester HitTester { get; }
    public HashSet<int> Selection { get; }
    public MapClipboard? Clipboard { get; private set; }

    public bool IsDirty => History.IsDirty;
    public bool IsStrokeOpen => _strokeOpen;

    public string DocumentId { get; set; } = Guid.NewGuid().ToString("D");
    public string? FilePath { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ProjectName { get; set; }
    public RufusMapEditor.LegacyCompatibility.Rufmap.RufmapSourceDto? Source { get; set; }

    public void MarkSaved()
    {
        History.MarkClean();
        CaptureSavedBaseline();
    }

    public void CaptureLoadBaseline()
    {
        _loadBaselineCodes = SnapshotAllCellCodes();
        _savedBaselineCodes = (string[])_loadBaselineCodes.Clone();
    }

    public void CaptureSavedBaseline() => _savedBaselineCodes = SnapshotAllCellCodes();

    public string? GetLoadBaselineCode(int cellId) =>
        _loadBaselineCodes is not null && cellId >= 0 && cellId < _loadBaselineCodes.Length
            ? _loadBaselineCodes[cellId]
            : null;

    public string? GetSavedBaselineCode(int cellId) =>
        _savedBaselineCodes is not null && cellId >= 0 && cellId < _savedBaselineCodes.Length
            ? _savedBaselineCodes[cellId]
            : null;

    private string[] SnapshotAllCellCodes()
    {
        var arr = new string[Document.Cells.Count];
        for (var i = 0; i < arr.Length; i++)
            arr[i] = MapDataCodec.EncodeCell(Document.Cells[i]);
        return arr;
    }

    private string[]? _loadBaselineCodes;
    private string[]? _savedBaselineCodes;

    public void MarkRecoveredDirty() => History.MarkDirty();

    public void ClearSelection() => Selection.Clear();

    public void SetSelection(IEnumerable<int> ids)
    {
        Selection.Clear();
        foreach (var id in ids)
        {
            if (id >= 0 && id < Document.Cells.Count)
                Selection.Add(id);
        }
    }

    /// <summary>MAP-AREA.1 — add Cell IDs without clearing existing ones (unique set).</summary>
    public void UnionSelection(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (id >= 0 && id < Document.Cells.Count)
                Selection.Add(id);
        }
    }

    public void ToggleSelection(int cellId)
    {
        if (cellId < 0 || cellId >= Document.Cells.Count)
            return;
        if (!Selection.Remove(cellId))
            Selection.Add(cellId);
    }

    public void BeginStroke(string name)
    {
        EndStroke();
        _strokeOpen = true;
        _strokeName = name;
        _strokeBefore.Clear();
    }

    public void StrokeMutate(int cellId, Action<CellData> mutate)
    {
        if (!_strokeOpen || cellId < 0 || cellId >= Document.Cells.Count)
            return;
        if (!_strokeBefore.ContainsKey(cellId))
            _strokeBefore[cellId] = CellSnapshot.Capture(cellId, Document.Cells[cellId]);
        mutate(Document.Cells[cellId]);
    }

    public bool EndStroke()
    {
        if (!_strokeOpen)
            return false;
        _strokeOpen = false;
        if (_strokeBefore.Count == 0)
            return false;

        var changes = new List<(CellSnapshot Before, CellSnapshot After)>();
        foreach (var (id, before) in _strokeBefore)
        {
            var after = CellSnapshot.Capture(id, Document.Cells[id]);
            if (!before.ContentEquals(after))
                changes.Add((before, after));
            else
                before.ApplyTo(Document.Cells[id]);
        }

        _strokeBefore.Clear();
        MapCellEditor.SyncDocument(Document);
        if (changes.Count == 0)
            return false;

        History.PushExecuted(new CellBatchEditCommand(_strokeName, changes));
        return true;
    }

    public bool Commit(string name, IEnumerable<int> cellIds, Action<int, CellData> mutate)
    {
        EndStroke();
        var cmd = CellBatchEditCommand.Build(name, Document, cellIds, mutate);
        if (cmd is null)
            return false;
        History.PushExecuted(cmd);
        return true;
    }

    public bool Undo()
    {
        EndStroke();
        return History.Undo(Document);
    }

    public bool Redo()
    {
        EndStroke();
        return History.Redo(Document);
    }

    public void CopySelection()
    {
        if (Selection.Count == 0)
            return;
        Clipboard = MapClipboard.Capture(
            Selection.ToList(),
            id => CellSnapshot.Capture(id, Document.Cells[id]),
            id =>
            {
                HitTester.TryGetCellCornersInHitSpace(id, out var c);
                return ((c.A.X + c.C.X) / 2.0, (c.B.Y + c.D.Y) / 2.0);
            });
    }

    /// <summary>
    /// Paste at destination cell. Returns (pasted, skippedOutside).
    /// </summary>
    public (int Pasted, int Skipped) PasteAt(int destCellId)
    {
        EndStroke();
        if (Clipboard is null || Clipboard.Entries.Count == 0)
            return (0, 0);
        if (destCellId < 0 || destCellId >= Document.Cells.Count)
            return (0, Clipboard.Entries.Count);

        HitTester.TryGetCellCornersInHitSpace(destCellId, out var destC);
        var (dx, dy) = ((destC.A.X + destC.C.X) / 2.0, (destC.B.Y + destC.D.Y) / 2.0);

        var changes = new List<(CellSnapshot Before, CellSnapshot After)>();
        var pasted = 0;
        var skipped = 0;
        var newSelection = new List<int>();

        foreach (var entry in Clipboard.Entries)
        {
            var target = IsoSelection.ResolvePasteTarget(HitTester, dx + entry.OffsetX, dy + entry.OffsetY);
            if (target is null)
            {
                skipped++;
                continue;
            }

            var before = CellSnapshot.Capture(target.Value, Document.Cells[target.Value]);
            entry.Snapshot.ApplyTo(Document.Cells[target.Value]);
            var after = CellSnapshot.Capture(target.Value, Document.Cells[target.Value]);
            if (!before.ContentEquals(after))
            {
                changes.Add((before, after));
                pasted++;
            }

            newSelection.Add(target.Value);
        }

        MapCellEditor.SyncDocument(Document);
        if (changes.Count > 0)
            History.PushExecuted(new CellBatchEditCommand("Pegar", changes));

        SetSelection(newSelection);
        return (pasted, skipped);
    }

    public void ResetFromReload(MapDocument document, IsoHitTester hitTester)
    {
        // Session is usually recreated; this helps if reused.
        History.Clear();
        History.MarkClean();
        Selection.Clear();
        _strokeBefore.Clear();
        _strokeOpen = false;
    }
}
