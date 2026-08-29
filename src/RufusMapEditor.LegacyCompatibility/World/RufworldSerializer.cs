using System.Text.Json;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.World;

public static class RufworldSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static RufworldFileDto FromWorld(WorldDocument world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var dto = new RufworldFileDto
        {
            FormatVersion = RufworldFormat.CurrentVersion,
            WorldId = world.WorldId,
            Name = world.Name,
            CreatedUtc = world.CreatedUtc,
            ModifiedUtc = DateTimeOffset.UtcNow,
            GridWidth = world.GridWidth,
            GridHeight = world.GridHeight,
            OriginX = world.OriginX,
            OriginY = world.OriginY,
            Placements = world.Placements.Select(p => new RufworldPlacementDto
            {
                DocumentKey = p.DocumentKey,
                X = p.WorldX,
                Y = p.WorldY,
            }).ToList(),
            UnplacedKeys = world.UnplacedDocumentKeys.ToList(),
            View = new RufworldViewDto
            {
                Zoom = world.View.Zoom,
                PanX = world.View.PanX,
                PanY = world.View.PanY,
                MosaicMode = world.View.MosaicMode,
                ShowInfoOverlay = world.View.ShowInfoOverlay,
            },
        };

        foreach (var (key, entry) in world.Documents)
        {
            MapCellEditor.SyncMapDataString(entry.Document);
            dto.Documents[key] = new RufworldDocumentDto
            {
                Origin = entry.Origin.ToString(),
                PublicationState = entry.PublicationState.ToString(),
                SourceLibraryMapId = entry.SourceLibraryMapId,
                LinkedRufmapPath = entry.LinkedRufmapPath,
                Map = ToMapDto(entry.Document),
            };
        }

        return dto;
    }

    public static WorldDocument ToWorld(RufworldFileDto dto)
    {
        if (dto.FormatVersion <= 0 || dto.FormatVersion > RufworldFormat.CurrentVersion)
            throw new InvalidDataException($"formatVersion {dto.FormatVersion} no soportado.");

        var world = new WorldDocument
        {
            WorldId = string.IsNullOrWhiteSpace(dto.WorldId) ? Guid.NewGuid().ToString("D") : dto.WorldId,
            Name = dto.Name ?? "Mundo",
            CreatedUtc = dto.CreatedUtc,
            ModifiedUtc = dto.ModifiedUtc,
            FilePath = null,
            IsDirty = false,
            GridWidth = dto.GridWidth,
            GridHeight = dto.GridHeight,
            OriginX = dto.OriginX,
            OriginY = dto.OriginY,
            View = dto.View is null
                ? new WorldViewState()
                : new WorldViewState
                {
                    Zoom = dto.View.Zoom,
                    PanX = dto.View.PanX,
                    PanY = dto.View.PanY,
                    MosaicMode = dto.View.MosaicMode,
                    ShowInfoOverlay = dto.View.ShowInfoOverlay,
                },
        };

        foreach (var (key, docDto) in dto.Documents)
        {
            world.Documents[key] = new WorldMapEntry
            {
                Key = key,
                Document = FromMapDto(docDto.Map),
                Origin = Enum.TryParse<WorldMapOrigin>(docDto.Origin, out var o) ? o : WorldMapOrigin.LocalNew,
                PublicationState = Enum.TryParse<WorldMapPublicationState>(docDto.PublicationState, out var ps)
                    ? ps
                    : WorldMapPublicationState.LocalUnpublished,
                SourceLibraryMapId = docDto.SourceLibraryMapId,
                LinkedRufmapPath = docDto.LinkedRufmapPath,
            };
        }

        world.Placements = dto.Placements
            .Where(p => world.Documents.ContainsKey(p.DocumentKey))
            .Select(p => new WorldMapPlacement
            {
                DocumentKey = p.DocumentKey,
                WorldX = p.X,
                WorldY = p.Y,
            }).ToList();

        world.UnplacedDocumentKeys = dto.UnplacedKeys
            .Where(k => world.Documents.ContainsKey(k))
            .ToList();

        return world;
    }

    public static string Serialize(RufworldFileDto dto) => JsonSerializer.Serialize(dto, JsonOptions);

    public static RufworldFileDto Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<RufworldFileDto>(json, JsonOptions)
                  ?? throw new InvalidDataException("JSON inválido.");
        return dto;
    }

    private static RufmapMapDto ToMapDto(MapDocument map)
    {
        MapCellEditor.SyncMapDataString(map);
        return new RufmapMapDto
        {
            Id = map.Id,
            Width = map.Width,
            Height = map.Height,
            DateMap = map.DateMap ?? "AME",
            Key = map.Key ?? "",
            FightPlaces = map.FightPlaces ?? "",
            BackgroundId = map.BackgroundId,
            MusicId = map.MusicId,
            AmbianceId = map.AmbianceId,
            Capabilities = map.Capabilities,
            Outdoor = map.Outdoor,
            MapData = map.MapData,
            Cells = map.Cells.Select(c => new RufmapCellDto
            {
                Active = c.Active,
                LineOfSight = c.LineOfSight,
                Movement = (int)c.Movement,
                GroundGfxId = c.GroundGfxId,
                Object1GfxId = c.Object1GfxId,
                Object2GfxId = c.Object2GfxId,
                FlipGround = c.FlipGround,
                FlipObject1 = c.FlipObject1,
                FlipObject2 = c.FlipObject2,
                GroundRotation = c.GroundRotation,
                Object1Rotation = c.Object1Rotation,
                GroundLevel = c.GroundLevel,
                GroundSlope = c.GroundSlope,
                InteractiveObject = c.InteractiveObject,
            }).ToList(),
        };
    }

    private static MapDocument FromMapDto(RufmapMapDto m)
    {
        var result = RufmapSerializer.ToDocument(new RufmapFileDto
        {
            FormatVersion = RufmapFormat.CurrentVersion,
            DocumentId = Guid.NewGuid().ToString("D"),
            CreatedUtc = DateTimeOffset.UtcNow,
            ModifiedUtc = DateTimeOffset.UtcNow,
            Map = m,
        });
        return result.Document;
    }
}
