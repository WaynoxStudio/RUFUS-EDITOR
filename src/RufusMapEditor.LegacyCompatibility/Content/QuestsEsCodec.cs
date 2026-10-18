using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>BD mision_etapas.recompensas ↔ SWF Q.s[].r and Q.o[].p encoding.</summary>
public static class QuestsEsCodec
{
    public const int RewardSlotCount = 6;

    /// <summary>SWF order: r[0]=oficios … r[3]=objetos [[qty,itemId],…] r[4]=kamas r[5]=exp.</summary>
    public static object?[] EncodeRewards(MissionRewardsDraft rewards)
    {
        if (IsEffectivelyEmpty(rewards))
            return new object?[RewardSlotCount];

        var bd = rewards.ToRaw().Split('|');
        while (bd.Length < RewardSlotCount)
            Array.Resize(ref bd, RewardSlotCount);

        var exp = ParseIntOrZero(bd[0]);
        var kamas = ParseIntOrZero(bd[1]);
        var objetos = EncodeObjetosSlot(bd[2]);
        var emotes = EncodeTokenSlot(bd[3]);
        var hechizos = EncodeTokenSlot(bd[4]);
        var oficios = EncodeTokenSlot(bd[5]);

        return new object?[] { oficios, hechizos, emotes, objetos, kamas, exp };
    }

    public static object? EncodeObjetosSlot(string? token)
    {
        if (IsNullToken(token))
            return null;
        var pairs = new List<int[]>();
        foreach (var chunk in token!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = chunk.Split(',');
            if (pair.Length >= 2
                && int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
            {
                pairs.Add(new[] { qty, itemId });
            }
        }

        if (pairs.Count == 0)
            return null;
        return pairs.Select(p => (object?)p).ToArray();
    }

    public static object? EncodeTokenSlot(string? token) =>
        IsNullToken(token) ? null : token!.Trim();

    public static bool IsEmptyRewards(object?[] slots)
    {
        if (slots.Length != RewardSlotCount)
            return false;
        return slots.All(s => s is null);
    }

    public static object?[] EncodeObjectiveParameters(MissionObjectiveDraft objective)
    {
        var (core, x, y) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        _ = x;
        _ = y;
        var detalle = StripJsonishQuotes(objective.Detalle);

        return objective.Tipo switch
        {
            MissionObjectiveTypes.Manual =>
                string.IsNullOrWhiteSpace(detalle) && string.IsNullOrWhiteSpace(core)
                    ? Array.Empty<object?>()
                    : new object?[] { string.IsNullOrWhiteSpace(detalle) ? StripJsonishQuotes(core) : detalle },
            MissionObjectiveTypes.TalkToNpc or MissionObjectiveTypes.ReturnToNpc =>
                BuildNpcParam(core),
            MissionObjectiveTypes.ShowItemToNpc or MissionObjectiveTypes.DeliverItemsToNpc =>
                ReverseTriple(core),
            MissionObjectiveTypes.DefeatMobs or MissionObjectiveTypes.DefeatMobAlt =>
                ReversePair(core),
            MissionObjectiveTypes.DiscoverMap or MissionObjectiveTypes.DiscoverArea =>
                BuildDiscoverParam(core, detalle),
            MissionObjectiveTypes.UseItem or MissionObjectiveTypes.ReachLevel
                or MissionObjectiveTypes.HaveSpells =>
                BuildSingleFromBracket(core),
            MissionObjectiveTypes.JobLevel =>
                ReversePair(core),
            _ => BuildGenericParam(core, detalle),
        };
    }

    public static (int? X, int? Y) ReadObjectiveCoords(MissionObjectiveDraft objective)
    {
        var (_, x, y) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        return (x, y);
    }

    public static object EncodeStageDescription(string description)
    {
        var text = description.Trim();
        if (text.Length > 0
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && text == n.ToString(CultureInfo.InvariantCulture))
            return n;
        return DialogEsLatin1.Prepare(text, "Q.s[].d");
    }

    public static string FormatRewards(IReadOnlyList<object?> rewards)
    {
        if (rewards.Count == 0)
            return "[]";
        var sb = new StringBuilder("[");
        for (var i = 0; i < rewards.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(FormatValue(rewards[i]));
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "null",
        int n => n.ToString(CultureInfo.InvariantCulture),
        string s => $"\"{s}\"",
        int[] arr => $"[{string.Join(",", arr)}]",
        object[] arr => $"[{string.Join(",", arr.Select(FormatValue))}]",
        IReadOnlyList<object?> list => $"[{string.Join(",", list.Select(FormatValue))}]",
        _ => value.ToString() ?? "?",
    };

    private static object?[] BuildNpcParam(string core)
    {
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        return nums.Length >= 1 ? new object?[] { nums[0] } : Array.Empty<object?>();
    }

    private static object?[] ReverseTriple(string core)
    {
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length < 3)
            return nums.Length == 0 ? Array.Empty<object?>() : nums.Cast<object?>().ToArray();
        return new object?[] { nums[2], nums[1], nums[0] };
    }

    private static object?[] ReversePair(string core)
    {
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length < 2)
            return nums.Length == 0 ? Array.Empty<object?>() : nums.Cast<object?>().ToArray();
        return new object?[] { nums[1], nums[0] };
    }

    private static object?[] BuildDiscoverParam(string core, string detalle)
    {
        _ = detalle; // Q.t[4]="Descubre el mapa: #1" — #1 comes from p[0] technical arg, not Detail.
        var trimmed = core.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapOrAreaId))
            return new object?[] { mapOrAreaId };
        if (!string.IsNullOrWhiteSpace(trimmed))
            return new object?[] { trimmed };
        return Array.Empty<object?>();
    }

    private static object?[] BuildSingleFromBracket(string core)
    {
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length == 0)
            return Array.Empty<object?>();
        return new object?[] { nums[0] };
    }

    private static object?[] BuildGenericParam(string core, string detalle)
    {
        if (!string.IsNullOrWhiteSpace(detalle))
            return new object?[] { detalle.Trim() };
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length >= 2)
            return new object?[] { nums[^1], nums[^2] };
        if (nums.Length == 1)
            return new object?[] { nums[0] };
        if (!string.IsNullOrWhiteSpace(core))
            return new object?[] { core.Trim() };
        return Array.Empty<object?>();
    }

    public static string StripJsonishQuotes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var s = text.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        return s;
    }

    private static int ParseIntOrZero(string? token) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static bool IsNullToken(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static bool IsEffectivelyEmpty(MissionRewardsDraft rewards) =>
        rewards.Exp == 0
        && rewards.Kamas == 0
        && rewards.Objetos.Count == 0
        && IsNullToken(rewards.Emotes)
        && IsNullToken(rewards.Hechizos)
        && IsNullToken(rewards.Oficios);
}
