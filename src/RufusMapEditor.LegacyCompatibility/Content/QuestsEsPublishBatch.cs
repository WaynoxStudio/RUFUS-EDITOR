using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class QuestsEsPublishBinding
{
    public required Guid MissionDraftId { get; init; }
    public required int QuestId { get; init; }
    public required string QuestName { get; init; }
    public required string Kind { get; init; }
    public QuestsEsMissionAssignment? Assignment { get; init; }
    public int StageCount { get; init; }
    public int ObjectiveCount { get; init; }
    public int? PreviousPlannedQuestId { get; init; }
    /// <summary>e.g. FORMA AVM1 NO CANÓNICA when only Q.q bytecode layout must be repaired.</summary>
    public string? RepairReason { get; init; }
}

public sealed class QuestsEsPublishBatch
{
    public required IReadOnlyList<QuestsEsPublishBinding> Bindings { get; init; }
    public required int SourceVersion { get; init; }
    public required int TargetVersion { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Unpublished drafts with stages that are not in ContentPublishPlan (e.g. no StartNpc in lote).
    /// Not written to quests_es; kept for diagnostics only — hidden from main preview by default.
    /// </summary>
    public IReadOnlyList<string> ExcludedDraftNames { get; init; } = Array.Empty<string>();

    /// <summary>When true, append BORRADORES EXCLUIDOS section (off by default).</summary>
    public bool ShowExcludedDraftsInPreview { get; init; }

    /// <summary>True when IDs come from ContentPublishPlan and BD is not published yet.</summary>
    public bool IsPreviewPendingBd { get; init; }

    /// <summary>COORDS_AND_PUBLISHED_REPAIR.6 — explicit Q.o[4663] coords repair preview.</summary>
    public QuestsEsAutoReturnCoordsRepair.PreviewInfo? AutoReturnCoordsRepair { get; init; }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<QuestsEsMissionAssignment> Additions =>
        Bindings.Where(b => b.Assignment is not null).Select(b => b.Assignment!).ToList();

    public int NewMissionCount => Additions.Count;

    public string FormatPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine(IsPreviewPendingBd
            ? "Estado: PREVISIÓN · pendiente de publicar BD"
            : "Estado: LISTO PARA PUBLICAR");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Actual: {SourceVersion}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Previsto nuevo: {(HasWritableEntries ? TargetVersion : SourceVersion)}"));
        sb.AppendLine();

        foreach (var note in Notes)
            sb.AppendLine(note);
        if (Notes.Count > 0)
            sb.AppendLine();

        if (AutoReturnCoordsRepair is { } ar)
        {
            sb.AppendLine("AUTO-RETURN REPAIR");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Quest ID: {ar.QuestId}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Stage ID: {ar.StageId}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Objective ID: {ar.ObjectiveId}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"NPC inicial real: {ar.NpcIdCorrect}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"NPC actual del objetivo: {ar.NpcIdCurrent}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"NPC correcto: {ar.NpcIdCorrect}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"NPC Map ID: {ar.NpcMapId}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"X: {ar.X}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Y: {ar.Y}"));
            sb.AppendLine();
            sb.AppendLine(ar.BdNeedsUpdate
                ? string.Create(CultureInfo.InvariantCulture,
                    $"BD: UPDATE args {ar.BdArgsCurrent} → {ar.BdArgsPredicted}")
                : "BD: SIN CAMBIOS");
            sb.AppendLine();
            sb.AppendLine("Actual:");
            sb.AppendLine(ar.ActualLiteral);
            sb.AppendLine();
            sb.AppendLine("Previsto:");
            sb.AppendLine(ar.PredictedLiteral);
            sb.AppendLine();
            sb.AppendLine("npc_es: SIN CAMBIOS");
            sb.AppendLine();
        }

        var questWrites = new List<(QuestsEsPublishBinding Binding, QuestsEsMissionAssignment Assignment)>();
        var stageWrites = new List<(QuestsEsPublishBinding Binding, QuestsEsStageAssignment Stage)>();
        var objectiveWrites = new List<(QuestsEsPublishBinding Binding, QuestsEsObjectiveAssignment Objective)>();
        var conflicts = new List<QuestsEsPublishBinding>();

        foreach (var b in Bindings)
        {
            if (b.Kind == "orphan" || (b.Assignment is null && b.Kind != "conflict"))
                continue;
            if (b.Kind == "conflict" || b.Assignment is null)
            {
                conflicts.Add(b);
                continue;
            }

            var a = b.Assignment;
            if (a.WriteQuest)
                questWrites.Add((b, a));
            foreach (var st in a.Stages)
                stageWrites.Add((b, st));
            foreach (var o in a.Objectives)
                objectiveWrites.Add((b, o));
        }

        sb.AppendLine("QUEST / Q.q");
        if (questWrites.Count == 0)
        {
            sb.AppendLine("SIN CAMBIOS");
        }
        else
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Cambios: {questWrites.Count}"));
            sb.AppendLine();
            foreach (var (b, a) in questWrites)
            {
                if (b.PreviousPlannedQuestId is int prev && prev != a.Quest.QuestId)
                {
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"⚠ IDs actualizados tras BD: previsto {prev} → publicado {a.Quest.QuestId}"));
                }

                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Quest ID: {a.Quest.QuestId}"));
                if (b.Kind == "replace")
                {
                    sb.AppendLine(string.IsNullOrWhiteSpace(b.RepairReason)
                        ? "Acción: actualizar nombre / reparar formato si corresponde"
                        : "Acción: reparar entrada existente");
                    if (!string.IsNullOrWhiteSpace(b.RepairReason))
                        sb.AppendLine($"Motivo: {b.RepairReason}");
                }
                else if (IsPreviewPendingBd)
                {
                    sb.AppendLine("Acción: nueva (previsto)");
                }
                else
                {
                    sb.AppendLine("Acción: nueva");
                }

                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Q.q[{a.Quest.QuestId}] = \"{a.Quest.Name}\";"));
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("ETAPAS / Q.s");
        if (stageWrites.Count == 0)
        {
            sb.AppendLine("SIN CAMBIOS");
        }
        else
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Cambios: {stageWrites.Count}"));
            sb.AppendLine();
            foreach (var (_, st) in stageWrites)
            {
                var r = QuestsEsCodec.EncodeRewards(st.Rewards);
                var d = QuestsEsCodec.EncodeStageDescription(st.Description);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Etapa ID: {st.StageId}"));
                sb.AppendLine($"Nombre: {st.Name}");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Q.s[{st.StageId}] = {{ n: \"{st.Name}\", d: {QuestsEsCodec.FormatValue(d)}, r: {QuestsEsCodec.FormatRewards(r)}, rbl: [] }}"));
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("OBJETIVOS / Q.o");
        if (objectiveWrites.Count == 0)
        {
            sb.AppendLine("SIN CAMBIOS");
        }
        else
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Cambios: {objectiveWrites.Count}"));
            sb.AppendLine();
            foreach (var (_, o) in objectiveWrites)
            {
                var draft = new MissionObjectiveDraft
                {
                    Id = o.ObjectiveId,
                    Tipo = o.Type,
                    Args = o.Args,
                    Detalle = o.Detalle,
                };
                var p = QuestsEsCodec.EncodeObjectiveParameters(draft);
                var (x, y) = QuestsEsCodec.ReadObjectiveCoords(draft);
                var xy = x is int xi && y is int yi
                    ? string.Create(CultureInfo.InvariantCulture, $", x: {xi}, y: {yi}")
                    : "";
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Objetivo ID: {o.ObjectiveId}"));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Tipo: {o.Type}"));
                if (o.Type is MissionObjectiveTypes.TalkToNpc or MissionObjectiveTypes.ReturnToNpc)
                {
                    var npc = MissionObjectiveArgsCodec.ParseBracketInts(
                        MissionObjectiveArgsCodec.StripCoords(o.Args).Core);
                    if (npc.Length > 0)
                        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"NPC ID: {npc[0]}"));
                    sb.AppendLine(x is int xv
                        ? string.Create(CultureInfo.InvariantCulture, $"x: {xv}")
                        : "x: DATO PENDIENTE DE CONFIRMAR");
                    sb.AppendLine(y is int yv
                        ? string.Create(CultureInfo.InvariantCulture, $"y: {yv}")
                        : "y: DATO PENDIENTE DE CONFIRMAR");
                }

                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Q.o[{o.ObjectiveId}] = {{ t: {o.Type}, p: {QuestsEsCodec.FormatValue(p)}{xy} }}"));
                sb.AppendLine();
            }
        }

        if (conflicts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CONFLICTOS");
            foreach (var b in conflicts)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Quest ID: {b.QuestId}"));
                sb.AppendLine($"Nombre: {b.QuestName}");
                sb.AppendLine("⚠ ID ya existe en quests_es remoto.");
                sb.AppendLine();
            }
        }

        if (ShowExcludedDraftsInPreview && ExcludedDraftNames.Count > 0)
        {
            sb.AppendLine("--------------------------------");
            sb.AppendLine("BORRADORES EXCLUIDOS DEL LOTE");
            sb.AppendLine("--------------------------------");
            foreach (var name in ExcludedDraftNames)
                sb.AppendLine("- " + name);
            sb.AppendLine();
        }

        if (IsPreviewPendingBd)
        {
            sb.AppendLine("Vista previa completa disponible. Publica primero el contenido en BD para");
            sb.AppendLine("confirmar los IDs de misión.");
        }

        sb.AppendLine();
        sb.AppendLine(AutoReturnCoordsRepair is { BdNeedsUpdate: true }
            ? "Escrituras BD: 1 (UPDATE mision_objetivos 4663 args)"
            : "Escrituras BD: 0");
        return sb.ToString();
    }

    private bool HasWritableEntries =>
        Bindings.Any(b => b.Assignment is not null
                          && (b.Assignment.WriteQuest
                              || b.Assignment.Stages.Count > 0
                              || b.Assignment.Objectives.Count > 0));
}

public static class QuestsEsPublishBatchBuilder
{
    public static bool IsPendingQuestsEs(MissionDraft mission) =>
        mission.PublishedBd
        && mission.PublishedQuestId is int qid and > 0
        && mission.Stages.Count > 0
        && !mission.QuestsEsPublished;

    /// <summary>Already written once — still re-check live SWF for structural repair.</summary>
    public static bool MayNeedQuestsEsRepair(MissionDraft mission) =>
        mission.PublishedBd
        && mission.PublishedQuestId is int and > 0
        && mission.Stages.Count > 0
        && mission.QuestsEsPublished;

    /// <summary>
    /// Missions still needing BD before their quests_es IDs are confirmed —
    /// only those that would enter ContentPublishPlan (linked to an unpublished NPC).
    /// Orphan drafts do not block client publish.
    /// </summary>
    public static bool HasUnpublishedBdMissions(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var unpublishedNpcIds = workspace.Npcs.Drafts
            .Where(n => !n.PublishedBd)
            .Select(n => n.Id)
            .ToHashSet();
        return workspace.Missions.Missions.Any(m =>
            !m.PublishedBd
            && m.Stages.Count > 0
            && m.StartNpcId is int sid
            && unpublishedNpcIds.Contains(sid));
    }

    /// <summary>
    /// Preview/publish quests_es only for first-write pending missions (or pre-BD unpublished).
    /// Does not include <see cref="MayNeedQuestsEsRepair"/> — that is optional maintenance, not
    /// part of a normal dialog/npc client publish.
    /// </summary>
    public static bool NeedsQuestsEsPreview(ContentDraftWorkspace workspace) =>
        HasUnpublishedBdMissions(workspace)
        || workspace.Missions.Missions.Any(IsPendingQuestsEs);

    /// <summary>Ready-to-write batch: first-write missions only (no SWF repair / auto-return).</summary>
    public static QuestsEsPublishBatch Build(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot snapshot,
        Func<int, (int X, int Y)?>? mapWorldLookup = null,
        Func<int, int?>? bdNpcMapIdLookup = null,
        IReadOnlySet<int>? npcIdFilter = null) =>
        BuildCore(workspace, snapshot, includeRepairs: false, mapWorldLookup, bdNpcMapIdLookup, npcIdFilter);

    /// <summary>Explicit maintenance batch: first-write + MayNeedRepair + auto-return coords patch.</summary>
    public static QuestsEsPublishBatch BuildWithRepairs(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot snapshot,
        Func<int, (int X, int Y)?>? mapWorldLookup = null,
        Func<int, int?>? bdNpcMapIdLookup = null,
        IReadOnlySet<int>? npcIdFilter = null) =>
        BuildCore(workspace, snapshot, includeRepairs: true, mapWorldLookup, bdNpcMapIdLookup, npcIdFilter);

    private static QuestsEsPublishBatch BuildCore(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot snapshot,
        bool includeRepairs,
        Func<int, (int X, int Y)?>? mapWorldLookup,
        Func<int, int?>? bdNpcMapIdLookup,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<string>();
        var notes = new List<string>();
        var bindings = new List<QuestsEsPublishBinding>();

        foreach (var m in workspace.Missions.Missions)
        {
            if (npcIdFilter is not null
                && m.StartNpcId is int filterSid
                && !ContentPublishNpcSelection.Includes(filterSid, npcIdFilter))
                continue;
            if (MissionAutoReturnSync.IsPublishedLocked(m))
                MissionAutoReturnSync.StripLocalOnlyAutoReturns(m);
        }

        QuestsEsAutoReturnCoordsRepair.PreviewInfo? autoReturnRepair = null;
        if (includeRepairs)
        {
            autoReturnRepair = QuestsEsAutoReturnCoordsRepair.TryPatchDraft(
                workspace, snapshot, mapWorldLookup, bdNpcMapIdLookup);
            if (autoReturnRepair is not null)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"AUTO-RETURN REPAIR · Q.o[{QuestsEsAutoReturnCoordsRepair.ObjectiveId}] coords desde mapa {autoReturnRepair.NpcMapId}."));
            }
            else if (mapWorldLookup is not null
                     && workspace.Missions.Missions.Any(m =>
                         m.PublishedQuestId == QuestsEsAutoReturnCoordsRepair.QuestId
                         && m.QuestsEsPublished
                         && m.Stages.Any(s => s.Objectives.Any(o =>
                             o.Id == QuestsEsAutoReturnCoordsRepair.ObjectiveId
                             && o.Tipo == MissionObjectiveTypes.ReturnToNpc
                             && MissionObjectiveArgsCodec.StripCoords(o.Args).X is null))))
            {
                errors.Add(QuestsEsAutoReturnCoordsRepair.CoordsUnavailableMessage);
            }
        }

        if (HasUnpublishedBdMissions(workspace))
        {
            errors.Add(
                "Vista previa completa disponible. Publica primero el contenido en BD para confirmar los IDs de misión.");
            return new QuestsEsPublishBatch
            {
                Bindings = bindings,
                SourceVersion = snapshot.Version,
                TargetVersion = snapshot.Version + 1,
                Errors = errors,
                IsPreviewPendingBd = true,
                AutoReturnCoordsRepair = autoReturnRepair,
            };
        }

        IEnumerable<MissionDraft> missions = includeRepairs
            ? workspace.Missions.Missions.Where(m => IsPendingQuestsEs(m) || MayNeedQuestsEsRepair(m))
            : workspace.Missions.Missions.Where(IsPendingQuestsEs);
        if (npcIdFilter is not null)
        {
            missions = missions.Where(m =>
                m.StartNpcId is int sid
                && ContentPublishNpcSelection.Includes(sid, npcIdFilter));
        }

        foreach (var mission in missions)
        {
            var questId = mission.PublishedQuestId!.Value;
            if (mission.PreviewPlannedQuestId is int planned && planned != questId)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"IDs regenerados tras BD: previsto {planned} → publicado {questId}."));
            }

            TryAddMission(
                bindings,
                errors,
                snapshot,
                mission,
                questId,
                MapStagesFromDraft(mission, provisional => provisional, provisional => provisional, errors, questId),
                previousPlanned: mission.PreviewPlannedQuestId,
                allowReplaceExisting: (includeRepairs && mission.QuestsEsPublished)
                                      || NeedsStringFormRepair(snapshot, questId));
        }

        return new QuestsEsPublishBatch
        {
            Bindings = bindings,
            SourceVersion = snapshot.Version,
            TargetVersion = snapshot.Version + 1,
            Errors = errors,
            Notes = notes,
            IsPreviewPendingBd = false,
            AutoReturnCoordsRepair = autoReturnRepair,
        };
    }

    /// <summary>
    /// Preview-only batch using the same ContentPublishPlan IDs as Publicar BD.
    /// Does not allow remote write (IsPreviewPendingBd=true).
    /// </summary>
    public static QuestsEsPublishBatch BuildFromBdPlan(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot snapshot,
        ContentPublishPlan plan)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        var notes = new List<string>();
        var bindings = new List<QuestsEsPublishBinding>();
        var excluded = new List<string>();

        foreach (var mission in workspace.Missions.Missions.Where(m => !m.PublishedBd && m.Stages.Count > 0))
        {
            if (!plan.QuestIdMap.TryGetValue(mission.DraftId, out var questId))
            {
                var label = string.IsNullOrWhiteSpace(mission.Nombre)
                    ? "(sin nombre)"
                    : mission.Nombre.Trim();
                excluded.Add(label);
                continue;
            }

            mission.PreviewPlannedQuestId = questId;

            int MapStage(int provisional) =>
                plan.StageIdMap.TryGetValue(provisional, out var sid) ? sid : 0;
            int MapObj(int provisional) =>
                plan.ObjectiveIdMap.TryGetValue(provisional, out var oid) ? oid : 0;

            // Prefer remapped args/detalle from plan rows when present.
            var objArgs = plan.Objectives.ToDictionary(o => o.ProvisionalId, o => (o.Args, o.Detalle, o.Tipo));

            TryAddMission(
                bindings,
                errors,
                snapshot,
                mission,
                questId,
                MapStagesFromDraft(
                    mission,
                    MapStage,
                    MapObj,
                    errors,
                    questId,
                    objArgs),
                previousPlanned: null);
        }

        return new QuestsEsPublishBatch
        {
            Bindings = bindings,
            SourceVersion = snapshot.Version,
            TargetVersion = snapshot.Version + 1,
            Errors = errors,
            Notes = notes,
            ExcludedDraftNames = excluded,
            ShowExcludedDraftsInPreview = false,
            IsPreviewPendingBd = true,
        };
    }

    private static (List<QuestsEsStageAssignment> Stages, List<QuestsEsObjectiveAssignment> Objectives, List<int> StageIds)?
        MapStagesFromDraft(
            MissionDraft mission,
            Func<int, int> mapStageId,
            Func<int, int> mapObjectiveId,
            List<string> errors,
            int questId,
            Dictionary<int, (string Args, string Detalle, int Tipo)>? planObjectives = null)
    {
        var stages = new List<QuestsEsStageAssignment>();
        var objectives = new List<QuestsEsObjectiveAssignment>();
        var stageIds = new List<int>();

        foreach (var stage in mission.Stages)
        {
            var sid = mapStageId(stage.Id);
            if (sid <= 0)
            {
                errors.Add($"Misión {questId}: etapa provisional {stage.Id} sin ID en plan BD.");
                return null;
            }

            stageIds.Add(sid);
            stages.Add(new QuestsEsStageAssignment
            {
                StageId = sid,
                Name = stage.Nombre.Trim(),
                Description = stage.Descripcion ?? "",
                Rewards = stage.Rewards.Clone(),
            });

            foreach (var obj in stage.Objectives)
            {
                var oid = mapObjectiveId(obj.Id);
                if (oid <= 0)
                {
                    errors.Add($"Misión {questId}: objetivo provisional {obj.Id} sin ID en plan BD.");
                    return null;
                }

                var args = obj.Args ?? "";
                var detalle = obj.Detalle ?? "";
                var tipo = obj.Tipo;
                if (planObjectives is not null && planObjectives.TryGetValue(obj.Id, out var planned))
                {
                    args = planned.Args;
                    detalle = planned.Detalle;
                    tipo = planned.Tipo;
                }

                // AUDIT_FIX.4 / FIX.7 — auto-return must have X/Y and match StartNpcId.
                if (obj.IsAutoReturnToStartNpc)
                {
                    var (_, cx, cy) = MissionObjectiveArgsCodec.StripCoords(args);
                    if (cx is null || cy is null)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture,
                            $"Misión {questId}: retorno automático (objetivo {obj.Id}). {QuestsEsAutoReturnCoordsRepair.CoordsUnavailableMessage}"));
                        return null;
                    }

                    if (mission.StartNpcId is int startNpc
                        && MissionObjectiveNpcRemap.TryReadLeadingNpc(args, out var autoNpc)
                        && autoNpc != startNpc)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture,
                            $"Misión {questId}: {QuestsEsAutoReturnCoordsRepair.NpcMismatchMessage} (auto={autoNpc}, start={startNpc})."));
                        return null;
                    }
                }

                objectives.Add(new QuestsEsObjectiveAssignment
                {
                    ObjectiveId = oid,
                    Type = tipo,
                    Args = args,
                    Detalle = detalle,
                });
            }
        }

        return (stages, objectives, stageIds);
    }

    private static void TryAddMission(
        List<QuestsEsPublishBinding> bindings,
        List<string> errors,
        QuestsEsSnapshot snapshot,
        MissionDraft mission,
        int questId,
        (List<QuestsEsStageAssignment> Stages, List<QuestsEsObjectiveAssignment> Objectives, List<int> StageIds)? mapped,
        int? previousPlanned,
        bool allowReplaceExisting = false)
    {
        if (mapped is null)
            return;

        var (stages, objectives, stageIds) = mapped.Value;
        if (string.IsNullOrWhiteSpace(mission.Nombre))
        {
            errors.Add($"Misión {questId}: nombre vacío.");
            return;
        }

        if (stages.Count == 0)
        {
            errors.Add($"Misión {questId}: sin etapas.");
            return;
        }

        try
        {
            var questName = DialogEsLatin1.Prepare(mission.Nombre.Trim(), $"Q.q[{questId}]");
            foreach (var st in stages)
            {
                st.Name = DialogEsLatin1.Prepare(st.Name, $"Q.s[{st.StageId}].n");
                _ = QuestsEsCodec.EncodeStageDescription(st.Description);
            }

            var quest = new QuestsEsQuestAssignment
            {
                QuestId = questId,
                Name = questName,
                StageIds = stageIds,
            };

            var exists = snapshot.ContainsQuest(questId)
                         || stages.Any(s => snapshot.ContainsStage(s.StageId))
                         || objectives.Any(o => snapshot.ContainsObjective(o.ObjectiveId));

            if (exists && allowReplaceExisting)
            {
                var layoutOk = !snapshot.NeedsQuestLayoutRepair(questId);
                var stringFormOk = !NeedsStringFormRepair(snapshot, questId);
                var questSemanticOk = QuestSemanticMatches(quest, snapshot);
                var writeQuest = !(questSemanticOk && layoutOk && stringFormOk);

                var changedStages = stages.Where(st => !StageMatches(st, snapshot)).ToList();
                var changedObjectives = objectives.Where(o => !ObjectiveMatches(o, snapshot)).ToList();

                if (!writeQuest && changedStages.Count == 0 && changedObjectives.Count == 0)
                    return; // identical remote entries

                string? repairReason = null;
                if (!stringFormOk)
                    repairReason = "Q.q objeto → string";
                else if (!layoutOk)
                    repairReason = "FORMA AVM1 NO CANÓNICA";

                bindings.Add(new QuestsEsPublishBinding
                {
                    MissionDraftId = mission.DraftId,
                    QuestId = questId,
                    QuestName = questName,
                    Kind = "replace",
                    Assignment = new QuestsEsMissionAssignment
                    {
                        Quest = quest,
                        WriteQuest = writeQuest,
                        Stages = changedStages,
                        Objectives = changedObjectives,
                    },
                    StageCount = changedStages.Count,
                    ObjectiveCount = changedObjectives.Count,
                    PreviousPlannedQuestId = previousPlanned,
                    RepairReason = repairReason,
                });
                return;
            }

            // Namespaces independientes: solo comprobar cada ID en su colección.
            if (snapshot.ContainsQuest(questId))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Misión {questId}: conflicto Q.q[{questId}] ya existe."));
                bindings.Add(ConflictBinding(mission, questId, stages.Count, objectives.Count, previousPlanned));
                return;
            }

            var stageHit = stages.FirstOrDefault(s => snapshot.ContainsStage(s.StageId));
            if (stageHit is not null)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Misión {questId}: conflicto Q.s[{stageHit.StageId}] ya existe."));
                bindings.Add(ConflictBinding(mission, questId, stages.Count, objectives.Count, previousPlanned));
                return;
            }

            var objHit = objectives.FirstOrDefault(o => snapshot.ContainsObjective(o.ObjectiveId));
            if (objHit is not null)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Misión {questId}: conflicto Q.o[{objHit.ObjectiveId}] ya existe."));
                bindings.Add(ConflictBinding(mission, questId, stages.Count, objectives.Count, previousPlanned));
                return;
            }

            bindings.Add(new QuestsEsPublishBinding
            {
                MissionDraftId = mission.DraftId,
                QuestId = questId,
                QuestName = questName,
                Kind = "new",
                Assignment = new QuestsEsMissionAssignment
                {
                    Quest = quest,
                    WriteQuest = true,
                    Stages = stages,
                    Objectives = objectives,
                },
                StageCount = stages.Count,
                ObjectiveCount = objectives.Count,
                PreviousPlannedQuestId = previousPlanned,
            });
        }
        catch (DialogEsEncodingException ex)
        {
            errors.Add(ex.Message);
        }
    }

    private static bool QuestSemanticMatches(QuestsEsQuestAssignment quest, QuestsEsSnapshot snapshot)
    {
        if (!snapshot.Quests.TryGetValue(quest.QuestId, out var q))
            return false;
        // FIX.11 — Q.q is the display name string; stages live in BD misiones.etapas + Q.s.
        return q.IsStringName
               && string.Equals(q.Name, quest.Name, StringComparison.Ordinal);
    }

    /// <summary>True when remote Q.q is still the obsolete {n,s} object form.</summary>
    public static bool NeedsStringFormRepair(QuestsEsSnapshot snapshot, int questId) =>
        snapshot.Quests.TryGetValue(questId, out var q) && !q.IsStringName;

    private static bool StageMatches(QuestsEsStageAssignment st, QuestsEsSnapshot snapshot)
    {
        if (!snapshot.Stages.TryGetValue(st.StageId, out var remote))
            return false;
        if (!string.Equals(remote.Name, st.Name, StringComparison.Ordinal))
            return false;
        var expectedD = QuestsEsCodec.EncodeStageDescription(st.Description);
        if (!ValuesEqual(remote.Description, expectedD))
            return false;
        var expectedR = QuestsEsCodec.EncodeRewards(st.Rewards);
        return RewardListsEqual(remote.Rewards, expectedR);
    }

    private static bool StagesMatch(IReadOnlyList<QuestsEsStageAssignment> stages, QuestsEsSnapshot snapshot) =>
        stages.All(st => StageMatches(st, snapshot));

    private static bool ObjectiveMatches(QuestsEsObjectiveAssignment o, QuestsEsSnapshot snapshot)
    {
        if (!snapshot.Objectives.TryGetValue(o.ObjectiveId, out var remote))
            return false;
        if (remote.Type != o.Type)
            return false;
        var draft = new MissionObjectiveDraft
        {
            Id = o.ObjectiveId,
            Tipo = o.Type,
            Args = o.Args,
            Detalle = o.Detalle,
        };
        var expectedP = QuestsEsCodec.EncodeObjectiveParameters(draft);
        if (!ParamListsEqual(remote.Parameters, expectedP))
            return false;
        var (x, y) = QuestsEsCodec.ReadObjectiveCoords(draft);
        return remote.X == x && remote.Y == y;
    }

    private static bool ObjectivesMatch(
        IReadOnlyList<QuestsEsObjectiveAssignment> objectives,
        QuestsEsSnapshot snapshot) =>
        objectives.All(o => ObjectiveMatches(o, snapshot));

    /// <summary>Semantic + string Q.q form. Used by tests.</summary>
    public static bool MissionMatchesSnapshot(QuestsEsMissionAssignment assignment, QuestsEsSnapshot snapshot) =>
        QuestSemanticMatches(assignment.Quest, snapshot)
        && StagesMatch(assignment.Stages, snapshot)
        && ObjectivesMatch(assignment.Objectives, snapshot)
        && !NeedsStringFormRepair(snapshot, assignment.Quest.QuestId);

    /// <summary>True when remote Q.q bytecode must be rewritten despite matching name.</summary>
    public static bool NeedsQuestObjectRepair(QuestsEsSnapshot snapshot, int questId) =>
        snapshot.NeedsQuestLayoutRepair(questId) || NeedsStringFormRepair(snapshot, questId);

    private static bool RewardListsEqual(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!ValuesEqual(a[i], b[i]))
                return false;
        }

        return true;
    }

    private static bool ParamListsEqual(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!ValuesEqual(a[i], b[i]))
                return false;
        }

        return true;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (TryAsInt64(a, out var ai) && TryAsInt64(b, out var bi))
            return ai == bi;
        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, StringComparison.Ordinal);
        if (a is int[] ia && b is int[] ib)
            return ia.AsSpan().SequenceEqual(ib);
        if (TryAsObjectSequence(a, out var la) && TryAsObjectSequence(b, out var lb))
        {
            if (la.Count != lb.Count) return false;
            for (var i = 0; i < la.Count; i++)
            {
                if (!ValuesEqual(la[i], lb[i]))
                    return false;
            }

            return true;
        }

        return Equals(a, b);
    }

    private static bool TryAsInt64(object value, out long n)
    {
        switch (value)
        {
            case int i:
                n = i;
                return true;
            case long l:
                n = l;
                return true;
            default:
                n = 0;
                return false;
        }
    }

    private static bool TryAsObjectSequence(object value, out IReadOnlyList<object?> list)
    {
        if (value is int[] ints)
        {
            list = ints.Cast<object?>().ToList();
            return true;
        }

        if (value is object[] arr)
        {
            list = arr;
            return true;
        }

        if (value is IReadOnlyList<object?> ro)
        {
            list = ro;
            return true;
        }

        if (value is System.Collections.IList il && value is not string)
        {
            var tmp = new List<object?>(il.Count);
            foreach (var item in il)
                tmp.Add(item);
            list = tmp;
            return true;
        }

        list = Array.Empty<object?>();
        return false;
    }

    private static QuestsEsPublishBinding ConflictBinding(
        MissionDraft mission,
        int questId,
        int stageCount,
        int objectiveCount,
        int? previousPlanned) =>
        new()
        {
            MissionDraftId = mission.DraftId,
            QuestId = questId,
            QuestName = mission.Nombre,
            Kind = "conflict",
            StageCount = stageCount,
            ObjectiveCount = objectiveCount,
            PreviousPlannedQuestId = previousPlanned,
        };

    public static void ApplyToWorkspace(
        ContentDraftWorkspace workspace,
        QuestsEsPublishBatch batch,
        int publishedVersion)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.IsPreviewPendingBd)
            return;

        foreach (var b in batch.Bindings.Where(x => x.Assignment is not null))
        {
            var mission = workspace.Missions.FindByDraftId(b.MissionDraftId);
            if (mission is null) continue;
            mission.QuestsEsPublished = true;
            mission.QuestsEsPublishedVersion = publishedVersion;
        }
    }
}
