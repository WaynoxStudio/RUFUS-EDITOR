using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// DIALOG.AI.FULL_MISSION_CONTEXT.1 — real mission draft snapshot for AI prompts (creative only).
/// Reuses <see cref="MissionObjectiveUiSync.UiTypeLabel"/> and existing Detalle/SuggestDetalle.
/// Does not invent map/NPC/item names.
/// </summary>
public static class AiMissionSnapshotBuilder
{
    public const string AntiInventionRule =
        "Los objetivos, requisitos y recompensas de la misión ya están definidos. " +
        "No inventes objetivos adicionales, objetos, enemigos, lugares, cantidades, " +
        "pruebas ni requisitos que no aparezcan en los datos proporcionados.";

    public const string NarrativeVsMissionRule =
        "El contexto narrativo describe el tono y la situación, pero NO puede modificar " +
        "los objetivos técnicos reales de la misión. La misión real tiene prioridad.";

    public const string NewlineGuidance =
        "Saltos de línea: puedes usar saltos de línea reales en el texto (en JSON: \\n). " +
        "Usa un salto (\\n) para cambiar de línea y dos (\\n\\n) solo para separar párrafos. " +
        "No uses /n.";

    public static string Build(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        MissionDraft? mission)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var sb = new StringBuilder();
        sb.AppendLine("=== SNAPSHOT REAL DE LA MISIÓN (prioridad sobre el tono) ===");
        sb.AppendLine();
        sb.AppendLine(AntiInventionRule);
        sb.AppendLine(NarrativeVsMissionRule);
        sb.AppendLine();

        if (mission is null)
        {
            sb.AppendLine("MISIÓN: (sin misión vinculada en el borrador)");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("MISIÓN:");
        sb.AppendLine("- Nombre: " + Blank(mission.Nombre));
        sb.AppendLine();

        if (mission.Stages.Count == 0)
        {
            sb.AppendLine("ETAPAS: (ninguna)");
        }
        else
        {
            sb.AppendLine("ETAPA(S):");
            var stageIndex = 1;
            foreach (var stage in mission.Stages)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"- Etapa {stageIndex}: {Blank(stage.Nombre)}"));
                if (!string.IsNullOrWhiteSpace(stage.Descripcion))
                    sb.AppendLine("  Descripción: " + stage.Descripcion.Trim());

                if (stage.Objectives.Count == 0)
                {
                    sb.AppendLine("  Objetivos: (ninguno)");
                }
                else
                {
                    sb.AppendLine("  Objetivos reales:");
                    var objIndex = 1;
                    foreach (var o in stage.Objectives)
                    {
                        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                            $"  {objIndex}. Tipo: {MissionObjectiveUiSync.UiTypeLabel(o.Tipo)}"));
                        var detail = FormatObjectiveDetail(o);
                        if (!string.IsNullOrWhiteSpace(detail))
                            sb.AppendLine("     Detalle: " + detail);
                        sb.AppendLine(
                            "     Nota: si el detalle cita un #ID, es referencia técnica; " +
                            "NO inventar nombres narrativos (mapa, NPC, objeto, monstruo). " +
                            "Hablar de «el lugar indicado», «esa zona», etc. si no hay nombre confirmado.");
                        objIndex++;
                    }
                }

                AppendRewardsIfUseful(sb, stage);
                stageIndex++;
            }
        }

        sb.AppendLine();
        sb.AppendLine("ROLES DE DIÁLOGO DE MISIÓN:");
        sb.AppendLine("- OFRECER → pregunta #" + FmtId(mission.PregDarPreguntaId));
        sb.AppendLine("- INCOMPLETA → pregunta #" + FmtId(mission.PregIncompletaPreguntaId));
        sb.AppendLine("- COMPLETADA → pregunta #" + FmtId(mission.PregCompletadaPreguntaId));
        sb.AppendLine(AiMissionRoleContextBuilder.MeaningOffer);
        sb.AppendLine(AiMissionRoleContextBuilder.MeaningIncomplete);
        sb.AppendLine(AiMissionRoleContextBuilder.MeaningComplete);

        return sb.ToString().TrimEnd();
    }

    private static string FormatObjectiveDetail(MissionObjectiveDraft o)
    {
        if (!string.IsNullOrWhiteSpace(o.Detalle))
            return o.Detalle.Trim();
        var suggested = MissionObjectiveArgsCodec.SuggestDetalle(o.Tipo, o.Args ?? "");
        return string.IsNullOrWhiteSpace(suggested) ? "" : suggested.Trim();
    }

    private static void AppendRewardsIfUseful(StringBuilder sb, MissionStageDraft stage)
    {
        var r = stage.Rewards;
        if (r is null)
            return;

        var parts = new List<string>();
        if (r.Exp > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"XP {r.Exp}"));
        if (r.Kamas > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Kamas {r.Kamas}"));
        if (r.Objetos is { Count: > 0 })
        {
            foreach (var item in r.Objetos.Where(i => i.ItemId > 0))
                parts.Add(string.Create(CultureInfo.InvariantCulture,
                    $"objeto #{item.ItemId} ×{Math.Max(1, item.Cantidad)}"));
        }

        if (parts.Count == 0)
            return;

        sb.AppendLine("  Recompensas configuradas (solo contexto; no inventar otras):");
        sb.AppendLine("  - " + string.Join("; ", parts));
    }

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(sin nombre)" : value.Trim();

    private static string FmtId(int? id) =>
        id is > 0 ? id.Value.ToString(CultureInfo.InvariantCulture) : "(no configurado)";
}
