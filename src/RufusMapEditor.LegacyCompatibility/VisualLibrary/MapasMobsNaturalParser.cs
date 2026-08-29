namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.4.5 — parse <c>mapas.mobs</c> for display (READ-ONLY audit rules from s-h.jar Mapa.mobPosibles).
/// Separator: <c>|</c>. Token: <c>mobId</c> or <c>mobId,minLvl,maxLvl,cantidad,probabilidad</c>.
/// Server skips duplicate mob IDs when loading.
/// </summary>
public static class MapasMobsNaturalParser
{
    public readonly record struct Token(
        int MobId,
        int? MinLvl,
        int? MaxLvl,
        int? Cantidad,
        int? Probabilidad,
        string Raw,
        bool HasExtendedFields);

    public static IReadOnlyList<Token> Parse(string? mobs)
    {
        if (string.IsNullOrWhiteSpace(mobs))
            return Array.Empty<Token>();

        var list = new List<Token>();
        foreach (var part in mobs.Split('|', StringSplitOptions.None))
        {
            var raw = part ?? "";
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var fields = raw.Split(',');
            if (!int.TryParse(fields[0].Trim(), out var mobId))
            {
                list.Add(new Token(0, null, null, null, null, raw, false));
                continue;
            }

            int? min = null, max = null, cant = null, prob = null;
            var extended = false;
            if (fields.Length > 1)
            {
                extended = true;
                if (fields.Length > 1 && int.TryParse(fields[1].Trim(), out var a)) min = a;
                if (fields.Length > 2 && int.TryParse(fields[2].Trim(), out var b)) max = b;
                if (fields.Length > 3 && int.TryParse(fields[3].Trim(), out var c)) cant = c;
                if (fields.Length > 4 && int.TryParse(fields[4].Trim(), out var d)) prob = d;
            }

            list.Add(new Token(mobId, min, max, cant, prob, raw.Trim(), extended));
        }

        return list;
    }

    /// <summary>Build simple pipe list of mob IDs (no extended fields). Local config only — not written yet.</summary>
    public static string BuildSimple(IEnumerable<int> mobIds) =>
        string.Join("|", mobIds.Where(id => id > 0));
}
