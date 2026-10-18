namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONTENT.LIBRARY.ORGANIZE.DELETE.2 — solo workspace local; nunca BD/SFTP/SWF.</summary>
public enum ContentLibraryRemovalKind
{
    DeleteUnpublishedDraft,
    RemoveLocalCopyOfPublished,
}

public sealed class ContentLibraryRemovalPlan
{
    public required ContentLibraryRemovalKind Kind { get; init; }
    public required bool Allowed { get; init; }
    public required string Caption { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> SummaryLines { get; init; } = Array.Empty<string>();
    public bool RequiresLinkedConfirm { get; init; }
    public bool HasLinkedLocalContent { get; init; }
}

public static class ContentLibraryLocalCleanup
{
    public static bool IsMissionPublishedLocally(MissionDraft mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return mission.PublishedBd || mission.PublishedQuestId is > 0;
    }

    public static ContentLibraryRemovalPlan PlanMission(ContentDraftWorkspace workspace, Guid draftId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var mission = workspace.Missions.FindByDraftId(draftId);
        if (mission is null)
        {
            return new ContentLibraryRemovalPlan
            {
                Kind = ContentLibraryRemovalKind.DeleteUnpublishedDraft,
                Allowed = false,
                Caption = "Misión",
                Message = "La misión ya no está en el workspace local.",
            };
        }

        var name = string.IsNullOrWhiteSpace(mission.Nombre) ? "(sin nombre)" : mission.Nombre.Trim();
        var stages = mission.Stages.Count;
        var objectives = mission.Stages.Sum(s => s.Objectives.Count(o => !o.IsAutoReturnToStartNpc));
        var dialogRefs = workspace.Dialogs.FindResponseRefsToMission(draftId).Count;
        var published = IsMissionPublishedLocally(mission);

        var lines = new List<string>
        {
            $"Misión: {name}",
            $"Etapas: {stages}",
            $"Objetivos: {objectives}",
        };
        if (dialogRefs > 0)
            lines.Add($"Enlaces acción 44 a desvincular: {dialogRefs}");

        if (published)
        {
            return new ContentLibraryRemovalPlan
            {
                Kind = ContentLibraryRemovalKind.RemoveLocalCopyOfPublished,
                Allowed = true,
                Caption = "Quitar copia local",
                Message =
                    "¿Quitar la copia local de esta misión?\n\n" +
                    "Se elimina únicamente del workspace de RUFUS ADMIN.\n" +
                    "La misión seguirá existiendo en el servidor.\n" +
                    "No se ejecuta DELETE en BD ni se modifica el cliente.",
                SummaryLines = lines,
            };
        }

        return new ContentLibraryRemovalPlan
        {
            Kind = ContentLibraryRemovalKind.DeleteUnpublishedDraft,
            Allowed = true,
            Caption = "Eliminar borrador",
            Message =
                "¿Eliminar este borrador?\n" +
                "Esta acción elimina únicamente la copia local de RUFUS ADMIN.",
            SummaryLines = lines,
        };
    }

    public static ContentLibraryRemovalPlan PlanNpc(ContentDraftWorkspace workspace, int npcId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var npc = workspace.Npcs.FindById(npcId);
        if (npc is null || npc.IsDbCatalogSnapshot)
        {
            return new ContentLibraryRemovalPlan
            {
                Kind = ContentLibraryRemovalKind.DeleteUnpublishedDraft,
                Allowed = false,
                Caption = "NPC",
                Message = "El NPC ya no está en el workspace local.",
            };
        }

        var name = string.IsNullOrWhiteSpace(npc.Nombre) ? "(sin nombre)" : npc.Nombre.Trim();
        var linkedMissions = workspace.Missions.Missions
            .Where(m => m.StartNpcId == npcId)
            .ToList();
        var unpublishedMissions = linkedMissions.Where(m => !IsMissionPublishedLocally(m)).ToList();
        var questions = workspace.Dialogs.QuestionsForNpc(npcId);
        var hasDialog = questions.Count > 0
            || npc.DialogMode == NpcDialogMode.Simple
                && (!string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal) || npc.Pregunta > 0);
        var hasLinked = linkedMissions.Count > 0 || hasDialog;

        var lines = new List<string> { $"NPC: {name}" };
        foreach (var m in unpublishedMissions)
        {
            var mn = string.IsNullOrWhiteSpace(m.Nombre) ? "(sin nombre)" : m.Nombre.Trim();
            lines.Add($"Misión: {mn}");
            lines.Add($"Etapas: {m.Stages.Count}");
            lines.Add($"Objetivos: {m.Stages.Sum(s => s.Objectives.Count(o => !o.IsAutoReturnToStartNpc))}");
        }

        if (hasDialog)
        {
            lines.Add(questions.Count > 0
                ? $"Diálogo: conversación ({questions.Count} preguntas)"
                : "Diálogo: simple");
        }

        if (npc.PublishedBd)
        {
            return new ContentLibraryRemovalPlan
            {
                Kind = ContentLibraryRemovalKind.RemoveLocalCopyOfPublished,
                Allowed = true,
                Caption = "Quitar copia local",
                Message =
                    "¿Quitar la copia local de este NPC?\n\n" +
                    "Se elimina únicamente del workspace de RUFUS ADMIN.\n" +
                    "El NPC seguirá existiendo en el servidor.\n" +
                    "No se ejecuta DELETE en BD ni se modifica el cliente.",
                SummaryLines = lines,
                HasLinkedLocalContent = hasLinked,
            };
        }

        if (hasLinked)
        {
            return new ContentLibraryRemovalPlan
            {
                Kind = ContentLibraryRemovalKind.DeleteUnpublishedDraft,
                Allowed = true,
                Caption = "Eliminar NPC",
                Message =
                    "Este NPC tiene una misión y/o conversación vinculadas.\n\n" +
                    "CANCELAR = no borrar.\n" +
                    "OK = ELIMINAR NPC Y CONTENIDO LOCAL VINCULADO (solo no publicado).",
                SummaryLines = lines,
                RequiresLinkedConfirm = true,
                HasLinkedLocalContent = true,
            };
        }

        return new ContentLibraryRemovalPlan
        {
            Kind = ContentLibraryRemovalKind.DeleteUnpublishedDraft,
            Allowed = true,
            Caption = "Eliminar borrador",
            Message =
                "¿Eliminar este borrador?\n" +
                "Esta acción elimina únicamente la copia local de RUFUS ADMIN.",
            SummaryLines = lines,
        };
    }

    /// <summary>Quita la misión del workspace local. Desvincula acción 44. No toca producción.</summary>
    public static bool RemoveMissionLocal(ContentDraftWorkspace workspace, Guid draftId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Missions.FindByDraftId(draftId) is null)
            return false;

        workspace.Dialogs.UnlinkAllMissionReferences(draftId);
        var result = workspace.Missions.TryDeleteMission(
            draftId,
            unlinkAndDelete: true,
            findDialogRefs: null,
            out _);
        return result == MissionDeleteResult.Deleted;
    }

    /// <summary>
    /// Quita NPC del workspace local.
    /// Si <paramref name="removeLinkedUnpublished"/>, elimina misiones no publicadas con StartNpcId
    /// y el diálogo del NPC. Misiones publicadas solo pierden la referencia StartNpcId.
    /// </summary>
    public static bool RemoveNpcLocal(
        ContentDraftWorkspace workspace,
        int npcId,
        bool removeLinkedUnpublished)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var npc = workspace.Npcs.FindById(npcId);
        if (npc is null || npc.IsDbCatalogSnapshot)
            return false;

        var linked = workspace.Missions.Missions
            .Where(m => m.StartNpcId == npcId)
            .ToList();

        foreach (var m in linked)
        {
            if (removeLinkedUnpublished && !IsMissionPublishedLocally(m))
            {
                RemoveMissionLocal(workspace, m.DraftId);
                continue;
            }

            if (m.StartNpcId == npcId)
            {
                m.StartNpcId = null;
                m.PregDarPreguntaId = null;
                m.PregIncompletaPreguntaId = null;
                m.PregCompletadaPreguntaId = null;
            }
        }

        foreach (var qid in workspace.Dialogs.QuestionsForNpc(npcId).Select(q => q.Id).ToList())
            workspace.Missions.ClearPreguntaReferences(qid);
        workspace.Dialogs.RemoveQuestionsForNpc(npcId);
        return workspace.Npcs.Remove(npc);
    }

    /// <summary>
    /// True si las preguntas del NPC solo se usan como roles de <paramref name="missionDraftId"/>
    /// (ninguna otra misión las referencia). No implica borrado automático al quitar una misión:
    /// los diálogos pertenecen al NPC y se preservan salvo borrado de NPC.
    /// </summary>
    public static bool DialogRolesExclusiveToMission(
        ContentDraftWorkspace workspace,
        Guid missionDraftId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var mission = workspace.Missions.FindByDraftId(missionDraftId);
        if (mission is null) return false;

        var roleIds = new HashSet<int>();
        if (mission.PregDarPreguntaId is int a and > 0) roleIds.Add(a);
        if (mission.PregIncompletaPreguntaId is int b and > 0) roleIds.Add(b);
        if (mission.PregCompletadaPreguntaId is int c and > 0) roleIds.Add(c);
        if (roleIds.Count == 0) return true;

        foreach (var other in workspace.Missions.Missions.Where(m => m.DraftId != missionDraftId))
        {
            if (other.PregDarPreguntaId is int x && roleIds.Contains(x)) return false;
            if (other.PregIncompletaPreguntaId is int y && roleIds.Contains(y)) return false;
            if (other.PregCompletadaPreguntaId is int z && roleIds.Contains(z)) return false;
        }

        return true;
    }
}
