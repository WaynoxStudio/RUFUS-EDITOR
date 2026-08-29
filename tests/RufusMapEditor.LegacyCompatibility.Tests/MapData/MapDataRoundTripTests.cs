using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapData;

public sealed class MapDataRoundTripTests
{
    private static string FixturesRoot
    {
        get
        {
            // Prefer copied fixtures next to the test project; fall back to searching from cwd.
            var fromProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
            if (Directory.Exists(fromProject))
                return fromProject;

            var fromCwd = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures", "maps"));
            if (Directory.Exists(fromCwd))
                return fromCwd;

            throw new DirectoryNotFoundException("Could not locate tests/fixtures/maps.");
        }
    }

    [Fact]
    public void Map_10420_geometry_matches_known_reference()
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, "10420.sql"));

        Assert.Equal(10420, map.Id);
        Assert.Equal(15, map.Width);
        Assert.Equal(17, map.Height);
        Assert.Equal(479, MapGeometry.CellCount(map.Width, map.Height));
        Assert.Equal(4790, map.MapData.Length);
        Assert.Equal(4790, MapGeometry.ExpectedMapDataLength(map.Width, map.Height));
    }

    [Fact]
    public void Map_10420_MapData_decode_encode_is_bit_identical()
    {
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(FixturesRoot, "10420.sql"));
        var roundTripped = MapDataCodec.RoundTrip(map.MapData);

        Assert.Equal(map.MapData, roundTripped);
    }

    [Fact]
    public void All_fixture_maps_MapData_roundTrip_identically()
    {
        var sqlFiles = Directory.GetFiles(FixturesRoot, "*.sql").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.NotEmpty(sqlFiles);

        var failures = new List<string>();

        foreach (var sqlPath in sqlFiles)
        {
            var map = AstriaSqlMapParser.ParseFile(sqlPath);
            var expectedLength = MapGeometry.ExpectedMapDataLength(map.Width, map.Height);

            if (map.MapData.Length != expectedLength)
            {
                failures.Add($"{map.Id}: MapData length {map.MapData.Length} != expected {expectedLength} for {map.Width}x{map.Height}");
                continue;
            }

            if (!string.IsNullOrEmpty(map.Key))
            {
                failures.Add($"{map.Id}: encrypted MapData (non-empty key) — decryption fixture support is DATO PENDIENTE DE CONFIRMAR for this suite");
                continue;
            }

            var roundTripped = MapDataCodec.RoundTrip(map.MapData);
            if (!string.Equals(map.MapData, roundTripped, StringComparison.Ordinal))
            {
                var firstDiff = FirstDifference(map.MapData, roundTripped);
                failures.Add($"{map.Id}: MapData differs after roundtrip at index {firstDiff}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(15, 17, 479)] // classic outdoor
    [InlineData(14, 17, 446)] // height*(width*2-1)-width+1
    [InlineData(15, 15, 421)]
    public void CellCount_matches_astria_vb_array_length(int width, int height, int expectedCells)
    {
        Assert.Equal(expectedCells, MapGeometry.CellCount(width, height));
    }

    [Fact]
    public void Single_empty_cell_roundtrips()
    {
        var cell = new CellData();
        var encoded = MapDataCodec.EncodeCell(cell);
        Assert.Equal(10, encoded.Length);
        var decoded = MapDataCodec.DecodeCell(encoded);
        Assert.Equal(encoded, MapDataCodec.EncodeCell(decoded));
    }

    private static int FirstDifference(string a, string b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            if (a[i] != b[i])
                return i;
        }

        return a.Length == b.Length ? -1 : len;
    }
}
