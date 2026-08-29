namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// CONT.6B — provisional ID reservation from live SWF + BD occupancy.
/// IDs are not definitive until a later publish re-reads SWF+BD.
/// </summary>
public sealed class DialogEsIdResolver
{
    private readonly DialogEsSnapshot _swf;
    private readonly DialogEsIdOccupancy _bd;
    private readonly HashSet<int> _reservedQ = new();
    private readonly HashSet<int> _reservedA = new();

    public DialogEsIdResolver(DialogEsSnapshot swf, DialogEsIdOccupancy? bd = null)
    {
        _swf = swf ?? throw new ArgumentNullException(nameof(swf));
        _bd = bd ?? new DialogEsIdOccupancy();
    }

    public IReadOnlySet<int> ReservedQuestions => _reservedQ;
    public IReadOnlySet<int> ReservedAnswers => _reservedA;

    /// <summary>A) Simple dialog — D.q only.</summary>
    public int ReserveSimpleQuestion()
    {
        var id = Math.Max(_swf.MaxQuestionId, 0) + 1;
        while (IsQuestionTaken(id))
            id++;
        _reservedQ.Add(id);
        return id;
    }

    /// <summary>B) Interactive question — same id in D.q and npc_preguntas.</summary>
    public int ReserveInteractiveQuestion()
    {
        var id = Math.Max(_swf.MaxQuestionId, _bd.BdQuestionMax) + 1;
        while (IsQuestionTaken(id) || _bd.QuestionOccupiedInBd(id))
            id++;
        _reservedQ.Add(id);
        return id;
    }

    /// <summary>C) Interactive response — same logical id in D.a and npc_respuestas.id (not orden).</summary>
    public int ReserveInteractiveAnswer()
    {
        var id = Math.Max(_swf.MaxAnswerId, _bd.BdResponseMax) + 1;
        while (IsAnswerTaken(id) || _bd.ResponseOccupiedInBd(id))
            id++;
        _reservedA.Add(id);
        return id;
    }

    private bool IsQuestionTaken(int id) =>
        _swf.ContainsQuestion(id) || _reservedQ.Contains(id);

    private bool IsAnswerTaken(int id) =>
        _swf.ContainsAnswer(id) || _reservedA.Contains(id);
}
