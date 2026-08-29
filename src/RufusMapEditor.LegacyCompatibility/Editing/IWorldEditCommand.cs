using RufusMapEditor.Domain.World;

namespace RufusMapEditor.LegacyCompatibility.Editing;

public interface IWorldEditCommand
{
    string Name { get; }
    void Execute(WorldDocument world);
    void Undo(WorldDocument world);
}
