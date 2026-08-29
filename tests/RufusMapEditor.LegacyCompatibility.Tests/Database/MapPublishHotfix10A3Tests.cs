using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Tests.Database;

/// <summary>HOTFIX 10A.3 — explicit (0,0) is valid; undefined is not.</summary>
public sealed class MapPublishHotfix10A3Tests
{
    private static MapDocument BaseMap(int x, int y, bool coordinatesSet)
    {
        var cells = Enumerable.Range(0, MapGeometry.CellCount(15, 17)).Select(_ => new CellData()).ToList();
        cells[2].FightCell = 1;
        cells[3].FightCell = 2;
        var map = new MapDocument
        {
            Id = 50030,
            Width = 15,
            Height = 17,
            DateMap = "0",
            BackgroundId = 1,
            BackgroundDefined = true,
            MusicId = 0,
            MusicDefined = true,
            AmbianceId = 0,
            AmbianceDefined = true,
            Capabilities = 0,
            CapabilitiesDefined = true,
            Outdoor = true,
            WorldX = x,
            WorldY = y,
            WorldCoordinatesSet = coordinatesSet,
            Cells = cells,
        };
        MapCellEditor.SyncDocument(map);
        return map;
    }

    [Fact]
    public void Undefined_coordinates_still_block_FromDocument_update_path()
    {
        var map = BaseMap(0, 0, coordinatesSet: false);
        // CREATE applies 0,0 via EnsureNewMapWorldCoordinates; UPDATE/FromDocument still refuses inventing.
        Assert.Empty(MapCreateLogic.ValidateDocumentForCreate(map));
        var ex = Assert.Throws<InvalidOperationException>(() => MapPublishLogic.FromDocument(map, "1"));
        Assert.Contains("WorldCoordinatesSet=false", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_plan_applies_zero_zero_without_mutating_until_ensure()
    {
        var map = BaseMap(5, 5, coordinatesSet: false);
        Assert.False(map.WorldCoordinatesSet);
        MapCreateLogic.EnsureNewMapWorldCoordinates(map);
        Assert.True(map.WorldCoordinatesSet);
        Assert.Equal(0, map.WorldX);
        Assert.Equal(0, map.WorldY);
        Assert.Empty(MapCreateLogic.ValidateDocumentForCreate(map));
        var values = MapPublishLogic.FromDocument(map, "0");
        Assert.Equal(0, values.X);
        Assert.Equal(0, values.Y);
    }

    [Fact]
    public void Explicit_zero_zero_allows_FromDocument()
    {
        var map = BaseMap(0, 0, coordinatesSet: true);
        var values = MapPublishLogic.FromDocument(map, "1");
        Assert.Equal(0, values.X);
        Assert.Equal(0, values.Y);
        Assert.Empty(MapCreateLogic.ValidateDocumentForCreate(map));
    }

    [Fact]
    public void Explicit_negative_x_zero_y_allows()
    {
        var map = BaseMap(-45, 0, coordinatesSet: true);
        var values = MapPublishLogic.FromDocument(map, "1");
        Assert.Equal(-45, values.X);
        Assert.Equal(0, values.Y);
    }

    [Fact]
    public void Explicit_zero_x_negative_y_allows()
    {
        var map = BaseMap(0, -2, coordinatesSet: true);
        var values = MapPublishLogic.FromDocument(map, "1");
        Assert.Equal(0, values.X);
        Assert.Equal(-2, values.Y);
    }

    [Fact]
    public void Sync_from_db_zero_zero_marks_coordinates_defined()
    {
        var map = BaseMap(99, 99, coordinatesSet: false);
        var row = new MapasRow
        {
            Id = map.Id,
            Fecha = "0706141524",
            Ancho = map.Width,
            Alto = map.Height,
            BgId = 0,
            MusicId = 0,
            AmbienteId = 0,
            OutDoor = 1,
            Capabilities = 0,
            PosPelea = map.FightPlaces ?? "",
            MapData = map.MapData ?? "",
            X = 0,
            Y = 0,
        };

        MapPublishLogic.SyncMetadataFromDatabase(map, row);
        Assert.True(map.WorldCoordinatesSet);
        Assert.Equal(0, map.WorldX);
        Assert.Equal(0, map.WorldY);
        var values = MapPublishLogic.FromDocument(map, "0706141525");
        Assert.Equal(0, values.X);
        Assert.Equal(0, values.Y);
    }

    [Fact]
    public void Rufmap_roundtrip_preserves_explicit_zero_zero()
    {
        var map = BaseMap(0, 0, coordinatesSet: true);
        var dto = RufmapSerializer.FromDocument(map, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow, null);
        Assert.True(dto.Map.WorldCoordinatesSet);
        Assert.Equal(0, dto.Map.WorldX);
        Assert.Equal(0, dto.Map.WorldY);

        var json = RufmapSerializer.Serialize(dto);
        Assert.Contains("\"worldCoordinatesSet\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"worldX\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"worldY\": 0", json, StringComparison.Ordinal);

        var loaded = RufmapSerializer.ToDocument(RufmapSerializer.DeserializeDto(json));
        Assert.True(loaded.Document.WorldCoordinatesSet);
        Assert.Equal(0, loaded.Document.WorldX);
        Assert.Equal(0, loaded.Document.WorldY);
    }
}
