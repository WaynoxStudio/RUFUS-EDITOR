using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>
/// LIB.4 — build/parse confirmed <c>mobs_fix.mobs</c> formats only:
/// <c>mobId,minLvl,maxLvl</c> joined by <c>;</c> (max 8).
/// </summary>
public static class MobsFixGroupString
{
    public static string Build(IReadOnlyList<MobsFixSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count is < 1 or > MapMonsterGroupLimits.MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(slots), "Group must contain 1..8 slots.");

        var sb = new StringBuilder(slots.Count * 16);
        for (var i = 0; i < slots.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var s = slots[i];
            sb.Append(s.MobId.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(s.MinLvl.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(s.MaxLvl.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses only the confirmed LIB.4 write format (<c>id,min,max</c> segments).
    /// Returns false for empty, id-only, or any legacy/corrupt variant — caller must keep original.
    /// </summary>
    public static bool TryParseStrict(string? raw, out IReadOnlyList<MobsFixSlot> slots)
    {
        slots = Array.Empty<MobsFixSlot>();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split(';', StringSplitOptions.None);
        if (parts.Length is < 1 or > MapMonsterGroupLimits.MaxSlots)
            return false;

        var list = new List<MobsFixSlot>(parts.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                return false;

            var fields = part.Split(',', StringSplitOptions.None);
            if (fields.Length != 3)
                return false;

            if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobId)
                || mobId <= 0)
                return false;
            if (!int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
                return false;
            if (!int.TryParse(fields[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
                return false;
            if (min < 0 || max < 0 || min > max)
                return false;

            list.Add(new MobsFixSlot(mobId, min, max));
        }

        slots = list;
        return true;
    }

    public static bool IsStrictFormat(string? raw) => TryParseStrict(raw, out _);
}
