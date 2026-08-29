using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Editing;

public sealed class MapIntFieldEditCommand : IEditCommand
{
    private readonly Action<MapDocument, int> _apply;
    private readonly int _before;
    private readonly int _after;

    public MapIntFieldEditCommand(string name, int before, int after, Action<MapDocument, int> apply)
    {
        Name = name;
        _before = before;
        _after = after;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public string Name { get; }

    public void Execute(MapDocument map) => _apply(map, _after);

    public void Undo(MapDocument map) => _apply(map, _before);
}

public sealed class MapStringFieldEditCommand : IEditCommand
{
    private readonly Action<MapDocument, string> _apply;
    private readonly string _before;
    private readonly string _after;

    public MapStringFieldEditCommand(string name, string before, string after, Action<MapDocument, string> apply)
    {
        Name = name;
        _before = before ?? "";
        _after = after ?? "";
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public string Name { get; }

    public void Execute(MapDocument map) => _apply(map, _after);

    public void Undo(MapDocument map) => _apply(map, _before);
}
