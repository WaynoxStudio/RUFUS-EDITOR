using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// MISSIONS.OBJECTIVES.QUICK_DELETE_AND_START_NPC_BINDING.2 —
/// tipo 2/3 always target mission StartNpc (no manual NPC pick).
/// </summary>
public static class MissionShowDeliverNpcBinding
{
    public const string MissingStartNpcMessage =
        "Selecciona/configura primero el NPC de la misión.";

    public static bool IsBoundTipo(int tipo) =>
        tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc;

    public static bool SyncMission(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.StartNpcId is not int startNpc || startNpc <= 0)
            return false;

        var changed = false;
        foreach (var o in mission.Stages.SelectMany(s => s.Objectives))
        {
            if (SyncObjective(o, startNpc))
                changed = true;
        }

        return changed;
    }

    public static bool SyncObjective(MissionObjectiveDraft objective, int startNpcId)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (!IsBoundTipo(objective.Tipo) || startNpcId <= 0)
            return false;

        var next = RewriteArgsNpc(objective.Args, startNpcId);
        if (string.Equals(next, objective.Args, StringComparison.Ordinal))
            return false;

        if (string.Equals(objective.Detalle, objective.Args, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(objective.Detalle))
        {
            objective.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(
                objective.Tipo, next);
        }

        objective.Args = next;
        return true;
    }

    /// <summary>Replace leading NPC in show/deliver args; preserve item, qty, coords when present.</summary>
    public static string RewriteArgsNpc(string? args, int startNpcId)
    {
        if (startNpcId <= 0)
            return args ?? "";

        var (core, x, y) = MissionObjectiveArgsCodec.StripCoords(args);
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        if (nums.Length >= 3)
        {
            if (nums[0] == startNpcId)
                return args ?? "";
            return MissionObjectiveArgsCodec.BuildShowOrDeliver(startNpcId, nums[1], nums[2], x, y);
        }

        if (nums.Length >= 2)
        {
            if (nums[0] == startNpcId)
                return args ?? "";
            return MissionObjectiveArgsCodec.BuildShowOrDeliver(startNpcId, nums[1], 1, x, y);
        }

        if (nums.Length == 1 && nums[0] == startNpcId)
            return args ?? "";

        // Incomplete: only NPC known — keep shape minimal so UI can finish Item ID/qty.
        return MissionObjectiveArgsCodec.AppendCoords(
            string.Create(CultureInfo.InvariantCulture, $"[{startNpcId}]"), x, y);
    }
}
