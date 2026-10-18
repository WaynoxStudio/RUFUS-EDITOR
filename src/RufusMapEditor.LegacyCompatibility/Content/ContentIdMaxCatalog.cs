using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// CONTENT.IDS.MAX.1 — central MAX(id) catalog for content tables.
/// Tables: npcs_modelo, npc_preguntas, npc_respuestas, misiones, mision_etapas, mision_objetivos.
/// Does not use npc_respuestas.orden (AUTO BD).
/// </summary>
public sealed class ContentIdMaxCatalog
{
    public int NpcMax { get; init; }
    public int QuestionMax { get; init; }
    public int ResponseMax { get; init; }
    public int QuestMax { get; init; }
    public int StageMax { get; init; }
    public int ObjectiveMax { get; init; }

    public static ContentIdMaxCatalog FromPublishSnapshot(ContentPublishMaxSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ContentIdMaxCatalog
        {
            NpcMax = s.NpcsModelo,
            QuestionMax = s.NpcPreguntas,
            ResponseMax = s.NpcRespuestas,
            QuestMax = s.Misiones,
            StageMax = s.MisionEtapas,
            ObjectiveMax = s.MisionObjetivos,
        };
    }

    public ContentPublishMaxSnapshot ToPublishSnapshot() => new()
    {
        NpcsModelo = NpcMax,
        NpcPreguntas = QuestionMax,
        NpcRespuestas = ResponseMax,
        Misiones = QuestMax,
        MisionEtapas = StageMax,
        MisionObjetivos = ObjectiveMax,
    };

    /// <summary>Apply BD MAX into workspace draft batches (editor provisional IDs).</summary>
    public void ApplyToWorkspace(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.Npcs.SetDbMaxId(NpcMax);
        workspace.Dialogs.SetDbMaxQuestionId(QuestionMax);
        workspace.Dialogs.SetDbMaxResponseId(ResponseMax);
        workspace.Missions.SetDbMaxQuestId(QuestMax);
        workspace.Missions.SetDbMaxStageId(StageMax);
        workspace.Missions.SetDbMaxObjectiveId(ObjectiveMax);
    }
}

/// <summary>Next provisional / publish IDs considering BD MAX + local unpublished drafts.</summary>
public sealed class ContentIdNextPreview
{
    public int NextNpcId { get; init; }
    public int NextQuestionId { get; init; }
    public int NextResponseId { get; init; }
    public int NextQuestId { get; init; }
    public int NextStageId { get; init; }
    public int NextObjectiveId { get; init; }

    public string FormatSummary()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"NPC próximo: {NextNpcId}");
        sb.Append(CultureInfo.InvariantCulture, $" · Pregunta próxima: {NextQuestionId}");
        sb.Append(CultureInfo.InvariantCulture, $" · Respuesta próxima: {NextResponseId}");
        sb.Append(CultureInfo.InvariantCulture, $" · Misión próxima: {NextQuestId}");
        sb.Append(CultureInfo.InvariantCulture, $" · Etapa próxima: {NextStageId}");
        sb.Append(CultureInfo.InvariantCulture, $" · Objetivo próximo: {NextObjectiveId}");
        return sb.ToString();
    }
}

public static class ContentIdReservation
{
    /// <summary>
    /// NEXT = max(BD max, local unpublished drafts) + 1.
    /// Response/Quest drafts use Guid until publish; their count still reserves consecutive slots.
    /// </summary>
    public static ContentIdNextPreview ComputeNext(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new ContentIdNextPreview
        {
            NextNpcId = workspace.Npcs.NextProvisionalId,
            NextQuestionId = workspace.Dialogs.NextQuestionId,
            NextResponseId = workspace.Dialogs.NextResponseId,
            NextQuestId = workspace.Missions.NextQuestId,
            NextStageId = workspace.Missions.NextStageId,
            NextObjectiveId = workspace.Missions.NextObjectiveId,
        };
    }

    /// <summary>Reserve <paramref name="count"/> consecutive IDs starting at <paramref name="maxExclusive"/>+1.</summary>
    public static int[] ReserveConsecutive(int maxExclusive, int count)
    {
        if (maxExclusive < 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var start = maxExclusive + 1;
        var ids = new int[count];
        for (var i = 0; i < count; i++)
            ids[i] = start + i;
        return ids;
    }

    /// <summary>
    /// Effective MAX for mission ID planning: max(BD, quests_es) per independent namespace.
    /// Q.q / Q.s / Q.o are independent — never cross-merge between them.
    /// </summary>
    public static ContentPublishMaxSnapshot EffectiveContentMax(
        ContentPublishMaxSnapshot bd,
        QuestsEsSnapshot? questsEs)
    {
        ArgumentNullException.ThrowIfNull(bd);
        if (questsEs is null)
            return bd;

        return new ContentPublishMaxSnapshot
        {
            NpcsModelo = bd.NpcsModelo,
            NpcPreguntas = bd.NpcPreguntas,
            NpcRespuestas = bd.NpcRespuestas,
            Misiones = Math.Max(bd.Misiones, MaxKey(questsEs.Quests.Keys)),
            MisionEtapas = Math.Max(bd.MisionEtapas, MaxKey(questsEs.Stages.Keys)),
            MisionObjetivos = Math.Max(bd.MisionObjetivos, MaxKey(questsEs.Objectives.Keys)),
        };
    }

    public static int MaxKey(IEnumerable<int> keys)
    {
        var max = 0;
        foreach (var k in keys)
        {
            if (k > max)
                max = k;
        }

        return max;
    }

    /// <summary>
    /// Reserve consecutive npc_preguntas.id values using BD MAX + local drafts (single batch read).
    /// </summary>
    public static bool TryReserveQuestionIds(
        ContentDraftWorkspace workspace,
        int count,
        ContentDbCatalogSnapshot? catalog,
        out int[] ids,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ids = Array.Empty<int>();
        errorMessage = null;
        if (count <= 0) return true;

        var maxExclusive = ComputeNext(workspace).NextQuestionId - 1;
        var candidate = ReserveConsecutive(maxExclusive, count);
        foreach (var id in candidate)
        {
            if (workspace.Dialogs.FindQuestion(id) is not null
                || catalog?.FindQuestion(id) is not null)
            {
                errorMessage = MissionDialogRolesUi.ReservationFailedMessage;
                ids = Array.Empty<int>();
                return false;
            }
        }

        ids = candidate;
        return true;
    }

    public static string FormatDuplicateError(string kind, int conflictingId) =>
        string.Create(CultureInfo.InvariantCulture,
            $"ID {kind} duplicado en el plan: {conflictingId}");
}

public sealed class ContentIdMaxReadResult
{
    public bool Success { get; init; }
    public ContentIdMaxCatalog? Catalog { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    public string FailureMessage =>
        Failures.Count == 0
            ? "No se pudo obtener MAX de contenido."
            : string.Join("\n", Failures);
}

/// <summary>Reads all six content MAX(id) values. Failures are per table.column.</summary>
public sealed class ContentIdMaxReader
{
    private readonly IContentPublishStore? _publishStore;
    private readonly Func<CancellationToken, Task<int>>? _npcMax;
    private readonly Func<CancellationToken, Task<int>>? _questionMax;
    private readonly Func<CancellationToken, Task<int>>? _responseMax;
    private readonly Func<CancellationToken, Task<int>>? _questMax;
    private readonly Func<CancellationToken, Task<int>>? _stageMax;
    private readonly Func<CancellationToken, Task<int>>? _objectiveMax;

    public ContentIdMaxReader(IContentPublishStore publishStore)
    {
        _publishStore = publishStore ?? throw new ArgumentNullException(nameof(publishStore));
    }

    public ContentIdMaxReader(
        Func<CancellationToken, Task<int>> npcMax,
        Func<CancellationToken, Task<int>> questionMax,
        Func<CancellationToken, Task<int>> responseMax,
        Func<CancellationToken, Task<int>> questMax,
        Func<CancellationToken, Task<int>> stageMax,
        Func<CancellationToken, Task<int>> objectiveMax)
    {
        _npcMax = npcMax ?? throw new ArgumentNullException(nameof(npcMax));
        _questionMax = questionMax ?? throw new ArgumentNullException(nameof(questionMax));
        _responseMax = responseMax ?? throw new ArgumentNullException(nameof(responseMax));
        _questMax = questMax ?? throw new ArgumentNullException(nameof(questMax));
        _stageMax = stageMax ?? throw new ArgumentNullException(nameof(stageMax));
        _objectiveMax = objectiveMax ?? throw new ArgumentNullException(nameof(objectiveMax));
    }

    public async Task<ContentIdMaxReadResult> ReadAsync(CancellationToken ct = default)
    {
        if (_publishStore is not null)
        {
            try
            {
                var snap = await _publishStore.ReadMaxIdsAsync(ct).ConfigureAwait(false);
                return new ContentIdMaxReadResult
                {
                    Success = true,
                    Catalog = ContentIdMaxCatalog.FromPublishSnapshot(snap),
                };
            }
            catch (Exception ex)
            {
                return new ContentIdMaxReadResult
                {
                    Success = false,
                    Failures =
                    [
                        "No se pudo obtener MAX de tablas de contenido: " + ex.Message,
                    ],
                };
            }
        }

        var failures = new List<string>();
        var npc = await TryRead(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, _npcMax!, failures, ct).ConfigureAwait(false);
        var q = await TryRead(NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, _questionMax!, failures, ct).ConfigureAwait(false);
        var r = await TryRead(NpcRespuestasColumns.DefaultTable, NpcRespuestasColumns.Id, _responseMax!, failures, ct).ConfigureAwait(false);
        var quest = await TryRead(MisionesColumns.DefaultTable, MisionesColumns.Id, _questMax!, failures, ct).ConfigureAwait(false);
        var stage = await TryRead(MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, _stageMax!, failures, ct).ConfigureAwait(false);
        var obj = await TryRead(MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, _objectiveMax!, failures, ct).ConfigureAwait(false);

        if (failures.Count > 0)
            return new ContentIdMaxReadResult { Success = false, Failures = failures };

        return new ContentIdMaxReadResult
        {
            Success = true,
            Catalog = new ContentIdMaxCatalog
            {
                NpcMax = npc,
                QuestionMax = q,
                ResponseMax = r,
                QuestMax = quest,
                StageMax = stage,
                ObjectiveMax = obj,
            },
        };
    }

    private static async Task<int> TryRead(
        string table,
        string column,
        Func<CancellationToken, Task<int>> read,
        List<string> failures,
        CancellationToken ct)
    {
        try
        {
            return await read(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add($"No se pudo obtener MAX de {table}.{column}: {ex.Message}");
            return 0;
        }
    }
}
