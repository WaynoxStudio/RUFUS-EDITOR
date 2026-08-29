using RufusMapEditor.Domain.World;

namespace RufusMapEditor.LegacyCompatibility.Editing;

/// <summary>
/// One logical undo step spanning multiple map documents (e.g. a cross-map paint stroke).
/// </summary>
public sealed class CompositeMapEditCommand : IWorldEditCommand
{
    private readonly IReadOnlyList<(string DocumentKey, CellBatchEditCommand Command)> _parts;

    public CompositeMapEditCommand(string name, IReadOnlyList<(string DocumentKey, CellBatchEditCommand Command)> parts)
    {
        Name = name;
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        if (_parts.Count == 0)
            throw new ArgumentException("Composite command requires at least one part.", nameof(parts));
    }

    public string Name { get; }

    public int PartCount => _parts.Count;

    public int TotalCellChanges => _parts.Sum(p => p.Command.ChangeCount);

    public void Execute(WorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var (key, cmd) in _parts)
        {
            if (!world.Documents.TryGetValue(key, out var entry))
                throw new InvalidOperationException($"Document key missing: {key}");
            cmd.Execute(entry.Document);
        }
    }

    public void Undo(WorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        for (var i = _parts.Count - 1; i >= 0; i--)
        {
            var (key, cmd) = _parts[i];
            if (!world.Documents.TryGetValue(key, out var entry))
                throw new InvalidOperationException($"Document key missing: {key}");
            cmd.Undo(entry.Document);
        }
    }
}
