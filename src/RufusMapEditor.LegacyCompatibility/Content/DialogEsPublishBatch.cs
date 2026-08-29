using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.6C — one reserved dialog_es assignment linked back to a draft.</summary>
public sealed class DialogEsPublishBinding
{
    public required DialogEsAssignment Assignment { get; init; }
    public int? OwnerNpcDraftId { get; init; }
    public int? OwnerQuestionDraftId { get; init; }
    public Guid? OwnerResponseDraftId { get; init; }
    public string Kind { get; init; } = "";
}

/// <summary>CONT.6C — recalculated batch ready for local generate / SFTP publish.</summary>
public sealed class DialogEsPublishBatch
{
    public required IReadOnlyList<DialogEsPublishBinding> Bindings { get; init; }
    public required int SourceVersion { get; init; }
    public required int TargetVersion { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0 && Bindings.Count > 0;

    public IReadOnlyList<DialogEsAssignment> Additions =>
        Bindings.Select(b => b.Assignment).ToList();

    public int NewQuestionCount => Bindings.Count(b => b.Assignment.Space == DialogEsSpace.Question);
    public int NewAnswerCount => Bindings.Count(b => b.Assignment.Space == DialogEsSpace.Answer);

    public string FormatPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"dialog_es actual: {SourceVersion}");
        sb.AppendLine($"dialog_es nuevo: {TargetVersion}");
        sb.AppendLine();
        sb.AppendLine($"Nuevos D.q: {NewQuestionCount}");
        sb.AppendLine($"Nuevos D.a: {NewAnswerCount}");
        sb.AppendLine();
        sb.AppendLine("IDs finales:");
        foreach (var b in Bindings)
        {
            var space = b.Assignment.Space == DialogEsSpace.Question ? "D.q" : "D.a";
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {space}[{b.Assignment.Id}] = \"{Truncate(b.Assignment.Text, 60)}\" ({b.Kind})"));
        }
        return sb.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>
/// CONT.6C — build publish batch from live SWF + BD occupancy (never trust provisional UI IDs).
/// </summary>
public static class DialogEsPublishBatchBuilder
{
    public static DialogEsPublishBatch Build(
        ContentDraftWorkspace workspace,
        DialogEsSnapshot snapshot,
        DialogEsIdOccupancy? occupancy = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<string>();
        var bindings = new List<DialogEsPublishBinding>();
        var occ = occupancy ?? new DialogEsIdOccupancy();
        var resolver = new DialogEsIdResolver(snapshot, occ);

        var npcs = workspace.Npcs.Drafts.Where(n => !n.PublishedBd).ToList();
        var npcIds = npcs.Select(n => n.Id).ToHashSet();
        var interactiveNpcIds = npcs
            .Where(n => n.DialogMode == NpcDialogMode.Interactive)
            .Select(n => n.Id)
            .ToHashSet();

        var questions = workspace.Dialogs.Questions
            .Where(q => !q.PublishedBd
                        && npcIds.Contains(q.OwnerNpcId)
                        && interactiveNpcIds.Contains(q.OwnerNpcId)
                        && !string.IsNullOrWhiteSpace(q.TextLocal))
            .ToList();

        // Interactive questions first (same order as plan builder).
        foreach (var q in questions)
        {
            try
            {
                DialogEsLatin1.Validate(q.TextLocal, $"Pregunta {q.Id}");
            }
            catch (DialogEsEncodingException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            var id = resolver.ReserveInteractiveQuestion();
            bindings.Add(new DialogEsPublishBinding
            {
                Assignment = new DialogEsAssignment
                {
                    Space = DialogEsSpace.Question,
                    Id = id,
                    Text = q.TextLocal,
                },
                OwnerNpcDraftId = q.OwnerNpcId,
                OwnerQuestionDraftId = q.Id,
                Kind = "interactive-question",
            });

            foreach (var r in q.Responses)
            {
                if (string.IsNullOrWhiteSpace(r.TextLocal))
                {
                    errors.Add($"Respuesta de pregunta {q.Id} sin texto local.");
                    continue;
                }

                try
                {
                    DialogEsLatin1.Validate(r.TextLocal, $"Respuesta {r.DraftId:N}");
                }
                catch (DialogEsEncodingException ex)
                {
                    errors.Add(ex.Message);
                    continue;
                }

                var aid = resolver.ReserveInteractiveAnswer();
                bindings.Add(new DialogEsPublishBinding
                {
                    Assignment = new DialogEsAssignment
                    {
                        Space = DialogEsSpace.Answer,
                        Id = aid,
                        Text = r.TextLocal,
                    },
                    OwnerNpcDraftId = q.OwnerNpcId,
                    OwnerQuestionDraftId = q.Id,
                    OwnerResponseDraftId = r.DraftId,
                    Kind = "interactive-answer",
                });
            }
        }

        foreach (var npc in npcs)
        {
            if (npc.DialogMode != NpcDialogMode.Simple || !npc.IsPendingDialogEs)
                continue;
            if (npc.DialogEsPublished)
                continue;

            try
            {
                DialogEsLatin1.Validate(npc.SimpleDialogTextLocal, $"NPC {npc.Id}");
            }
            catch (DialogEsEncodingException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            var id = resolver.ReserveSimpleQuestion();
            bindings.Add(new DialogEsPublishBinding
            {
                Assignment = new DialogEsAssignment
                {
                    Space = DialogEsSpace.Question,
                    Id = id,
                    Text = npc.SimpleDialogTextLocal,
                },
                OwnerNpcDraftId = npc.Id,
                Kind = "simple",
            });
        }

        if (bindings.Count == 0 && errors.Count == 0)
            errors.Add("No hay textos nuevos pendientes de publicar en dialog_es.");

        return new DialogEsPublishBatch
        {
            Bindings = bindings,
            SourceVersion = snapshot.Version,
            TargetVersion = snapshot.Version + 1,
            Errors = errors,
        };
    }

    /// <summary>Apply final IDs after successful remote publish. Does not write BD.</summary>
    public static void ApplyToWorkspace(
        ContentDraftWorkspace workspace,
        DialogEsPublishBatch batch,
        int publishedVersion)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(batch);

        var questionRemap = new Dictionary<int, int>();

        foreach (var b in batch.Bindings)
        {
            if (b.Kind == "simple" && b.OwnerNpcDraftId is int npcId)
            {
                var npc = workspace.Npcs.FindById(npcId);
                if (npc is null) continue;
                npc.Pregunta = b.Assignment.Id;
                npc.DialogEsPublished = true;
                npc.DialogEsPublishedVersion = publishedVersion;
            }
            else if (b.Kind == "interactive-question"
                     && b.OwnerQuestionDraftId is int oldQ
                     && b.Assignment.Space == DialogEsSpace.Question)
            {
                questionRemap[oldQ] = b.Assignment.Id;
            }
            else if (b.Kind == "interactive-answer"
                     && b.OwnerResponseDraftId is Guid rid
                     && b.Assignment.Space == DialogEsSpace.Answer)
            {
                foreach (var q in workspace.Dialogs.Questions)
                {
                    var r = q.Responses.FirstOrDefault(x => x.DraftId == rid);
                    if (r is null) continue;
                    r.PublishedResponseId = b.Assignment.Id;
                    break;
                }
            }
        }

        if (questionRemap.Count > 0)
            RemapQuestionIds(workspace, questionRemap, publishedVersion);
    }

    private static void RemapQuestionIds(
        ContentDraftWorkspace workspace,
        Dictionary<int, int> remap,
        int publishedVersion)
    {
        foreach (var q in workspace.Dialogs.Questions.ToList())
        {
            if (!remap.TryGetValue(q.Id, out var newId))
                continue;
            if (newId == q.Id)
                continue;

            // Rebuild question with final id (DialogDraftBatch may keep list by id).
            var oldId = q.Id;
            q.Id = newId;

            foreach (var npc in workspace.Npcs.Drafts)
            {
                if (npc.Pregunta == oldId)
                    npc.Pregunta = newId;
                if (npc.DialogMode == NpcDialogMode.Interactive && npc.Id == q.OwnerNpcId)
                {
                    npc.DialogEsPublished = true;
                    npc.DialogEsPublishedVersion = publishedVersion;
                }
            }

            foreach (var m in workspace.Missions.Missions)
            {
                if (m.PregDarPreguntaId == oldId) m.PregDarPreguntaId = newId;
                if (m.PregCompletadaPreguntaId == oldId) m.PregCompletadaPreguntaId = newId;
                if (m.PregIncompletaPreguntaId == oldId) m.PregIncompletaPreguntaId = newId;
            }

            foreach (var other in workspace.Dialogs.Questions)
            {
                foreach (var r in other.Responses)
                {
                    foreach (var a in r.Actions)
                    {
                        if (a.TargetQuestionId == oldId)
                        {
                            a.TargetQuestionId = newId;
                            a.SyncGotoArgs();
                        }
                    }
                }
            }
        }
    }
}
