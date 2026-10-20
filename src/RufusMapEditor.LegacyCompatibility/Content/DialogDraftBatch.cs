namespace RufusMapEditor.LegacyCompatibility.Content;

public enum QuestionDeleteResult
{
    NotFound,
    HasReferences,
    Deleted,
}

public readonly struct QuestionDeleteBlocked
{
    public QuestionDeleteBlocked(int questionId, IReadOnlyList<Guid> responseDraftIds)
    {
        QuestionId = questionId;
        ResponseDraftIds = responseDraftIds;
    }

    public int QuestionId { get; }
    public IReadOnlyList<Guid> ResponseDraftIds { get; }
}

/// <summary>
/// Global question ID batch for Content module (CONT.3).
/// Response drafts use Guid only until CONT.5 — numeric Response ID is reserved at publish.
/// DbMaxResponseId is cached for UI "próximo" and dialog_es coordination (CONTENT.IDS.MAX.1).
/// </summary>
public sealed class DialogDraftBatch
{
    private readonly List<DialogQuestionDraft> _questions = new();
    private int _dbMaxQuestionId;
    private int _dbMaxResponseId;

    public IReadOnlyList<DialogQuestionDraft> Questions => _questions;
    public int DbMaxQuestionId => _dbMaxQuestionId;
    public int DbMaxResponseId => _dbMaxResponseId;
    public int NextQuestionId => ComputeNextQuestionId();
    /// <summary>
    /// Next logical response id after BD MAX + unpublished local response drafts
    /// (drafts hold Guid until publish; count reserves consecutive slots).
    /// </summary>
    public int NextResponseId => ComputeNextResponseId();

    public void SetDbMaxQuestionId(int maxId)
    {
        if (maxId < 0) throw new ArgumentOutOfRangeException(nameof(maxId));
        _dbMaxQuestionId = maxId;
    }

    public void SetDbMaxResponseId(int maxId)
    {
        if (maxId < 0) throw new ArgumentOutOfRangeException(nameof(maxId));
        _dbMaxResponseId = maxId;
    }

    public void LoadQuestions(IEnumerable<DialogQuestionDraft> questions)
    {
        _questions.Clear();
        foreach (var q in questions)
            _questions.Add(q);
    }

    public DialogQuestionDraft CreateQuestion(int ownerNpcId)
    {
        var id = ComputeNextQuestionId();
        return CreateQuestionWithId(ownerNpcId, id);
    }

    /// <summary>MISSIONS.DIALOG.ROLES.3B — create with a pre-reserved npc_preguntas.id.</summary>
    public DialogQuestionDraft CreateQuestionWithId(int ownerNpcId, int id)
    {
        EnsureQuestionUnique(id);
        var q = new DialogQuestionDraft { Id = id, OwnerNpcId = ownerNpcId };
        _questions.Add(q);
        return q;
    }

    public DialogQuestionDraft DuplicateQuestion(DialogQuestionDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.CloneNewIdentity(ComputeNextQuestionId());
        EnsureQuestionUnique(copy.Id);
        // Remap internal goto links that pointed to source.Id stay pointing to source;
        // links to other questions kept as-is. Links that were self-goto become new id only if Target==source.
        foreach (var r in copy.Responses)
        {
            foreach (var a in r.Actions)
            {
                if (a.Accion == DialogActionCodes.GotoQuestion && a.TargetQuestionId == source.Id)
                {
                    a.TargetQuestionId = copy.Id;
                    a.SyncGotoArgs();
                }
            }
        }
        _questions.Add(copy);
        return copy;
    }

    public QuestionDeleteResult TryDeleteQuestion(int questionId, bool unlinkAndDelete, out QuestionDeleteBlocked? blocked)
    {
        blocked = null;
        var q = FindQuestion(questionId);
        if (q is null) return QuestionDeleteResult.NotFound;

        var refs = FindResponseRefsToQuestion(questionId);
        if (refs.Count > 0 && !unlinkAndDelete)
        {
            blocked = new QuestionDeleteBlocked(questionId, refs);
            return QuestionDeleteResult.HasReferences;
        }

        if (unlinkAndDelete)
            UnlinkAllReferencesToQuestion(questionId);

        _questions.RemoveAll(x => x.Id == questionId);
        return QuestionDeleteResult.Deleted;
    }

    public DialogResponseDraft AddResponse(DialogQuestionDraft question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var r = new DialogResponseDraft();
        question.Responses.Add(r);
        return r;
    }

    public DialogResponseDraft DuplicateResponse(DialogQuestionDraft question, DialogResponseDraft source)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.CloneNewIdentity();
        var idx = question.Responses.IndexOf(source);
        if (idx < 0) question.Responses.Add(copy);
        else question.Responses.Insert(idx + 1, copy);
        return copy;
    }

    public bool RemoveResponse(DialogQuestionDraft question, DialogResponseDraft response)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(response);
        return question.Responses.Remove(response)
               || question.Responses.RemoveAll(r => r.DraftId == response.DraftId) > 0;
    }

    public bool MoveResponse(DialogQuestionDraft question, DialogResponseDraft response, int delta)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(response);
        var idx = question.Responses.FindIndex(r => r.DraftId == response.DraftId);
        if (idx < 0) return false;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= question.Responses.Count) return false;
        question.Responses.RemoveAt(idx);
        question.Responses.Insert(newIdx, response);
        return true;
    }

    public DialogActionDraft AddAction(DialogResponseDraft response, int accion = DialogActionCodes.GotoQuestion)
    {
        if (!TryAddAction(response, accion, out var action, out var error))
            throw new InvalidOperationException(error);
        return action!;
    }

    /// <summary>Acción avanzada / cargas legacy: permite estados inválidos detectados por validación.</summary>
    public DialogActionDraft AddActionAllowingRuleViolations(
        DialogResponseDraft response, int accion = DialogActionCodes.Teleport)
    {
        ArgumentNullException.ThrowIfNull(response);
        var a = new DialogActionDraft { Accion = accion };
        response.Actions.Add(a);
        return a;
    }

    public bool TryAddAction(
        DialogResponseDraft response, int accion, out DialogActionDraft? action, out string? error)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!DialogResponseActionRules.CanAddAction(response, accion))
        {
            action = null;
            error = DialogResponseActionRules.DescribeAddBlock(response, accion);
            return false;
        }

        action = new DialogActionDraft { Accion = accion };
        response.Actions.Add(action);
        error = null;
        return true;
    }

    public bool RemoveAction(DialogResponseDraft response, DialogActionDraft action)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(action);
        return response.Actions.Remove(action);
    }

    /// <summary>True when another accion=44 still links to <paramref name="missionDraftId"/>.</summary>
    public bool HasStartQuestReferenceToMission(Guid missionDraftId, DialogActionDraft? excludingAction = null)
    {
        foreach (var q in _questions)
        {
            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (ReferenceEquals(a, excludingAction)) continue;
                    if (a.Accion == DialogActionCodes.StartQuest && a.TargetMissionDraftId == missionDraftId)
                        return true;
                }
            }
        }

        return false;
    }

    public void LinkGotoQuestion(DialogActionDraft action, int targetQuestionId)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (FindQuestion(targetQuestionId) is null)
            throw new InvalidOperationException($"Pregunta {targetQuestionId} no existe en el lote.");
        action.Accion = DialogActionCodes.GotoQuestion;
        action.TargetQuestionId = targetQuestionId;
        action.TargetMissionDraftId = null;
        action.SyncGotoArgs();
    }

    public void LinkStartMission(DialogActionDraft action, Guid missionDraftId)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.Accion = DialogActionCodes.StartQuest;
        action.TargetMissionDraftId = missionDraftId;
        action.TargetQuestionId = null;
        action.SyncStartQuestLink();
    }

    public void ClearStartMissionLink(DialogActionDraft action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.TargetMissionDraftId = null;
        if (action.Accion == DialogActionCodes.StartQuest)
            action.Args = "";
    }

    public IReadOnlyList<Guid> FindResponseRefsToMission(Guid missionDraftId)
    {
        var list = new List<Guid>();
        foreach (var q in _questions)
        {
            foreach (var r in q.Responses)
            {
                if (r.Actions.Any(a =>
                        a.Accion == DialogActionCodes.StartQuest &&
                        a.TargetMissionDraftId == missionDraftId))
                    list.Add(r.DraftId);
            }
        }
        return list;
    }

    public void UnlinkAllMissionReferences(Guid missionDraftId)
    {
        foreach (var q in _questions)
        {
            foreach (var r in q.Responses)
            {
                foreach (var a in r.Actions)
                {
                    if (a.TargetMissionDraftId == missionDraftId)
                    {
                        a.TargetMissionDraftId = null;
                        if (a.Accion == DialogActionCodes.StartQuest)
                            a.Args = "";
                    }
                }
            }
        }
    }

    public void ClearGotoLink(DialogActionDraft action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.TargetQuestionId = null;
        if (action.Accion == DialogActionCodes.GotoQuestion)
            action.Args = "";
    }

    /// <summary>Creates a new question and links accion=1 to it.</summary>
    public DialogQuestionDraft CreateQuestionLinkedFrom(
        DialogActionDraft action, int ownerNpcId)
    {
        ArgumentNullException.ThrowIfNull(action);
        var q = CreateQuestion(ownerNpcId);
        LinkGotoQuestion(action, q.Id);
        return q;
    }

    public void SetInitialQuestion(NpcsModeloDraft npc, int questionId)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var q = FindQuestion(questionId)
                ?? throw new InvalidOperationException($"Pregunta {questionId} no existe.");
        if (q.OwnerNpcId != npc.Id)
            throw new InvalidOperationException("La pregunta no pertenece a este NPC.");
        npc.Pregunta = questionId;
    }

    public DialogQuestionDraft? FindQuestion(int id) =>
        _questions.FirstOrDefault(q => q.Id == id);

    public IReadOnlyList<DialogQuestionDraft> QuestionsForNpc(int npcId) =>
        _questions.Where(q => q.OwnerNpcId == npcId).ToList();

    /// <summary>CONT-DIALOG.3 — drop interactive drafts when switching NPC to Simple mode.</summary>
    public int RemoveQuestionsForNpc(int npcId)
    {
        var ids = _questions.Where(q => q.OwnerNpcId == npcId).Select(q => q.Id).ToList();
        foreach (var id in ids)
            TryDeleteQuestion(id, unlinkAndDelete: true, out _);
        return ids.Count;
    }

    /// <summary>
    /// CONT.5 / CONT-DIALOG.1 — same rule as publish prevalidation: every response needs ≥1 action.
    /// </summary>
    public static bool IsResponseIncomplete(DialogResponseDraft response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Actions.Count == 0;
    }

    /// <summary>Responses of <paramref name="npcId"/> that would fail CONT.5 (0 actions).</summary>
    public IReadOnlyList<DialogResponseDraft> FindIncompleteResponsesForNpc(int npcId) =>
        QuestionsForNpc(npcId)
            .SelectMany(q => q.Responses)
            .Where(IsResponseIncomplete)
            .ToList();

    public bool HasIncompleteResponsesForNpc(int npcId) =>
        FindIncompleteResponsesForNpc(npcId).Count > 0;

    public IReadOnlyList<(Guid ResponseDraftId, string Error)> FindActionRuleViolationsForNpc(int npcId) =>
        QuestionsForNpc(npcId)
            .SelectMany(q => q.Responses)
            .SelectMany(r => DialogResponseActionRules.ValidateResponse(r)
                .Select(err => (r.DraftId, err)))
            .ToList();

    public bool HasActionRuleViolationsForNpc(int npcId) =>
        FindActionRuleViolationsForNpc(npcId).Count > 0;

    public IReadOnlyList<Guid> FindResponseRefsToQuestion(int questionId)
    {
        var list = new List<Guid>();
        foreach (var q in _questions)
        {
            foreach (var r in q.Responses)
            {
                if (r.Actions.Any(a =>
                        a.Accion == DialogActionCodes.GotoQuestion &&
                        (a.TargetQuestionId == questionId ||
                         (int.TryParse(a.Args, out var parsed) && parsed == questionId))))
                {
                    list.Add(r.DraftId);
                }
            }
        }
        return list;
    }

    public int? ResolveGotoTargetQuestionId(DialogActionDraft action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Accion != DialogActionCodes.GotoQuestion)
            return null;
        if (action.TargetQuestionId is int tid)
            return tid;
        if (int.TryParse(action.Args, out var parsed))
            return parsed;
        return null;
    }

    public int CountGotoReferencesToQuestion(int questionId) =>
        _questions.Sum(q => q.Responses.Sum(r =>
            r.Actions.Count(a =>
                a.Accion == DialogActionCodes.GotoQuestion &&
                (a.TargetQuestionId == questionId ||
                 (int.TryParse(a.Args, out var parsed) && parsed == questionId)))));

    /// <summary>True when <paramref name="gotoAction"/> is the only goto pointing at its target.</summary>
    public bool IsExclusiveGotoReference(DialogActionDraft gotoAction)
    {
        ArgumentNullException.ThrowIfNull(gotoAction);
        if (ResolveGotoTargetQuestionId(gotoAction) is not int questionId)
            return false;
        return CountGotoReferencesToQuestion(questionId) == 1;
    }

    public void ConvertActionAwayFromGoto(DialogActionDraft action, int newAccion)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Accion == DialogActionCodes.GotoQuestion)
            ClearGotoLink(action);
        action.Accion = newAccion;
        if (newAccion != DialogActionCodes.StartQuest)
            action.TargetMissionDraftId = null;
    }

    public bool HasDuplicateQuestionIds()
    {
        var seen = new HashSet<int>();
        foreach (var q in _questions)
            if (!seen.Add(q.Id)) return true;
        return false;
    }

    public bool AnyResponseUsesNumericPublishId()
    {
        // Guard: response drafts must never carry a reserved publish id field.
        // Model only has Guid DraftId — this always returns false if model is correct.
        return false;
    }

    public IReadOnlyList<int> ProvisionalQuestionIds => _questions.Select(q => q.Id).ToList();

    private void UnlinkAllReferencesToQuestion(int questionId)
    {
        foreach (var q in _questions)
        {
            foreach (var r in q.Responses)
            {
                r.Actions.RemoveAll(a =>
                    a.Accion == DialogActionCodes.GotoQuestion &&
                    (a.TargetQuestionId == questionId ||
                     (int.TryParse(a.Args, out var parsed) && parsed == questionId)));
            }
        }
    }

    private int ComputeNextQuestionId()
    {
        var maxDraft = _questions.Count == 0 ? 0 : _questions.Max(q => q.Id);
        return Math.Max(_dbMaxQuestionId, maxDraft) + 1;
    }

    private int ComputeNextResponseId()
    {
        var unpublished = _questions
            .Where(q => !q.PublishedBd)
            .SelectMany(q => q.Responses)
            .Count();
        return _dbMaxResponseId + unpublished + 1;
    }

    private void EnsureQuestionUnique(int id)
    {
        if (_questions.Any(q => q.Id == id))
            throw new InvalidOperationException($"ID provisional de pregunta duplicado: {id}");
    }
}
