using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.Domain.Maps;
using Xunit;

namespace RufusMapEditor.LegacyCompatibility.Tests;

public sealed class MapImportFromDatabaseLogicTests
{
    [Fact]
    public void TryCreateDocument_decodes_mapData_and_skips_posPelea()
    {
        var width = BlankMapFactory.MedioWidth;
        var height = BlankMapFactory.MedioHeight;
        var blank = BlankMapFactory.Create(12345, width, height);
        MapCellEditor.SyncDocument(blank);

        var row = new MapasRow
        {
            Id = 12345,
            Fecha = "250101",
            Ancho = width,
            Alto = height,
            BgId = 7,
            MusicId = 1,
            AmbienteId = 2,
            OutDoor = 1,
            Capabilities = 0,
            PosPelea = "aAbB",
            MapData = blank.MapData,
            X = -3,
            Y = 12,
        };

        Assert.True(MapImportFromDatabaseLogic.TryCreateDocument(row, out var doc, out var error), error);
        Assert.Equal(12345, doc.Id);
        Assert.Equal(width, doc.Width);
        Assert.Equal(height, doc.Height);
        Assert.Equal(blank.MapData, doc.MapData);
        Assert.Equal(MapGeometry.CellCount(width, height), doc.Cells.Count);
        // posPelea from BD is ignored; SyncDocument may encode empty cells as "|".
        Assert.NotEqual("aAbB", doc.FightPlaces);
        Assert.Equal(7, doc.BackgroundId);
        Assert.True(doc.WorldCoordinatesSet);
        Assert.Equal(-3, doc.WorldX);
        Assert.Equal(12, doc.WorldY);
    }

    [Fact]
    public void TryCreateDocument_rejects_mismatched_dimensions()
    {
        var row = new MapasRow
        {
            Id = 1,
            Ancho = 15,
            Alto = 17,
            MapData = new string('a', 10), // 1 cell, not Medio
        };

        Assert.False(MapImportFromDatabaseLogic.TryCreateDocument(row, out _, out var error));
        Assert.Contains("no coincide", error!, StringComparison.OrdinalIgnoreCase);
    }
}
