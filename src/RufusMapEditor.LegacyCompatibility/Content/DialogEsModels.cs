namespace RufusMapEditor.LegacyCompatibility.Content;

public enum DialogEsSpace
{
    Question = 0,
    Answer = 1,
}

public sealed class DialogEsAssignment
{
    public required DialogEsSpace Space { get; init; }
    public required int Id { get; init; }
    public required string Text { get; init; }
}

public sealed class DialogEsSnapshot
{
    public required int Version { get; init; }
    public required int SwfVersion { get; init; }
    public required bool WasCompressed { get; init; }
    public required string Signature { get; init; }
    public required IReadOnlyDictionary<int, string> Questions { get; init; }
    public required IReadOnlyDictionary<int, string> Answers { get; init; }
    public required int QuestionAssignmentCount { get; init; }
    public required int AnswerAssignmentCount { get; init; }
    public required bool HasFileEnd { get; init; }
    public required int ConstantPoolCount { get; init; }
    public required int DoActionCount { get; init; }

    public int MaxQuestionId => Questions.Count == 0 ? 0 : Questions.Keys.Max();
    public int MaxAnswerId => Answers.Count == 0 ? 0 : Answers.Keys.Max();
    public bool ContainsQuestion(int id) => Questions.ContainsKey(id);
    public bool ContainsAnswer(int id) => Answers.ContainsKey(id);
}

public sealed class DialogEsGenerateRequest
{
    public required byte[] SourceSwfBytes { get; init; }
    public required IReadOnlyList<DialogEsAssignment> Additions { get; init; }
    public string? OutputDirectory { get; init; }
}

public sealed class DialogEsGenerateResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? OutputBytes { get; init; }
    public string? OutputPath { get; init; }
    public int SourceVersion { get; init; }
    public int TargetVersion { get; init; }
    public DialogEsSnapshot? SourceSnapshot { get; init; }
    public DialogEsSnapshot? OutputSnapshot { get; init; }
}

public sealed class DialogEsPreviewLine
{
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public int? DialogQuestionId { get; init; }
    public int? DialogAnswerId { get; init; }
    public int? BdQuestionId { get; init; }
    public int? BdResponseId { get; init; }
    public int? NpcPreguntaColumn { get; init; }
    public int? OwnerNpcDraftId { get; init; }
}

public sealed class DialogEsIdOccupancy
{
    public int BdQuestionMax { get; init; }
    public int BdResponseMax { get; init; }
    public IReadOnlySet<int>? BdOccupiedQuestions { get; init; }
    public IReadOnlySet<int>? BdOccupiedResponses { get; init; }

    public bool QuestionOccupiedInBd(int id)
    {
        if (BdOccupiedQuestions is not null)
            return BdOccupiedQuestions.Contains(id);
        return id > 0 && id <= BdQuestionMax;
    }

    public bool ResponseOccupiedInBd(int id)
    {
        if (BdOccupiedResponses is not null)
            return BdOccupiedResponses.Contains(id);
        return id > 0 && id <= BdResponseMax;
    }
}
