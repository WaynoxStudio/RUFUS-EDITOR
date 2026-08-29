using System.Globalization;
using System.Xml.Linq;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>ADMIN.UI.4B.2A.3C — READ-ONLY parse of <c>clips/sprites/sprites.xml</c>.</summary>
public static class SpritesXmlParser
{
    public sealed class ParseResult
    {
        public required IReadOnlyDictionary<int, string> Names { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }

    public static ParseResult ParseFile(string spritesXmlPath)
    {
        if (string.IsNullOrWhiteSpace(spritesXmlPath) || !File.Exists(spritesXmlPath))
            return Empty("sprites.xml no encontrado.");

        try
        {
            return Parse(XDocument.Load(spritesXmlPath));
        }
        catch (Exception ex)
        {
            return Empty($"sprites.xml ilegible: {ex.Message}");
        }
    }

    public static ParseResult Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new Dictionary<int, string>();
        var warnings = new List<string>();

        foreach (var sprite in document.Descendants("sprite"))
        {
            var idAttr = sprite.Attribute("id")?.Value;
            var nameAttr = sprite.Attribute("name")?.Value?.Trim();
            if (!int.TryParse(idAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;
            if (string.IsNullOrWhiteSpace(nameAttr))
                continue;

            if (names.TryGetValue(id, out var existing))
            {
                if (!string.Equals(existing, nameAttr, StringComparison.Ordinal))
                {
                    warnings.Add(
                        $"sprites.xml: gfx {id} tiene nombres distintos ('{existing}' vs '{nameAttr}'); se conserva el primero.");
                }

                continue;
            }

            names[id] = nameAttr;
        }

        return new ParseResult
        {
            Names = names,
            Warnings = warnings,
        };
    }

    public static string? ResolveSpritesXmlPath(string? clipsRoot)
    {
        if (string.IsNullOrWhiteSpace(clipsRoot))
            return null;
        return Path.Combine(Path.GetFullPath(clipsRoot.Trim()), "sprites", "sprites.xml");
    }

    private static ParseResult Empty(string warning) =>
        new()
        {
            Names = new Dictionary<int, string>(),
            Warnings = new[] { warning },
        };
}
