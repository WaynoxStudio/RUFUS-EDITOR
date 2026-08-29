using System.Globalization;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.6B / CONT.6A — parse dialog_es DoAction: D.q / D.a / VERSION / FILE_END.</summary>
public static class DialogEsParser
{
    public const int DoActionTagCode = 12;

    public static DialogEsSnapshot Parse(byte[] swfBytes)
    {
        var parsed = ParseInternal(swfBytes);
        return ToSnapshot(parsed);
    }

    internal static DialogEsParsed ParseInternal(byte[] swfBytes)
    {
        var container = SwfContainer.Read(swfBytes);
        var signature = container.WasCompressed ? "CWS" : "FWS";
        var doActionIndex = -1;
        var doActionCount = 0;
        for (var i = 0; i < container.Tags.Count; i++)
        {
            if (container.Tags[i].Code != DoActionTagCode)
                continue;
            doActionCount++;
            if (doActionIndex < 0)
                doActionIndex = i;
        }

        if (doActionCount == 0)
            throw new InvalidOperationException("dialog_es: no se encontró DoAction.");
        if (doActionCount > 1)
            throw new InvalidOperationException("dialog_es: más de un DoAction; abortado.");

        var actionData = container.Tags[doActionIndex].Data;
        var pool = Avm1Bytecode.ReadConstantPool(actionData, out var poolEnd);
        var actions = Avm1Bytecode.ParseActions(actionData, poolEnd, pool);
        var (version, versionOff) = FindVersion(actions, actionData);
        var fileEnd = FindFileEndPush(actions)
            ?? throw new InvalidOperationException("dialog_es: no se encontró FILE_END.");

        var idxD = Avm1Bytecode.IndexOfName(pool, "D")
            ?? throw new InvalidOperationException("ConstantPool: falta 'D'.");
        var idxQ = Avm1Bytecode.IndexOfName(pool, "q")
            ?? throw new InvalidOperationException("ConstantPool: falta 'q'.");
        var idxA = Avm1Bytecode.IndexOfName(pool, "a")
            ?? throw new InvalidOperationException("ConstantPool: falta 'a'.");

        ExtractMaps(actions, out var questions, out var answers, out var qCount, out var aCount, out var qHits, out var aHits);

        return new DialogEsParsed
        {
            Container = container,
            Signature = signature,
            DoActionTagIndex = doActionIndex,
            ActionData = actionData,
            ConstantPool = pool,
            Actions = actions,
            VersionValue = version,
            VersionIntOffset = versionOff,
            FileEndPushOffset = fileEnd.Offset,
            Questions = questions,
            Answers = answers,
            QuestionAssignmentCount = qCount,
            AnswerAssignmentCount = aCount,
            QuestionHits = qHits,
            AnswerHits = aHits,
            IdxD = idxD,
            IdxQ = idxQ,
            IdxA = idxA,
        };
    }

    internal static DialogEsSnapshot ToSnapshot(DialogEsParsed parsed) => new()
    {
        Version = parsed.VersionValue,
        SwfVersion = parsed.Container.Version,
        WasCompressed = parsed.Container.WasCompressed,
        Signature = parsed.Signature,
        Questions = parsed.Questions,
        Answers = parsed.Answers,
        QuestionAssignmentCount = parsed.QuestionAssignmentCount,
        AnswerAssignmentCount = parsed.AnswerAssignmentCount,
        HasFileEnd = true,
        ConstantPoolCount = parsed.ConstantPool.Count,
        DoActionCount = 1,
    };

    private static (int version, int intOffset) FindVersion(IReadOnlyList<Avm1Action> actions, byte[] actionData)
    {
        for (var i = 0; i < actions.Count - 2; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null || a.PushItems.Count != 1)
                continue;
            if (!Avm1Bytecode.IsConstantNamed(a.PushItems[0], "VERSION")
                && a.PushItems[0].StringValue != "VERSION")
                continue;
            var b = actions[i + 1];
            if (b.Code != Avm1Opcode.Push || b.PushItems is null || b.PushItems.Count != 1
                || b.PushItems[0].Type != Avm1PushType.Integer)
                continue;
            if (actions[i + 2].Code != Avm1Opcode.SetVariable)
                continue;

            var intOffset = b.Offset + 3 + 1;
            var value = BitConverter.ToInt32(actionData, intOffset);
            if (value != b.PushItems[0].IntValue)
                throw new InvalidOperationException("VERSION: inconsistencia al leer entero.");
            return (value, intOffset);
        }

        throw new InvalidOperationException("VERSION no pudo localizarse en dialog_es.");
    }

    private static Avm1Action? FindFileEndPush(IReadOnlyList<Avm1Action> actions)
    {
        for (var i = 0; i < actions.Count - 1; i++)
        {
            var a = actions[i];
            if (a.Code != Avm1Opcode.Push || a.PushItems is null)
                continue;
            var has = a.PushItems.Any(p =>
                (p.Type == Avm1PushType.String && p.StringValue == "FILE_END")
                || Avm1Bytecode.IsConstantNamed(p, "FILE_END"));
            if (has && actions[i + 1].Code == Avm1Opcode.SetVariable)
                return a;
        }

        return null;
    }

    private static void ExtractMaps(
        IReadOnlyList<Avm1Action> actions,
        out Dictionary<int, string> questions,
        out Dictionary<int, string> answers,
        out int qCount,
        out int aCount,
        out Dictionary<int, int> qHits,
        out Dictionary<int, int> aHits)
    {
        questions = new Dictionary<int, string>();
        answers = new Dictionary<int, string>();
        qHits = new Dictionary<int, int>();
        aHits = new Dictionary<int, int>();
        qCount = 0;
        aCount = 0;
        var stack = new List<(string? Path, string? Str, int? Int)>();

        void Push((string? Path, string? Str, int? Int) v) => stack.Add(v);
        (string? Path, string? Str, int? Int) Pop()
        {
            if (stack.Count == 0) return (null, "empty", null);
            var v = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return v;
        }

        string? NameOf((string? Path, string? Str, int? Int) v) => v.Path ?? v.Str;

        foreach (var act in actions)
        {
            switch (act.Code)
            {
                case Avm1Opcode.Push:
                    if (act.PushItems is null) break;
                    foreach (var p in act.PushItems)
                    {
                        if (p.Type == Avm1PushType.Integer && p.IntValue is int n)
                            Push((null, n.ToString(CultureInfo.InvariantCulture), n));
                        else if (!string.IsNullOrEmpty(p.StringValue))
                        {
                            int? parsed = int.TryParse(p.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pn)
                                ? pn : null;
                            Push((null, p.StringValue, parsed));
                        }
                        else
                            Push((null, p.Type.ToString(), p.IntValue));
                    }
                    break;
                case Avm1Opcode.GetVariable:
                    Push((NameOf(Pop()) ?? "?", null, null));
                    break;
                case Avm1Opcode.GetMember:
                {
                    var name = Pop();
                    var obj = Pop();
                    var n = NameOf(name) ?? name.Int?.ToString(CultureInfo.InvariantCulture) ?? "?";
                    Push(((obj.Path ?? NameOf(obj) ?? "?") + "." + n, null, null));
                    break;
                }
                case Avm1Opcode.SetMember:
                {
                    var value = Pop();
                    var name = Pop();
                    var obj = Pop();
                    var path = obj.Path ?? NameOf(obj) ?? "?";
                    var key = name.Int;
                    var text = value.Str ?? "";
                    if (path == "D.q" && key is int qid)
                    {
                        questions[qid] = text;
                        qCount++;
                        qHits[qid] = qHits.GetValueOrDefault(qid) + 1;
                    }
                    else if (path == "D.a" && key is int aid)
                    {
                        answers[aid] = text;
                        aCount++;
                        aHits[aid] = aHits.GetValueOrDefault(aid) + 1;
                    }
                    break;
                }
                case Avm1Opcode.SetVariable:
                    Pop();
                    Pop();
                    break;
                case Avm1Opcode.InitObject:
                {
                    var n = Pop().Int ?? 0;
                    for (var i = 0; i < n; i++) { Pop(); Pop(); }
                    Push(("initObject", null, null));
                    break;
                }
                case Avm1Opcode.NewObject:
                {
                    var argc = Pop().Int ?? 0;
                    Pop();
                    for (var i = 0; i < argc; i++) Pop();
                    Push(("new", null, null));
                    break;
                }
                case Avm1Opcode.InitArray:
                {
                    var n = Pop().Int ?? 0;
                    for (var i = 0; i < n; i++) Pop();
                    Push(("array", null, null));
                    break;
                }
            }
        }
    }
}

internal sealed class DialogEsParsed
{
    public required SwfContainer Container { get; init; }
    public required string Signature { get; init; }
    public required int DoActionTagIndex { get; init; }
    public required byte[] ActionData { get; init; }
    public required IReadOnlyList<string> ConstantPool { get; init; }
    public required IReadOnlyList<Avm1Action> Actions { get; init; }
    public required int VersionValue { get; init; }
    public required int VersionIntOffset { get; init; }
    public required int FileEndPushOffset { get; init; }
    public required Dictionary<int, string> Questions { get; init; }
    public required Dictionary<int, string> Answers { get; init; }
    public required int QuestionAssignmentCount { get; init; }
    public required int AnswerAssignmentCount { get; init; }
    public required Dictionary<int, int> QuestionHits { get; init; }
    public required Dictionary<int, int> AnswerHits { get; init; }
    public required int IdxD { get; init; }
    public required int IdxQ { get; init; }
    public required int IdxA { get; init; }
}
