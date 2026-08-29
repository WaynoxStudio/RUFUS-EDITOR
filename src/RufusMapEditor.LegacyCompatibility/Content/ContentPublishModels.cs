using System.Globalization;
using System.Text.Json.Serialization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Live MAX(id) snapshot used to reserve publish IDs (CONT.5).</summary>
public sealed class ContentPublishMaxSnapshot
{
    public int NpcsModelo { get; init; }
    public int NpcPreguntas { get; init; }
    public int NpcRespuestas { get; init; }
    public int Misiones { get; init; }
    public int MisionEtapas { get; init; }
    public int MisionObjetivos { get; init; }
}

public sealed class ContentTableEngineInfo
{
    public required string Table { get; init; }
    public required string Engine { get; init; }
    public bool SupportsTransactions =>
        Engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase)
        || Engine.Equals("InnoDB Compact", StringComparison.OrdinalIgnoreCase);
}

public enum ContentPublishConcurrencyMode
{
    Transaction,
    TableLocks,
}

public sealed class ContentPublishPlan
{
    public ContentPublishMaxSnapshot Maxes { get; init; } = new();
    public ContentPublishConcurrencyMode ConcurrencyMode { get; set; }

    public Dictionary<int, int> NpcIdMap { get; } = new();
    public Dictionary<int, int> QuestionIdMap { get; } = new();
    public Dictionary<Guid, int> ResponseIdMap { get; } = new();
    public Dictionary<Guid, int> QuestIdMap { get; } = new();
    public Dictionary<int, int> StageIdMap { get; } = new();
    public Dictionary<int, int> ObjectiveIdMap { get; } = new();

    public List<NpcModeloInsertRow> Npcs { get; } = new();
    public List<NpcUbicacionInsertRow> Locations { get; } = new();
    public List<NpcPreguntaInsertRow> Questions { get; } = new();
    public List<NpcRespuestaInsertRow> ResponseActions { get; } = new();
    public List<MisionInsertRow> Missions { get; } = new();
    public List<MisionEtapaInsertRow> Stages { get; } = new();
    public List<MisionObjetivoInsertRow> Objectives { get; } = new();

    public List<string> Errors { get; } = new();

    /// <summary>CONT.6B — provisional dialog_es IDs (not definitive until a later publish re-reads SWF+BD).</summary>
    public bool DialogEsIdsAreProvisional { get; set; }
    public int? DialogEsSourceVersion { get; set; }
    public int? DialogEsTargetVersion { get; set; }
    public string? DialogEsCacheStatus { get; set; }
    public List<DialogEsPreviewLine> DialogEsPreview { get; } = new();
    public List<DialogEsAssignment> DialogEsAdditions { get; } = new();
    public bool HasPendingSimpleDialogEs => DialogEsPreview.Any(p => p.Kind == "simple");

    public bool IsValid => Errors.Count == 0 && HasWork;
    public bool HasWork =>
        Npcs.Count > 0 || Locations.Count > 0 || Questions.Count > 0
        || ResponseActions.Count > 0 || Missions.Count > 0
        || Stages.Count > 0 || Objectives.Count > 0;

    public int LogicalResponseCount => ResponseIdMap.Count;
    public int ResponseActionRowCount => ResponseActions.Count;

    public string FormatIdRange(IEnumerable<int> ids)
    {
        var list = ids.OrderBy(x => x).ToList();
        if (list.Count == 0) return "(ninguno)";
        if (list.Count == 1) return list[0].ToString(CultureInfo.InvariantCulture);
        return $"{list[0]} → {list[^1]} ({list.Count})";
    }

    public string FormatDialogEsPreviewBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("dialog_es (provisional — no definitivo):");
        if (!string.IsNullOrWhiteSpace(DialogEsCacheStatus))
            sb.AppendLine(DialogEsCacheStatus);
        if (DialogEsPreview.Count == 0)
        {
            sb.AppendLine("(sin altas dialog_es en este lote)");
            return sb.ToString();
        }

        foreach (var line in DialogEsPreview)
        {
            if (line.Kind == "simple")
            {
                sb.AppendLine();
                sb.AppendLine("DIÁLOGO SIMPLE");
                sb.AppendLine($"Texto nuevo: \"{line.Label}\"");
                sb.AppendLine($"ID D.q provisional: {line.DialogQuestionId?.ToString(CultureInfo.InvariantCulture) ?? "(n/d)"}");
                sb.AppendLine($"dialog_es actual: {DialogEsSourceVersion?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
                sb.AppendLine($"dialog_es previsto: {DialogEsTargetVersion?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
                sb.AppendLine("Tabla: npcs_modelo");
                sb.AppendLine("Columna: pregunta");
                sb.AppendLine(line.NpcPreguntaColumn?.ToString(CultureInfo.InvariantCulture) ?? "(n/d)");
            }
            else if (line.Kind == "simple-existing")
            {
                sb.AppendLine();
                sb.AppendLine("DIÁLOGO SIMPLE (ID existente)");
                sb.AppendLine($"Texto: \"{line.Label}\"");
                sb.AppendLine($"ID D.q: {line.DialogQuestionId?.ToString(CultureInfo.InvariantCulture) ?? "(n/d)"}");
                sb.AppendLine("Tabla: npcs_modelo");
                sb.AppendLine("Columna: pregunta");
                sb.AppendLine(line.NpcPreguntaColumn?.ToString(CultureInfo.InvariantCulture) ?? "(n/d)");
            }
            else if (line.Kind == "interactive-question")
            {
                sb.AppendLine();
                sb.AppendLine("Pregunta:");
                sb.AppendLine($"ID conjunto D.q / npc_preguntas: {line.DialogQuestionId}");
            }
            else if (line.Kind == "interactive-answer")
            {
                sb.AppendLine("Respuesta:");
                sb.AppendLine($"ID conjunto D.a / npc_respuestas: {line.DialogAnswerId}");
            }
        }

        if (HasPendingSimpleDialogEs)
        {
            sb.AppendLine();
            sb.AppendLine($"NPCs BD: {Npcs.Count}");
            sb.AppendLine("Preguntas BD: 0");
            sb.AppendLine("Respuestas BD: 0");
            sb.AppendLine();
            sb.AppendLine("Publicación BD:");
            sb.AppendLine("BLOQUEADA hasta que dialog_es sea publicado por la futura CONT.6C.");
        }

        return sb.ToString();
    }

    public IReadOnlyList<int> ReservedNpcIds => Npcs.Select(n => n.Id).ToList();
    public IReadOnlyList<int> ReservedQuestionIds => Questions.Select(q => q.Id).ToList();
    public IReadOnlyList<int> ReservedResponseIds => ResponseIdMap.Values.Distinct().OrderBy(x => x).ToList();
    public IReadOnlyList<int> ReservedQuestIds => Missions.Select(m => m.Id).ToList();
    public IReadOnlyList<int> ReservedStageIds => Stages.Select(s => s.Id).ToList();
    public IReadOnlyList<int> ReservedObjectiveIds => Objectives.Select(o => o.Id).ToList();
}

public sealed class NpcModeloInsertRow
{
    public int Id { get; init; }
    public int GfxId { get; init; }
    public int ScaleX { get; init; }
    public int ScaleY { get; init; }
    public int Sexo { get; init; }
    public int Color1 { get; init; }
    public int Color2 { get; init; }
    public int Color3 { get; init; }
    public string Accesorios { get; init; } = "";
    public int Foto { get; init; }
    public int Pregunta { get; init; }
    public string Ventas { get; init; } = "";
    public string Nombre { get; init; } = "";
    public int ObjetoCompra { get; init; }
    public int ProvisionalId { get; init; }
}

public sealed class NpcUbicacionInsertRow
{
    public int Mapa { get; init; }
    public int Celda { get; init; }
    public int Npc { get; init; }
    public int Orientacion { get; init; }
    public string Nombre { get; init; } = "";
    public string Condicion { get; init; } = "";
}

public sealed class NpcPreguntaInsertRow
{
    public int Id { get; init; }
    public string Respuestas { get; init; } = "";
    public string Params { get; init; } = "";
    public string Alternos { get; init; } = "";
    public int ProvisionalId { get; init; }
}

public sealed class NpcRespuestaInsertRow
{
    public int Id { get; init; }
    public int Accion { get; init; }
    public string Args { get; init; } = "";
    public string Condicion { get; init; } = "";
    public Guid ResponseDraftId { get; init; }
}

public sealed class MisionInsertRow
{
    public int Id { get; init; }
    public string Nombre { get; init; } = "";
    public string Etapas { get; init; } = "";
    public string PregDarMision { get; init; } = "";
    public string PregMisCompletada { get; init; } = "";
    public string PregMisIncompleta { get; init; } = "";
    public bool PuedeRepetirse { get; init; }
    public Guid DraftId { get; init; }
}

public sealed class MisionEtapaInsertRow
{
    public int Id { get; init; }
    public string Nombre { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string Recompensas { get; init; } = "";
    public string Objetivos { get; init; } = "";
    public string VariosObj { get; init; } = "0";
    public int ProvisionalId { get; init; }
}

public sealed class MisionObjetivoInsertRow
{
    public int Id { get; init; }
    public int Tipo { get; init; }
    public string Args { get; init; } = "";
    public string Detalle { get; init; } = "";
    public string EsAlHablar { get; init; } = "0";
    public int EsOculto { get; init; }
    public string Condicion { get; init; } = "";
    public int ProvisionalId { get; init; }
}

public sealed class ContentPublishOutcome
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public ContentPublishPlan? Plan { get; init; }
    public string? JournalPath { get; init; }
    public bool UsedTableLocks { get; init; }
    public bool UsedTransaction { get; init; }
    public bool CompensatingRollbackAttempted { get; init; }
    public bool CompensatingRollbackOk { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();
}

public sealed class ContentPublishJournal
{
    public string TimestampUtc { get; init; } = "";
    public string WorkspaceJson { get; init; } = "";
    public ContentPublishMaxSnapshot Maxes { get; init; } = new();
    public List<int> ReservedNpcIds { get; init; } = new();
    public List<int> ReservedQuestionIds { get; init; } = new();
    public List<int> ReservedResponseIds { get; init; } = new();
    public List<int> ReservedQuestIds { get; init; } = new();
    public List<int> ReservedStageIds { get; init; } = new();
    public List<int> ReservedObjectiveIds { get; init; } = new();
}
