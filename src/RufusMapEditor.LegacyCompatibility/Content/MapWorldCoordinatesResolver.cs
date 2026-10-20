using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// COORDS_AND_PUBLISHED_REPAIR.6 — MapId → mapas.X / mapas.Y (cached).
/// Cell ID is never used. Does not invent coordinates.
/// </summary>
public sealed class MapWorldCoordinatesResolver
{
    private readonly Func<int, (int X, int Y)?> _lookup;
    private readonly Dictionary<int, (int X, int Y)> _cache = new();

    public MapWorldCoordinatesResolver(Func<int, (int X, int Y)?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public static MapWorldCoordinatesResolver FromRepository(IMapasRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return new MapWorldCoordinatesResolver(mapId =>
        {
            if (mapId <= 0) return null;
            var row = Task.Run(() => repository.TryGetAsync(mapId)).GetAwaiter().GetResult();
            return row is null ? null : (row.X, row.Y);
        });
    }

    public (int X, int Y)? TryResolve(int mapId)
    {
        if (mapId <= 0) return null;
        if (_cache.TryGetValue(mapId, out var cached))
            return cached;

        var coords = _lookup(mapId);
        if (coords is { } c)
            _cache[mapId] = c;
        return coords;
    }

    public void ClearCache() => _cache.Clear();
}
