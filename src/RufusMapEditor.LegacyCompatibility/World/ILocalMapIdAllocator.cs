namespace RufusMapEditor.LegacyCompatibility.World;

/// <summary>
/// Proposes local Map IDs before BD validation (Fase 10).
/// </summary>
public interface ILocalMapIdAllocator
{
    int ProposeNextId(int sourceId, IReadOnlyCollection<int> reservedIds);
    bool IsAvailable(int mapId, IReadOnlyCollection<int> reservedIds);
}

public sealed class LocalMapIdAllocator : ILocalMapIdAllocator
{
    public int ProposeNextId(int sourceId, IReadOnlyCollection<int> reservedIds)
    {
        if (sourceId < 0)
            sourceId = 0;
        var candidate = sourceId + 1;
        var reserved = reservedIds as HashSet<int> ?? reservedIds.ToHashSet();
        while (reserved.Contains(candidate))
            candidate++;
        return candidate;
    }

    public bool IsAvailable(int mapId, IReadOnlyCollection<int> reservedIds) =>
        mapId > 0 && !reservedIds.Contains(mapId);
}
