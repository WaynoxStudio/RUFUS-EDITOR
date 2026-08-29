using System.Xml.Linq;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Parses Astria <c>ArrayOfPos</c> XML (<c>grounds.xml</c> / <c>objects.xml</c>).
/// Handles null-byte padding used by the shipped XML files.
/// Duplicate IDs: keeps the FIRST entry (matches <c>Tile.Get_*_Pos</c> first-match).
/// </summary>
public static class GfxAnchorXmlParser
{
    public sealed class ParseResult
    {
        public required IReadOnlyDictionary<int, GfxAnchor> AnchorsById { get; init; }

        /// <summary>IDs that had more than one Pos row; value lists every X/Y in document order.</summary>
        public required IReadOnlyDictionary<int, IReadOnlyList<GfxAnchor>> AmbiguousAnchorsById { get; init; }

        public required IReadOnlyList<GfxCatalogIssue> Issues { get; init; }
        public bool HadNullPadding { get; init; }
        public int EntryCount { get; init; }
    }

    public static ParseResult ParseFile(string path, GfxCategory category)
    {
        if (!File.Exists(path))
        {
            return EmptyError(category, path, $"XML file not found: {path}", hadNullPadding: false);
        }

        var bytes = File.ReadAllBytes(path);
        return ParseBytes(bytes, category, path);
    }

    public static ParseResult ParseBytes(byte[] bytes, GfxCategory category, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var issues = new List<GfxCatalogIssue>();
        var hadNullPadding = false;

        var length = bytes.Length;
        var firstNull = Array.IndexOf(bytes, (byte)0);
        if (firstNull >= 0)
        {
            hadNullPadding = firstNull < bytes.Length - 1 || bytes[^1] == 0;
            length = firstNull;
            issues.Add(new GfxCatalogIssue
            {
                Severity = GfxIssueSeverity.Info,
                Code = GfxIssueCode.XmlNullPaddingStripped,
                Category = category,
                Path = sourcePath,
                Message = $"Stripped null-byte padding starting at offset {firstNull} (file size {bytes.Length}).",
            });
        }

        if (length <= 0)
            return EmptyError(category, sourcePath, "XML content is empty after removing null padding.", hadNullPadding, issues);

        string text;
        try
        {
            text = System.Text.Encoding.UTF8.GetString(bytes, 0, length);
        }
        catch (Exception ex)
        {
            return EmptyError(category, sourcePath, $"Failed to decode XML as UTF-8: {ex.Message}", hadNullPadding, issues);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.None);
        }
        catch (Exception ex)
        {
            return EmptyError(category, sourcePath, $"Malformed XML: {ex.Message}", hadNullPadding, issues);
        }

        var map = new Dictionary<int, GfxAnchor>();
        var allById = new Dictionary<int, List<GfxAnchor>>();
        var entryCount = 0;
        foreach (var pos in document.Descendants("Pos"))
        {
            entryCount++;
            var idText = pos.Element("ID")?.Value;
            var xText = pos.Element("X")?.Value;
            var yText = pos.Element("Y")?.Value;

            if (!int.TryParse(idText, out var id) ||
                !int.TryParse(xText, out var x) ||
                !int.TryParse(yText, out var y))
            {
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Error,
                    Code = GfxIssueCode.InvalidAnchorData,
                    Category = category,
                    Path = sourcePath,
                    Message = $"Invalid Pos entry (ID='{idText}', X='{xText}', Y='{yText}').",
                });
                continue;
            }

            var anchor = new GfxAnchor(x, y);
            if (!allById.TryGetValue(id, out var list))
            {
                list = [];
                allById[id] = list;
            }

            list.Add(anchor);

            if (map.ContainsKey(id))
            {
                issues.Add(new GfxCatalogIssue
                {
                    Severity = GfxIssueSeverity.Warning,
                    Code = GfxIssueCode.DuplicateAnchor,
                    Category = category,
                    GfxId = id,
                    Path = sourcePath,
                    Message = $"Duplicate anchor for GfxID {id}; keeping FIRST entry {map[id]} (ignoring later {anchor}). Astria Get_*_Pos is first-match.",
                });
                continue;
            }

            map[id] = anchor;
        }

        var ambiguous = allById
            .Where(kv => kv.Value.Count > 1)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GfxAnchor>)kv.Value);

        return new ParseResult
        {
            AnchorsById = map,
            AmbiguousAnchorsById = ambiguous,
            Issues = issues,
            HadNullPadding = hadNullPadding,
            EntryCount = entryCount,
        };
    }

    private static ParseResult EmptyError(
        GfxCategory category,
        string? path,
        string message,
        bool hadNullPadding,
        List<GfxCatalogIssue>? existing = null)
    {
        var issues = existing ?? [];
        issues.Add(new GfxCatalogIssue
        {
            Severity = GfxIssueSeverity.Error,
            Code = GfxIssueCode.MalformedXml,
            Category = category,
            Path = path,
            Message = message,
        });

        return new ParseResult
        {
            AnchorsById = new Dictionary<int, GfxAnchor>(),
            AmbiguousAnchorsById = new Dictionary<int, IReadOnlyList<GfxAnchor>>(),
            Issues = issues,
            HadNullPadding = hadNullPadding,
            EntryCount = 0,
        };
    }
}
