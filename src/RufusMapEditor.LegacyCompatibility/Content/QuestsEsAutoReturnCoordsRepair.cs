using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Explicit repair for published Q.o[4663] / Quest 100005.
/// NPC_ID_SYNC.FIX.7 — StartNpc confirmed 20066 (acción 44 / preg 20028); never cross-fallback coords.
/// </summary>
public static class QuestsEsAutoReturnCoordsRepair
{
    public const int QuestId = 100005;
    public const int StageId = 5502;
    public const int ObjectiveId = 4663;
    /// <summary>Confirmed StartNpc / accion 44 owner for Quest 100005.</summary>
    public const int CorrectNpcId = 20066;
    /// <summary>Stale provisional id that leaked into Q.o[4663].p.</summary>
    public const int StaleNpcId = 20067;

    public const string CoordsUnavailableMessage =
        "No se pudieron obtener las coordenadas X/Y del mapa del NPC.";

    public const string NpcMismatchMessage =
        "El NPC del auto-return no coincide con el NPC inicial de la misión.";

    public sealed class PreviewInfo
    {
        public int QuestId { get; init; }
        public int StageId { get; init; }
        public int ObjectiveId { get; init; }
        public int NpcIdCurrent { get; init; }
        public int NpcIdCorrect { get; init; }
        public int NpcMapId { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public bool BdNeedsUpdate { get; init; }
        public string BdArgsCurrent { get; init; } = "";
        public string BdArgsPredicted { get; init; } = "";
        public required string ActualLiteral { get; init; }
        public required string PredictedLiteral { get; init; }
    }

    /// <summary>
    /// Forces objective 4663 → StartNpcId (20066) + mapas.X/Y of THAT same NPC only.
    /// </summary>
    public static PreviewInfo? TryPatchDraft(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot? snapshot,
        Func<int, (int X, int Y)?>? mapWorldLookup,
        Func<int, int?>? bdNpcMapIdLookup = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (mapWorldLookup is null)
            return null;

        var mission = workspace.Missions.Missions.FirstOrDefault(m =>
            m.PublishedQuestId == QuestId
            && m.PublishedBd
            && m.QuestsEsPublished);
        if (mission is null)
            return null;

        if (mission.StartNpcId is not int startNpc || startNpc <= 0)
            return null;

        var stage = mission.Stages.FirstOrDefault(s => s.Id == StageId);
        var objective = stage?.Objectives.FirstOrDefault(o => o.Id == ObjectiveId);
        if (stage is null || objective is null)
            return null;

        if (objective.Tipo != MissionObjectiveTypes.ReturnToNpc)
            return null;

        MissionObjectiveNpcRemap.TryReadLeadingNpc(objective.Args, out var currentNpc);
        if (currentNpc <= 0)
            currentNpc = StaleNpcId;

        // Same NPC for p[] and MapId/X/Y — StartNpc only (no cross-fallback).
        var correctNpc = startNpc;
        var mapId = TryResolveMapIdForNpc(correctNpc, workspace.Npcs.Drafts, bdNpcMapIdLookup);
        if (mapId is null || mapId <= 0)
            return null;

        var coords = mapWorldLookup(mapId.Value);
        if (coords is null)
            return null;

        var (x, y) = coords.Value;
        var actual = FormatQo(snapshot, ObjectiveId);
        var argsBefore = objective.Args ?? "";
        MissionAutoReturnSync.ApplyReturnNpc(objective, correctNpc, x, y);
        objective.IsAutoReturnToStartNpc = true;

        var predictedArgs = objective.Args ?? "";
        var predicted = string.Create(
            CultureInfo.InvariantCulture,
            $"Q.o[{ObjectiveId}] = {{ t: 9, p: [{correctNpc}], x: {x}, y: {y} }}");

        return new PreviewInfo
        {
            QuestId = QuestId,
            StageId = StageId,
            ObjectiveId = ObjectiveId,
            NpcIdCurrent = currentNpc,
            NpcIdCorrect = correctNpc,
            NpcMapId = mapId.Value,
            X = x,
            Y = y,
            BdNeedsUpdate = !string.Equals(
                MissionObjectiveArgsCodec.StripCoords(argsBefore).Core.Trim(),
                MissionObjectiveArgsCodec.StripCoords(predictedArgs).Core.Trim(),
                StringComparison.Ordinal),
            BdArgsCurrent = argsBefore,
            BdArgsPredicted = predictedArgs,
            ActualLiteral = actual,
            PredictedLiteral = predicted,
        };
    }

    /// <summary>MapId only for the given npcId — never another NPC.</summary>
    public static int? TryResolveMapIdForNpc(
        int npcId,
        IEnumerable<NpcsModeloDraft> drafts,
        Func<int, int?>? bdNpcMapIdLookup)
    {
        if (npcId <= 0) return null;

        var bd = bdNpcMapIdLookup?.Invoke(npcId);
        if (bd is > 0)
            return bd;

        foreach (var npc in drafts)
        {
            if (npc.Id != npcId) continue;
            foreach (var loc in npc.Locations)
            {
                if (loc.MapId > 0)
                    return loc.MapId;
            }
        }

        return null;
    }

    private static string FormatQo(QuestsEsSnapshot? snapshot, int objectiveId)
    {
        if (snapshot is null || !snapshot.Objectives.TryGetValue(objectiveId, out var remote))
            return string.Create(CultureInfo.InvariantCulture, $"Q.o[{objectiveId}] = {{t:9,p:[…]}}");

        var p = QuestsEsCodec.FormatValue(remote.Parameters.ToArray());
        var xy = remote.X is int xi && remote.Y is int yi
            ? string.Create(CultureInfo.InvariantCulture, $", x: {xi}, y: {yi}")
            : "";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Q.o[{objectiveId}] = {{ t: {remote.Type}, p: {p}{xy} }}");
    }
}
