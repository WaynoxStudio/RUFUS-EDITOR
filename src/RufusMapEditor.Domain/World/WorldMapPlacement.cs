namespace RufusMapEditor.Domain.World;

/// <summary>
/// A map document placed on the world grid at integer coordinates.
/// </summary>
public sealed class WorldMapPlacement
{
    public required string DocumentKey { get; init; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
}
