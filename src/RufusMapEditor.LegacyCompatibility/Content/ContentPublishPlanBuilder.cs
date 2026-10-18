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
        DialogEsIdOccupancy? dialogEsOccupancy = null,
        QuestsEsSnapshot? questsEs = null,
        Func<int, bool>? itemIdExistsInObjetosModelo = null,
        Func<int, bool>? mobIdExistsInMobsModelo = null,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(maxes);
        maxes = ContentIdReservation.EffectiveContentMax(maxes, questsEs);
        var plan = new ContentPublishPlan { Maxes = maxes };
        itemIdExistsInObjetosModelo ??= ObjetosModeloNameCache.SharedExistsPredicate;
        mobIdExistsInMobsModelo ??= MobsModeloNameCache.SharedExistsPredicate;


        var npcs = workspace.Npcs.Drafts
            .Where(n => !n.PublishedBd && ContentPublishNpcSelection.Includes(n.Id, npcIdFilter))
            .ToList();
        var npcIds = npcs.Select(n => n.Id).ToHashSet();
        var interactiveNpcIds = npcs
            .Where(n => n.DialogMode == NpcDialogMode.Interactive)
            .Select(n => n.Id)
            .ToHashSet();

        // CONT.5.1 — only content reachable from NPCs in this publish batch.
        // CONT-DIALOG.3 — Simple mode never publishes npc_preguntas / npc_respuestas.
        // Diálogo simple → no misiones (ni nuevas ni updates) en el plan BD.
        // Missions without StartNpcId on a batch NPC are orphans (toggle off / leftovers).
        var questions = workspace.Dialogs.Questions
            .Where(q => !q.PublishedBd
                        && npcIds.Contains(q.OwnerNpcId)
                        && interactiveNpcIds.Contains(q.OwnerNpcId))
            .ToList();
        var missions = workspace.Missions.Missions
            .Where(m => !m.PublishedBd
                        && m.StartNpcId is int sid
                        && npcIds.Contains(sid)
                        && !IsStartNpcSimpleDialog(workspace, m))
            .ToList();
        var updateMissions = workspace.Missions.Missions
            .Where(m => m.PublishedBd
                        && m.HasPendingBdChanges
                        && m.PublishedQuestId is int and > 0
                        && !IsStartNpcSimpleDialog(workspace, m)
                        && (npcIdFilter is null
                            || (m.StartNpcId is int usid
                                && ContentPublishNpcSelection.Includes(usid, npcIdFilter))))
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
        foreach (var m in updateMissions)
            plan.QuestIdMap[m.DraftId] = m.PublishedQuestId!.Value;

        var nextStage = maxes.MisionEtapas + 1;
        foreach (var m in missions)
        {
            foreach (var s in m.Stages)
                plan.StageIdMap[s.Id] = nextStage++;
        }

        foreach (var m in updateMissions)
        {
            var legacyAllPublished = m.PublishedStageIds.Count == 0;
            foreach (var s in m.Stages)
            {
                if (legacyAllPublished || m.PublishedStageIds.Contains(s.Id))
                    plan.StageIdMap[s.Id] = s.Id;
                else
                    plan.StageIdMap[s.Id] = nextStage++;
            }
        }

        var nextObj = maxes.MisionObjetivos + 1;
        foreach (var m in missions)
        {
            foreach (var s in m.Stages)
            foreach (var o in s.Objectives)
                plan.ObjectiveIdMap[o.Id] = nextObj++;
        }

        foreach (var m in updateMissions)
        {
            var legacyAllPublished = m.PublishedObjectiveIds.Count == 0;
            foreach (var s in m.Stages)
            foreach (var o in s.Objectives)
            {
                if (legacyAllPublished || m.PublishedObjectiveIds.Contains(o.Id))
                    plan.ObjectiveIdMap[o.Id] = o.Id;
                else
                    plan.ObjectiveIdMap[o.Id] = nextObj++;
            }
        }

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

        foreach (var m in missions.Concat(updateMissions))
        {
            var isUpdate = m.PublishedBd && m.HasPendingBdChanges;
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
                    else if (MissionObjectiveNpcRemap.UsesNpcInArgs(o.Tipo))
                    {
                        args = MissionObjectiveNpcRemap.RemapLeadingNpc(args, plan.NpcIdMap);
                        if (string.Equals(detalle, o.Args, StringComparison.Ordinal))
                            detalle = args;
                        else if (MissionObjectiveNpcRemap.UsesNpcInArgs(o.Tipo))
                            detalle = MissionObjectiveNpcRemap.RemapLeadingNpc(detalle, plan.NpcIdMap);
                    }

                    // QUICK_DELETE_AND_START_NPC_BINDING.2 — tipo 2/3 always publish under StartNpc.
                    if (MissionShowDeliverNpcBinding.IsBoundTipo(o.Tipo))
                    {
                        var startFinal = ResolveNpcRef(m.StartNpcId, plan);
                        if (startFinal is not int sn || sn <= 0)
                        {
                            plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                                $"Objetivo {oid}: {MissionShowDeliverNpcBinding.MissingStartNpcMessage}"));
                        }
                        else
                        {
                            var rewritten = MissionShowDeliverNpcBinding.RewriteArgsNpc(args, sn);
                            if (!string.Equals(rewritten, args, StringComparison.Ordinal))
                            {
                                if (string.Equals(detalle, args, StringComparison.Ordinal))
                                    detalle = rewritten;
                                args = rewritten;
                            }
                        }
                    }

                    // Auto-return must publish under definitive StartNpc.
                    if (o.IsAutoReturnToStartNpc)
                    {
                        var startFinal = ResolveNpcRef(m.StartNpcId, plan);
                        if (startFinal is int sn && sn > 0)
                        {
                            var (_, x, y) = MissionObjectiveArgsCodec.StripCoords(args);
                            args = MissionObjectiveArgsCodec.BuildTalk(sn, x, y);
                            detalle = args;
                        }
                    }

                    var objIsUpdate = isUpdate
                        && (m.PublishedObjectiveIds.Count == 0 || m.PublishedObjectiveIds.Contains(o.Id));
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
                        IsUpdate = objIsUpdate,
                    });
                }

                var stageIsUpdate = isUpdate
                    && (m.PublishedStageIds.Count == 0 || m.PublishedStageIds.Contains(s.Id));
                plan.Stages.Add(new MisionEtapaInsertRow
                {
                    Id = sid,
                    ProvisionalId = s.Id,
                    Nombre = s.Nombre ?? "",
                    Descripcion = s.Descripcion ?? "",
                    Recompensas = s.Rewards.ToRaw(),
                    Objetivos = BuildObjetivosField(objFinals),
                    VariosObj = string.IsNullOrWhiteSpace(s.VariosObj) ? "" : s.VariosObj.Trim(),
                    IsUpdate = stageIsUpdate,
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
                IsUpdate = isUpdate,
            });

            if (isUpdate)
            {
                var present = m.Stages.SelectMany(s => s.Objectives).Select(o => o.Id).ToHashSet();
                foreach (var id in m.PublishedObjectiveIds
                             .Concat(m.PendingDeletedObjectiveIds)
                             .Where(id => id > 0)
                             .Distinct())
                {
                    if (present.Contains(id)) continue;
                    if (!plan.ObjectiveIdsToDelete.Contains(id))
                        plan.ObjectiveIdsToDelete.Add(id);
                }
            }
        }

        FillDialogEsPreview(plan, npcs, questions, dialogResolver);
        ValidatePlan(plan, workspace, itemIdExistsInObjetosModelo, mobIdExistsInMobsModelo);
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
        string prepared;
        try
        {
            // Latin1 belongs to client/dialog_es scope — never block BD publish.
            prepared = DialogEsLatin1.Prepare(text, label);
        }
        catch (DialogEsEncodingException ex)
        {
            plan.ClientWarnings.Add(ex.Message);
            return;
        }

        plan.DialogEsAdditions.Add(new DialogEsAssignment
        {
            Space = space,
            Id = id,
            Text = prepared,
        });
    }

    private static void ValidatePlan(
        ContentPublishPlan plan,
        ContentDraftWorkspace workspace,
        Func<int, bool>? itemIdExistsInObjetosModelo,
        Func<int, bool>? mobIdExistsInMobsModelo)
    {
        if (!plan.HasWork)
        {
            plan.Errors.Add("No hay borradores pendientes de publicar (¿ya Publicados BD?).");
            return;
        }

        // MISSIONS.OBJECTIVES.REMOVE_UNCONFIRMED_MANUAL_TYPE0.3
        foreach (var o in plan.Objectives)
        {
            if (o.Tipo == MissionObjectiveTypes.Manual)
            {
                plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Objetivo {o.Id}: {MissionObjectiveUiSync.UnsupportedManualMessage}"));
            }
        }

        // MISSIONS.OBJECTIVES.ITEM_ID_DIRECT_INPUT.1 — tipo 2/3 Item ID vs objetos_modelo.
        foreach (var o in plan.Objectives)
        {
            if (o.Tipo is not (MissionObjectiveTypes.ShowItemToNpc or MissionObjectiveTypes.DeliverItemsToNpc))
                continue;

            if (!MissionObjectiveArgsCodec.TryGetShowOrDeliverItemId(o.Args, out var itemId))
            {
                plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Objetivo {o.Id}: Indica un Item ID."));
                continue;
            }

            if (itemIdExistsInObjetosModelo is not null && !itemIdExistsInObjetosModelo(itemId))
            {
                plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Objetivo {o.Id}: {ObjetosModeloNameCache.NotFoundMessage} (Item ID {itemId})."));
            }
        }

        // MAP_DELETE_AND_MONSTER_ID_INPUT.4 — tipo 6 Monster ID vs mobs_modelo.
        foreach (var o in plan.Objectives)
        {
            if (o.Tipo != MissionObjectiveTypes.DefeatMobs)
                continue;

            var pairs = MissionObjectiveArgsCodec.ParseDefeatMobPairs(o.Args);
            if (pairs.Count == 0)
            {
                plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Objetivo {o.Id}: Indica un Monster ID."));
                continue;
            }

            foreach (var (mobId, _) in pairs)
            {
                if (mobIdExistsInMobsModelo is not null && !mobIdExistsInMobsModelo(mobId))
                {
                    plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"Objetivo {o.Id}: {MobsModeloNameCache.NotFoundMessage} (Monster ID {mobId})."));
                }
            }
        }

        // QUICK_DELETE_AND_START_NPC_BINDING.2 — tipo 2/3 require StartNpc.
        foreach (var draft in workspace.Missions.Missions)
        {
            if (!plan.QuestIdMap.ContainsKey(draft.DraftId))
                continue;

            var bound = draft.Stages.SelectMany(s => s.Objectives)
                .Where(o => MissionShowDeliverNpcBinding.IsBoundTipo(o.Tipo))
                .ToList();
            if (bound.Count == 0)
                continue;

            var startFinal = ResolveNpcRef(draft.StartNpcId, plan);
            if (startFinal is not int expectedNpc || expectedNpc <= 0)
            {
                plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Misión '{draft.Nombre}': {MissionShowDeliverNpcBinding.MissingStartNpcMessage}"));
                continue;
            }

            foreach (var o in bound)
            {
                var planned = plan.Objectives.FirstOrDefault(r => r.ProvisionalId == o.Id);
                if (planned is null)
                    continue;
                if (!MissionObjectiveNpcRemap.TryReadLeadingNpc(planned.Args, out var plannedNpc)
                    || plannedNpc != expectedNpc)
                {
                    plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"Objetivo {planned.Id}: NPC destino ≠ StartNpc {expectedNpc}."));
                }
            }
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

        // NPC_ID_SYNC.FIX.7 — planned auto-return args must use definitive StartNpc.
        foreach (var draft in workspace.Missions.Missions)
        {
            if (!plan.QuestIdMap.ContainsKey(draft.DraftId))
                continue;

            var startFinal = ResolveNpcRef(draft.StartNpcId, plan);
            if (startFinal is not int expectedNpc || expectedNpc <= 0)
                continue;

            foreach (var o in draft.Stages.SelectMany(s => s.Objectives).Where(x => x.IsAutoReturnToStartNpc))
            {
                var planned = plan.Objectives.FirstOrDefault(r => r.ProvisionalId == o.Id);
                if (planned is null)
                    continue;
                if (!MissionObjectiveNpcRemap.TryReadLeadingNpc(planned.Args, out var plannedNpc)
                    || plannedNpc != expectedNpc)
                {
                    plan.Errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"Misión auto-return NPC ≠ StartNpc {expectedNpc}. {QuestsEsAutoReturnCoordsRepair.NpcMismatchMessage}"));
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

        // CONT-DIALOG.3 — Simple mode: local text must be in dialog_es before BD.
        // Only NPCs in this publish batch (selection / filter) — others can wait.
        foreach (var draft in workspace.Npcs.Drafts.Where(n => !n.PublishedBd))
        {
            if (!plan.NpcIdMap.ContainsKey(draft.Id))
                continue;
            if (draft.DialogMode != NpcDialogMode.Simple) continue;
            if (draft.IsPendingDialogEs)
            {
                plan.Errors.Add($"NPC {draft.Id}: diálogo simple pendiente de publicación dialog_es.");
            }
            else if (draft.Pregunta <= 0)
            {
                plan.Errors.Add($"NPC {draft.Id}: diálogo simple sin texto ni ID existente.");
            }
        }

        // CONTENT.NPC.SCALEX.OUT_OF_RANGE.FIX.1 — tinyint signed; block before MySQL Out of range.
        foreach (var n in plan.Npcs)
        {
            foreach (var err in NpcsModeloScale.ValidateNpc(n.Id, n.Nombre, n.ScaleX, n.ScaleY))
                plan.Errors.Add(err);
        }

        // DIALOGFLOW.3 — duplicate friendly actions / Teleport ↔ GotoQuestion on same response.
        foreach (var q in workspace.Dialogs.Questions)
        {
            foreach (var r in q.Responses)
            {
                foreach (var err in DialogResponseActionRules.ValidateResponse(r))
                    plan.Errors.Add($"{err} (respuesta {r.DraftId}).");
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

        // Duplicate reserved IDs inside plan (CONTENT.IDS.MAX.1)
        AddDuplicateIdErrors(plan, plan.ReservedNpcIds, "NPC");
        AddDuplicateIdErrors(plan, plan.ReservedQuestionIds, "pregunta");
        AddDuplicateIdErrors(plan, plan.ReservedResponseIds, "respuesta");
        AddDuplicateIdErrors(plan, plan.ReservedQuestIds, "misión");
        AddDuplicateIdErrors(plan, plan.ReservedStageIds, "etapa");
        AddDuplicateIdErrors(plan, plan.ReservedObjectiveIds, "objetivo");
    }

    /// <summary>
    /// Diálogo simple never owns quest BD work — leftover HasPendingBdChanges on a
    /// mission whose StartNpc is Simple must not pollute «Publicar contenido en BD».
    /// </summary>
    internal static bool IsStartNpcSimpleDialog(ContentDraftWorkspace workspace, MissionDraft mission)
    {
        if (mission.StartNpcId is not int sid || sid <= 0)
            return false;
        var npc = workspace.Npcs.FindById(sid);
        return npc is not null && npc.DialogMode == NpcDialogMode.Simple;
    }

    private static void AddDuplicateIdErrors(ContentPublishPlan plan, IReadOnlyList<int> ids, string kind)
    {
        var seen = new HashSet<int>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                plan.Errors.Add(ContentIdReservation.FormatDuplicateError(kind, id));
        }
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
