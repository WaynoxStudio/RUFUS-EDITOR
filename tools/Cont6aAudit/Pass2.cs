using System.Globalization;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

// Local-only second pass on already-downloaded SWF. No SFTP.
var path = args.Length > 0 ? args[0] : throw new InvalidOperationException("swf path required");
var bytes = File.ReadAllBytes(path);
var container = SwfContainer.Read(bytes);
var tag = container.Tags.Single(t => t.Code == 12);
var pool = Avm1Bytecode.ReadConstantPool(tag.Data, out var poolEnd);
var actions = Avm1Bytecode.ParseActions(tag.Data, poolEnd, pool);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("FILE_END in pool: " + (Avm1Bytecode.IndexOfName(pool, "FILE_END") is int));
Console.WriteLine("pool names containing D/q/a/VERSION:");
for (var i = 0; i < pool.Count; i++)
{
    var s = pool[i];
    if (s is "D" or "q" or "a" or "VERSION" or "MA" or "m" or "FILE_END")
        Console.WriteLine($"  [{i}] {s}");
}

var q = new Dictionary<int, string>();
var a = new Dictionary<int, string>();
var qDup = 0;
var aDup = 0;
var other = new Dictionary<string, int>(StringComparer.Ordinal);
var paths = new Dictionary<string, int>(StringComparer.Ordinal);

var stack = new List<StackVal>();
void Push(StackVal v) => stack.Add(v);
StackVal Pop()
{
    if (stack.Count == 0) return StackVal.Unk("empty");
    var v = stack[^1];
    stack.RemoveAt(stack.Count - 1);
    return v;
}

foreach (var act in actions)
{
    switch (act.Code)
    {
        case Avm1Opcode.Push:
            if (act.PushItems is null) break;
            foreach (var p in act.PushItems)
            {
                if (p.Type == Avm1PushType.Integer && p.IntValue is int n)
                    Push(StackVal.Number(n));
                else if (!string.IsNullOrEmpty(p.StringValue))
                    Push(StackVal.Text(p.StringValue));
                else
                    Push(StackVal.Unk(p.Type.ToString()));
            }
            break;
        case Avm1Opcode.GetVariable:
            Push(StackVal.Obj(Pop().AsName() ?? "?"));
            break;
        case Avm1Opcode.GetMember:
        {
            var name = Pop();
            var obj = Pop();
            var n = name.AsName() ?? name.Int?.ToString(CultureInfo.InvariantCulture) ?? "?";
            Push(StackVal.Obj((obj.Path ?? obj.AsName() ?? "?") + "." + n));
            break;
        }
        case Avm1Opcode.SetMember:
        {
            var value = Pop();
            var name = Pop();
            var obj = Pop();
            var objPath = obj.Path ?? obj.AsName() ?? "?";
            paths[objPath] = paths.GetValueOrDefault(objPath) + 1;
            var key = name.Int;
            var text = value.Str ?? "";
            if (objPath == "D.q" && key is int qid)
            {
                if (!q.TryAdd(qid, text)) qDup++;
            }
            else if (objPath == "D.a" && key is int aid)
            {
                if (!a.TryAdd(aid, text)) aDup++;
            }
            else
            {
                var lab = objPath + "." + (name.AsName() ?? name.Int?.ToString() ?? "?");
                other[lab] = other.GetValueOrDefault(lab) + 1;
            }
            break;
        }
        case Avm1Opcode.SetVariable:
            Pop(); Pop();
            break;
        case Avm1Opcode.InitObject:
        {
            var n = Pop().Int ?? 0;
            for (var i = 0; i < n; i++) { Pop(); Pop(); }
            Push(StackVal.Obj("initObject"));
            break;
        }
        case Avm1Opcode.NewObject:
        {
            var argc = Pop().Int ?? 0;
            Pop();
            for (var i = 0; i < argc; i++) Pop();
            Push(StackVal.Obj("new"));
            break;
        }
        case Avm1Opcode.InitArray:
        {
            var n = Pop().Int ?? 0;
            for (var i = 0; i < n; i++) Pop();
            Push(StackVal.Obj("array"));
            break;
        }
    }
}

Console.WriteLine("\nSetMember object paths:");
foreach (var kv in paths.OrderByDescending(x => x.Value))
    Console.WriteLine($"  {kv.Key} x{kv.Value}");
Console.WriteLine("q dups: " + qDup + "  a dups: " + aDup);
Console.WriteLine("D.q unique: " + q.Count + " min=" + q.Keys.Min() + " max=" + q.Keys.Max());
Console.WriteLine("D.a unique: " + a.Count + " min=" + a.Keys.Min() + " max=" + a.Keys.Max());

var aHigh = a.Keys.Where(i => i > 20000).OrderBy(i => i).ToList();
Console.WriteLine("D.a IDs > 20000 (" + aHigh.Count + "): " + string.Join(", ", aHigh));
foreach (var id in aHigh)
    Console.WriteLine($"  a[{id}] " + Trunc(a[id], 100));

var qHigh = q.Keys.Where(i => i >= 19990).OrderBy(i => i).ToList();
Console.WriteLine("D.q IDs >= 19990 (" + qHigh.Count + "): " + string.Join(", ", qHigh.Take(40)));

Console.WriteLine("\n1075 q=" + q.ContainsKey(1075) + " a=" + a.ContainsKey(1075));
Console.WriteLine("20024 q=" + q.ContainsKey(20024) + " a=" + a.ContainsKey(20024));
if (q.TryGetValue(1075, out var tq)) Console.WriteLine("q1075 latin1: " + tq);
if (a.TryGetValue(1075, out var ta)) Console.WriteLine("a1075 latin1: " + ta);

var overlap = q.Keys.Intersect(a.Keys).Count();
Console.WriteLine("overlap exact D.q∩D.a: " + overlap);

// Next free after max excluding sparse outliers for answers: largest contiguous-ish cluster
var aSorted = a.Keys.OrderBy(x => x).ToList();
var gapsAfter13k = aSorted.Where(x => x > 13179).ToList();
Console.WriteLine("D.a after 13179: " + string.Join(", ", gapsAfter13k));
var nextQ = q.Keys.Max() + 1;
var nextADense = aSorted.Where(x => x < 20000).DefaultIfEmpty(0).Max() + 1;
Console.WriteLine("MAX(q)+1 = " + nextQ);
Console.WriteLine("MAX(a < 20000)+1 = " + nextADense);
Console.WriteLine("MAX(a)+1 = " + (a.Keys.Max() + 1));
Console.WriteLine("90001 in a? " + a.ContainsKey(90001) + "  90002? " + a.ContainsKey(90002));
Console.WriteLine("20007 in a? " + a.ContainsKey(20007) + "  20006 in q? " + q.ContainsKey(20006));
Console.WriteLine("other SetMember top:");
foreach (var kv in other.OrderByDescending(x => x.Value).Take(15))
    Console.WriteLine("  " + kv.Key + " x" + kv.Value);

bool HasFileEnd = actions.Any(x =>
    x.Code == Avm1Opcode.Push && x.PushItems is not null
    && x.PushItems.Any(p => p.StringValue == "FILE_END"));
Console.WriteLine("FILE_END push in actions: " + HasFileEnd);

static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
