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
            var suffix = b.Assignment.ReplaceExisting ? " (reemplazo)" : $" ({b.Kind})";
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {space}[{b.Assignment.Id}] = \"{Truncate(b.Assignment.Text, 60)}\"{suffix}"));
        }
        return sb.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>
/// CONT.6C — build publish batch from live SWF + BD occupancy (never trust provisional UI IDs).
/// Includes PublishedBd content that is still pending dialog_es (BD ≠ cliente).
/// </summary>
public static class DialogEsPublishBatchBuilder
{
    public static DialogEsPublishBatch Build(
        ContentDraftWorkspace workspace,
        DialogEsSnapshot snapshot,
        DialogEsIdOccupancy? occupancy = null,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<string>();
        var bindings = new List<DialogEsPublishBinding>();
        var occ = occupancy ?? new DialogEsIdOccupancy();
        var resolver = new DialogEsIdResolver(snapshot, occ);

        var pendingNpcs = workspace.Npcs.Drafts
            .Where(n =>
                (n.DialogMode == NpcDialogMode.Interactive
                 || n.DialogMode == NpcDialogMode.Simple)
                && ContentPublishNpcSelection.Includes(n.Id, npcIdFilter))
            .ToList();
        var interactiveNpcIds = pendingNpcs
            .Where(n => n.DialogMode == NpcDialogMode.Interactive)
            .Select(n => n.Id)
            .ToHashSet();

        var questions = workspace.Dialogs.Questions
            .Where(q => interactiveNpcIds.Contains(q.OwnerNpcId)
                        && !string.IsNullOrWhiteSpace(q.TextLocal))
            .ToList();

        // Interactive questions first (same order as plan builder).
        foreach (var q in questions)
        {
            string preparedQ;
            try
            {
                preparedQ = DialogEsLatin1.Prepare(q.TextLocal, $"Pregunta {q.Id}");
            }
            catch (DialogEsEncodingException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            if (!string.Equals(q.TextLocal, preparedQ, StringComparison.Ordinal))
                q.TextLocal = preparedQ;

            int id;
            var replace = false;
            if (q.PublishedBd)
            {
                id = q.Id;
                if (snapshot.Questions.TryGetValue(id, out var existing))
                {
                    if (string.Equals(existing, DialogEsLatin1.Normalize(preparedQ), StringComparison.Ordinal))
                        continue; // already correct in dialog_es

                    replace = true;
                }
                else if (!resolver.TryClaimQuestion(id))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"D.q[{id}] no se pudo reservar (conflicto local)."));
                    continue;
                }
            }
            else
            {
                id = resolver.ReserveInteractiveQuestion();
            }

            bindings.Add(new DialogEsPublishBinding
            {
                Assignment = new DialogEsAssignment
                {
                    Space = DialogEsSpace.Question,
                    Id = id,
                    Text = preparedQ,
                    ReplaceExisting = replace,
                },
                OwnerNpcDraftId = q.OwnerNpcId,
                OwnerQuestionDraftId = q.Id,
                Kind = replace ? "interactive-question-replace" : "interactive-question",
            });

            foreach (var r in q.Responses)
            {
                if (string.IsNullOrWhiteSpace(r.TextLocal))
                {
                    errors.Add($"Respuesta de pregunta {q.Id} sin texto local.");
                    continue;
                }

                string preparedA;
                try
                {
                    preparedA = DialogEsLatin1.Prepare(r.TextLocal, $"Respuesta {r.DraftId:N}");
                }
                catch (DialogEsEncodingException ex)
                {
                    errors.Add(ex.Message);
                    continue;
                }

                if (!string.Equals(r.TextLocal, preparedA, StringComparison.Ordinal))
                    r.TextLocal = preparedA;

                int aid;
                var replaceA = false;
                if (q.PublishedBd && r.PublishedResponseId is int publishedAid and > 0)
                {
                    aid = publishedAid;
                    if (snapshot.Answers.TryGetValue(aid, out var existingA))
                    {
                        if (string.Equals(existingA, DialogEsLatin1.Normalize(preparedA), StringComparison.Ordinal))
                            continue;

                        replaceA = true;
                    }
                    else if (!resolver.TryClaimAnswer(aid))
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture,
                            $"D.a[{aid}] no se pudo reservar (conflicto local)."));
                        continue;
                    }
                }
                else
                {
                    aid = resolver.ReserveInteractiveAnswer();
                }

                bindings.Add(new DialogEsPublishBinding
                {
                    Assignment = new DialogEsAssignment
                    {
                        Space = DialogEsSpace.Answer,
                        Id = aid,
                        Text = preparedA,
                        ReplaceExisting = replaceA,
                    },
                    OwnerNpcDraftId = q.OwnerNpcId,
                    OwnerQuestionDraftId = q.Id,
                    OwnerResponseDraftId = r.DraftId,
                    Kind = replaceA ? "interactive-answer-replace" : "interactive-answer",
                });
            }
        }

        foreach (var npc in pendingNpcs)
        {
            if (npc.DialogMode != NpcDialogMode.Simple)
                continue;

            var hasText = !string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal);
            if (!hasText)
                continue;

            // Pre-BD: Pregunta<=0 → reserve. Post-BD / repair: Pregunta already assigned → reuse.
            if (npc.Pregunta <= 0 && !npc.IsPendingDialogEs)
                continue;

            string preparedSimple;
            try
            {
                preparedSimple = DialogEsLatin1.Prepare(npc.SimpleDialogTextLocal, $"NPC {npc.Id}");
            }
            catch (DialogEsEncodingException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            if (!string.Equals(npc.SimpleDialogTextLocal, preparedSimple, StringComparison.Ordinal))
                npc.SimpleDialogTextLocal = preparedSimple;

            int id;
            var replaceSimple = false;
            if (npc.Pregunta > 0)
            {
                id = npc.Pregunta;
                if (snapshot.Questions.TryGetValue(id, out var existing))
                {
                    if (string.Equals(existing, DialogEsLatin1.Normalize(preparedSimple), StringComparison.Ordinal))
                        continue;

                    replaceSimple = true;
                }
                else if (!resolver.TryClaimQuestion(id))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"D.q[{id}] no se pudo reservar (conflicto local)."));
                    continue;
                }
            }
            else
            {
                id = resolver.ReserveSimpleQuestion();
            }

            bindings.Add(new DialogEsPublishBinding
            {
                Assignment = new DialogEsAssignment
                {
                    Space = DialogEsSpace.Question,
                    Id = id,
                    Text = preparedSimple,
                    ReplaceExisting = replaceSimple,
                },
                OwnerNpcDraftId = npc.Id,
                Kind = replaceSimple ? "simple-replace" : "simple",
            });
        }

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
            if ((b.Kind is "simple" or "simple-replace") && b.OwnerNpcDraftId is int npcId)
            {
                var npc = workspace.Npcs.FindById(npcId);
                if (npc is null) continue;
                npc.Pregunta = b.Assignment.Id;
                npc.DialogEsIdProvisional = false;
                npc.DialogEsPublished = true;
                npc.DialogEsPublishedVersion = publishedVersion;
            }
            else if ((b.Kind is "interactive-question" or "interactive-question-replace")
                     && b.OwnerQuestionDraftId is int oldQ
                     && b.Assignment.Space == DialogEsSpace.Question)
            {
                questionRemap[oldQ] = b.Assignment.Id;
            }
            else if ((b.Kind is "interactive-answer" or "interactive-answer-replace")
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

        // Post-BD path: IDs already final — still mark interactive NPCs as dialog_es published.
        foreach (var b in batch.Bindings.Where(x =>
                     x.Kind is "interactive-question" or "interactive-question-replace"))
        {
            if (b.OwnerNpcDraftId is not int ownerNpcId) continue;
            var npc = workspace.Npcs.FindById(ownerNpcId);
            if (npc is null) continue;
            npc.DialogEsPublished = true;
            npc.DialogEsPublishedVersion = publishedVersion;
        }
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
