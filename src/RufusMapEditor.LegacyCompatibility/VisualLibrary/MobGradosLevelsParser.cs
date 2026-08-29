using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.2 — extract only grade levels <c>l</c> from <c>mobs_modelo.grados</c>.
/// Does not interpret resistances or other grade fields.
/// </summary>
public static class MobGradosLevelsParser
{
    private static readonly Regex LevelToken = new(
        @"\bl\s*:\s*(-?\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<int> ParseLevels(string? grados)
    {
        if (string.IsNullOrWhiteSpace(grados))
            return Array.Empty<int>();

        var list = new List<int>(8);
        foreach (Match m in LevelToken.Matches(grados))
        {
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl))
                list.Add(lvl);
        }

        return list;
    }
}
