using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

public enum ContentLibraryEntryKind
{
    Npc,
    Mission,
    Dialog,
}

/// <summary>Modalidad principal de la Biblioteca: NPC+Diálogo | Misiones (sin solapar).</summary>
public enum ContentLibraryMode
{
    NpcAndDialog,
    Missions,
}

public enum ContentLibraryFilter
{
    All,
    Drafts,
    Published,
    PendingChanges,
    WithoutStartNpc,
}

public enum ContentLibraryStatusGroup
{
    PendingChanges,
    Drafts,
    Published,
}

/// <summary>CONTENT.LIBRARY.ORGANIZE.DELETE.2 / GRID.CARDS.3 — proyección del workspace.json.</summary>
public sealed class ContentLibraryEntry
{
    public required ContentLibraryEntryKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required string Relation { get; init; }
    public int? NpcId { get; init; }
    public int? QuestId { get; init; }
    public Guid? MissionDraftId { get; init; }
    public int StageCount { get; init; }
    public int ObjectiveCount { get; init; }
    public int QuestionCount { get; init; }
    public string StartNpcName { get; init; } = "";
    public string LinkedMissionName { get; init; } = "";
    public bool HasStartNpc { get; init; }
    public bool HasLinkedMission { get; init; }
    public bool HasConversation { get; init; }
    public bool IsPublishedLocally { get; init; }
    public ContentLibraryStatusGroup StatusGroup { get; init; }
    public bool CanDuplicate { get; init; }
    public bool CanOpen { get; init; } = true;
    public bool CanDeleteDraft { get; init; }
    public bool CanRemoveLocalCopy { get; init; }

    /// <summary>Resumen del bloque NPC/misión/diálogos al pasar el ratón (solo workspace local).</summary>
    public string HoverTooltip { get; init; } = "";

    public string KindLabel => Kind switch
    {
        ContentLibraryEntryKind.Npc => "NPC + Diálogo",
        ContentLibraryEntryKind.Mission => "Misión",
        ContentLibraryEntryKind.Dialog => "Diálogo",
        _ => "",
    };

    public string? RemoveActionLabel =>
        CanDeleteDraft ? "Eliminar borrador"
        : CanRemoveLocalCopy ? "Quitar copia local"
        : null;

    public bool CanRemove => CanDeleteDraft || CanRemoveLocalCopy;

    /// <summary>Quest # / NPC # for card face — never DraftId/GUID.</summary>
    public string CardIdLine => Kind switch
    {
        ContentLibraryEntryKind.Mission when QuestId is int qid and > 0 =>
            string.Create(CultureInfo.InvariantCulture, $"Quest #{qid}"),
        ContentLibraryEntryKind.Npc when NpcId is int nid =>
            string.Create(CultureInfo.InvariantCulture, $"NPC #{nid}"),
        _ => "",
    };

    public bool ShowCardIdLine => CardIdLine.Length > 0;

    public string CardLinkLine => Kind switch
    {
        ContentLibraryEntryKind.Mission =>
            HasStartNpc ? $"NPC: {StartNpcName}" : "Sin NPC inicial",
        ContentLibraryEntryKind.Npc =>
            string.IsNullOrWhiteSpace(LinkedMissionName)
                ? "Sin misión"
                : $"Misión: {LinkedMissionName.Trim()}",
        ContentLibraryEntryKind.Dialog => Relation,
        _ => Relation,
    };

    public string CardStatsLine => Kind switch
    {
        ContentLibraryEntryKind.Mission => string.Create(
            CultureInfo.InvariantCulture,
            $"{StageCount} {(StageCount == 1 ? "etapa" : "etapas")} · {ObjectiveCount} {(ObjectiveCount == 1 ? "objetivo" : "objetivos")}"),
        ContentLibraryEntryKind.Npc when HasConversation && QuestionCount > 0 => string.Create(
            CultureInfo.InvariantCulture,
            $"Diálogo · {QuestionCount} {(QuestionCount == 1 ? "pregunta" : "preguntas")}"),
        ContentLibraryEntryKind.Npc when HasConversation => "Diálogo simple",
        ContentLibraryEntryKind.Npc => "Sin diálogo",
        ContentLibraryEntryKind.Dialog => string.Create(
            CultureInfo.InvariantCulture,
            $"Preguntas: {QuestionCount}"),
        _ => "",
    };

    public bool ShowCardStatsLine => CardStatsLine.Length > 0;
}

public sealed class ContentLibrarySection
{
    public required string Title { get; init; }
    public bool IsSubSection { get; init; }
    public IReadOnlyList<ContentLibraryEntry> Entries { get; init; } = Array.Empty<ContentLibraryEntry>();
}

public sealed class ContentLibraryListItem
{
    public bool IsSectionHeader { get; init; }
    public string SectionTitle { get; init; } = "";
    public ContentLibraryEntry? Entry { get; init; }

    public static ContentLibraryListItem Header(string title) => new()
    {
        IsSectionHeader = true,
        SectionTitle = title,
    };

    public static ContentLibraryListItem FromEntry(ContentLibraryEntry entry) => new()
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry)),
    };
}

public sealed class ContentLibraryCounters
{
    public int Drafts { get; init; }
    public int PendingChanges { get; init; }
    public int Published { get; init; }
    public int Total => Drafts + PendingChanges + Published;
}

public static class ContentLibraryCatalog
{
    public const string StatusBorrador = "BORRADOR";
    public const string StatusPublicado = "PUBLICADO";
    public const string StatusCambiosPendientes = "CAMBIOS PENDIENTES";
    public const string StatusBdPendiente = "BD PENDIENTE";
    public const string StatusClientePendiente = "CLIENTE PENDIENTE";
    public const string StatusAlDia = "AL DÍA";

    public static IReadOnlyList<ContentLibraryEntry> Build(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var list = new List<ContentLibraryEntry>();

        foreach (var npc in workspace.Npcs.Drafts.Where(n => !n.IsDbCatalogSnapshot))
        {
            var mission = workspace.Missions.Missions
                .FirstOrDefault(m => m.StartNpcId == npc.Id);
            var status = ResolveNpcStatus(npc, workspace);
            var published = npc.PublishedBd;
            var hasSimpleDialog = npc.DialogMode == NpcDialogMode.Simple
                && (!string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal) || npc.Pregunta > 0);
            var interactiveQuestions = npc.DialogMode == NpcDialogMode.Interactive
                ? workspace.Dialogs.QuestionsForNpc(npc.Id).ToList()
                : new List<DialogQuestionDraft>();
            var hasConversation = hasSimpleDialog || interactiveQuestions.Count > 0;
            var hover = BuildBlockHoverTooltip(workspace, npc, mission, focus: "NPC");
            // Un solo bloque NPC+Diálogo: el diálogo vive en la misma tarjeta (no entrada Dialog aparte).
            list.Add(new ContentLibraryEntry
            {
                Kind = ContentLibraryEntryKind.Npc,
                Title = string.IsNullOrWhiteSpace(npc.Nombre) ? "(sin nombre)" : npc.Nombre.Trim(),
                Status = status,
                Summary = string.Create(CultureInfo.InvariantCulture, $"NPC #{npc.Id}"),
                Relation = mission is null
                    ? "Sin misión vinculada"
                    : $"Misión vinculada: {(string.IsNullOrWhiteSpace(mission.Nombre) ? "(sin nombre)" : mission.Nombre.Trim())}",
                NpcId = npc.Id,
                LinkedMissionName = mission?.Nombre ?? "",
                HasLinkedMission = mission is not null,
                HasConversation = hasConversation,
                QuestionCount = interactiveQuestions.Count,
                IsPublishedLocally = published,
                StatusGroup = ClassifyStatus(status),
                CanDuplicate = false,
                CanDeleteDraft = !published,
                CanRemoveLocalCopy = published,
                HoverTooltip = hover,
            });
        }

        foreach (var mission in workspace.Missions.Missions)
        {
            var npcName = "";
            var hasStart = mission.StartNpcId is int;
            NpcsModeloDraft? startNpc = null;
            if (mission.StartNpcId is int sid)
            {
                startNpc = workspace.Npcs.FindById(sid);
                npcName = startNpc is null || string.IsNullOrWhiteSpace(startNpc.Nombre)
                    ? string.Create(CultureInfo.InvariantCulture, $"NPC #{sid}")
                    : startNpc.Nombre.Trim();
            }

            var stages = mission.Stages.Count;
            var objectives = mission.Stages.Sum(s => s.Objectives.Count(o => !o.IsAutoReturnToStartNpc));
            var status = ResolveMissionStatus(mission);
            var published = ContentLibraryLocalCleanup.IsMissionPublishedLocally(mission);
            var questLine = mission.PublishedQuestId is int qid
                ? string.Create(CultureInfo.InvariantCulture, $"Quest #{qid}")
                : "";
            list.Add(new ContentLibraryEntry
            {
                Kind = ContentLibraryEntryKind.Mission,
                Title = string.IsNullOrWhiteSpace(mission.Nombre) ? "(sin nombre)" : mission.Nombre.Trim(),
                Status = status,
                Summary = string.IsNullOrEmpty(questLine)
                    ? string.Create(CultureInfo.InvariantCulture, $"{stages} etapas · {objectives} objetivos")
                    : questLine,
                Relation = hasStart
                    ? $"NPC inicial: {npcName}"
                    : "Sin NPC inicial",
                NpcId = mission.StartNpcId,
                QuestId = mission.PublishedQuestId,
                MissionDraftId = mission.DraftId,
                StageCount = stages,
                ObjectiveCount = objectives,
                StartNpcName = npcName,
                HasStartNpc = hasStart,
                HasLinkedMission = hasStart,
                IsPublishedLocally = published,
                StatusGroup = ClassifyStatus(status),
                CanDuplicate = true,
                CanDeleteDraft = !published,
                CanRemoveLocalCopy = published,
                HoverTooltip = BuildBlockHoverTooltip(workspace, startNpc, mission, focus: "Misión"),
            });
        }

        return SortEntries(list);
    }

    public static ContentLibraryCounters Count(IReadOnlyList<ContentLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new ContentLibraryCounters
        {
            Drafts = entries.Count(e => e.StatusGroup == ContentLibraryStatusGroup.Drafts),
            PendingChanges = entries.Count(e => e.StatusGroup == ContentLibraryStatusGroup.PendingChanges),
            Published = entries.Count(e => e.StatusGroup == ContentLibraryStatusGroup.Published),
        };
    }

    /// <summary>Compat: combina ambas modalidades (tests / llamadas sin Mode).</summary>
    public static IReadOnlyList<ContentLibraryEntry> Filter(
        IReadOnlyList<ContentLibraryEntry> entries,
        ContentLibraryFilter filter,
        string? search)
    {
        if (filter == ContentLibraryFilter.WithoutStartNpc)
            return Filter(entries, ContentLibraryMode.Missions, filter, search);

        var a = Filter(entries, ContentLibraryMode.NpcAndDialog, filter, search);
        var b = Filter(entries, ContentLibraryMode.Missions, filter, search);
        return SortEntries(a.Concat(b).ToList());
    }

    public static IReadOnlyList<ContentLibraryEntry> Filter(
        IReadOnlyList<ContentLibraryEntry> entries,
        ContentLibraryMode mode,
        ContentLibraryFilter filter,
        string? search)
    {
        IEnumerable<ContentLibraryEntry> q = entries;

        // Modalidad exclusiva: NPC+Diálogo = tarjeta NPC (diálogo incluido); Misiones = solo misiones.
        q = mode switch
        {
            ContentLibraryMode.NpcAndDialog => q.Where(e => e.Kind == ContentLibraryEntryKind.Npc),
            ContentLibraryMode.Missions => q.Where(e => e.Kind == ContentLibraryEntryKind.Mission),
            _ => q,
        };

        q = filter switch
        {
            ContentLibraryFilter.Drafts => q.Where(e => e.StatusGroup == ContentLibraryStatusGroup.Drafts),
            ContentLibraryFilter.Published => q.Where(e => e.StatusGroup == ContentLibraryStatusGroup.Published),
            ContentLibraryFilter.PendingChanges => q.Where(e =>
                e.StatusGroup == ContentLibraryStatusGroup.PendingChanges),
            ContentLibraryFilter.WithoutStartNpc => mode == ContentLibraryMode.Missions
                ? q.Where(e => !e.HasStartNpc)
                : q.Where(e => !e.HasLinkedMission),
            _ => q,
        };

        var term = search?.Trim() ?? "";
        if (term.Length > 0)
        {
            q = q.Where(e =>
                e.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.Relation.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.LinkedMissionName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || e.StartNpcName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (e.NpcId is int nid && nid.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal))
                || (e.QuestId is int qid && qid.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)));
        }

        return SortEntries(q.ToList());
    }

    public static IReadOnlyList<ContentLibrarySection> OrganizeSections(
        IReadOnlyList<ContentLibraryEntry> filtered,
        ContentLibraryMode mode,
        ContentLibraryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filtered);
        var sections = new List<ContentLibrarySection>();

        void AddKind(string title, IEnumerable<ContentLibraryEntry> entries)
        {
            var list = SortEntries(entries.ToList());
            if (list.Count == 0) return;
            sections.Add(new ContentLibrarySection
            {
                Title = $"{title} ({list.Count})",
                Entries = list,
            });
        }

        void AddMissionGroups(IEnumerable<ContentLibraryEntry> entries)
        {
            var missions = entries.Where(e => e.Kind == ContentLibraryEntryKind.Mission).ToList();
            if (missions.Count == 0) return;

            sections.Add(new ContentLibrarySection
            {
                Title = $"MISIONES ({missions.Count})",
                Entries = Array.Empty<ContentLibraryEntry>(),
            });

            void Sub(string title, ContentLibraryStatusGroup group)
            {
                var list = SortEntries(missions.Where(m => m.StatusGroup == group).ToList());
                if (list.Count == 0) return;
                sections.Add(new ContentLibrarySection
                {
                    Title = $"{title} ({list.Count})",
                    IsSubSection = true,
                    Entries = list,
                });
            }

            Sub("Cambios pendientes", ContentLibraryStatusGroup.PendingChanges);
            Sub("Borradores", ContentLibraryStatusGroup.Drafts);
            Sub("Publicadas", ContentLibraryStatusGroup.Published);
        }

        if (mode == ContentLibraryMode.Missions)
        {
            AddMissionGroups(filtered);
            return sections;
        }

        // Un solo bloque: NPC + Diálogo (el diálogo no se lista aparte).
        AddKind("NPC + Diálogo", filtered.Where(e => e.Kind == ContentLibraryEntryKind.Npc));
        return sections;
    }

    public static IReadOnlyList<ContentLibraryListItem> Organize(
        IReadOnlyList<ContentLibraryEntry> filtered,
        ContentLibraryMode mode,
        ContentLibraryFilter filter)
    {
        var rows = new List<ContentLibraryListItem>();
        foreach (var section in OrganizeSections(filtered, mode, filter))
        {
            var title = section.IsSubSection ? $"  {section.Title}" : section.Title;
            rows.Add(ContentLibraryListItem.Header(title));
            foreach (var e in section.Entries)
                rows.Add(ContentLibraryListItem.FromEntry(e));
        }

        return rows;
    }

    /// <summary>Compat: OrganizeSections sin Mode.</summary>
    public static IReadOnlyList<ContentLibrarySection> OrganizeSections(
        IReadOnlyList<ContentLibraryEntry> filtered,
        ContentLibraryFilter filter)
    {
        if (filter == ContentLibraryFilter.All)
        {
            var sections = new List<ContentLibrarySection>();
            sections.AddRange(OrganizeSections(filtered, ContentLibraryMode.NpcAndDialog, ContentLibraryFilter.All));
            sections.AddRange(OrganizeSections(filtered, ContentLibraryMode.Missions, ContentLibraryFilter.All));
            return sections;
        }

        var mode = filter == ContentLibraryFilter.WithoutStartNpc
            ? ContentLibraryMode.Missions
            : ContentLibraryMode.NpcAndDialog;
        return OrganizeSections(filtered, mode, filter);
    }

    /// <summary>Compat: Organize(All) muestra ambas modalidades en secuencia.</summary>
    public static IReadOnlyList<ContentLibraryListItem> Organize(
        IReadOnlyList<ContentLibraryEntry> filtered,
        ContentLibraryFilter filter)
    {
        var rows = new List<ContentLibraryListItem>();
        foreach (var section in OrganizeSections(filtered, filter))
        {
            var title = section.IsSubSection ? $"  {section.Title}" : section.Title;
            rows.Add(ContentLibraryListItem.Header(title));
            foreach (var e in section.Entries)
                rows.Add(ContentLibraryListItem.FromEntry(e));
        }

        return rows;
    }

    public static string ResolveMissionStatus(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (!mission.PublishedBd)
            return StatusBorrador;
        if (mission.HasPendingBdChanges)
            return StatusCambiosPendientes;
        if (QuestsEsPublishBatchBuilder.IsPendingQuestsEs(mission))
            return StatusClientePendiente;
        if (mission.QuestsEsPublished)
            return StatusAlDia;
        return StatusPublicado;
    }

    public static string ResolveNpcStatus(NpcsModeloDraft npc, ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!npc.PublishedBd)
        {
            if (npc.IsPendingDialogEs || npc.IsPendingNpcEsFor(workspace))
                return StatusClientePendiente;
            return StatusBorrador;
        }

        if (npc.IsPendingDialogEs || npc.IsPendingNpcEsFor(workspace) || npc.IsNpcEsIncompleteFor(workspace))
            return StatusClientePendiente;
        return StatusAlDia;
    }

    public static ContentLibraryStatusGroup ClassifyStatus(string status) =>
        status switch
        {
            StatusCambiosPendientes or StatusBdPendiente or StatusClientePendiente =>
                ContentLibraryStatusGroup.PendingChanges,
            StatusBorrador => ContentLibraryStatusGroup.Drafts,
            _ => ContentLibraryStatusGroup.Published,
        };

    private static List<ContentLibraryEntry> SortEntries(IReadOnlyList<ContentLibraryEntry> entries) =>
        entries
            .OrderBy(e => e.StatusGroup)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Tooltip del bloque local: NPC + misión(es) + diálogos previos (sin GUID/flags).</summary>
    private static string BuildBlockHoverTooltip(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft? npc,
        MissionDraft? focusMission,
        string focus)
    {
        var lines = new List<string> { $"Bloque · {focus}" };

        if (npc is not null)
        {
            var npcName = string.IsNullOrWhiteSpace(npc.Nombre) ? "(sin nombre)" : npc.Nombre.Trim();
            lines.Add("");
            lines.Add("NPC");
            lines.Add($"· {npcName}");
            lines.Add($"· Estado: {ResolveNpcStatus(npc, workspace)}");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"· ID: #{npc.Id}"));
        }
        else
        {
            lines.Add("");
            lines.Add("NPC");
            lines.Add("· Sin NPC inicial");
        }

        lines.Add("");
        lines.Add("MISIÓN");
        IEnumerable<MissionDraft> missions;
        if (focusMission is not null)
            missions = new[] { focusMission };
        else if (npc is not null)
            missions = workspace.Missions.Missions.Where(m => m.StartNpcId == npc.Id);
        else
            missions = Array.Empty<MissionDraft>();

        var missionList = missions.ToList();
        if (missionList.Count == 0)
        {
            lines.Add("· Sin misión vinculada");
        }
        else
        {
            foreach (var m in missionList.Take(4))
            {
                var mn = string.IsNullOrWhiteSpace(m.Nombre) ? "(sin nombre)" : m.Nombre.Trim();
                var st = ResolveMissionStatus(m);
                var stages = m.Stages.Count;
                var objectives = m.Stages.Sum(s => s.Objectives.Count(o => !o.IsAutoReturnToStartNpc));
                lines.Add($"· {mn} · {st}");
                if (m.PublishedQuestId is int qid)
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"  Quest #{qid}"));
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  {stages} {(stages == 1 ? "etapa" : "etapas")} · {objectives} {(objectives == 1 ? "objetivo" : "objetivos")}"));
                AppendMissionRewardsAndObjectives(lines, m);
            }

            if (missionList.Count > 4)
                lines.Add($"· … y {missionList.Count - 4} misión(es) más");
        }

        lines.Add("");
        lines.Add("DIÁLOGO");
        if (npc is null)
        {
            lines.Add("· Sin diálogo (sin NPC)");
        }
        else if (npc.DialogMode == NpcDialogMode.Simple
                 && (!string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal) || npc.Pregunta > 0))
        {
            lines.Add("· Diálogo simple");
            var preview = Truncate(npc.SimpleDialogTextLocal, 90);
            if (preview != "(sin texto)")
                lines.Add($"  «{preview}»");
        }
        else if (npc.DialogMode == NpcDialogMode.Interactive)
        {
            var questions = workspace.Dialogs.QuestionsForNpc(npc.Id).ToList();
            if (questions.Count == 0)
            {
                lines.Add("· Conversación vacía");
            }
            else
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"· Conversación · {questions.Count} pregunta(s)"));
                foreach (var q in questions.Take(5))
                {
                    var text = Truncate(q.TextLocal, 72);
                    var resp = q.Responses.Count;
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"  · {text} ({resp} resp.)"));
                }

                if (questions.Count > 5)
                    lines.Add($"  · … y {questions.Count - 5} más");
            }
        }
        else
        {
            lines.Add("· Sin diálogo");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendMissionRewardsAndObjectives(List<string> lines, MissionDraft mission)
    {
        if (mission.Stages.Count == 0)
        {
            lines.Add("  Recompensas: (ninguna)");
            lines.Add("  Objetivos: (ninguno)");
            return;
        }

        var stageIndex = 0;
        foreach (var stage in mission.Stages.Take(5))
        {
            stageIndex++;
            var stageName = string.IsNullOrWhiteSpace(stage.Nombre)
                ? string.Create(CultureInfo.InvariantCulture, $"Etapa {stageIndex}")
                : stage.Nombre.Trim();
            lines.Add($"  [{stageName}]");

            var rewardParts = FormatRewardParts(stage.Rewards);
            lines.Add(rewardParts.Count == 0
                ? "    Recompensas: (ninguna)"
                : "    Recompensas: " + string.Join(" · ", rewardParts));

            var objs = stage.Objectives.Where(o => !o.IsAutoReturnToStartNpc).ToList();
            if (objs.Count == 0)
            {
                lines.Add("    Objetivos: (ninguno)");
                continue;
            }

            lines.Add("    Objetivos:");
            foreach (var o in objs.Take(6))
            {
                var label = FormatObjectiveLine(o);
                lines.Add($"      · {label}");
            }

            if (objs.Count > 6)
                lines.Add($"      · … y {objs.Count - 6} más");
        }

        if (mission.Stages.Count > 5)
            lines.Add($"  · … y {mission.Stages.Count - 5} etapa(s) más");
    }

    private static List<string> FormatRewardParts(MissionRewardsDraft rewards)
    {
        var parts = new List<string>();
        if (rewards.Exp > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"XP {rewards.Exp}"));
        if (rewards.Kamas > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Kamas {rewards.Kamas}"));
        foreach (var item in rewards.Objetos.Where(i => i.ItemId > 0).Take(8))
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"objeto #{item.ItemId} ×{Math.Max(1, item.Cantidad)}"));
        }

        if (rewards.Objetos.Count(i => i.ItemId > 0) > 8)
            parts.Add("…");

        if (!string.IsNullOrWhiteSpace(rewards.Emotes))
            parts.Add("emotes: " + Truncate(rewards.Emotes, 24));
        if (!string.IsNullOrWhiteSpace(rewards.Hechizos))
            parts.Add("hechizos: " + Truncate(rewards.Hechizos, 24));
        if (!string.IsNullOrWhiteSpace(rewards.Oficios))
            parts.Add("oficios: " + Truncate(rewards.Oficios, 24));
        if (!string.IsNullOrWhiteSpace(rewards.Acciones))
            parts.Add("acciones: " + Truncate(rewards.Acciones, 24));

        return parts;
    }

    private static string FormatObjectiveLine(MissionObjectiveDraft objective)
    {
        var typeName = MissionObjectiveTypes.DisplayName(objective.Tipo);
        var detail = !string.IsNullOrWhiteSpace(objective.Detalle)
            ? Truncate(objective.Detalle, 64)
            : Truncate(MissionObjectiveArgsCodec.SuggestDetalle(objective.Tipo, objective.Args ?? ""), 64);
        if (string.IsNullOrWhiteSpace(detail) || detail == "(sin texto)")
            return typeName;
        return $"{typeName}: {detail}";
    }

    private static ContentLibraryEntry BuildSimpleDialogEntry(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        MissionDraft? mission)
    {
        var npcName = string.IsNullOrWhiteSpace(npc.Nombre) ? "(NPC)" : npc.Nombre.Trim();
        var missionName = mission is null || string.IsNullOrWhiteSpace(mission.Nombre)
            ? null
            : mission.Nombre.Trim();
        var status = npc.IsPendingDialogEs ? StatusClientePendiente
            : npc.DialogEsPublished || npc.Pregunta > 0 ? StatusPublicado : StatusBorrador;
        return new ContentLibraryEntry
        {
            Kind = ContentLibraryEntryKind.Dialog,
            Title = "Diálogo simple",
            Status = status,
            Summary = "1 texto",
            Relation = missionName is null
                ? $"NPC: {npcName}"
                : $"NPC: {npcName} · Misión: {missionName}",
            NpcId = npc.Id,
            MissionDraftId = mission?.DraftId,
            LinkedMissionName = missionName ?? "",
            HasLinkedMission = mission is not null,
            StartNpcName = npcName,
            QuestionCount = 1,
            IsPublishedLocally = npc.PublishedBd,
            StatusGroup = ClassifyStatus(status),
            CanDuplicate = false,
            HoverTooltip = BuildBlockHoverTooltip(workspace, npc, mission, focus: "Diálogo"),
        };
    }

    private static ContentLibraryEntry BuildInteractiveDialogEntry(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        MissionDraft? mission,
        IReadOnlyList<DialogQuestionDraft> questions)
    {
        var questionCount = questions.Count;
        var npcName = string.IsNullOrWhiteSpace(npc.Nombre) ? "(NPC)" : npc.Nombre.Trim();
        var missionName = mission is null || string.IsNullOrWhiteSpace(mission.Nombre)
            ? null
            : mission.Nombre.Trim();
        var status = npc.PublishedBd ? StatusPublicado : StatusBorrador;
        return new ContentLibraryEntry
        {
            Kind = ContentLibraryEntryKind.Dialog,
            Title = "Conversación",
            Status = status,
            Summary = string.Create(CultureInfo.InvariantCulture, $"{questionCount} preguntas"),
            Relation = missionName is null
                ? $"NPC: {npcName}"
                : $"NPC: {npcName} · Misión: {missionName}",
            NpcId = npc.Id,
            MissionDraftId = mission?.DraftId,
            LinkedMissionName = missionName ?? "",
            HasLinkedMission = mission is not null,
            StartNpcName = npcName,
            QuestionCount = questionCount,
            IsPublishedLocally = npc.PublishedBd,
            StatusGroup = ClassifyStatus(status),
            CanDuplicate = false,
            HoverTooltip = BuildBlockHoverTooltip(workspace, npc, mission, focus: "Diálogo"),
        };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(sin texto)";
        var t = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= max ? t : t[..max] + "…";
    }
}
