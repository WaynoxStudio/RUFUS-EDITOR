using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapData;

public sealed class FightPlacesCodecTests
{
    private static string FixturesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));

    private static MapDocument LoadFixture(int mapId)
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, $"{mapId}.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
        return map;
    }

    [Fact]
    public void Golden_10421_cell_67_team1_from_places()
    {
        var map = LoadFixture(10421);
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        Assert.Equal(1, map.Cells[67].FightCell);
        Assert.Equal("HxGaeaaaaa", MapDataCodec.EncodeCell(map.Cells[67]));
    }

    [Fact]
    public void Golden_10421_cell_330_team2_from_places()
    {
        var map = LoadFixture(10421);
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        Assert.Equal(2, map.Cells[330].FightCell);
        Assert.Equal("HhGaeaaaaa", MapDataCodec.EncodeCell(map.Cells[330]));
    }

    [Fact]
    public void Roundtrip_places_exact_for_10421()
    {
        var map = LoadFixture(10421);
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        var encoded = FightPlacesCodec.Encode(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
        Assert.Equal(map.FightPlaces, encoded);
    }

    [Fact]
    public void Roundtrip_places_exact_for_all_fixtures_with_places()
    {
        foreach (var file in Directory.GetFiles(FixturesRoot, "*.sql"))
        {
            var map = AstriaSqlMapParser.ParseFile(file);
            if (string.IsNullOrEmpty(map.FightPlaces))
                continue;

            map.Cells = MapDataCodec.DecodeMap(map.MapData).ToList();
            FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
            var encoded = FightPlacesCodec.Encode(map.Cells as IReadOnlyList<CellData> ?? map.Cells.ToList());
            Assert.True(encoded == map.FightPlaces,
                $"FightPlaces roundtrip failed for {Path.GetFileNameWithoutExtension(file)}");
        }
    }
}
