using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// MISSIONS.OBJECTIVES.AUTO_RETURN_NPC / AUDIT_FIX.4 —
/// Ensure one final tipo-9 auto objective when a stage has DiscoverMap (tipo 4),
/// targeting <see cref="MissionDraft.StartNpcId"/> with world coords from mapas.X/Y.
/// Evidence: Q.t[9]="Vuelve a ver a #1"; real t9 e.g. [143], x: 24, y: -6 (= mapa 829).
/// Only for unpublished drafts. Never mutates published missions.
/// Without confirmed X/Y: does not create/keep auto-return (DATO PENDIENTE).
/// </summary>
public static class MissionAutoReturnSync
{
    public const string PendingCoordsMessage =
        QuestsEsAutoReturnCoordsRepair.CoordsUnavailableMessage;

    /// <summary>Published identity — auto-return must not run (including Cambios pendientes).</summary>
    public static bool IsPublishedLocked(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return mission.PublishedBd || mission.PublishedQuestId is > 0;
    }

    /// <summary>True when stage has map objectives but StartNpcId is missing (UI pending).</summary>
    public static bool NeedsStartNpcForAutoReturn(MissionDraft mission, MissionStageDraft stage)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(stage);
        if (IsPublishedLocked(mission)) return false;
        if (!HasDiscoverMap(stage)) return false;
        return mission.StartNpcId is not int sid || sid <= 0;
    }

    /// <summary>True when map+NPC exist but world X/Y could not be resolved.</summary>
    public static bool NeedsCoordsForAutoReturn(
        MissionDraft mission,
        MissionStageDraft stage,
        Func<int, (int X, int Y)?>? resolveNpcWorldCoords)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(stage);
        if (IsPublishedLocked(mission)) return false;
        if (!HasDiscoverMap(stage)) return false;
        if (mission.StartNpcId is not int sid || sid <= 0) return false;
        return resolveNpcWorldCoords?.Invoke(sid) is null;
    }

    public static bool HasDiscoverMap(MissionStageDraft stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Objectives.Any(o =>
            !o.IsAutoReturnToStartNpc && o.Tipo == MissionObjectiveTypes.DiscoverMap);
    }

    public static MissionObjectiveDraft? FindAutoReturn(MissionStageDraft stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Objectives.FirstOrDefault(o => o.IsAutoReturnToStartNpc);
    }

    /// <summary>
    /// NPC → MapId (draft locations, then optional BD npcs_ubicacion.mapa) → mapas.X/Y.
    /// Cell ID is never used.
    /// </summary>
    public static (int X, int Y)? TryResolveNpcWorldCoords(
        int npcId,
        IEnumerable<NpcsModeloDraft> npcDrafts,
        Func<int, (int X, int Y)?>? mapWorldLookup,
        Func<int, int?>? bdNpcMapIdLookup = null)
    {
        if (npcId <= 0 || mapWorldLookup is null)
            return null;

        var mapId = QuestsEsAutoReturnCoordsRepair.TryResolveMapIdForNpc(
            npcId, npcDrafts, bdNpcMapIdLookup);
        if (mapId is null || mapId <= 0)
            return null;

        return mapWorldLookup(mapId.Value);
    }

    public static bool SyncMission(
        MissionDraft mission,
        Func<int> nextObjectiveId,
        Func<int, (int X, int Y)?>? resolveNpcWorldCoords = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(nextObjectiveId);
        if (IsPublishedLocked(mission))
            return StripLocalOnlyAutoReturns(mission);

        var changed = false;
        foreach (var stage in mission.Stages)
            changed |= SyncStage(mission, stage, nextObjectiveId, resolveNpcWorldCoords);
        return changed;
    }

    public static bool SyncStage(
        MissionDraft mission,
        MissionStageDraft stage,
        Func<int> nextObjectiveId,
        Func<int, (int X, int Y)?>? resolveNpcWorldCoords = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(nextObjectiveId);
        if (IsPublishedLocked(mission))
            return false;

        var changed = false;
        var autos = stage.Objectives.Where(o => o.IsAutoReturnToStartNpc).ToList();
        var hasMap = HasDiscoverMap(stage);

        if (!hasMap)
        {
            foreach (var a in autos)
            {
                stage.Objectives.Remove(a);
                changed = true;
            }

            return changed;
        }

        if (mission.StartNpcId is not int npcId || npcId <= 0)
        {
            foreach (var a in autos)
            {
                stage.Objectives.Remove(a);
                changed = true;
            }

            return changed;
        }

        var coords = resolveNpcWorldCoords?.Invoke(npcId);
        if (coords is null)
        {
            // AUDIT_FIX.4 — no incomplete Q.o without mapas.X/Y.
            foreach (var a in autos)
            {
                stage.Objectives.Remove(a);
                changed = true;
            }

            return changed;
        }

        var (x, y) = coords.Value;

        MissionObjectiveDraft? keep = null;
        foreach (var a in autos)
        {
            if (keep is null)
            {
                keep = a;
                continue;
            }

            stage.Objectives.Remove(a);
            changed = true;
        }

        if (keep is null)
        {
            keep = new MissionObjectiveDraft
            {
                Id = nextObjectiveId(),
                Tipo = MissionObjectiveTypes.ReturnToNpc,
                IsAutoReturnToStartNpc = true,
                EsAlHablar = "0",
                EsOculto = 0,
                Condicion = "",
            };
            ApplyReturnNpc(keep, npcId, x, y);
            stage.Objectives.Add(keep);
            changed = true;
        }
        else
        {
            if (keep.Tipo != MissionObjectiveTypes.ReturnToNpc)
            {
                keep.Tipo = MissionObjectiveTypes.ReturnToNpc;
                changed = true;
            }

            var beforeArgs = keep.Args;
            var beforeDetalle = keep.Detalle;
            ApplyReturnNpc(keep, npcId, x, y);
            if (!string.Equals(beforeArgs, keep.Args, StringComparison.Ordinal)
                || !string.Equals(beforeDetalle, keep.Detalle, StringComparison.Ordinal))
                changed = true;
        }

        var last = stage.Objectives.Count > 0 ? stage.Objectives[^1] : null;
        if (!ReferenceEquals(last, keep))
        {
            stage.Objectives.Remove(keep);
            stage.Objectives.Add(keep);
            changed = true;
        }

        return changed;
    }

    public static bool StripLocalOnlyAutoReturns(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (!IsPublishedLocked(mission))
            return false;

        var changed = false;
        foreach (var stage in mission.Stages)
        {
            for (var i = stage.Objectives.Count - 1; i >= 0; i--)
            {
                var o = stage.Objectives[i];
                if (!o.IsAutoReturnToStartNpc)
                    continue;
                if (mission.IsPublishedObjective(o.Id))
                    continue;
                stage.Objectives.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    public static void ApplyReturnNpc(MissionObjectiveDraft objective, int npcId, int? x = null, int? y = null)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        var args = MissionObjectiveArgsCodec.BuildTalk(npcId, x, y);
        objective.Args = args;
        // Cont41 / real BD: detalle mirrors args ([npcId] or [npcId], x: N, y: M).
        objective.Detalle = args;
        objective.EsAlHablar = "0";
        objective.EsOculto = 0;
    }

    public static string FormatAutoReturnLabel(int? startNpcId, Func<int?, string>? npcName)
    {
        if (startNpcId is not int sid || sid <= 0)
            return "DATO PENDIENTE DE CONFIRMAR · NPC inicial";

        var name = npcName?.Invoke(sid)?.Trim();
        if (!string.IsNullOrWhiteSpace(name)
            && !name.StartsWith("NPC #", StringComparison.Ordinal)
            && !string.Equals(name, sid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return "Vuelve con " + name;

        return string.Create(CultureInfo.InvariantCulture, $"Vuelve con NPC #{sid}");
    }
}
