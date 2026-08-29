using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Editing;

public interface IEditCommand
{
    string Name { get; }
    void Execute(MapDocument map);
    void Undo(MapDocument map);
}

/// <summary>
/// Restores before/after full cell snapshots for one or more cells.
/// </summary>
public sealed class CellBatchEditCommand : IEditCommand
{
    private readonly IReadOnlyList<(CellSnapshot Before, CellSnapshot After)> _changes;

    public CellBatchEditCommand(string name, IReadOnlyList<(CellSnapshot Before, CellSnapshot After)> changes)
    {
        Name = name;
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        if (_changes.Count == 0)
            throw new ArgumentException("Command must contain at least one change.", nameof(changes));
    }

    public string Name { get; }

    public int ChangeCount => _changes.Count;

    public void Execute(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (_, after) in _changes)
            Apply(map, after);
        MapCellEditor.SyncDocument(map);
    }

    public void Undo(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (before, _) in _changes)
            Apply(map, before);
        MapCellEditor.SyncDocument(map);
    }

    private static void Apply(MapDocument map, CellSnapshot snap)
    {
        if (snap.CellId < 0 || snap.CellId >= map.Cells.Count)
            throw new InvalidOperationException($"Cell {snap.CellId} out of range.");
        snap.ApplyTo(map.Cells[snap.CellId]);
    }

    /// <summary>
    /// Builds a command from before→mutate, skipping no-ops. Returns null if nothing changed.
    /// </summary>
    public static CellBatchEditCommand? Build(
        string name,
        MapDocument map,
        IEnumerable<int> cellIds,
        Action<int, CellData> mutate)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(mutate);

        var changes = new List<(CellSnapshot Before, CellSnapshot After)>();
        foreach (var id in cellIds.Distinct())
        {
            if (id < 0 || id >= map.Cells.Count)
                continue;
            var before = CellSnapshot.Capture(id, map.Cells[id]);
            mutate(id, map.Cells[id]);
            var after = CellSnapshot.Capture(id, map.Cells[id]);
            if (!before.ContentEquals(after))
                changes.Add((before, after));
            else
                before.ApplyTo(map.Cells[id]); // ensure identical if mutate was no-op with side effects
        }

        if (changes.Count == 0)
        {
            MapCellEditor.SyncDocument(map);
            return null;
        }

        // Mutate already applied during build — Execute should re-apply after for consistency when pushed after undo.
        // Caller typically: build (applies), then history.PushExecuted(cmd) without re-Execute.
        MapCellEditor.SyncDocument(map);
        return new CellBatchEditCommand(name, changes);
    }

    /// <summary>
    /// Like Build but does not mutate live cells; only computes snapshots from a planned after-state producer.
    /// </summary>
    public static CellBatchEditCommand? FromSnapshots(
        string name,
        IEnumerable<(CellSnapshot Before, CellSnapshot After)> changes)
    {
        var list = changes.Where(c => !c.Before.ContentEquals(c.After)).ToList();
        return list.Count == 0 ? null : new CellBatchEditCommand(name, list);
    }
}
