using RufusMapEditor.Domain.World;

namespace RufusMapEditor.LegacyCompatibility.Editing;

public sealed class WorldEditHistory
{
    private readonly Stack<(IWorldEditCommand Cmd, long Seq)> _undo = new();
    private readonly Stack<(IWorldEditCommand Cmd, long Seq)> _redo = new();
    private bool _clean = true;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => ! _clean;
    public long TopUndoSequence => _undo.Count > 0 ? _undo.Peek().Seq : long.MinValue;
    public long TopRedoSequence => _redo.Count > 0 ? _redo.Peek().Seq : long.MinValue;

    public void PushExecuted(IWorldEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.Push((command, CombinedHistoryClock.Next()));
        _redo.Clear();
        _clean = false;
    }

    public bool Undo(WorldDocument world)
    {
        if (_undo.Count == 0) return false;
        var (cmd, seq) = _undo.Pop();
        cmd.Undo(world);
        _redo.Push((cmd, seq));
        return true;
    }

    public bool Redo(WorldDocument world)
    {
        if (_redo.Count == 0) return false;
        var (cmd, seq) = _redo.Pop();
        cmd.Execute(world);
        _undo.Push((cmd, seq));
        return true;
    }

    public void MarkClean() => _clean = true;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _clean = true;
    }
}
