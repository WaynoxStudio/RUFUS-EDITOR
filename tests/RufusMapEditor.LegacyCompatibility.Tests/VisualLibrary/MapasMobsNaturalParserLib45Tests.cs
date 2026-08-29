using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class MapasMobsNaturalParserLib45Tests
{
    [Fact]
    public void Parse_pipe_separated_mob_ids()
    {
        var tokens = MapasMobsNaturalParser.Parse("492|491|236|490|493");
        Assert.Equal(5, tokens.Count);
        Assert.Equal(492, tokens[0].MobId);
        Assert.False(tokens[0].HasExtendedFields);
        Assert.Equal(493, tokens[4].MobId);
    }

    [Fact]
    public void Parse_optional_extended_fields()
    {
        var tokens = MapasMobsNaturalParser.Parse("671,48,48|747,40,40");
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].HasExtendedFields);
        Assert.Equal(671, tokens[0].MobId);
        Assert.Equal(48, tokens[0].MinLvl);
        Assert.Equal(48, tokens[0].MaxLvl);
    }

    [Fact]
    public void BuildSimple_joins_with_pipe()
    {
        Assert.Equal("31|34|37", MapasMobsNaturalParser.BuildSimple(new[] { 31, 34, 37 }));
    }

    [Fact]
    public void Parse_empty_returns_empty()
    {
        Assert.Empty(MapasMobsNaturalParser.Parse(null));
        Assert.Empty(MapasMobsNaturalParser.Parse(""));
        Assert.Empty(MapasMobsNaturalParser.Parse("   "));
    }
}
