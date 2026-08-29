namespace RufusMapEditor.Domain.Maps;

/// <summary>
/// Movement / walkability codes encoded in MapData (3 bits).
/// Values match Astria <c>MovementEnum</c>.
/// </summary>
public enum MovementType
{
    Unwalkable = 0,
    Door = 1,
    Trigger = 2,
    Walkable = 4,
    Paddock = 5,
    Path = 7,
}
