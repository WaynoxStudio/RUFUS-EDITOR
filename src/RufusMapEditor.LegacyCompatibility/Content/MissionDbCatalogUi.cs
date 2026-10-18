using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>MISSIONS.DB.READ.1 — merge local drafts with RO BD catalogs for mission UI.</summary>
public static class MissionDbCatalogUi
{
    public static IReadOnlyList<NpcsModeloDraft> MergeNpcChoices(
        IEnumerable<NpcsModeloDraft> localDrafts,
        ContentDbCatalogSnapshot? catalog)
    {
        var byId = new Dictionary<int, NpcsModeloDraft>();
        foreach (var n in localDrafts)
            byId[n.Id] = n;

        if (catalog is null)
            return byId.Values.OrderBy(n => n.Id).ToList();

        foreach (var row in catalog.Npcs)
        {
            if (byId.ContainsKey(row.Id))
                continue;
            byId[row.Id] = FromDbNpcRow(row);
        }

        return byId.Values.OrderBy(n => n.Id).ToList();
    }

    public static NpcsModeloDraft FromDbNpcRow(DbNpcCatalogRow row) => new()
    {
        Id = row.Id,
        Nombre = row.Nombre ?? "",
        Pregunta = row.Pregunta,
        IsDbCatalogSnapshot = true,
        PublishedBd = true,
    };

    public static string ResolveNpcDisplayName(int npcId, ContentDraftWorkspace workspace, ContentDbCatalogSnapshot? catalog)
    {
        var local = workspace.Npcs.FindById(npcId);
        if (local is not null)
        {
            return string.IsNullOrWhiteSpace(local.Nombre)
                ? $"#{npcId}"
                : $"{local.Nombre} · #{npcId}";
        }

        var bd = catalog?.FindNpc(npcId);
        if (bd is not null)
        {
            return string.IsNullOrWhiteSpace(bd.Nombre)
                ? $"#{npcId}"
                : $"{bd.Nombre} · #{npcId}";
        }

        return $"#{npcId}";
    }

    public static string ResolveNpcShortName(int? npcId, ContentDraftWorkspace workspace, ContentDbCatalogSnapshot? catalog)
    {
        if (npcId is not int id) return "(sin NPC)";
        var local = workspace.Npcs.FindById(id);
        if (local is not null)
            return string.IsNullOrWhiteSpace(local.Nombre) ? $"#{id}" : local.Nombre;
        var bd = catalog?.FindNpc(id);
        if (bd is not null)
            return string.IsNullOrWhiteSpace(bd.Nombre) ? $"#{id}" : bd.Nombre;
        return $"#{id}";
    }

    /// <summary>
    /// Question IDs associated with an NPC via demonstrable relations only
    /// (npcs_modelo.pregunta, preg* on missions for that NPC). No invented owner on npc_preguntas.
    /// </summary>
    public static IReadOnlyCollection<int> ResolveQuestionIdsForNpc(
        int npcId,
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog)
    {
        var ids = new HashSet<int>();
        foreach (var q in dialogs.QuestionsForNpc(npcId))
            ids.Add(q.Id);

        if (catalog?.FindNpc(npcId) is { Pregunta: > 0 } npcRow)
            ids.Add(npcRow.Pregunta);

        if (catalog is not null)
        {
            foreach (var m in catalog.Missions)
            {
                foreach (var raw in new[] { m.PregDarMision, m.PregMisIncompleta, m.PregMisCompletada })
                {
                    if (!MissionPregPairCodec.TryParse(raw, out var pairNpc, out var pairQ))
                        continue;
                    if (pairNpc == npcId)
                        ids.Add(pairQ);
                }
            }
        }

        return ids;
    }

    public static IReadOnlyList<DialogQuestionDraft> BuildQuestionChoices(
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog,
        int? startNpcId,
        int? pregDarId,
        int? pregIncId,
        int? pregCompId)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var byId = new Dictionary<int, DialogQuestionDraft>();

        if (startNpcId is int npcId)
        {
            foreach (var q in dialogs.QuestionsForNpc(npcId))
                byId[q.Id] = q;

            foreach (var qid in ResolveQuestionIdsForNpc(npcId, dialogs, catalog))
            {
                if (byId.ContainsKey(qid))
                    continue;
                if (dialogs.FindQuestion(qid) is { } localQ)
                {
                    byId[qid] = localQ;
                    continue;
                }

                if (catalog?.FindQuestion(qid) is { } bdQ)
                    byId[qid] = FromDbQuestionRow(bdQ, catalog);
            }
        }

        foreach (var qid in new[] { pregDarId, pregIncId, pregCompId })
        {
            if (qid is not int id)
                continue;
            if (byId.ContainsKey(id))
                continue;
            if (dialogs.FindQuestion(id) is { } localAssigned)
            {
                byId[id] = localAssigned;
                continue;
            }
            if (catalog?.FindQuestion(id) is { } bdAssigned)
            {
                byId[id] = FromDbQuestionRow(bdAssigned, catalog);
                continue;
            }

            byId[id] = new DialogQuestionDraft
            {
                Id = id,
                OwnerNpcId = startNpcId ?? 0,
                TextLocal = "",
            };
        }

        return byId.Values.OrderBy(q => q.Id).ToList();
    }

    public static IReadOnlyList<MissionQuestionChoiceItem> BuildQuestionChoiceItems(
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog,
        int? startNpcId,
        int? pregDarId,
        int? pregIncId,
        int? pregCompId) =>
        BuildQuestionChoices(dialogs, catalog, startNpcId, pregDarId, pregIncId, pregCompId)
            .Select(q => new MissionQuestionChoiceItem
            {
                Id = q.Id,
                Label = FormatQuestionChoiceLabel(q, catalog),
            })
            .ToList();

    public static DialogQuestionDraft? ResolveQuestionDraft(
        int questionId,
        DialogDraftBatch dialogs,
        ContentDbCatalogSnapshot? catalog)
    {
        if (dialogs.FindQuestion(questionId) is { } local)
            return local;
        if (catalog?.FindQuestion(questionId) is { } bd)
            return FromDbQuestionRow(bd, catalog);
        return null;
    }

    public static DialogQuestionDraft FromDbQuestionRow(
        DbQuestionCatalogRow row,
        ContentDbCatalogSnapshot? catalog)
    {
        var text = ResolveQuestionText(row.Id, null, catalog);
        return new DialogQuestionDraft
        {
            Id = row.Id,
            OwnerNpcId = 0,
            TextLocal = text ?? "",
            Params = row.Params ?? "",
            Alternos = row.Alternos ?? "",
            PublishedBd = true,
            IsDbCatalogSnapshot = true,
        };
    }

    public static string? ResolveQuestionText(
        int questionId,
        DialogQuestionDraft? localDraft,
        ContentDbCatalogSnapshot? catalog)
    {
        var localText = (localDraft?.TextLocal ?? "").Trim();
        if (localText.Length > 0)
            return localText;

        if (catalog?.DialogEs?.Questions.TryGetValue(questionId, out var esText) == true
            && !string.IsNullOrWhiteSpace(esText))
            return esText.Trim();

        return null;
    }

    public static string FormatQuestionChoiceLabel(
        DialogQuestionDraft question,
        ContentDbCatalogSnapshot? catalog,
        int maxTextLength = 48)
    {
        ArgumentNullException.ThrowIfNull(question);
        var text = ResolveQuestionText(question.Id, question, catalog);
        return MissionDialogRolesUi.FormatQuestionLabel(question.Id, text, maxTextLength);
    }
}
