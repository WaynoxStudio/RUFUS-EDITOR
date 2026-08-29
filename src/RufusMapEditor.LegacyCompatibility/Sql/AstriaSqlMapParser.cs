using System.Globalization;
using System.Text.RegularExpressions;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Sql;

/// <summary>
/// Minimal parser for Astria-exported <c>*.sql</c> INSERT statements (maps table).
/// Does not execute SQL; extracts fields needed for MapData fixtures.
/// </summary>
public static partial class AstriaSqlMapParser
{
    // Matches the VALUES tuple of Astria Get_SqlMap output.
    [GeneratedRegex(
        @"VALUES\s*\(\s*'(?<id>[^']*)'\s*,\s*'(?<date>[^']*)'\s*,\s*'(?<width>[^']*)'\s*,\s*'(?<height>[^']*)'\s*,\s*'(?<mapData>[^']*)'\s*,\s*'(?<key>[^']*)'\s*,\s*'(?<places>[^']*)'",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValuesRegex();

    public static MapDocument Parse(string sqlContent, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(sqlContent);
        var match = ValuesRegex().Match(sqlContent);
        if (!match.Success)
            throw new FormatException($"Could not parse Astria maps SQL{(sourcePath is null ? "" : $" from '{sourcePath}'")}.");

        var width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
        var mapData = match.Groups["mapData"].Value;

        return new MapDocument
        {
            Id = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture),
            DateMap = match.Groups["date"].Value,
            Width = width,
            Height = height,
            MapData = mapData,
            Key = match.Groups["key"].Value,
            FightPlaces = match.Groups["places"].Value,
        };
    }

    public static MapDocument ParseFile(string path)
    {
        var sql = File.ReadAllText(path);
        return Parse(sql, path);
    }
}
