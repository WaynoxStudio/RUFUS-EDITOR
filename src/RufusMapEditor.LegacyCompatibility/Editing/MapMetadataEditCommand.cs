using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Editing;

public sealed class MapMetadataEditCommand : IEditCommand
{
    private readonly int _beforeBackground;
    private readonly int _afterBackground;
    private readonly bool _beforeDefined;

    public MapMetadataEditCommand(string name, int beforeBackground, int afterBackground, bool beforeDefined = true)
    {
        Name = name;
        _beforeBackground = beforeBackground;
        _afterBackground = afterBackground;
        _beforeDefined = beforeDefined;
    }

    public string Name { get; }

    public void Execute(MapDocument map)
    {
        map.BackgroundId = _afterBackground;
        map.BackgroundDefined = true;
    }

    public void Undo(MapDocument map)
    {
        map.BackgroundId = _beforeBackground;
        map.BackgroundDefined = _beforeDefined;
    }
}
