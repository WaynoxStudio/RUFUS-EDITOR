namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// Candidates and filters for «Publicar BD» / «Publicar cliente» NPC selection.
/// </summary>
public static class ContentPublishNpcSelection
{
    public sealed record Candidate(int Id, string Nombre, string Reason);

    public static bool Includes(int npcId, IReadOnlySet<int>? filter) =>
        filter is null || filter.Contains(npcId);

    public static IReadOnlyList<Candidate> ListBdCandidates(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var list = new List<Candidate>();
        foreach (var n in workspace.Npcs.Drafts.OrderBy(n => n.Id))
        {
            if (n.PublishedBd)
                continue;
            var name = string.IsNullOrWhiteSpace(n.Nombre) ? "(sin nombre)" : n.Nombre.Trim();
            list.Add(new Candidate(n.Id, name, "Pendiente BD"));
        }

        // Published NPCs that still own a mission UPDATE (not simple-dialog blocked).
        foreach (var m in workspace.Missions.Missions)
        {
            if (!m.PublishedBd || !m.HasPendingBdChanges || m.PublishedQuestId is not int or <= 0)
                continue;
            if (ContentPublishPlanBuilder.IsStartNpcSimpleDialog(workspace, m))
                continue;
            if (m.StartNpcId is not int sid || sid <= 0)
                continue;
            if (list.Any(c => c.Id == sid))
                continue;
            var npc = workspace.Npcs.FindById(sid);
            var name = npc is null || string.IsNullOrWhiteSpace(npc.Nombre)
                ? $"NPC #{sid}"
                : npc.Nombre.Trim();
            list.Add(new Candidate(sid, name, "Misión con cambios BD"));
        }

        return list;
    }

    public static IReadOnlyList<Candidate> ListClientCandidates(ContentDraftWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var list = new List<Candidate>();
        foreach (var n in workspace.Npcs.Drafts.OrderBy(n => n.Id))
        {
            var reasons = new List<string>(2);
            if (n.DialogMode == NpcDialogMode.Simple)
            {
                if (n.IsPendingDialogEs
                    || (n.Pregunta > 0 && !string.IsNullOrWhiteSpace(n.SimpleDialogTextLocal)))
                    reasons.Add("dialog_es");
            }
            else if (n.DialogMode == NpcDialogMode.Interactive
                     && workspace.Dialogs.QuestionsForNpc(n.Id).Any(q =>
                         !string.IsNullOrWhiteSpace(q.TextLocal)))
            {
                reasons.Add("dialog_es");
            }

            if (n.IsPendingNpcEsFor(workspace))
                reasons.Add("npc_es");

            if (reasons.Count == 0)
                continue;

            var name = string.IsNullOrWhiteSpace(n.Nombre) ? "(sin nombre)" : n.Nombre.Trim();
            list.Add(new Candidate(n.Id, name, string.Join(" + ", reasons)));
        }

        return list;
    }
}
