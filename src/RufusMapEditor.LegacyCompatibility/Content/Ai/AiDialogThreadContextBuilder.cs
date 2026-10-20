namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// Spoken-text conversation thread for bubble AI. No IDs, actions, args, or conditions.
/// Caps size to keep backend cost reasonable (backend concatenates Master+Context+Task).
/// </summary>
public static class AiDialogThreadContextBuilder
{
    public const int MaxTurns = 8;
    public const int MaxCharsPerTurn = 220;

    public static DialogQuestionDraft? FindParentQuestion(
        ContentDraftWorkspace workspace,
        DialogResponseDraft response)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(response);
        foreach (var q in workspace.Dialogs.Questions)
        {
            if (q.Responses.Any(r => r.DraftId == response.DraftId))
                return q;
        }

        return null;
    }

    /// <summary>
    /// Prior spoken turns before <paramref name="question"/> (does not include that question's text).
    /// </summary>
    public static string BuildPriorForQuestion(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogQuestionDraft question)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(question);

        var path = PathFromInitial(workspace, npc, question);
        if (path.Count == 0 && question.Id != npc.Pregunta)
            path = ReverseIncomingPath(workspace, npc, question);

        var lines = new List<string>();
        foreach (var (q, r) in path)
        {
            AppendSpoken(lines, "NPC", q.TextLocal);
            AppendSpoken(lines, "Jugador", r.TextLocal);
        }

        if (lines.Count > MaxTurns)
            lines = lines.TakeLast(MaxTurns).ToList();

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Prior turns before the parent NPC question. Parent question text is supplied separately.
    /// </summary>
    public static string BuildPriorForResponse(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogQuestionDraft parentQuestion) =>
        BuildPriorForQuestion(workspace, npc, parentQuestion);

    private static List<(DialogQuestionDraft Question, DialogResponseDraft Response)> PathFromInitial(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogQuestionDraft target)
    {
        var byId = workspace.Dialogs.QuestionsForNpc(npc.Id).ToDictionary(q => q.Id);
        var initialId = npc.Pregunta;
        if (initialId <= 0 || target.Id == initialId || !byId.ContainsKey(initialId))
            return [];

        var parentOf = new Dictionary<int, (DialogQuestionDraft Q, DialogResponseDraft R)>();
        var queue = new Queue<int>();
        var seen = new HashSet<int> { initialId };
        queue.Enqueue(initialId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!byId.TryGetValue(id, out var q))
                continue;

            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (a.Accion != DialogActionCodes.GotoQuestion || a.TargetQuestionId is not int tid)
                        continue;
                    if (!seen.Add(tid))
                        continue;
                    if (!byId.ContainsKey(tid))
                        continue;

                    parentOf[tid] = (q, r);
                    if (tid == target.Id)
                        return Reconstruct(parentOf, target.Id);
                    queue.Enqueue(tid);
                }
            }
        }

        return [];
    }

    private static List<(DialogQuestionDraft Question, DialogResponseDraft Response)> ReverseIncomingPath(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogQuestionDraft target)
    {
        var incoming = new Dictionary<int, (DialogQuestionDraft Q, DialogResponseDraft R)>();
        foreach (var q in workspace.Dialogs.QuestionsForNpc(npc.Id))
        {
            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (a.Accion != DialogActionCodes.GotoQuestion || a.TargetQuestionId is not int tid)
                        continue;
                    if (tid == q.Id)
                        continue;
                    incoming.TryAdd(tid, (q, r));
                }
            }
        }

        var parentOf = new Dictionary<int, (DialogQuestionDraft Q, DialogResponseDraft R)>();
        var visited = new HashSet<int>();
        var currentId = target.Id;
        while (incoming.TryGetValue(currentId, out var parent) && visited.Add(currentId))
        {
            parentOf[currentId] = parent;
            currentId = parent.Q.Id;
        }

        return Reconstruct(parentOf, target.Id);
    }

    private static List<(DialogQuestionDraft Question, DialogResponseDraft Response)> Reconstruct(
        Dictionary<int, (DialogQuestionDraft Q, DialogResponseDraft R)> parentOf,
        int targetId)
    {
        var stack = new List<(DialogQuestionDraft, DialogResponseDraft)>();
        var id = targetId;
        var guard = 0;
        while (parentOf.TryGetValue(id, out var parent) && guard++ < 64)
        {
            stack.Add(parent);
            id = parent.Q.Id;
        }

        stack.Reverse();
        return stack;
    }

    private static void AppendSpoken(List<string> lines, string speaker, string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0)
            return;
        t = t.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        if (t.Length > MaxCharsPerTurn)
            t = t[..MaxCharsPerTurn].TrimEnd() + "…";
        lines.Add($"{speaker}: {t}");
    }
}
