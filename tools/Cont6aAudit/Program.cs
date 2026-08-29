using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

// CONT.6A — READ ONLY audit of active dialog_es via shared Mapas SFTP.
// Never writes SFTP / versions_es / SWF / BD / Mapas.
Console.OutputEncoding = Encoding.UTF8;

var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RufusMapEditor", "settings.json");
using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
var sftpEl = doc.RootElement.GetProperty("LangSftp");
var sftp = new LangSftpSettings
{
    Host = sftpEl.GetProperty("Host").GetString() ?? "",
    Port = sftpEl.GetProperty("Port").GetInt32(),
    User = sftpEl.GetProperty("User").GetString() ?? "",
    PasswordProtectedBase64 = sftpEl.GetProperty("PasswordProtectedBase64").GetString(),
    LangRemotePath = sftpEl.TryGetProperty("LangRemotePath", out var lp)
        ? lp.GetString() ?? LangSftpSettings.DefaultLangRemotePath
        : LangSftpSettings.DefaultLangRemotePath,
    SwfRemotePath = sftpEl.TryGetProperty("SwfRemotePath", out var sp)
        ? sp.GetString() ?? LangSftpSettings.DefaultSwfRemotePath
        : LangSftpSettings.DefaultSwfRemotePath,
};

var langDir = NormalizeDir(string.IsNullOrWhiteSpace(sftp.LangRemotePath)
    ? LangSftpSettings.DefaultLangRemotePath
    : sftp.LangRemotePath);
var swfDir = NormalizeDir(string.IsNullOrWhiteSpace(sftp.SwfRemotePath)
    ? LangSftpSettings.DefaultSwfRemotePath
    : sftp.SwfRemotePath);
var versionsRemote = Combine(langDir, LangSftpSettings.VersionsFileName);

Console.WriteLine("CONT.6A READ-ONLY · SFTP " + sftp.User + "@" + sftp.Host + ":" + sftp.Port);
Console.WriteLine("Escrituras SFTP: 0 (solo Connect/Read/Exists)");
Console.WriteLine("Escrituras BD: 0");
Console.WriteLine();

var password = LangSftpPasswordProtector.Unprotect(sftp.PasswordProtectedBase64);
using var client = LangSftpReadClientFactory.Create(sftp, password);
client.Connect();
if (client.WriteAttemptCount != 0)
    throw new InvalidOperationException("Cliente SFTP escribió en Connect.");

if (!client.FileExists(versionsRemote))
    throw new InvalidOperationException("No existe " + versionsRemote);

var versionsText = client.ReadAllText(versionsRemote);
if (client.WriteAttemptCount != 0)
    throw new InvalidOperationException("Cliente SFTP escribió al leer versions_es.");

Dump("versions_es.txt (primeros 800 chars)", Truncate(versionsText.Replace("\r", "\\r").Replace("\n", "\\n"), 800));

if (!TryParseDialogVersion(versionsText, out var dialogVersion, out var dialogToken, out var parseErr))
    throw new InvalidOperationException(parseErr);

var swfName = string.Create(CultureInfo.InvariantCulture, $"dialog_es_{dialogVersion}.swf");
var swfRemote = Combine(swfDir, swfName);
Console.WriteLine($"Token dialog,es: {dialogToken}");
Console.WriteLine($"SWF esperado: {swfRemote}");
if (!client.FileExists(swfRemote))
    throw new InvalidOperationException("SWF activo no existe: " + swfRemote);

var remoteLen = client.GetFileLength(swfRemote);
var swfBytes = client.DownloadBytes(swfRemote);
if (client.WriteAttemptCount != 0)
    throw new InvalidOperationException("Cliente SFTP escribió al descargar SWF.");
if (swfBytes.Length != remoteLen)
    throw new InvalidOperationException($"Tamaño remoto={remoteLen} descarga={swfBytes.Length}");

var tmpDir = Path.Combine(Path.GetTempPath(), "rufus-cont6a-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmpDir);
var localSwf = Path.Combine(tmpDir, swfName);
File.WriteAllBytes(localSwf, swfBytes);
var sha = Convert.ToHexString(SHA256.HashData(swfBytes)).ToLowerInvariant();
var sig = swfBytes.Length >= 3
    ? $"{(char)swfBytes[0]}{(char)swfBytes[1]}{(char)swfBytes[2]}"
    : "?";
var swfVerByte = swfBytes.Length >= 4 ? swfBytes[3] : (byte)0;

Console.WriteLine();
Console.WriteLine("=== Copia local temporal ===");
Console.WriteLine("Path: " + localSwf);
Console.WriteLine($"Tamaño: {swfBytes.Length} bytes");
Console.WriteLine("SHA256: " + sha);
Console.WriteLine("Firma: " + sig);
Console.WriteLine("Versión SWF (byte): " + swfVerByte);
Console.WriteLine("WriteAttemptCount: " + client.WriteAttemptCount);

var container = SwfContainer.Read(swfBytes);
Console.WriteLine();
Console.WriteLine("=== Contenedor SWF ===");
Console.WriteLine($"WasCompressed={container.WasCompressed} Version={container.Version} Tags={container.Tags.Count} Frames={container.FrameCount}");
var tagCounts = container.Tags.GroupBy(t => t.Code).OrderBy(g => g.Key)
    .Select(g => $"tag{g.Key}={g.Count()}");
Console.WriteLine("Tags: " + string.Join(", ", tagCounts));

var doActions = container.Tags
    .Select((t, i) => (t, i))
    .Where(x => x.t.Code == 12)
    .ToList();
Console.WriteLine("DoAction count: " + doActions.Count);
if (doActions.Count == 0)
    throw new InvalidOperationException("No hay DoAction en dialog_es.");

var qIds = new SortedSet<int>();
var aIds = new SortedSet<int>();
var qTexts = new Dictionary<int, string>();
var aTexts = new Dictionary<int, string>();
var otherSetMembers = new Dictionary<string, int>(StringComparer.Ordinal);
int? internalVersion = null;
string? versionHow = null;
var opcodeHist = new Dictionary<byte, int>();
var poolPreview = new List<string>();
var memberRoots = new HashSet<string>(StringComparer.Ordinal);
var setMemberSamples = new List<string>();

foreach (var (tag, idx) in doActions)
{
    Console.WriteLine($"\n--- DoAction tagIndex={idx} bytes={tag.Data.Length} ---");
    IReadOnlyList<string> pool;
    int poolEnd;
    try
    {
        pool = Avm1Bytecode.ReadConstantPool(tag.Data, out poolEnd);
    }
    catch (Exception ex)
    {
        Console.WriteLine("ConstantPool: " + ex.Message);
        pool = Array.Empty<string>();
        poolEnd = 0;
    }

    if (pool.Count > 0)
    {
        Console.WriteLine($"ConstantPool count={pool.Count}");
        poolPreview.AddRange(pool.Take(40));
        foreach (var name in new[] { "D", "q", "a", "VERSION", "MA", "m", "FILE_END", "dialog" })
        {
            var i = Avm1Bytecode.IndexOfName(pool, name);
            Console.WriteLine($"  pool '{name}': {(i is int x ? "idx " + x : "AUSENTE")}");
        }
        Console.WriteLine("  pool[0..min(25)]: " + string.Join(" | ", pool.Take(25).Select(s => s.Length > 24 ? s[..24] + "…" : s)));
    }

    var actions = Avm1Bytecode.ParseActions(tag.Data, poolEnd == 0 ? 0 : poolEnd, pool);
    foreach (var a in actions)
        opcodeHist[a.Code] = opcodeHist.GetValueOrDefault(a.Code) + 1;

    var ver = FindVersion(actions);
    if (ver is int v)
    {
        internalVersion = v;
        versionHow = "SetVariable VERSION (entero)";
        Console.WriteLine("VERSION interna: " + v);
    }

    ExtractAssignments(actions, qIds, aIds, qTexts, aTexts, otherSetMembers, memberRoots, setMemberSamples);
}

Console.WriteLine("\n=== Opcodes DoAction (top) ===");
foreach (var kv in opcodeHist.OrderByDescending(x => x.Value).Take(15))
    Console.WriteLine($"  0x{kv.Key:X2} count={kv.Value}");

Console.WriteLine("\n=== Raíces/miembros observados en SetMember ===");
Console.WriteLine(string.Join(", ", memberRoots.OrderBy(x => x)));
if (otherSetMembers.Count > 0)
{
    Console.WriteLine("Otros SetMember (objeto.nombre ≠ D.q/D.a), top 20:");
    foreach (var kv in otherSetMembers.OrderByDescending(x => x.Value).Take(20))
        Console.WriteLine($"  {kv.Key} x{kv.Value}");
}

Console.WriteLine("\n=== Muestras SetMember D.q / D.a ===");
foreach (var s in setMemberSamples.Take(8))
    Console.WriteLine("  " + s);

void DumpSpace(string title, SortedSet<int> ids, Dictionary<int, string> texts)
{
    Console.WriteLine($"\n=== {title} ===");
    Console.WriteLine("Cantidad: " + ids.Count);
    if (ids.Count == 0)
    {
        Console.WriteLine("MIN/MAX: n/a");
        return;
    }
    Console.WriteLine("MIN: " + ids.Min);
    Console.WriteLine("MAX: " + ids.Max);
    Console.WriteLine("IDs cerca 1075: " + JoinNear(ids, 1075, 8));
    Console.WriteLine("IDs cerca 20024: " + JoinNear(ids, 20024, 8));
    Console.WriteLine("Últimos 12 IDs: " + string.Join(", ", ids.Reverse().Take(12).Reverse()));
    var in2k = ids.Count(i => i >= 20000 && i < 30000);
    Console.WriteLine("IDs en [20000,30000): " + in2k);
    foreach (var id in new[] { 1075, 20024 })
    {
        if (!texts.TryGetValue(id, out var t))
        {
            Console.WriteLine($"ID {id}: AUSENTE");
            continue;
        }
        Console.WriteLine($"ID {id} texto: " + Truncate(t.Replace("\n", "\\n"), 160));
    }
}

DumpSpace("Preguntas / frases (D.q)", qIds, qTexts);
DumpSpace("Respuestas jugador (D.a)", aIds, aTexts);

var overlap = qIds.Intersect(aIds).OrderBy(x => x).ToList();
Console.WriteLine("\n=== Solape numérico q∩a ===");
Console.WriteLine("Cantidad IDs en ambos espacios: " + overlap.Count);
if (overlap.Count > 0)
    Console.WriteLine("Muestra: " + string.Join(", ", overlap.Take(25)) + (overlap.Count > 25 ? "…" : ""));

var versionsMatch = internalVersion is int iv && iv == dialogVersion;
Console.WriteLine("\n=== Coincidencia VERSION ===");
Console.WriteLine($"versions_es dialog,es={dialogVersion} · SWF VERSION={internalVersion?.ToString() ?? "NO ENCONTRADA"} · match={(versionsMatch ? "OK" : "ERROR")}");
Console.WriteLine("Cómo se leyó VERSION: " + (versionHow ?? "no hallada"));

Console.WriteLine("\n=== Mutación (viabilidad, no aplicada) ===");
Console.WriteLine($"DoAction único: {(doActions.Count == 1 ? "SI" : "NO (" + doActions.Count + ")")}");
Console.WriteLine($"CWS recomprimible (SwfContainer.Write): SI (mismo contenedor que maps_es)");
Console.WriteLine($"Asignaciones D.q SetMember: {qIds.Count}");
Console.WriteLine($"Asignaciones D.a SetMember: {aIds.Count}");
Console.WriteLine("WriteAttemptCount final: " + client.WriteAttemptCount);

Console.WriteLine("\nDONE_CONT6A");
Console.WriteLine("LOCAL_SWF=" + localSwf);

static bool TryParseDialogVersion(string text, out int version, out string token, out string error)
{
    version = 0;
    token = "";
    error = "";
    var rx = new Regex(@"(?<![A-Za-z0-9_])dialog\s*,\s*es\s*,\s*(\d+)(?!\d)", RegexOptions.CultureInvariant);
    var matches = rx.Matches(text ?? "");
    if (matches.Count == 0)
    {
        error = "Token dialog,es,N no encontrado en versions_es.txt.";
        return false;
    }
    if (matches.Count > 1)
    {
        error = "versions_es.txt contiene más de un token dialog,es,N.";
        return false;
    }
    if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
        || version <= 0)
    {
        error = "Version dialog,es inválida.";
        return false;
    }
    token = string.Create(CultureInfo.InvariantCulture, $"dialog,es,{version}");
    return true;
}

static int? FindVersion(IReadOnlyList<Avm1Action> actions)
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
        return b.PushItems[0].IntValue;
    }
    return null;
}

static void ExtractAssignments(
    IReadOnlyList<Avm1Action> actions,
    SortedSet<int> qIds,
    SortedSet<int> aIds,
    Dictionary<int, string> qTexts,
    Dictionary<int, string> aTexts,
    Dictionary<string, int> otherSetMembers,
    HashSet<string> memberRoots,
    List<string> samples)
{
    var stack = new List<StackVal>();
    void Push(StackVal v) => stack.Add(v);
    StackVal Pop()
    {
        if (stack.Count == 0) return StackVal.Unk("empty");
        var v = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return v;
    }

    foreach (var a in actions)
    {
        switch (a.Code)
        {
            case Avm1Opcode.Push:
                if (a.PushItems is null) break;
                foreach (var p in a.PushItems)
                    Push(FromPush(p));
                break;
            case Avm1Opcode.GetVariable:
            {
                var name = Pop();
                Push(StackVal.Obj(name.AsName() ?? "?"));
                break;
            }
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
                var path = obj.Path ?? obj.AsName() ?? "?";
                var key = name.Int ?? (int.TryParse(name.AsName(), out var parsed) ? parsed : (int?)null);
                memberRoots.Add(path + "[" + (name.AsName() ?? name.Int?.ToString() ?? "?") + "]");
                var space = Classify(path);
                if (space is "q" or "a" && key is int id)
                {
                    var text = value.Str ?? "";
                    if (space == "q")
                    {
                        qIds.Add(id);
                        qTexts.TryAdd(id, text);
                    }
                    else
                    {
                        aIds.Add(id);
                        aTexts.TryAdd(id, text);
                    }
                    if (samples.Count < 12)
                        samples.Add($"{path}[{id}] = {Truncate(text.Replace('\n', ' '), 80)}");
                }
                else
                {
                    var label = path + "." + (name.AsName() ?? name.Int?.ToString() ?? "?");
                    otherSetMembers[label] = otherSetMembers.GetValueOrDefault(label) + 1;
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
                for (var i = 0; i < n; i++)
                {
                    Pop();
                    Pop();
                }
                Push(StackVal.Obj("initObject"));
                break;
            }
            case Avm1Opcode.NewObject:
            {
                var argc = Pop().Int ?? 0;
                Pop();
                for (var i = 0; i < argc; i++)
                    Pop();
                Push(StackVal.Obj("new"));
                break;
            }
            case Avm1Opcode.InitArray:
            {
                var n = Pop().Int ?? 0;
                for (var i = 0; i < n; i++)
                    Pop();
                Push(StackVal.Obj("array"));
                break;
            }
            default:
                // Keep stack from drifting too far on unknown ops: leave as-is (best-effort).
                break;
        }
    }
}

static string? Classify(string path)
{
    var p = path.Replace(" ", "");
    if (p.Equals("D.q", StringComparison.Ordinal) || p.EndsWith(".q", StringComparison.Ordinal))
        return "q";
    if (p.Equals("D.a", StringComparison.Ordinal) || p.EndsWith(".a", StringComparison.Ordinal))
        return "a";
    return null;
}

static StackVal FromPush(Avm1PushItem p)
{
    if (p.Type == Avm1PushType.Integer && p.IntValue is int i)
        return StackVal.Number(i);
    if (!string.IsNullOrEmpty(p.StringValue))
        return StackVal.Text(p.StringValue);
    if (p.Type == Avm1PushType.Boolean)
        return StackVal.Text(p.BoolValue == true ? "true" : "false");
    return StackVal.Unk(p.Type.ToString());
}

static string JoinNear(SortedSet<int> ids, int center, int take)
{
    var near = ids.Where(i => Math.Abs(i - center) <= 40).Take(take).ToList();
    return near.Count == 0 ? "(ninguno en ±40)" : string.Join(", ", near);
}

static string Combine(string dir, string file) => dir.TrimEnd('/') + "/" + file.TrimStart('/');
static string NormalizeDir(string path)
{
    var p = path.Replace('\\', '/').Trim();
    if (!p.EndsWith('/')) p += "/";
    return p;
}
static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

static void Dump(string title, string body)
{
    Console.WriteLine("=== " + title + " ===");
    Console.WriteLine(body);
    Console.WriteLine();
}
