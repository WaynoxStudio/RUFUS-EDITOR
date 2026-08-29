using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.LangMaps;

/// <summary>HOTFIX 11B.1.1 — parser versions_es.txt formato real 11&f=…|…</summary>
public sealed class LangMapsHotfix11B11Tests
{
    [Theory]
    [InlineData("11&f=maps,es,1282|quests,es,1275", 1282)]
    [InlineData("maps,es,1282|quests,es,1275", 1282)]
    [InlineData("11&f=quests,es,1275|maps,es,1282|spells,es,1308", 1282)]
    [InlineData("  \r\n11&f=maps,es,1282|quests,es,1275\r\n  ", 1282)]
    public void Parses_real_pipe_format(string text, int expected)
    {
        Assert.True(VersionsEsParser.TryParseMapsVersion(text, out var v, out var err), err);
        Assert.Equal(expected, v);
        Assert.Equal("maps,es," + expected, VersionsEsParser.ExtractMapsLine(text));
    }

    [Fact]
    public void Parses_bom_plus_real_content()
    {
        var text = "\uFEFF11&f=maps,es,1282|quests,es,1275|spells,es,1308";
        Assert.True(VersionsEsParser.TryParseMapsVersion(text, out var v, out var err), err);
        Assert.Equal(1282, v);
    }

    [Fact]
    public void Missing_maps_returns_error()
    {
        Assert.False(VersionsEsParser.TryParseMapsVersion("11&f=quests,es,1275|spells,es,1308", out _, out var err));
        Assert.Contains("maps,es", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Maps_es_non_integer_returns_error()
    {
        Assert.False(VersionsEsParser.TryParseMapsVersion("11&f=maps,es,abc|quests,es,1", out _, out var err));
        Assert.Contains("invalida", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_valid_maps_tokens_block_ambiguity()
    {
        Assert.False(VersionsEsParser.TryParseMapsVersion(
            "11&f=maps,es,1282|quests,es,1|maps,es,999",
            out _,
            out var err));
        Assert.Contains("ambiguedad", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_match_mymaps_prefix()
    {
        Assert.False(VersionsEsParser.TryParseMapsVersion("11&f=mymaps,es,1282|quests,es,1", out _, out var err));
        Assert.Contains("maps,es", err!, StringComparison.OrdinalIgnoreCase);
    }
}