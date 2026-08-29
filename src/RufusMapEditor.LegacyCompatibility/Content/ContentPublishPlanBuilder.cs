using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Builds a complete ContentPublishPlan in memory from workspace + live MAX snapshot.
/// Provisional draft ints are remapped to MAX+1 consecutive blocks (CONT.5).
/// </summary>
public static class ContentPublishPlanBuilder
{
    public static ContentPublishPlan Build(
        ContentDraftWorkspace workspace,
        ContentPublishMaxSnapshot maxes,
        DialogEsSnapshot? dialogEs = null,
        DialogEsIdOccupancy? dialogEsOccupancy = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(maxes);
        var plan = new ContentPublishPlan { Maxes = maxes };

        var npcs = workspace.Npcs.Drafts.Where(n => !n.PublishedBd).ToList();
        var npcIds = npcs.Select(n => n.Id).ToHashSet();
        var interactiveNpcIds = npcs
            .Where(n => n.DialogMode == NpcDialogMode.Interactive)
            .Select(n => n.Id)
            .ToHashSet();

        // CONT.5.1 — only content reachable from NPCs in this publish batch.
        // CONT-DIALOG.3 — Simple mode never publishes npc_preguntas / npc_respuestas.
        // Missions without StartNpcId on a batch NPC are orphans (toggle off / leftovers).
        var questions = workspace.Dialogs.Questions
            .Where(q => !q.PublishedBd
                        && npcIds.Contains(q.OwnerNpcId)
                        && interactiveNpcIds.Contains(q.OwnerNpcId))
            .ToList();
        var missions = workspace.Missions.Missions
            .Where(m => !m.PublishedBd
                        && m.StartNpcId is int sid
                        && npcIds.Contains(sid))
            .ToList();

        // --- Reserve ID blocks (order: NPC, Question, Response, Quest, Stage, Objective) ---
        DialogEsIdResolver? dialogResolver = null;
        if (dialogEs is not null)
        {
            dialogResolver = new DialogEsIdResolver(
                dialogEs,
                dialogEsOccupancy ?? new DialogEsIdOccupancy
                {
                    BdQuestionMax = maxes.NpcPreguntas,
                    BdResponseMax = maxes.NpcRespuestas,
                });
            plan.DialogEsSourceVersion = dialogEs.Version;
            plan.DialogEsTargetVersion = dialogEs.Version + 1;
            plan.DialogEsIdsAreProvisional = true;
        }

        var nextNpc = maxes.NpcsModelo + 1;
        foreach (var n in npcs)
            plan.NpcIdMap[n.Id] = nextNpc++;

        var nextQ = maxes.NpcPreguntas + 1;
        foreach (var q in questions)
        {
            plan.QuestionIdMap[q.Id] = dialogResolver is null
                ? nextQ++
                : dialogResolver.ReserveInteractiveQuestion();
        }

        var responses = questions.SelectMany(q => q.Responses).ToList();
        var nextR = maxes.NpcRespuestas + 1;
        foreach (var r in responses)
        {
            if (plan.ResponseIdMap.ContainsKey(r.DraftId))
                continue;
            plan.ResponseIdMap[r.DraftId] = dialogResolver is null
                ? nextR++
                : dialogResolver.ReserveInteractiveAnswer();
        }

        var nextQuest = maxes.Misiones + 1;
        foreach (var m in missions)
            plan.QuestIdMap[m.DraftId] = nextQuest++;

        var stages = missions.SelectMany(m => m.Stages).ToList();
        var nextStage = maxes.MisionEtapas + 1;
        foreach (var s in stages)
            plan.StageIdMap[s.Id] = nextStage++;

        var objectives = stages.SelectMany(s => s.Objectives).ToList();
        var nextObj = maxes.MisionObjetivos + 1;
        foreach (var o in objectives)
            plan.ObjectiveIdMap[o.Id] = nextObj++;

        // --- Resolve rows ---
        foreach (var n in npcs)
        {
            var finalId = plan.NpcIdMap[n.Id];
            var pregunta = ResolveNpcPregunta(n.Pregunta, plan);
            plan.Npcs.Add(new NpcModeloInsertRow
            {
                Id = finalId,
                ProvisionalId = n.Id,
                GfxId = n.GfxId,
                ScaleX = n.ScaleX,
                ScaleY = n.ScaleY,
                Sexo = n.Sexo != 0 ? 1 : 0,
                Color1 = n.Color1,
                Color2 = n.Color2,
                Color3 = n.Color3,
                Accesorios = n.Accesorios ?? NpcsModeloDraft.DefaultAccesorios,
                Foto = n.Foto,
                Pregunta = pregunta,
                Ventas = n.Ventas ?? "",
                Nombre = n.Nombre ?? "",
                ObjetoCompra = n.ObjetoCompra,
            });

            foreach (var loc in n.Locations)
            {
                plan.Locations.Add(new NpcUbicacionInsertRow
                {
                    Mapa = loc.MapId,
                    Celda = loc.CellId,
                    Npc = finalId,
                    Orientacion = loc.Orientation,
                    // CONT-NPC.2: always NPC nombre at publish time (not a per-location editable copy).
                    Nombre = n.Nombre ?? "",
                    Condicion = loc.Condition ?? "",
                });
            }
        }

        foreach (var q in questions)
        {
            var finalQ = plan.QuestionIdMap[q.Id];
            if (q.Responses.Count == 0)
            {
                // Allowed: empty respuestas (observed in BD). Still publish question.
            }

            var responseIds = new List<int>();
            foreach (var r in q.Responses)
            {
                if (!plan.ResponseIdMap.TryGetValue(r.DraftId, out var rid))
                {
                    plan.Errors.Add($"Respuesta DraftId {r.DraftId} sin ID reservado.");
                    continue;
                }
                responseIds.Add(rid);

                if (r.Actions.Count == 0)
                {
                    // Multiacción: at least need one row? Empty response with no actions —
                    // still allocate logical id but insert 0 action rows. BD may allow orphan id unused.
                    // Prefer inserting nothing for empty; respuestas CSV would still list id.
                    // Safer: require ≥1 action or insert placeholder? Spec: multiacción shares ID.
                    // Empty response → error in prevalidation.
                    plan.Errors.Add($"Respuesta {r.DraftId} (→{rid}) no tiene acciones.");
                    continue;
                }

                foreach (var a in r.Actions)
                {
                    var args = ResolveActionArgs(a, plan);
                    plan.ResponseActions.Add(new NpcRespuestaInsertRow
                    {
                        Id = rid,
                        Accion = a.Accion,
                        Args = args,
                        Condicion = a.Condicion ?? "",
                        ResponseDraftId = r.DraftId,
                    });
                }
            }

            plan.Questions.Add(new NpcPreguntaInsertRow
            {
                Id = finalQ,
                ProvisionalId = q.Id,
                Respuestas = string.Join(",", responseIds.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Params = q.Params ?? "",
                Alternos = q.Alternos ?? "",
            });

            // Owner NPC remap is informational only (not a BD column on npc_preguntas).
            if (plan.NpcIdMap.ContainsKey(q.OwnerNpcId) || workspace.Npcs.FindById(q.OwnerNpcId) is not null
                || q.OwnerNpcId > 0)
            {
                // OK — owner may be draft or existing BD NPC
            }
        }

        foreach (var m in missions)
        {
            var questId = plan.QuestIdMap[m.DraftId];
            var stageFinals = new List<int>();
            foreach (var s in m.Stages)
            {
                if (!plan.StageIdMap.TryGetValue(s.Id, out var sid))
                {
                    plan.Errors.Add($"Etapa provisional {s.Id} sin ID reservado.");
                    continue;
                }
                stageFinals.Add(sid);

                var objFinals = new List<int>();
                foreach (var o in s.Objectives)
                {
                    if (!plan.ObjectiveIdMap.TryGetValue(o.Id, out var oid))
                    {
                        plan.Errors.Add($"Objetivo provisional {o.Id} sin ID reservado.");
                        continue;
                    }
                    objFinals.Add(oid);

                    var args = o.Args ?? "";
                    var detalle = o.Detalle ?? "";
                    if (o.Tipo == MissionObjectiveTypes.DeliverItemsToNpc)
                    {
                        args = RemapDeliverArgs(args, plan);
                        if (!string.IsNullOrWhiteSpace(detalle) && detalle == (o.Args ?? ""))
                            detalle = args;
                        else if (detalle.StartsWith('[') && detalle.Contains(','))
                            detalle = RemapDeliverArgs(detalle, plan);
                    }

                    plan.Objectives.Add(new MisionObjetivoInsertRow
                    {
                        Id = oid,
                        ProvisionalId = o.Id,
                        Tipo = o.Tipo,
                        Args = args,
                        Detalle = detalle,
                        EsAlHablar = string.IsNullOrWhiteSpace(o.EsAlHablar) ? "0" : o.EsAlHablar,
                        EsOculto = o.EsOculto,
                        Condicion = o.Condicion ?? "",
                    });
                }

                plan.Stages.Add(new MisionEtapaInsertRow
                {
                    Id = sid,
                    ProvisionalId = s.Id,
                    Nombre = s.Nombre ?? "",
                    Descripcion = s.Descripcion ?? "",
                    Recompensas = s.Rewards.ToRaw(),
                    Objetivos = BuildObjetivosField(objFinals),
                    VariosObj = string.IsNullOrWhiteSpace(s.VariosObj) ? "0" : s.VariosObj,
                });
            }

            var startNpc = ResolveNpcRef(m.StartNpcId, plan);
            plan.Missions.Add(new MisionInsertRow
            {
                Id = questId,
                DraftId = m.DraftId,
                Nombre = m.Nombre ?? "",
                Etapas = string.Join(",", stageFinals.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                PregDarMision = BuildPreg(startNpc, ResolveQuestionRef(m.PregDarPreguntaId, plan)),
                PregMisCompletada = BuildPreg(startNpc, ResolveQuestionRef(m.PregCompletadaPreguntaId, plan)),
                PregMisIncompleta = BuildPreg(startNpc, ResolveQuestionRef(m.PregIncompletaPreguntaId, plan)),
                PuedeRepetirse = m.PuedeRepetirse,
            });
        }

        FillDialogEsPreview(plan, npcs, questions, dialogResolver);
        ValidatePlan(plan, workspace);
        return plan;
    }

    private static void FillDialogEsPreview(
        ContentPublishPlan plan,
        IReadOnlyList<NpcsModeloDraft> npcs,
        IReadOnlyList<DialogQuestionDraft> questions,
        DialogEsIdResolver? dialogResolver)
    {
        foreach (var q in questions)
        {
            if (!plan.QuestionIdMap.TryGetValue(q.Id, out var qid))
                continue;
            TryAddAssignment(plan, DialogEsSpace.Question, qid, q.TextLocal ?? "", $"Pregunta draft {q.Id}");
            plan.DialogEsPreview.Add(new DialogEsPreviewLine
            {
                Kind = "interactive-question",
                Label = "Pregunta: ID conjunto D.q / npc_preguntas",
                DialogQuestionId = qid,
                BdQuestionId = qid,
            });

            foreach (var r in q.Responses)
            {
                if (!plan.ResponseIdMap.TryGetValue(r.DraftId, out var rid))
                    continue;
                TryAddAssignment(plan, DialogEsSpace.Answer, rid, r.TextLocal ?? "", $"Respuesta {r.DraftId:N}");
                plan.DialogEsPreview.Add(new DialogEsPreviewLine
                {
                    Kind = "interactive-answer",
                    Label = "Respuesta: ID conjunto D.a / npc_respuestas",
                    DialogAnswerId = rid,
                    BdResponseId = rid,
                });
            }
        }

        foreach (var npc in npcs)
        {
            if (npc.DialogMode != NpcDialogMode.Simple)
                continue;

            if (npc.IsPendingDialogEs)
            {
                int? reserved = dialogResolver?.ReserveSimpleQuestion();
                if (reserved is int id)
                    TryAddAssignment(plan, DialogEsSpace.Question, id, npc.SimpleDialogTextLocal, $"NPC {npc.Id} diálogo simple");
                plan.DialogEsPreview.Add(new DialogEsPreviewLine
                {
                    Kind = "simple",
                    Label = npc.SimpleDialogTextLocal,
                    DialogQuestionId = reserved,
                    NpcPreguntaColumn = reserved,
                    OwnerNpcDraftId = npc.Id,
                });
            }
            else if (npc.Pregunta > 0)
            {
                plan.DialogEsPreview.Add(new DialogEsPreviewLine
                {
                    Kind = "simple-existing",
                    Label = npc.SimpleDialogTextLocal,
                    DialogQuestionId = npc.Pregunta,
                    NpcPreguntaColumn = npc.Pregunta,
                    OwnerNpcDraftId = npc.Id,
                });
            }
        }
    }

    private static void TryAddAssignment(ContentPublishPlan plan, DialogEsSpace space, int id, string text, string label)
    {
        try
        {
            DialogEsLatin1.Validate(text, label);
        }
        catch (DialogEsEncodingException ex)
        {
            plan.Errors.Add(ex.Message);
            return;
        }

        plan.DialogEsAdditions.Add(new DialogEsAssignment
        {
            Space = space,
            Id = id,
            Text = text,
        });
    }

    private static void ValidatePlan(ContentPublishPlan plan, ContentDraftWorkspace workspace)
    {
        if (!plan.HasWork)
        {
            plan.Errors.Add("No hay borradores pendientes de publicar (¿ya Publicados BD?).");
            return;
        }

        // Structured accion=1 must resolve
        foreach (var row in plan.ResponseActions)
        {
            if (row.Accion == DialogActionCodes.GotoQuestion)
            {
                if (string.IsNullOrWhiteSpace(row.Args)
                    || !int.TryParse(row.Args, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    plan.Errors.Add($"accion=1 sin pregunta resuelta (respuesta lógica {row.Id}).");
                }
            }
            if (row.Accion == DialogActionCodes.StartQuest)
            {
                if (string.IsNullOrWhiteSpace(row.Args)
                    || !int.TryParse(row.Args, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    plan.Errors.Add($"accion=44 sin Quest ID resuelto (respuesta lógica {row.Id}).");
                }
            }
        }

        // Questions must only reference reserved response ids
        foreach (var q in plan.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Respuestas)) continue;
            foreach (var part in q.Respuestas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rid)
                    || !plan.ReservedResponseIds.Contains(rid))
                {
                    plan.Errors.Add($"Pregunta {q.Id} referencia respuesta inexistente '{part}'.");
                }
            }
        }

        // Stages / objectives consistency
        foreach (var s in plan.Stages)
        {
            if (string.IsNullOrWhiteSpace(s.Objetivos)) continue;
            foreach (var part in s.Objetivos.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oid)
                    || !plan.ObjectiveIdMap.ContainsValue(oid))
                {
                    plan.Errors.Add($"Etapa {s.Id} referencia objetivo inexistente '{part}'.");
                }
            }
        }

        foreach (var m in plan.Missions)
        {
            if (string.IsNullOrWhiteSpace(m.Etapas)) continue;
            foreach (var part in m.Etapas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid)
                    || !plan.StageIdMap.ContainsValue(sid))
                {
                    plan.Errors.Add($"Misión {m.Id} referencia etapa inexistente '{part}'.");
                }
            }
        }

        // NPC initial question: if non-zero must exist as reserved or leave as external (>0 without map is OK for existing BD)
        foreach (var n in plan.Npcs)
        {
            if (n.Pregunta <= 0) continue;
            var isReserved = plan.QuestionIdMap.ContainsValue(n.Pregunta);
            var isExternalDraftPending = workspace.Dialogs.Questions.Any(q =>
                !q.PublishedBd && plan.QuestionIdMap.ContainsKey(q.Id) == false && q.Id == n.Pregunta);
            // External live ID: allowed (not in this batch)
            _ = isReserved;
            _ = isExternalDraftPending;
        }

        // CONT-DIALOG.3 — Simple mode requires an existing dialog text ID (no inventing dialog_es).
        foreach (var draft in workspace.Npcs.Drafts.Where(n => !n.PublishedBd))
        {
            if (draft.DialogMode != NpcDialogMode.Simple) continue;
            if (draft.Pregunta <= 0)
            {
                plan.Errors.Add(draft.IsPendingDialogEs
                    ? $"NPC {draft.Id}: diálogo simple pendiente de publicación dialog_es."
                    : $"NPC {draft.Id}: diálogo simple sin texto ni ID existente.");
            }
        }

        // Locations must have mapa/celda user-set? Spec allows 0 — BD may accept. Warn only if negative.
        foreach (var loc in plan.Locations)
        {
            if (loc.Mapa < 0 || loc.Celda < 0)
                plan.Errors.Add($"Ubicación inválida mapa={loc.Mapa} celda={loc.Celda} npc={loc.Npc}.");
            if (!plan.NpcIdMap.ContainsValue(loc.Npc))
                plan.Errors.Add($"Ubicación apunta a NPC no reservado {loc.Npc}.");
        }

        // Duplicate reserved IDs inside plan
        if (plan.ReservedNpcIds.Count != plan.ReservedNpcIds.Distinct().Count())
            plan.Errors.Add("IDs NPC reservados duplicados en el plan.");
        if (plan.ReservedQuestionIds.Count != plan.ReservedQuestionIds.Distinct().Count())
            plan.Errors.Add("IDs pregunta reservados duplicados en el plan.");
        if (plan.ReservedResponseIds.Count != plan.ReservedResponseIds.Distinct().Count())
            plan.Errors.Add("IDs respuesta reservados duplicados en el plan.");
        if (plan.ReservedQuestIds.Count != plan.ReservedQuestIds.Distinct().Count())
            plan.Errors.Add("IDs quest reservados duplicados en el plan.");
    }

    private static int ResolveNpcPregunta(int pregunta, ContentPublishPlan plan)
    {
        if (pregunta <= 0) return 0;
        return plan.QuestionIdMap.TryGetValue(pregunta, out var mapped) ? mapped : pregunta;
    }

    private static int? ResolveNpcRef(int? npcId, ContentPublishPlan plan)
    {
        if (npcId is null or <= 0) return npcId;
        return plan.NpcIdMap.TryGetValue(npcId.Value, out var mapped) ? mapped : npcId;
    }

    private static int? ResolveQuestionRef(int? qid, ContentPublishPlan plan)
    {
        if (qid is null or <= 0) return qid;
        return plan.QuestionIdMap.TryGetValue(qid.Value, out var mapped) ? mapped : qid;
    }

    private static string BuildPreg(int? npcId, int? preguntaId)
    {
        if (npcId is null or <= 0 || preguntaId is null or <= 0) return "";
        return string.Create(CultureInfo.InvariantCulture, $"{npcId.Value};{preguntaId.Value}");
    }

    private static string ResolveActionArgs(DialogActionDraft a, ContentPublishPlan plan)
    {
        if (a.Accion == DialogActionCodes.GotoQuestion)
        {
            if (a.TargetQuestionId is int tq)
            {
                var final = plan.QuestionIdMap.TryGetValue(tq, out var mapped) ? mapped : tq;
                return final.ToString(CultureInfo.InvariantCulture);
            }
            // Structured link missing — if Args looks like provisional draft id, remap
            if (int.TryParse(a.Args, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
                && plan.QuestionIdMap.TryGetValue(raw, out var m2))
                return m2.ToString(CultureInfo.InvariantCulture);
            return a.Args ?? "";
        }

        if (a.Accion == DialogActionCodes.StartQuest)
        {
            if (a.TargetMissionDraftId is Guid mid)
            {
                if (plan.QuestIdMap.TryGetValue(mid, out var qid))
                    return qid.ToString(CultureInfo.InvariantCulture);
                return ""; // unresolved
            }
            // Raw args kept if no structured link
            return a.Args ?? "";
        }

        // Advanced/raw — keep exactly
        return a.Args ?? "";
    }

    private static string BuildObjetivosField(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return "";
        if (ids.Count == 1) return ids[0].ToString(CultureInfo.InvariantCulture);
        return string.Join("|", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
    }

    private static readonly Regex DeliverArgsRegex = new(
        @"^\[(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\]",
        RegexOptions.Compiled);

    private static string RemapDeliverArgs(string args, ContentPublishPlan plan)
    {
        var m = DeliverArgsRegex.Match(args.Trim());
        if (!m.Success) return args;
        var npc = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var item = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var qty = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (plan.NpcIdMap.TryGetValue(npc, out var mapped))
            npc = mapped;
        var suffix = args.Trim().Length > m.Length ? args.Trim()[m.Length..] : "";
        return string.Create(CultureInfo.InvariantCulture, $"[{npc},{item},{qty}]") + suffix;
    }
}
