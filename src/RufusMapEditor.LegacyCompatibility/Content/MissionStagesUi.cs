using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MISSIONS.STAGES.1 — stage/objective/rewards UI helpers (no BD).</summary>
public static class MissionStagesUi
{
    public static bool IsObjectiveConfigured(MissionObjectiveDraft objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (!string.IsNullOrWhiteSpace(objective.Condicion))
            return true;
        if (objective.Tipo == MissionObjectiveTypes.Manual)
            return !string.IsNullOrWhiteSpace(objective.Detalle) || !string.IsNullOrWhiteSpace(objective.Args);
        return !string.IsNullOrWhiteSpace(objective.Args);
    }

    public static string BuildStageTechnicalPreview(MissionStageDraft stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var objIds = stage.Objectives.Count == 0
            ? "(ninguno)"
            : string.Join(", ", stage.Objectives.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture,
            $"ID etapa: {stage.Id}\n" +
            $"objetivos: {stage.BuildObjetivosField()}  → ids [{objIds}]\n" +
            $"recompensas: {stage.Rewards.ToRaw()}");
    }

    public static string BuildMissionStagesCsv(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return mission.BuildEtapasCsv();
    }
}
