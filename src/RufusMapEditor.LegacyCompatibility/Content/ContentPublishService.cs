using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.5 content BD publisher. Preview is dry; Execute writes only after explicit confirm.</summary>
public sealed class ContentPublishService
{
    private readonly IContentPublishStore _store;
    private readonly string _journalDir;

    public ContentPublishService(IContentPublishStore store, string? journalDirectory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _journalDir = journalDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RufusMapEditor", "content-publish-journal");
    }

    public async Task<(ContentPublishPlan Plan, IReadOnlyList<ContentTableEngineInfo> Engines)> PreparePreviewAsync(
        ContentDraftWorkspace workspace,
        CancellationToken ct = default,
        string? dialogEsCacheDirectory = null,
        DialogEsSnapshot? dialogEsSnapshot = null,
        string? dialogEsStatusOverride = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RufusLog.Info("Prevalidando contenido");
        var engines = await _store.GetEnginesAsync(ct).ConfigureAwait(false);
        var maxes = await _store.ReadMaxIdsAsync(ct).ConfigureAwait(false);
        var occupancy = new DialogEsIdOccupancy
        {
            BdQuestionMax = maxes.NpcPreguntas,
            BdResponseMax = maxes.NpcRespuestas,
        };
        string cacheStatus;
        DialogEsSnapshot? dialogEs;
        if (dialogEsSnapshot is not null)
        {
            dialogEs = dialogEsSnapshot;
            cacheStatus = dialogEsStatusOverride
                          ?? string.Create(CultureInfo.InvariantCulture, $"dialog_es activo remoto v{dialogEs.Version}");
        }
        else
        {
            dialogEs = TryParseLocalDialogEs(dialogEsCacheDirectory, out cacheStatus);
            if (!string.IsNullOrWhiteSpace(dialogEsStatusOverride))
                cacheStatus = dialogEsStatusOverride;
        }
        var plan = ContentPublishPlanBuilder.Build(workspace, maxes, dialogEs, occupancy);
        plan.DialogEsCacheStatus = cacheStatus;
        plan.ConcurrencyMode = ResolveConcurrencyMode(engines);
        RufusLog.Info($"IDs tentativos · NPC {plan.FormatIdRange(plan.ReservedNpcIds)} · Quest {plan.FormatIdRange(plan.ReservedQuestIds)}");
        return (plan, engines);
    }

    private static DialogEsSnapshot? TryParseLocalDialogEs(string? cacheDirectory, out string status)
    {
        var dir = cacheDirectory ?? LangRemoteSyncService.DefaultCacheDirectory;
        if (!DialogEsLocalCache.TryLoadLatest(dir, out var bytes, out var path, out var err))
        {
            status = err ?? "Sin dialog_es local.";
            return null;
        }

        try
        {
            var snap = DialogEsParser.Parse(bytes);
            status = $"Caché local: {Path.GetFileName(path)} (IDs provisionales)";
            return snap;
        }
        catch (Exception ex)
        {
            status = "Caché dialog_es ilegible: " + ex.Message;
            return null;
        }
    }

    public async Task<ContentPublishOutcome> PublishAsync(
        ContentDraftWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var logs = new List<string>();
        void Log(string s)
        {
            logs.Add(s);
            if (s.StartsWith("OK ", StringComparison.Ordinal)) RufusLog.Ok(s[3..]);
            else if (s.StartsWith("ERROR ", StringComparison.Ordinal)) RufusLog.Error(s[6..]);
            else RufusLog.Info(s.StartsWith("INFO ", StringComparison.Ordinal) ? s[5..] : s);
        }

        Log("INFO Prevalidando contenido");
        var engines = await _store.GetEnginesAsync(ct).ConfigureAwait(false);
        var mode = ResolveConcurrencyMode(engines);
        var useLocks = mode == ContentPublishConcurrencyMode.TableLocks;

        if (useLocks)
        {
            var canLock = await _store.CanLockTablesAsync(ct).ConfigureAwait(false);
            if (!canLock)
            {
                return Fail("LOCK TABLES no permitido. Publicación bloqueada (MyISAM requiere locks).", logs);
            }
        }

        var locked = false;
        var inTx = false;
        ContentPublishPlan? plan = null;
        string? journalPath = null;
        var insertedSomething = false;

        try
        {
            if (useLocks)
            {
                await _store.LockTablesWriteAsync(ContentPublishTables.All, ct).ConfigureAwait(false);
                locked = true;
                Log("INFO LOCK TABLES adquirido");
            }
            else
            {
                await _store.BeginTransactionAsync(ct).ConfigureAwait(false);
                inTx = true;
                Log("INFO Transacción iniciada");
            }

            Log("INFO Reservando IDs");
            var maxes = await _store.ReadMaxIdsAsync(ct).ConfigureAwait(false);
            plan = ContentPublishPlanBuilder.Build(workspace, maxes);
            plan.ConcurrencyMode = mode;

            if (!plan.IsValid)
            {
                return Fail("Prevalidación fallida:\n" + string.Join("\n", plan.Errors), logs, plan);
            }

            // Collision check against live BD
            await AssertNoCollisionsAsync(plan, ct).ConfigureAwait(false);
            Log("OK IDs reservados");

            journalPath = WriteJournal(workspace, plan);
            Log("INFO Journal local: " + journalPath);

            // INSERT order: objectives → stages → missions → responses → questions → npcs → ubicaciones
            // Actually better: npcs first (locations need npc), questions need response ids in CSV but responses can insert first
            // Order: objetivos, etapas, misiones, respuestas, preguntas, npcs_modelo, npcs_ubicacion
            Log("INFO Publicando mision_objetivos...");
            foreach (var o in plan.Objectives)
            {
                await _store.InsertObjetivoAsync(o, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando mision_etapas...");
            foreach (var s in plan.Stages)
            {
                await _store.InsertEtapaAsync(s, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando misiones...");
            foreach (var m in plan.Missions)
            {
                await _store.InsertMisionAsync(m, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando npc_respuestas...");
            foreach (var r in plan.ResponseActions)
            {
                await _store.InsertRespuestaActionAsync(r, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando npc_preguntas...");
            foreach (var q in plan.Questions)
            {
                await _store.InsertPreguntaAsync(q, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando npcs_modelo...");
            foreach (var n in plan.Npcs)
            {
                await _store.InsertNpcAsync(n, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Publicando npcs_ubicacion...");
            foreach (var u in plan.Locations)
            {
                await _store.InsertUbicacionAsync(u, ct).ConfigureAwait(false);
                insertedSomething = true;
            }

            Log("INFO Verificando BD...");
            await VerifyAsync(plan, ct).ConfigureAwait(false);
            Log("OK Verificación BD");

            if (inTx)
            {
                await _store.CommitTransactionAsync(ct).ConfigureAwait(false);
                inTx = false;
            }

            ApplyPublishedState(workspace, plan);
            Log("OK Publicación completada");

            return new ContentPublishOutcome
            {
                Success = true,
                Plan = plan,
                JournalPath = journalPath,
                UsedTableLocks = useLocks,
                UsedTransaction = !useLocks,
                LogLines = logs,
            };
        }
        catch (Exception ex)
        {
            Log("ERROR " + ex.Message);
            var rollbackOk = false;
            var rollbackAttempted = false;

            if (inTx)
            {
                try
                {
                    await _store.RollbackTransactionAsync(ct).ConfigureAwait(false);
                    inTx = false;
                    Log("INFO Rollback transaccional");
                    rollbackAttempted = true;
                    rollbackOk = true;
                }
                catch (Exception rex)
                {
                    Log("ERROR Rollback transaccional falló: " + rex.Message);
                    rollbackAttempted = true;
                }
            }
            else if (useLocks && insertedSomething && plan is not null)
            {
                rollbackAttempted = true;
                try
                {
                    await CompensatingRollbackAsync(plan, ct).ConfigureAwait(false);
                    await VerifyRollbackAsync(plan, ct).ConfigureAwait(false);
                    rollbackOk = true;
                    Log("OK Rollback compensatorio del batch actual");
                }
                catch (Exception rex)
                {
                    Log("ERROR Rollback compensatorio incompleto: " + rex.Message);
                    rollbackOk = false;
                }
            }

            return new ContentPublishOutcome
            {
                Success = false,
                Error = ex.Message,
                Plan = plan,
                JournalPath = journalPath,
                UsedTableLocks = useLocks,
                UsedTransaction = !useLocks,
                CompensatingRollbackAttempted = rollbackAttempted,
                CompensatingRollbackOk = rollbackOk,
                LogLines = logs,
            };
        }
        finally
        {
            if (inTx)
            {
                try { await _store.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { /* ignore */ }
            }
            if (locked)
            {
                try { await _store.UnlockTablesAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { /* ignore */ }
            }
        }
    }

    private static ContentPublishConcurrencyMode ResolveConcurrencyMode(IReadOnlyList<ContentTableEngineInfo> engines)
    {
        if (engines.Count == 0)
            return ContentPublishConcurrencyMode.TableLocks;
        return engines.All(e => e.SupportsTransactions)
            ? ContentPublishConcurrencyMode.Transaction
            : ContentPublishConcurrencyMode.TableLocks;
    }

    private async Task AssertNoCollisionsAsync(ContentPublishPlan plan, CancellationToken ct)
    {
        async Task Check(string table, string col, IReadOnlyList<int> ids, string label)
        {
            if (ids.Count == 0) return;
            var existing = await _store.FindExistingIdsAsync(table, col, ids, ct).ConfigureAwait(false);
            if (existing.Count > 0)
                throw new InvalidOperationException(
                    $"Colisión {label}: IDs ya existen: {string.Join(",", existing)}");
        }

        await Check(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, plan.ReservedNpcIds, "NPC").ConfigureAwait(false);
        await Check(NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, plan.ReservedQuestionIds, "pregunta").ConfigureAwait(false);
        await Check(NpcRespuestasColumns.DefaultTable, NpcRespuestasColumns.Id, plan.ReservedResponseIds, "respuesta").ConfigureAwait(false);
        await Check(MisionesColumns.DefaultTable, MisionesColumns.Id, plan.ReservedQuestIds, "misión").ConfigureAwait(false);
        await Check(MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, plan.ReservedStageIds, "etapa").ConfigureAwait(false);
        await Check(MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, plan.ReservedObjectiveIds, "objetivo").ConfigureAwait(false);
    }

    private async Task VerifyAsync(ContentPublishPlan plan, CancellationToken ct)
    {
        async Task ExpectCount(string table, string col, IReadOnlyList<int> ids, int expected, string label)
        {
            if (expected == 0) return;
            var n = await _store.CountByIdsAsync(table, col, ids, ct).ConfigureAwait(false);
            if (n != expected)
                throw new InvalidOperationException($"Verificación {label}: esperaba {expected}, hay {n}");
        }

        await ExpectCount(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, plan.ReservedNpcIds, plan.Npcs.Count, "npcs_modelo").ConfigureAwait(false);
        await ExpectCount(NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, plan.ReservedQuestionIds, plan.Questions.Count, "npc_preguntas").ConfigureAwait(false);
        await ExpectCount(MisionesColumns.DefaultTable, MisionesColumns.Id, plan.ReservedQuestIds, plan.Missions.Count, "misiones").ConfigureAwait(false);
        await ExpectCount(MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, plan.ReservedStageIds, plan.Stages.Count, "mision_etapas").ConfigureAwait(false);
        await ExpectCount(MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, plan.ReservedObjectiveIds, plan.Objectives.Count, "mision_objetivos").ConfigureAwait(false);

        if (plan.Locations.Count > 0)
        {
            var locCount = await _store.CountUbicacionesByNpcIdsAsync(plan.ReservedNpcIds, ct).ConfigureAwait(false);
            if (locCount < plan.Locations.Count)
                throw new InvalidOperationException($"Verificación ubicaciones: esperaba ≥{plan.Locations.Count}, hay {locCount}");
        }

        if (plan.ResponseActionRowCount > 0)
        {
            var rows = await _store.CountRespuestaRowsByLogicalIdsAsync(plan.ReservedResponseIds, ct).ConfigureAwait(false);
            if (rows != plan.ResponseActionRowCount)
                throw new InvalidOperationException($"Verificación npc_respuestas filas: esperaba {plan.ResponseActionRowCount}, hay {rows}");
        }
    }

    private async Task CompensatingRollbackAsync(ContentPublishPlan plan, CancellationToken ct)
    {
        // Only delete IDs reserved by THIS plan — never pre-existing.
        await _store.DeleteUbicacionesByNpcIdsAsync(plan.ReservedNpcIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, plan.ReservedNpcIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, plan.ReservedQuestionIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(NpcRespuestasColumns.DefaultTable, NpcRespuestasColumns.Id, plan.ReservedResponseIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(MisionesColumns.DefaultTable, MisionesColumns.Id, plan.ReservedQuestIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, plan.ReservedStageIds, ct).ConfigureAwait(false);
        await _store.DeleteByIdsAsync(MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, plan.ReservedObjectiveIds, ct).ConfigureAwait(false);
    }

    private async Task VerifyRollbackAsync(ContentPublishPlan plan, CancellationToken ct)
    {
        async Task ExpectZero(string table, string col, IReadOnlyList<int> ids, string label)
        {
            if (ids.Count == 0) return;
            var n = await _store.CountByIdsAsync(table, col, ids, ct).ConfigureAwait(false);
            if (n != 0)
                throw new InvalidOperationException($"Tras rollback, {label} aún tiene {n} filas del batch.");
        }

        await ExpectZero(NpcsModeloColumns.DefaultTable, NpcsModeloColumns.Id, plan.ReservedNpcIds, "npcs_modelo").ConfigureAwait(false);
        await ExpectZero(NpcPreguntasColumns.DefaultTable, NpcPreguntasColumns.Id, plan.ReservedQuestionIds, "npc_preguntas").ConfigureAwait(false);
        await ExpectZero(NpcRespuestasColumns.DefaultTable, NpcRespuestasColumns.Id, plan.ReservedResponseIds, "npc_respuestas").ConfigureAwait(false);
        await ExpectZero(MisionesColumns.DefaultTable, MisionesColumns.Id, plan.ReservedQuestIds, "misiones").ConfigureAwait(false);
        await ExpectZero(MisionEtapasColumns.DefaultTable, MisionEtapasColumns.Id, plan.ReservedStageIds, "etapas").ConfigureAwait(false);
        await ExpectZero(MisionObjetivosColumns.DefaultTable, MisionObjetivosColumns.Id, plan.ReservedObjectiveIds, "objetivos").ConfigureAwait(false);
        var locs = await _store.CountUbicacionesByNpcIdsAsync(plan.ReservedNpcIds, ct).ConfigureAwait(false);
        if (locs != 0)
            throw new InvalidOperationException($"Tras rollback, ubicaciones residuales={locs}");
    }

    public static void ApplyPublishedState(ContentDraftWorkspace workspace, ContentPublishPlan plan)
    {
        foreach (var n in workspace.Npcs.Drafts)
        {
            if (!plan.NpcIdMap.TryGetValue(n.Id, out var finalId)) continue;
            n.Id = finalId;
            if (plan.QuestionIdMap.TryGetValue(n.Pregunta, out var pq))
                n.Pregunta = pq;
            else if (n.Pregunta > 0)
            {
                // may already be remapped in plan row — sync from plan.Npcs
                var row = plan.Npcs.FirstOrDefault(x => x.Id == finalId);
                if (row is not null) n.Pregunta = row.Pregunta;
            }
            n.PublishedBd = true;
        }

        foreach (var q in workspace.Dialogs.Questions)
        {
            if (!plan.QuestionIdMap.TryGetValue(q.Id, out var finalQ)) continue;
            if (plan.NpcIdMap.TryGetValue(q.OwnerNpcId, out var own))
                q.OwnerNpcId = own;
            q.Id = finalQ;
            foreach (var r in q.Responses)
            {
                if (plan.ResponseIdMap.TryGetValue(r.DraftId, out var rid))
                    r.PublishedResponseId = rid;
                foreach (var a in r.Actions)
                {
                    if (a.Accion == DialogActionCodes.GotoQuestion && a.TargetQuestionId is int tq
                        && plan.QuestionIdMap.TryGetValue(tq, out var mappedQ))
                    {
                        a.TargetQuestionId = mappedQ;
                        a.SyncGotoArgs();
                    }
                    if (a.Accion == DialogActionCodes.StartQuest && a.TargetMissionDraftId is Guid mid
                        && plan.QuestIdMap.TryGetValue(mid, out var questId))
                    {
                        a.Args = questId.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            q.PublishedBd = true;
        }

        foreach (var m in workspace.Missions.Missions)
        {
            if (!plan.QuestIdMap.TryGetValue(m.DraftId, out var questId)) continue;
            m.PublishedQuestId = questId;
            if (m.StartNpcId is int sn && plan.NpcIdMap.TryGetValue(sn, out var snf))
                m.StartNpcId = snf;
            if (m.PregDarPreguntaId is int pd && plan.QuestionIdMap.TryGetValue(pd, out var pdf))
                m.PregDarPreguntaId = pdf;
            if (m.PregIncompletaPreguntaId is int pi && plan.QuestionIdMap.TryGetValue(pi, out var pif))
                m.PregIncompletaPreguntaId = pif;
            if (m.PregCompletadaPreguntaId is int pc && plan.QuestionIdMap.TryGetValue(pc, out var pcf))
                m.PregCompletadaPreguntaId = pcf;

            foreach (var s in m.Stages.ToList())
            {
                if (!plan.StageIdMap.TryGetValue(s.Id, out var sid)) continue;
                foreach (var o in s.Objectives.ToList())
                {
                    if (plan.ObjectiveIdMap.TryGetValue(o.Id, out var oid))
                        o.Id = oid;
                }
                s.Id = sid;
            }
            m.PublishedBd = true;
        }
    }

    private string WriteJournal(ContentDraftWorkspace workspace, ContentPublishPlan plan)
    {
        Directory.CreateDirectory(_journalDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(_journalDir, $"content-publish-{stamp}.json");
        var journal = new ContentPublishJournal
        {
            TimestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            WorkspaceJson = ContentWorkspaceSerializer.Serialize(workspace),
            Maxes = plan.Maxes,
            ReservedNpcIds = plan.ReservedNpcIds.ToList(),
            ReservedQuestionIds = plan.ReservedQuestionIds.ToList(),
            ReservedResponseIds = plan.ReservedResponseIds.ToList(),
            ReservedQuestIds = plan.ReservedQuestIds.ToList(),
            ReservedStageIds = plan.ReservedStageIds.ToList(),
            ReservedObjectiveIds = plan.ReservedObjectiveIds.ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static ContentPublishOutcome Fail(string error, List<string> logs, ContentPublishPlan? plan = null)
    {
        logs.Add("ERROR " + error);
        RufusLog.Error(error);
        return new ContentPublishOutcome
        {
            Success = false,
            Error = error,
            Plan = plan,
            LogLines = logs,
        };
    }
}
