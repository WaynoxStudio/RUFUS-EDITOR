using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Resolve sprite.xml display names without full catalog load (identity panel).</summary>
public static class NpcGfxAppearanceNames
{
    private static readonly object Gate = new();
    private static string? _cachedClipsRoot;
    private static IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>();

    public static string Resolve(int gfxId, string? clipsRoot)
    {
        EnsureLoaded(clipsRoot);
        lock (Gate)
        {
            if (_names.TryGetValue(gfxId, out var name) && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        return NpcGfxCatalogFormatting.FormatDisplayName(gfxId, null);
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _cachedClipsRoot = null;
            _names = new Dictionary<int, string>();
        }
    }

    private static void EnsureLoaded(string? clipsRoot)
    {
        var effective = ClipsRootPaths.ResolveEffective(clipsRoot);
        var normalized = string.IsNullOrWhiteSpace(effective) ? null : Path.GetFullPath(effective.Trim());
        lock (Gate)
        {
            if (string.Equals(_cachedClipsRoot, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _cachedClipsRoot = normalized;
            if (normalized is null)
            {
                _names = new Dictionary<int, string>();
                return;
            }

            var xml = SpritesXmlParser.ResolveSpritesXmlPath(normalized);
            _names = xml is null
                ? new Dictionary<int, string>()
                : SpritesXmlParser.ParseFile(xml).Names;
        }
    }
}
