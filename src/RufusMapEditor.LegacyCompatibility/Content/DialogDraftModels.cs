namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Local question draft (future npc_preguntas + dialog_es text). CONT.3.</summary>
public sealed class DialogQuestionDraft
{
    public int Id { get; set; }
    public int OwnerNpcId { get; set; }
    public string TextLocal { get; set; } = "";
    public string Params { get; set; } = "";
    public string Alternos { get; set; } = "";
    public bool PublishedBd { get; set; }
    public List<DialogResponseDraft> Responses { get; set; } = new();

    public DialogQuestionDraft CloneNewIdentity(int newId)
    {
        var q = new DialogQuestionDraft
        {
            Id = newId,
            OwnerNpcId = OwnerNpcId,
            TextLocal = TextLocal,
            Params = Params,
            Alternos = Alternos,
            PublishedBd = false,
        };
        foreach (var r in Responses)
            q.Responses.Add(r.CloneNewIdentity());
        return q;
    }
}

/// <summary>
/// Local response draft. Numeric npc_respuestas.id is NOT assigned until CONT.5 publish.
/// </summary>
public sealed class DialogResponseDraft
{
    public Guid DraftId { get; set; } = Guid.NewGuid();
    public string TextLocal { get; set; } = "";
    /// <summary>Assigned on CONT.5 publish (npc_respuestas.id lógico).</summary>
    public int? PublishedResponseId { get; set; }
    public List<DialogActionDraft> Actions { get; set; } = new();

    public DialogResponseDraft CloneNewIdentity()
    {
        var r = new DialogResponseDraft
        {
            DraftId = Guid.NewGuid(),
            TextLocal = TextLocal,
            PublishedResponseId = null,
        };
        foreach (var a in Actions)
            r.Actions.Add(a.Clone());
        return r;
    }
}

public sealed class DialogActionDraft
{
    public int Accion { get; set; }
    public string Args { get; set; } = "";
    public string Condicion { get; set; } = "";

    /// <summary>When Accion==1, preferred link target (also mirrored into Args).</summary>
    public int? TargetQuestionId { get; set; }

    /// <summary>When Accion==44, link to mission draft. Numeric Quest ID resolved in CONT.5.</summary>
    public Guid? TargetMissionDraftId { get; set; }

    public DialogActionDraft Clone() => new()
    {
        Accion = Accion,
        Args = Args,
        Condicion = Condicion,
        TargetQuestionId = TargetQuestionId,
        TargetMissionDraftId = TargetMissionDraftId,
    };

    public void SyncGotoArgs()
    {
        if (Accion == DialogActionCodes.GotoQuestion && TargetQuestionId is int qid)
            Args = qid.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Keep Args empty until CONT.5 assigns Quest ID; only store DraftId link.</summary>
    public void SyncStartQuestLink()
    {
        if (Accion == DialogActionCodes.StartQuest)
            Args = "";
    }
}
