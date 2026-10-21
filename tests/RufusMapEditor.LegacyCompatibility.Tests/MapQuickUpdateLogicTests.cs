using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.MapData;
using Xunit;

namespace RufusMapEditor.LegacyCompatibility.Tests;

public sealed class MapQuickUpdateLogicTests
{
    [Fact]
    public void TryBuildValues_updates_mapData_bg_xy_and_bumps_fecha()
    {
        var map = BlankMapFactory.Create(100, 15, 17);
        map.BackgroundId = 71;
        map.BackgroundDefined = true;
        map.WorldX = -3;
        map.WorldY = -6;
        map.WorldCoordinatesSet = true;
        MapCellEditor.SyncDocument(map);

        var db = new MapasRow
        {
            Id = 100,
            Fecha = "12",
            Ancho = 15,
            Alto = 17,
            BgId = 1,
            MapData = "old",
            X = 0,
            Y = 0,
            PosPelea = "keep-me",
            MusicId = 9,
        };

        Assert.True(MapQuickUpdateLogic.TryBuildValues(map, db, out var values, out var error), error);
        Assert.Equal("13", values.Fecha);
        Assert.Equal(71, values.BgId);
        Assert.Equal(map.MapData, values.MapData);
        Assert.Equal(-3, values.X);
        Assert.Equal(-6, values.Y);
        Assert.Equal(15, values.Ancho);
        Assert.Equal(17, values.Alto);
        Assert.Equal("keep-me", values.PosPelea);
        Assert.Equal(MapQuickUpdateLogic.ColumnsToUpdate, values.EffectiveUpdateColumns);
    }

    [Fact]
    public void TryBuildValues_rejects_size_mismatch()
    {
        var map = BlankMapFactory.Create(1, 15, 17);
        var db = new MapasRow { Id = 1, Fecha = "1", Ancho = 19, Alto = 22, MapData = "x" };
        Assert.False(MapQuickUpdateLogic.TryBuildValues(map, db, out _, out var error));
        Assert.Contains("tamaño", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildValues_keeps_named_fecha()
    {
        var map = BlankMapFactory.Create(30100, 19, 22);
        map.BackgroundId = 105;
        map.BackgroundDefined = true;
        MapCellEditor.SyncDocument(map);

        var db = new MapasRow
        {
            Id = 30100,
            Fecha = "Torre desafio",
            Ancho = 19,
            Alto = 22,
            BgId = 284,
            MapData = "old",
            X = 0,
            Y = 0,
        };

        Assert.True(MapQuickUpdateLogic.TryBuildValues(map, db, out var values, out var error), error);
        Assert.Equal("Torre desafio", values.Fecha);
        Assert.Equal(map.MapData, values.MapData);
        Assert.Equal(105, values.BgId);
    }
}
