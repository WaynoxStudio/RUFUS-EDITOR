using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Editing;

/// <summary>
/// Per-document undo/redo stack. Default capacity 100 commands (oldest discarded).
/// Tracks a clean marker for dirty-state integration before save exists.
/// </summary>
public sealed class EditHistory
{
    public const int DefaultCapacity = 100;

    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private int _cleanDepth;

    public EditHistory(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => _undo.Count != _cleanDepth;
    public string? UndoName => CanUndo ? _undo[^1].Name : null;
    public string? RedoName => CanRedo ? _redo[^1].Name : null;

    /// <summary>Push a command that has already been applied to the document.</summary>
    public void PushExecuted(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.Add(command);
        _redo.Clear();
        while (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
            if (_cleanDepth > 0)
                _cleanDepth--;
            else
                _cleanDepth = -1; // clean state fell off the stack → permanently dirty until MarkClean
        }
    }

    public bool Undo(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!CanUndo)
            return false;
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo(map);
        _redo.Add(cmd);
        return true;
    }

    public bool Redo(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!CanRedo)
            return false;
        var cmd = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        cmd.Execute(map);
        _undo.Add(cmd);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _cleanDepth = 0;
    }

    /// <summary>Mark current history position as matching the loaded original (or future save).</summary>
    public void MarkClean() => _cleanDepth = _undo.Count;

    /// <summary>
    /// Marks the document dirty even with an empty undo stack (e.g. recovered unsaved autosave).
    /// </summary>
    public void MarkDirty()
    {
        if (_undo.Count == _cleanDepth)
            _cleanDepth = _undo.Count - 1; // 0 → -1 when empty
    }
}
