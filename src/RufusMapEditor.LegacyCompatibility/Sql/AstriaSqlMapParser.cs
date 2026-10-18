using System.Globalization;
using System.Text.RegularExpressions;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Sql;

/// <summary>
/// Parser for Astria-exported <c>*.sql</c> INSERT statements.
/// Supports both classic <c>maps</c> (no bgID) and Dutyfree <c>mapas</c> (bgID before mapData).
/// </summary>
public static partial class AstriaSqlMapParser
{
    [GeneratedRegex(
        @"INSERT\s+INTO\s+`?(?<table>[\w]+)`?\s*\((?<cols>[^)]+)\)\s*VALUES\s*\((?<vals>.*)\)\s*;?",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InsertRegex();

    // Classic Astria Get_SqlMap: id, date, width, height, mapData, key, places
    [GeneratedRegex(
        @"VALUES\s*\(\s*'(?<id>[^']*)'\s*,\s*'(?<date>[^']*)'\s*,\s*'(?<width>[^']*)'\s*,\s*'(?<height>[^']*)'\s*,\s*'(?<mapData>[^']*)'\s*,\s*'(?<key>[^']*)'\s*,\s*'(?<places>[^']*)'",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValuesRegex();

    public static MapDocument Parse(string sqlContent, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(sqlContent);
        if (TryParseByColumns(sqlContent, out var byColumns))
            return byColumns;

        var match = ValuesRegex().Match(sqlContent);
        if (!match.Success)
            throw new FormatException($"Could not parse Astria maps SQL{(sourcePath is null ? "" : $" from '{sourcePath}'")}.");

        return FromNamed(
            id: match.Groups["id"].Value,
            date: match.Groups["date"].Value,
            width: match.Groups["width"].Value,
            height: match.Groups["height"].Value,
            mapData: match.Groups["mapData"].Value,
            key: match.Groups["key"].Value,
            places: match.Groups["places"].Value,
            bgId: null);
    }

    public static MapDocument ParseFile(string path)
    {
        var sql = File.ReadAllText(path);
        return Parse(sql, path);
    }

    private static bool TryParseByColumns(string sqlContent, out MapDocument document)
    {
        document = null!;
        var insert = InsertRegex().Match(sqlContent);
        if (!insert.Success)
            return false;

        var columns = ParseIdentifiers(insert.Groups["cols"].Value);
        var values = ParseQuotedValues(insert.Groups["vals"].Value);
        if (columns.Count == 0 || values.Count == 0)
            return false;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var n = Math.Min(columns.Count, values.Count);
        for (var i = 0; i < n; i++)
            fields[columns[i]] = values[i];

        if (!TryGet(fields, out var mapData, "mapData"))
            return false;
        if (!TryGet(fields, out var width, "width", "ancho"))
            return false;
        if (!TryGet(fields, out var height, "height", "alto", "heigth"))
            return false;
        if (!TryGet(fields, out var id, "id"))
            return false;

        TryGet(fields, out var date, "date", "fecha");
        TryGet(fields, out var key, "key");
        TryGet(fields, out var places, "places", "posPelea");
        TryGet(fields, out var bgId, "bgID", "bgId", "backgroundNum");

        document = FromNamed(id, date, width, height, mapData, key, places, bgId);
        return true;
    }

    private static MapDocument FromNamed(
        string id,
        string? date,
        string width,
        string height,
        string mapData,
        string? key,
        string? places,
        string? bgId)
    {
        var doc = new MapDocument
        {
            Id = int.Parse(id, CultureInfo.InvariantCulture),
            DateMap = string.IsNullOrEmpty(date) ? "AME" : date,
            Width = int.Parse(width, CultureInfo.InvariantCulture),
            Height = int.Parse(height, CultureInfo.InvariantCulture),
            MapData = mapData,
            Key = key ?? "",
            FightPlaces = places ?? "",
        };

        if (int.TryParse(bgId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bg) && bg != 0)
        {
            doc.BackgroundId = bg;
            doc.BackgroundDefined = true;
        }

        return doc;
    }

    private static bool TryGet(Dictionary<string, string> fields, out string value, params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out value!))
                return true;
        }

        value = "";
        return false;
    }

    private static List<string> ParseIdentifiers(string columnList)
    {
        var list = new List<string>();
        foreach (var raw in columnList.Split(','))
        {
            var name = raw.Trim().Trim('`', '"', '\'', '[', ']');
            if (name.Length > 0)
                list.Add(name);
        }

        return list;
    }

    /// <summary>Splits a SQL VALUES tuple of quoted (or unquoted) scalars. Handles '' escapes.</summary>
    private static List<string> ParseQuotedValues(string valuesClause)
    {
        var list = new List<string>();
        var i = 0;
        var s = valuesClause.Trim();
        if (s.EndsWith(';'))
            s = s[..^1].TrimEnd();
        if (s.EndsWith(')'))
        {
            // InsertRegex is greedy to last ')'; keep content inside the tuple.
        }

        while (i < s.Length)
        {
            while (i < s.Length && (s[i] == ',' || char.IsWhiteSpace(s[i]) || s[i] == ')'))
                i++;
            if (i >= s.Length)
                break;

            if (s[i] == '\'')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < s.Length)
                {
                    if (s[i] == '\'')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(s[i]);
                    i++;
                }

                list.Add(sb.ToString());
            }
            else
            {
                var start = i;
                while (i < s.Length && s[i] != ',' && s[i] != ')')
                    i++;
                list.Add(s[start..i].Trim());
            }
        }

        return list;
    }
}
