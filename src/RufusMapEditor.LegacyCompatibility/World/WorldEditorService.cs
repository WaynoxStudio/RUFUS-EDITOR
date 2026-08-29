using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;

namespace RufusMapEditor.LegacyCompatibility.World;

public enum WorldMoveResult
{
    Ok,
    Occupied,
    MissingDocument,
    InvalidTarget,
}

public enum WorldGridEdge
{
    North,
    South,
    East,
    West,
}

public enum WorldGridResizeResult
{
    Ok,
    NoGrid,
    MinSize,
}

public sealed class WorldEditorService
{
    private readonly ILocalMapIdAllocator _mapIds;

    public WorldEditorService(ILocalMapIdAllocator? mapIds = null)
    {
        _mapIds = mapIds ?? new LocalMapIdAllocator();
    }

    public WorldDocument CreateNew(
        string? name = null,
        int gridWidth = 0,
        int gridHeight = 0,
        int originX = 0,
        int originY = 0) => new()
    {
        WorldId = Guid.NewGuid().ToString("D"),
        Name = name ?? "Nuevo mundo",
        CreatedUtc = DateTimeOffset.UtcNow,
        ModifiedUtc = DateTimeOffset.UtcNow,
        GridWidth = gridWidth,
        GridHeight = gridHeight,
        OriginX = originX,
        OriginY = originY,
    };

    public HashSet<int> CollectReservedMapIds(WorldDocument world, IEnumerable<int>? libraryMapIds = null)
    {
        var set = new HashSet<int>();
        foreach (var entry in world.Documents.Values)
            set.Add(entry.Document.Id);
        if (libraryMapIds is not null)
        {
            foreach (var id in libraryMapIds)
                set.Add(id);
        }

        return set;
    }

    public string AddDocument(WorldDocument world, MapDocument document, WorldMapOrigin origin, WorldMapPublicationState publication)
    {
        var key = Guid.NewGuid().ToString("N");
        world.Documents[key] = new WorldMapEntry
        {
            Key = key,
            Document = document,
            Origin = origin,
            PublicationState = publication,
            SourceLibraryMapId = origin == WorldMapOrigin.Library ? document.Id : null,
        };
        Touch(world);
        return key;
    }

    public WorldMoveResult PlaceAt(WorldDocument world, string documentKey, int x, int y, bool swapIfOccupied = false)
    {
        if (!world.Documents.ContainsKey(documentKey))
            return WorldMoveResult.MissingDocument;

        var existing = FindPlacementAt(world, x, y);
        if (existing is not null && existing.DocumentKey != documentKey)
        {
            if (!swapIfOccupied)
                return WorldMoveResult.Occupied;
            var moving = world.Placements.FirstOrDefault(p => p.DocumentKey == documentKey);
            if (moving is null)
                return WorldMoveResult.InvalidTarget;
            var oldX = moving.WorldX;
            var oldY = moving.WorldY;
            moving.WorldX = x;
            moving.WorldY = y;
            existing.WorldX = oldX;
            existing.WorldY = oldY;
            world.UnplacedDocumentKeys.Remove(documentKey);
            Touch(world);
            return WorldMoveResult.Ok;
        }

        var placement = world.Placements.FirstOrDefault(p => p.DocumentKey == documentKey);
        if (placement is null)
        {
            world.Placements.Add(new WorldMapPlacement
            {
                DocumentKey = documentKey,
                WorldX = x,
                WorldY = y,
            });
        }
        else
        {
            placement.WorldX = x;
            placement.WorldY = y;
        }

        world.UnplacedDocumentKeys.Remove(documentKey);
        Touch(world);
        return WorldMoveResult.Ok;
    }

    public void RemoveFromWorld(WorldDocument world, string documentKey, bool moveToTray = true)
    {
        world.Placements.RemoveAll(p => p.DocumentKey == documentKey);
        if (moveToTray && world.Documents.ContainsKey(documentKey) &&
            !world.UnplacedDocumentKeys.Contains(documentKey))
            world.UnplacedDocumentKeys.Add(documentKey);
        Touch(world);
    }

    public DuplicateMapResult DuplicateMap(
        WorldDocument world,
        string sourceKey,
        int? requestedMapId = null,
        (int X, int Y)? preferredPosition = null)
    {
        if (!world.Documents.TryGetValue(sourceKey, out var sourceEntry))
            throw new KeyNotFoundException(sourceKey);

        var reserved = CollectReservedMapIds(world);
        var newId = requestedMapId ?? _mapIds.ProposeNextId(sourceEntry.Document.Id, reserved);
        if (! _mapIds.IsAvailable(newId, reserved))
            throw new InvalidOperationException($"Map ID {newId} ya está en uso localmente.");

        var copy = MapDocumentDuplicator.DeepCopy(sourceEntry.Document, newId);
        var newKey = AddDocument(
            world,
            copy,
            WorldMapOrigin.LocalDuplicate,
            WorldMapPublicationState.LocalUnpublished);

        var sourcePlacement = world.Placements.FirstOrDefault(p => p.DocumentKey == sourceKey);
        var occupied = OccupiedCells(world);
        (int X, int Y)? pos = preferredPosition;
        if (pos is null && sourcePlacement is not null)
            pos = WorldGeometry.FindAdjacentFree(sourcePlacement.WorldX, sourcePlacement.WorldY, occupied);
        if (pos is not null)
            PlaceAt(world, newKey, pos.Value.X, pos.Value.Y);
        else
            world.UnplacedDocumentKeys.Add(newKey);

        Touch(world);
        return new DuplicateMapResult(newKey, newId, pos);
    }

    public HashSet<(int X, int Y)> OccupiedCells(WorldDocument world) =>
        world.Placements.Select(p => (p.WorldX, p.WorldY)).ToHashSet();

    public WorldMapPlacement? FindPlacementAt(WorldDocument world, int x, int y) =>
        world.Placements.FirstOrDefault(p => p.WorldX == x && p.WorldY == y);

    public string? FindDocumentKeyAt(WorldDocument world, int x, int y) =>
        FindPlacementAt(world, x, y)?.DocumentKey;

    public void MarkMapDocumentEdited(WorldDocument world, string documentKey)
    {
        if (world.Documents.ContainsKey(documentKey))
            Touch(world);
    }

    public WorldGridResizeResult ExpandGrid(WorldDocument world, WorldGridEdge edge)
    {
        if (!world.HasGrid)
            return WorldGridResizeResult.NoGrid;

        switch (edge)
        {
            case WorldGridEdge.East:
                world.GridWidth++;
                break;
            case WorldGridEdge.West:
                world.OriginX--;
                world.GridWidth++;
                break;
            case WorldGridEdge.South:
                world.GridHeight++;
                break;
            case WorldGridEdge.North:
                world.OriginY--;
                world.GridHeight++;
                break;
            default:
                return WorldGridResizeResult.NoGrid;
        }

        Touch(world);
        return WorldGridResizeResult.Ok;
    }

    public bool CanShrinkGrid(WorldDocument world, WorldGridEdge edge)
    {
        if (!world.HasGrid) return false;
        return edge switch
        {
            WorldGridEdge.East or WorldGridEdge.West => world.GridWidth > 1,
            WorldGridEdge.North or WorldGridEdge.South => world.GridHeight > 1,
            _ => false,
        };
    }

    /// <summary>
    /// Removes one row/column on the given edge. Maps on that edge are moved to the tray.
    /// </summary>
    public WorldGridResizeResult ShrinkGrid(
        WorldDocument world,
        WorldGridEdge edge,
        out IReadOnlyList<string> removedDocumentKeys)
    {
        removedDocumentKeys = Array.Empty<string>();
        if (!world.HasGrid)
            return WorldGridResizeResult.NoGrid;
        if (!CanShrinkGrid(world, edge))
            return WorldGridResizeResult.MinSize;

        var removed = new List<string>();
        switch (edge)
        {
            case WorldGridEdge.East:
            {
                var x = world.OriginX + world.GridWidth - 1;
                CollectAndUnplaceColumn(world, x, removed);
                world.GridWidth--;
                break;
            }
            case WorldGridEdge.West:
            {
                CollectAndUnplaceColumn(world, world.OriginX, removed);
                world.OriginX++;
                world.GridWidth--;
                break;
            }
            case WorldGridEdge.South:
            {
                var y = world.OriginY + world.GridHeight - 1;
                CollectAndUnplaceRow(world, y, removed);
                world.GridHeight--;
                break;
            }
            case WorldGridEdge.North:
            {
                CollectAndUnplaceRow(world, world.OriginY, removed);
                world.OriginY++;
                world.GridHeight--;
                break;
            }
            default:
                return WorldGridResizeResult.NoGrid;
        }

        removedDocumentKeys = removed;
        Touch(world);
        return WorldGridResizeResult.Ok;
    }

    public int CountPlacementsOnEdge(WorldDocument world, WorldGridEdge edge)
    {
        if (!world.HasGrid) return 0;
        return edge switch
        {
            WorldGridEdge.East => world.Placements.Count(p => p.WorldX == world.OriginX + world.GridWidth - 1),
            WorldGridEdge.West => world.Placements.Count(p => p.WorldX == world.OriginX),
            WorldGridEdge.South => world.Placements.Count(p => p.WorldY == world.OriginY + world.GridHeight - 1),
            WorldGridEdge.North => world.Placements.Count(p => p.WorldY == world.OriginY),
            _ => 0,
        };
    }

    private void CollectAndUnplaceColumn(WorldDocument world, int x, List<string> removed)
    {
        foreach (var p in world.Placements.Where(p => p.WorldX == x).ToList())
        {
            removed.Add(p.DocumentKey);
            RemoveFromWorld(world, p.DocumentKey, moveToTray: true);
        }
    }

    private void CollectAndUnplaceRow(WorldDocument world, int y, List<string> removed)
    {
        foreach (var p in world.Placements.Where(p => p.WorldY == y).ToList())
        {
            removed.Add(p.DocumentKey);
            RemoveFromWorld(world, p.DocumentKey, moveToTray: true);
        }
    }

    private static void Touch(WorldDocument world)
    {
        world.IsDirty = true;
        world.ModifiedUtc = DateTimeOffset.UtcNow;
    }
}

public sealed record DuplicateMapResult(string DocumentKey, int MapId, (int X, int Y)? PlacedAt);
