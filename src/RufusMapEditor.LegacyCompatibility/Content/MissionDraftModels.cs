using System.Collections.ObjectModel;
using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Local mission draft. Quest numeric ID is NOT assigned until CONT.5 (policy pending).
/// </summary>
public sealed class MissionDraft
{
    public const string StatusBorrador = "Borrador";

    public Guid DraftId { get; set; } = Guid.NewGuid();
    /// <summary>Assigned on CONT.5 publish (misiones.id).</summary>
    public int? PublishedQuestId { get; set; }
    public bool PublishedBd { get; set; }
    /// <summary>Quest ID from last client preview that used ContentPublishPlan (pre-BD).</summary>
    public int? PreviewPlannedQuestId { get; set; }
    public bool QuestsEsPublished { get; set; }
    public int? QuestsEsPublishedVersion { get; set; }
    public string Nombre { get; set; } = "";
    public bool PuedeRepetirse { get; set; }

    /// <summary>Published mission edited locally; next BD publish should UPDATE same IDs.</summary>
    public bool HasPendingBdChanges { get; set; }

    /// <summary>Stage IDs that already exist in BD for this published mission.</summary>
    public List<int> PublishedStageIds { get; set; } = new();

    /// <summary>Objective IDs that already exist in BD for this published mission.</summary>
    public List<int> PublishedObjectiveIds { get; set; } = new();

    /// <summary>
    /// Published objective IDs removed from the local draft; DELETE on next BD publish.
    /// </summary>
    public List<int> PendingDeletedObjectiveIds { get; set; } = new();

    /// <summary>NPC used to build preg* strings (npcId;preguntaId).</summary>
    public int? StartNpcId { get; set; }
    public int? PregDarPreguntaId { get; set; }
    public int? PregIncompletaPreguntaId { get; set; }
    public int? PregCompletadaPreguntaId { get; set; }

    public ObservableCollection<MissionStageDraft> Stages { get; set; } = new();

    public string Status => DisplayStatus;

    public string DisplayStatus =>
        !PublishedBd ? StatusBorrador
        : HasPendingBdChanges ? StatusCambiosPendientes
        : StatusPublicada;

    public const string StatusPublicada = "Publicada";
    public const string StatusCambiosPendientes = "Cambios pendientes";

    public void MarkPublishedSnapshot()
    {
        PublishedStageIds = Stages.Select(s => s.Id).Distinct().ToList();
        PublishedObjectiveIds = Stages.SelectMany(s => s.Objectives.Select(o => o.Id)).Distinct().ToList();
        PendingDeletedObjectiveIds.Clear();
        HasPendingBdChanges = false;
    }

    public void QueueDeletedPublishedObjective(int objectiveId)
    {
        if (objectiveId <= 0) return;
        if (!PendingDeletedObjectiveIds.Contains(objectiveId))
            PendingDeletedObjectiveIds.Add(objectiveId);
        MarkPendingBdEdit();
    }

    public void MarkPendingBdEdit()
    {
        if (PublishedBd)
            HasPendingBdChanges = true;
    }

    public bool IsPublishedStage(int stageId) =>
        PublishedBd && PublishedStageIds.Contains(stageId);

    public bool IsPublishedObjective(int objectiveId) =>
        PublishedBd && PublishedObjectiveIds.Contains(objectiveId);

    public string BuildPregDar() => BuildPregPair(StartNpcId, PregDarPreguntaId);
    public string BuildPregIncompleta() => BuildPregPair(StartNpcId, PregIncompletaPreguntaId);
    public string BuildPregCompletada() => BuildPregPair(StartNpcId, PregCompletadaPreguntaId);

    public static string BuildPregPair(int? npcId, int? preguntaId)
    {
        if (npcId is null || preguntaId is null || npcId <= 0 || preguntaId <= 0)
            return "";
        return string.Create(CultureInfo.InvariantCulture, $"{npcId.Value};{preguntaId.Value}");
    }

    /// <summary>MISSIONS.DIALOG.ROLES.2 — keep StartNpcId aligned with the edited NPC owner.</summary>
    public static bool SyncStartNpcFromOwner(MissionDraft mission, int ownerNpcId)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (ownerNpcId <= 0) return false;
        if (mission.StartNpcId == ownerNpcId) return false;
        mission.StartNpcId = ownerNpcId;
        return true;
    }

    public string BuildEtapasCsv() =>
        string.Join(",", Stages.Select(s => s.Id.ToString(CultureInfo.InvariantCulture)));

    public MissionDraft CloneNewIdentity(
        Func<int> nextStageId,
        Func<int> nextObjectiveId)
    {
        var copy = new MissionDraft
        {
            DraftId = Guid.NewGuid(),
            PublishedQuestId = null,
            PublishedBd = false,
            PreviewPlannedQuestId = null,
            QuestsEsPublished = false,
            QuestsEsPublishedVersion = null,
            HasPendingBdChanges = false,
            PublishedStageIds = new List<int>(),
            PublishedObjectiveIds = new List<int>(),
            PendingDeletedObjectiveIds = new List<int>(),
            Nombre = Nombre,
            PuedeRepetirse = PuedeRepetirse,
            StartNpcId = StartNpcId,
            PregDarPreguntaId = null,
            PregIncompletaPreguntaId = null,
            PregCompletadaPreguntaId = null,
        };
        foreach (var s in Stages)
            copy.Stages.Add(s.CloneNewIdentity(nextStageId(), nextObjectiveId));
        return copy;
    }
}

public sealed class MissionStageDraft
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public MissionRewardsDraft Rewards { get; set; } = new();
    /// <summary>MISSIONS.STAGES.VARIOSOBJ.EMPTY.1 — empty by default (matches live mision_etapas).</summary>
    public string VariosObj { get; set; } = "";
    public ObservableCollection<MissionObjectiveDraft> Objectives { get; set; } = new();

    public string BuildObjetivosField()
    {
        // Observed both CSV and pipe — CONT.1 etapa 55 used | ; RUFUS 5500 used single id.
        // Prefer pipe when multiple (vanilla), single id alone when one.
        if (Objectives.Count <= 1)
            return Objectives.Count == 0
                ? ""
                : Objectives[0].Id.ToString(CultureInfo.InvariantCulture);
        return string.Join("|", Objectives.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)));
    }

    public MissionStageDraft CloneNewIdentity(int newStageId, Func<int> nextObjectiveId)
    {
        var copy = new MissionStageDraft
        {
            Id = newStageId,
            Nombre = Nombre,
            Descripcion = Descripcion,
            Rewards = Rewards.Clone(),
            VariosObj = VariosObj,
        };
        foreach (var o in Objectives)
            copy.Objectives.Add(o.CloneNewIdentity(nextObjectiveId()));
        return copy;
    }
}

public sealed class MissionObjectiveDraft
{
    public int Id { get; set; }
    public int Tipo { get; set; }
    public string Args { get; set; } = "";
    public string Detalle { get; set; } = "";
    public string EsAlHablar { get; set; } = "0";
    public int EsOculto { get; set; }
    public string Condicion { get; set; } = "";

    /// <summary>
    /// Editor-only: auto-generated return-to-start-NPC (tipo 9).
    /// Not a BD column; not written to quests_es props beyond normal Q.o.
    /// </summary>
    public bool IsAutoReturnToStartNpc { get; set; }

    public MissionObjectiveDraft CloneNewIdentity(int newId) => new()
    {
        Id = newId,
        Tipo = Tipo,
        Args = Args,
        Detalle = Detalle,
        EsAlHablar = EsAlHablar,
        EsOculto = EsOculto,
        Condicion = Condicion,
        IsAutoReturnToStartNpc = IsAutoReturnToStartNpc,
    };

    public static MissionObjectiveDraft CreateDeliverItems(int id, int npcId, int itemId, int qty)
    {
        var args = string.Create(CultureInfo.InvariantCulture, $"[{npcId},{itemId},{qty}]");
        return new MissionObjectiveDraft
        {
            Id = id,
            Tipo = MissionObjectiveTypes.DeliverItemsToNpc,
            Args = args,
            Detalle = args,
            EsAlHablar = "0",
            EsOculto = 0,
            Condicion = "",
        };
    }
}

/// <summary>recompensas = exp|kamas|objetos|emotes|hechizos|oficios|acciones</summary>
public sealed class MissionRewardsDraft
{
    public int Exp { get; set; }
    public int Kamas { get; set; }
    public List<MissionRewardItem> Objetos { get; set; } = new();
    public string Emotes { get; set; } = "";
    public string Hechizos { get; set; } = "";
    public string Oficios { get; set; } = "";
    public string Acciones { get; set; } = "";

    public MissionRewardsDraft Clone() => new()
    {
        Exp = Exp,
        Kamas = Kamas,
        Objetos = Objetos.Select(o => new MissionRewardItem { ItemId = o.ItemId, Cantidad = o.Cantidad }).ToList(),
        Emotes = Emotes,
        Hechizos = Hechizos,
        Oficios = Oficios,
        Acciones = Acciones,
    };

    public string ToRaw()
    {
        var objetos = Objetos.Count == 0
            ? "null"
            : string.Join(";", Objetos.Select(o =>
                string.Create(CultureInfo.InvariantCulture, $"{o.ItemId},{o.Cantidad}")));
        static string Slot(string s) => string.IsNullOrWhiteSpace(s) ? "null" : s.Trim();
        return string.Create(CultureInfo.InvariantCulture,
            $"{Exp}|{Kamas}|{objetos}|{Slot(Emotes)}|{Slot(Hechizos)}|{Slot(Oficios)}|{Slot(Acciones)}");
    }

    public static MissionRewardsDraft FromRaw(string? raw)
    {
        var d = new MissionRewardsDraft();
        if (string.IsNullOrWhiteSpace(raw))
            return d;
        var parts = raw.Split('|');
        if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
            d.Exp = exp;
        if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kam))
            d.Kamas = kam;
        if (parts.Length > 2 && !IsNullToken(parts[2]))
        {
            foreach (var chunk in parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = chunk.Split(',');
                if (pair.Length >= 2
                    && int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                    && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
                {
                    d.Objetos.Add(new MissionRewardItem { ItemId = itemId, Cantidad = qty });
                }
            }
        }
        if (parts.Length > 3 && !IsNullToken(parts[3])) d.Emotes = parts[3];
        if (parts.Length > 4 && !IsNullToken(parts[4])) d.Hechizos = parts[4];
        if (parts.Length > 5 && !IsNullToken(parts[5])) d.Oficios = parts[5];
        if (parts.Length > 6 && !IsNullToken(parts[6])) d.Acciones = parts[6];
        return d;
    }

    private static bool IsNullToken(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase);
}

public sealed class MissionRewardItem
{
    public int ItemId { get; set; }
    public int Cantidad { get; set; } = 1;
}

/// <summary>
/// MISSIONS.DRAFTS.UNSAVED_GUARD — when to prompt before leaving the current mission draft.
/// Same DraftId rebind (Reload/list rebuild) is NOT abandonment.
/// </summary>
public static class MissionDraftNavigationGuard
{
    public static bool RequiresUnsavedPrompt(Guid? currentDraftId, bool hasUnsavedLocalEdits, Guid? nextDraftId)
    {
        if (!hasUnsavedLocalEdits || currentDraftId is null)
            return false;
        if (nextDraftId is Guid next && next == currentDraftId.Value)
            return false;
        return true;
    }
}
