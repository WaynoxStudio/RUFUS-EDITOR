using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// ADMIN.UI.4B — builds/parses <c>mision_objetivos.args</c> for server format (not quests_es Q.o.p).
/// Coordinates append as <c>, x: N, y: M</c> when required by types 1/2/3/6/7/9.
/// </summary>
public static class MissionObjectiveArgsCodec
{
    private static readonly Regex CoordTail = new(
        @"\s*,\s*x\s*:\s*(-?\d+)\s*,\s*y\s*:\s*(-?\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string BuildManual(string description) =>
        string.IsNullOrWhiteSpace(description) ? "" : "";

    public static string BuildTalk(int npcId, int? x, int? y) =>
        AppendCoords(string.Create(CultureInfo.InvariantCulture, $"[{npcId}]"), x, y);

    public static string BuildShowOrDeliver(int npcId, int itemId, int qty, int? x, int? y) =>
        AppendCoords(
            string.Create(CultureInfo.InvariantCulture, $"[{npcId},{itemId},{qty}]"),
            x, y);

    public static string BuildDiscoverMap(int mapId) =>
        mapId.ToString(CultureInfo.InvariantCulture);

    public static string BuildDiscoverArea(int areaId) =>
        areaId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Server format: pairs mobId,cantidad inside one bracket list.</summary>
    public static string BuildDefeatMobs(IReadOnlyList<(int MobId, int Qty)> mobs, int? x, int? y)
    {
        if (mobs.Count == 0)
            return AppendCoords("[]", x, y);
        var inner = string.Join(",", mobs.Select(m =>
            string.Create(CultureInfo.InvariantCulture, $"{m.MobId},{m.Qty}")));
        return AppendCoords($"[{inner}]", x, y);
    }

    public static string BuildUseItem(int itemId) =>
        string.Create(CultureInfo.InvariantCulture, $"[{itemId}]");

    public static string BuildReachLevel(int level) =>
        string.Create(CultureInfo.InvariantCulture, $"[{level}]");

    public static string BuildHaveSpells(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"[{count}]");

    public static string BuildJobLevel(int jobCount, int level) =>
        string.Create(CultureInfo.InvariantCulture, $"[{jobCount},{level}]");

    public static string SuggestDetalle(int tipo, string args, string? manualText = null)
    {
        if (tipo == MissionObjectiveTypes.Manual)
            return string.IsNullOrWhiteSpace(manualText) ? "" : manualText.Trim();

        var (core, _, _) = StripCoords(args);
        var nums = ParseBracketInts(core);
        return tipo switch
        {
            MissionObjectiveTypes.TalkToNpc when nums.Length >= 1 =>
                string.Create(CultureInfo.InvariantCulture, $"Habla con NPC #{nums[0]}."),
            MissionObjectiveTypes.ShowItemToNpc when nums.Length >= 3 =>
                string.Create(CultureInfo.InvariantCulture, $"Enseña objeto #{nums[1]} x{nums[2]} a NPC #{nums[0]}."),
            MissionObjectiveTypes.DeliverItemsToNpc when nums.Length >= 3 =>
                string.Create(CultureInfo.InvariantCulture, $"Entrega objeto #{nums[1]} x{nums[2]} a NPC #{nums[0]}."),
            MissionObjectiveTypes.DiscoverMap =>
                int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId)
                    ? string.Create(CultureInfo.InvariantCulture, $"Descubre el mapa #{mapId}.")
                    : "Descubre un mapa.",
            MissionObjectiveTypes.DiscoverArea =>
                int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var areaId)
                    ? string.Create(CultureInfo.InvariantCulture, $"Descubre la zona #{areaId}.")
                    : "Descubre una zona.",
            MissionObjectiveTypes.DefeatMobs when nums.Length >= 2 =>
                string.Create(CultureInfo.InvariantCulture, $"Vence monstruo #{nums[0]} x{nums[1]}."),
            MissionObjectiveTypes.UseItem when nums.Length >= 1 =>
                string.Create(CultureInfo.InvariantCulture, $"Utiliza el objeto #{nums[0]}."),
            MissionObjectiveTypes.ReturnToNpc when nums.Length >= 1 =>
                string.Create(CultureInfo.InvariantCulture, $"Vuelve a ver a NPC #{nums[0]}."),
            MissionObjectiveTypes.ReachLevel when nums.Length >= 1 =>
                string.Create(CultureInfo.InvariantCulture, $"Alcanza el nivel {nums[0]}."),
            MissionObjectiveTypes.HaveSpells when nums.Length >= 1 =>
                string.Create(CultureInfo.InvariantCulture, $"Ten al menos {nums[0]} hechizo(s)."),
            MissionObjectiveTypes.JobLevel when nums.Length >= 2 =>
                string.Create(CultureInfo.InvariantCulture, $"Ten {nums[0]} oficio(s) a nivel {nums[1]}."),
            _ => string.IsNullOrWhiteSpace(args) ? "" : args.Trim(),
        };
    }

    public static string AppendCoords(string core, int? x, int? y)
    {
        if (x is null || y is null)
            return core;
        return string.Create(CultureInfo.InvariantCulture, $"{core}, x: {x.Value}, y: {y.Value}");
    }

    public static (string Core, int? X, int? Y) StripCoords(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return ("", null, null);
        var m = CoordTail.Match(args);
        if (!m.Success)
            return (args.Trim(), null, null);
        var core = args[..m.Index].Trim();
        var x = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var y = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return (core, x, y);
    }

    public static int[] ParseBracketInts(string? core)
    {
        if (string.IsNullOrWhiteSpace(core))
            return [];
        var s = core.Trim();
        if (s.StartsWith('[') && s.EndsWith(']'))
            s = s[1..^1];
        if (string.IsNullOrWhiteSpace(s))
            return [];
        var list = new List<int>();
        foreach (var part in s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                list.Add(n);
        }

        return list.ToArray();
    }

    public static string FormatBracket(params int[] values)
    {
        if (values.Length == 0) return "[]";
        var sb = new StringBuilder("[");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }
}
