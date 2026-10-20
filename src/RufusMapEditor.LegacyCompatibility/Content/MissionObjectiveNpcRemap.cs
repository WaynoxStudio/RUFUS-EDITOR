using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// NPC_ID_SYNC.FIX.7 — remap first bracket NPC id in objective args when provisional → final.
/// </summary>
public static class MissionObjectiveNpcRemap
{
    /// <summary>
    /// Remaps leading npcId inside <c>[npc…]</c> args (optional <c>, x: N, y: M</c> preserved).
    /// </summary>
    public static string RemapLeadingNpc(string args, IReadOnlyDictionary<int, int> npcIdMap)
    {
        if (string.IsNullOrWhiteSpace(args) || npcIdMap.Count == 0)
            return args;

        var (core, x, y) = MissionObjectiveArgsCodec.StripCoords(args);
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length < 1)
            return args;
        if (!npcIdMap.TryGetValue(nums[0], out var mapped) || mapped == nums[0])
            return args;

        nums[0] = mapped;
        var rebuilt = nums.Length == 1
            ? string.Create(CultureInfo.InvariantCulture, $"[{nums[0]}]")
            : string.Create(CultureInfo.InvariantCulture, $"[{string.Join(",", nums)}]");
        return MissionObjectiveArgsCodec.AppendCoords(rebuilt, x, y);
    }

    public static bool TryReadLeadingNpc(string args, out int npcId)
    {
        npcId = 0;
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(
            MissionObjectiveArgsCodec.StripCoords(args).Core);
        if (nums.Length < 1 || nums[0] <= 0)
            return false;
        npcId = nums[0];
        return true;
    }

    public static bool UsesNpcInArgs(int tipo) =>
        tipo is MissionObjectiveTypes.TalkToNpc
            or MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc
            or MissionObjectiveTypes.ReturnToNpc;
}
