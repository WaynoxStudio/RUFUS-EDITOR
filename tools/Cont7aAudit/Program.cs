using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

// CONT.7A — READ ONLY audit of active npc_es via shared Mapas SFTP + RO BD compare.
// Never writes SFTP / versions_es / SWF / BD / Mapas / dialog_es.
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

var langDir = NormalizeDir(BlankTo(sftp.LangRemotePath, LangSftpSettings.DefaultLangRemotePath));
var swfDir = NormalizeDir(BlankTo(sftp.SwfRemotePath, LangSftpSettings.DefaultSwfRemotePath));
var versionsRemote = Combine(langDir, LangSftpSettings.VersionsFileName);

Console.WriteLine("CONT.7A READ-ONLY · SFTP " + sftp.User + "@" + sftp.Host + ":" + sftp.Port);
Console.WriteLine("Escrituras SFTP: 0 (solo Connect/Read/Exists)");
Console.WriteLine("Escrituras BD: 0 (solo SELECT)");
Console.WriteLine();

var password = LangSftpPasswordProtector.Unprotect(sftp.PasswordProtectedBase64);
using var client = LangSftpReadClientFactory.Create(sftp, password);
client.Connect();
AssertNoWrites(client, "Connect");

if (!client.FileExists(versionsRemote))
    throw new InvalidOperationException("No existe " + versionsRemote);

var versionsText = client.ReadAllText(versionsRemote);
AssertNoWrites(client, "Read versions_es");

Dump("versions_es.txt (primeros 900 chars)",
    Truncate(versionsText.Replace("\r", "\\r").Replace("\n", "\\n"), 900));

if (!TryParseNpcVersion(versionsText, out var npcVersion, out var npcToken, out var parseErr))
    throw new InvalidOperationException(parseErr);

var swfName = string.Create(CultureInfo.InvariantCulture, $"npc_es_{npcVersion}.swf");
var swfRemote = Combine(swfDir, swfName);
Console.WriteLine($"Token npc,es: {npcToken}");
Console.WriteLine($"SWF esperado: {swfRemote}");
if (!client.FileExists(swfRemote))
    throw new InvalidOperationException("SWF activo no existe: " + swfRemote);

var remoteLen = client.GetFileLength(swfRemote);
var swfBytes = client.DownloadBytes(swfRemote);
AssertNoWrites(client, "Download npc_es");
if (swfBytes.Length != remoteLen)
    throw new InvalidOperationException($"Tamaño remoto={remoteLen} descarga={swfBytes.Length}");

var tmpDir = Path.Combine(Path.GetTempPath(), "rufus-cont7a-" + Guid.NewGuid().ToString("N"));
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
    throw new InvalidOperationException("No hay DoAction en npc_es.");

int? internalVersion = null;
string? versionHow = null;
var opcodeHist = new Dictionary<byte, int>();
var allPools = new List<IReadOnlyList<string>>();
var setMemberNumeric = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
var setMemberOther = new Dictionary<string, int>(StringComparer.Ordinal);
var setVariableNumeric = new Dictionary<int, string>();
var setVariableNamed = new Dictionary<string, string>(StringComparer.Ordinal);
var pushTypeHist = new Dictionary<string, int>(StringComparer.Ordinal);
var samplesByPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
var latin1Fail = 0;
var latin1Ok = 0;
var utf8Suspect = 0;

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

    allPools.Add(pool);
    if (pool.Count > 0)
    {
        Console.WriteLine($"ConstantPool count={pool.Count}");
        foreach (var name in new[]
                 {
                     "VERSION", "FILE_END", "N", "n", "PN", "npc", "NPC", "NO", "NM",
                     "name", "nombre", "D", "q", "a", "MA", "m",
                 })
        {
            var i = Avm1Bytecode.IndexOfName(pool, name);
            Console.WriteLine($"  pool '{name}': {(i is int x ? "idx " + x : "AUSENTE")}");
        }

        Console.WriteLine("  pool[0..min(40)]: " + string.Join(" | ",
            pool.Take(40).Select(s => s.Length > 28 ? s[..28] + "…" : s)));
        Console.WriteLine("  pool short tokens (len<=3): " + string.Join(", ",
            pool.Where(s => s.Length is > 0 and <= 3).Distinct().Take(40)));
    }

    var actions = Avm1Bytecode.ParseActions(tag.Data, poolEnd == 0 ? 0 : poolEnd, pool);
    foreach (var a in actions)
    {
        opcodeHist[a.Code] = opcodeHist.GetValueOrDefault(a.Code) + 1;
        if (a.Code == Avm1Opcode.Push && a.PushItems is not null)
        {
            foreach (var p in a.PushItems)
                pushTypeHist[p.Type.ToString()] = pushTypeHist.GetValueOrDefault(p.Type.ToString()) + 1;
        }
    }

    var ver = FindVersion(actions);
    if (ver is int v)
    {
        internalVersion = v;
        versionHow = "SetVariable VERSION (entero)";
        Console.WriteLine("VERSION interna: " + v);
    }

    Extract(actions, setMemberNumeric, setMemberOther, setVariableNumeric, setVariableNamed, samplesByPath);
}

foreach (var pathMap in setMemberNumeric.Values)
{
    foreach (var text in pathMap.Values)
    {
        if (LooksLatin1Ok(text)) latin1Ok++;
        else latin1Fail++;
        if (LooksUtf8Mojibake(text)) utf8Suspect++;
    }
}

Console.WriteLine("\n=== Opcodes DoAction (top) ===");
foreach (var kv in opcodeHist.OrderByDescending(x => x.Value).Take(20))
    Console.WriteLine($"  0x{kv.Key:X2} count={kv.Value}");

Console.WriteLine("\n=== Push types ===");
foreach (var kv in pushTypeHist.OrderByDescending(x => x.Value))
    Console.WriteLine($"  {kv.Key}: {kv.Value}");

Console.WriteLine("\n=== SetMember paths con clave numérica (candidatos ID→texto) ===");
foreach (var kv in setMemberNumeric.OrderByDescending(x => x.Value.Count))
{
    var ids = kv.Value.Keys.OrderBy(x => x).ToList();
    Console.WriteLine($"PATH={kv.Key} count={ids.Count} MIN={ids.Min()} MAX={ids.Max()}");
    if (samplesByPath.TryGetValue(kv.Key, out var samples))
    {
        foreach (var s in samples.Take(10))
            Console.WriteLine("  sample: " + s);
    }
}

if (setMemberOther.Count > 0)
{
    Console.WriteLine("\n=== Otros SetMember (no numéricos), top 25 ===");
    foreach (var kv in setMemberOther.OrderByDescending(x => x.Value).Take(25))
        Console.WriteLine($"  {kv.Key} x{kv.Value}");
}

Console.WriteLine("\n=== SetVariable numéricos (nombre var = ID?) ===");
Console.WriteLine("count=" + setVariableNumeric.Count);
if (setVariableNumeric.Count > 0)
{
    var ids = setVariableNumeric.Keys.OrderBy(x => x).ToList();
    Console.WriteLine($"MIN={ids.Min()} MAX={ids.Max()}");
    foreach (var id in ids.Take(5).Concat(ids.AsEnumerable().Reverse().Take(5)).Distinct())
        Console.WriteLine($"  var[{id}] = {Truncate(setVariableNumeric[id], 80)}");
}

if (setVariableNamed.Count > 0)
{
    Console.WriteLine("\n=== SetVariable nombrados (muestra) ===");
    foreach (var kv in setVariableNamed.Take(20))
        Console.WriteLine($"  {kv.Key} = {Truncate(kv.Value, 80)}");
}

// Prefer N.d (NPC dictionary) over N.a (action labels) when present.
var primary = setMemberNumeric
    .OrderByDescending(x => string.Equals(x.Key, "N.d", StringComparison.Ordinal) ? int.MaxValue : x.Value.Count)
    .ThenByDescending(x => x.Value.Count)
    .ThenBy(x => x.Key, StringComparer.Ordinal)
    .FirstOrDefault();

Dictionary<int, string> names = primary.Value ?? new Dictionary<int, string>();
var primaryPath = primary.Key ?? "(ninguno)";

Console.WriteLine("\n=== Mapa primario elegido ===");
Console.WriteLine("Path: " + primaryPath);
Console.WriteLine("Cantidad: " + names.Count);
if (names.Count > 0)
{
    Console.WriteLine("MIN ID: " + names.Keys.Min());
    Console.WriteLine("MAX ID: " + names.Keys.Max());
    var gaps = CountReassignedHints(new SortedSet<int>(names.Keys));
    Console.WriteLine("Huecos en rango [MIN,MAX]: " + gaps);
    Console.WriteLine("IDs en [20000,30000): " + names.Keys.Count(i => i is >= 20000 and < 30000));
    Console.WriteLine("Últimos 15 IDs: " + string.Join(", ", names.Keys.OrderBy(x => x).Reverse().Take(15).Reverse()));
}

var checkIds = new[] { 251, 20001, 20002, 20003, 20062 };
Console.WriteLine("\n=== Casos reales (npc_es) ===");
foreach (var id in checkIds)
{
    if (names.TryGetValue(id, out var n))
        Console.WriteLine($"NPC {id}: PRESENTE · \"{Truncate(n.Replace('\n', ' '), 120)}\"");
    else
        Console.WriteLine($"NPC {id}: AUSENTE");
}

// Encoding summary
Console.WriteLine("\n=== Codificación (heurística sobre textos del mapa primario) ===");
Console.WriteLine($"Latin1-compatible OK≈{latin1Ok} FAIL≈{latin1Fail} UTF8-mojibake-suspect≈{utf8Suspect}");
if (names.Count > 0)
{
    var accent = names.Values.FirstOrDefault(v => v.Any(c => c is 'á' or 'é' or 'í' or 'ó' or 'ú' or 'ñ' or '¿' or '¡'));
    if (accent is not null)
        Console.WriteLine("Muestra con acentos: " + Truncate(accent, 100));
}

var versionsMatch = internalVersion is int iv && iv == npcVersion;
Console.WriteLine("\n=== Coincidencia VERSION ===");
Console.WriteLine($"versions_es npc,es={npcVersion} · SWF VERSION={internalVersion?.ToString() ?? "NO ENCONTRADA"} · match={(versionsMatch ? "OK" : "ERROR")}");
Console.WriteLine("Cómo se leyó VERSION: " + (versionHow ?? "no hallada"));

// BD RO compare
Console.WriteLine("\n=== BD RO npcs_modelo.id/nombre ===");
var dbEl = doc.RootElement.GetProperty("Database");
var db = new DatabaseSettings
{
    Host = dbEl.GetProperty("Host").GetString() ?? "",
    Port = dbEl.GetProperty("Port").GetInt32(),
    User = dbEl.GetProperty("User").GetString() ?? "",
    Database = dbEl.TryGetProperty("Database", out var dbe)
        ? dbe.GetString() ?? "estaticos"
        : "estaticos",
    PasswordProtectedBase64 = dbEl.GetProperty("PasswordProtectedBase64").GetString(),
};
var schema = string.IsNullOrWhiteSpace(db.Database) ? "estaticos" : db.Database.Trim();
var dbPassword = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
await using var conn = new MySqlConnection(db.BuildConnectionString(dbPassword));
await conn.OpenAsync();

var bdNames = new Dictionary<int, string>();
await using (var cmd = new MySqlCommand(
                 $"SELECT `{NpcsModeloColumns.Id}`, `{NpcsModeloColumns.Nombre}` FROM `{schema}`.`{NpcsModeloColumns.DefaultTable}` WHERE `{NpcsModeloColumns.Id}` IN (251,20001,20002,20003,20062)",
                 conn))
await using (var rd = await cmd.ExecuteReaderAsync())
{
    while (await rd.ReadAsync())
        bdNames[rd.GetInt32(0)] = rd.IsDBNull(1) ? "" : rd.GetString(1);
}

foreach (var id in checkIds)
{
    var inSwf = names.TryGetValue(id, out var sn);
    var inBd = bdNames.TryGetValue(id, out var bn);
    Console.WriteLine($"ID {id}:");
    Console.WriteLine($"  npc_es: {(inSwf ? "\"" + Truncate(sn!, 100) + "\"" : "AUSENTE")}");
    Console.WriteLine($"  BD:     {(inBd ? "\"" + Truncate(bn!, 100) + "\"" : "AUSENTE")}");
    if (inSwf && inBd)
    {
        var same = string.Equals(sn, bn, StringComparison.Ordinal);
        Console.WriteLine($"  nombre igual: {(same ? "SI" : "NO")}");
    }
}

// Broader sample: pick some IDs from SWF and check BD
var sampleIds = names.Keys.OrderBy(x => x).Where((_, i) => i % Math.Max(1, names.Count / 12) == 0).Take(12).ToList();
if (sampleIds.Count > 0)
{
    Console.WriteLine("\n=== Muestra aleatoria-estratificada SWF→BD ===");
    var inList = string.Join(",", sampleIds);
    await using var cmd2 = new MySqlCommand(
        $"SELECT `{NpcsModeloColumns.Id}`, `{NpcsModeloColumns.Nombre}` FROM `{schema}`.`{NpcsModeloColumns.DefaultTable}` WHERE `{NpcsModeloColumns.Id}` IN ({inList})",
        conn);
    var bdSample = new Dictionary<int, string>();
    await using (var rd2 = await cmd2.ExecuteReaderAsync())
    {
        while (await rd2.ReadAsync())
            bdSample[rd2.GetInt32(0)] = rd2.IsDBNull(1) ? "" : rd2.GetString(1);
    }

    var matchName = 0;
    var mismatchName = 0;
    var missingBd = 0;
    foreach (var id in sampleIds)
    {
        var swfN = names[id];
        if (!bdSample.TryGetValue(id, out var bdN))
        {
            missingBd++;
            Console.WriteLine($"  {id}: SWF=\"{Truncate(swfN, 40)}\" BD=AUSENTE");
            continue;
        }

        if (string.Equals(swfN, bdN, StringComparison.Ordinal))
        {
            matchName++;
            Console.WriteLine($"  {id}: OK mismo nombre \"{Truncate(swfN, 40)}\"");
        }
        else
        {
            mismatchName++;
            Console.WriteLine($"  {id}: DIF SWF=\"{Truncate(swfN, 40)}\" BD=\"{Truncate(bdN, 40)}\"");
        }
    }

    Console.WriteLine($"Resumen muestra: same={matchName} dif={mismatchName} bd_ausente={missingBd} de {sampleIds.Count}");
}

Console.WriteLine("\n=== Mutación (viabilidad, NO aplicada) ===");
Console.WriteLine($"DoAction único: {(doActions.Count == 1 ? "SI" : "NO (" + doActions.Count + ")")}");
Console.WriteLine($"CWS recomprimible (SwfContainer): SI");
Console.WriteLine($"Mapa primario SetMember numérico: {primaryPath} × {names.Count}");
Console.WriteLine($"VERSION SetVariable localizable: {(internalVersion is not null ? "SI" : "NO")}");
var mutationViable = doActions.Count == 1 && names.Count > 0 && internalVersion is not null && versionsMatch;
Console.WriteLine($"Mutación automática viable (preliminar): {(mutationViable ? "SI" : "PENDIENTE/NO")}");
Console.WriteLine("WriteAttemptCount final SFTP: " + client.WriteAttemptCount);

Console.WriteLine("\nDONE_CONT7A");
Console.WriteLine("LOCAL_SWF=" + localSwf);
Console.WriteLine("PRIMARY_PATH=" + primaryPath);
Console.WriteLine("NPC_VERSION=" + npcVersion);
Console.WriteLine("INTERNAL_VERSION=" + (internalVersion?.ToString() ?? "null"));
Console.WriteLine("COUNT=" + names.Count);
Console.WriteLine("HAS_20062=" + (names.ContainsKey(20062) ? "SI" : "NO"));

static void AssertNoWrites(ILangSftpReadClient client, string step)
{
    if (client.WriteAttemptCount != 0)
        throw new InvalidOperationException("Cliente SFTP escribió en " + step);
}

static bool TryParseNpcVersion(string text, out int version, out string token, out string error)
{
    version = 0;
    token = "";
    error = "";
    var rx = new Regex(@"(?<![A-Za-z0-9_])npc\s*,\s*es\s*,\s*(\d+)(?!\d)", RegexOptions.CultureInvariant);
    var matches = rx.Matches(text ?? "");
    if (matches.Count == 0)
    {
        error = "Token npc,es,N no encontrado en versions_es.txt.";
        return false;
    }

    if (matches.Count > 1)
    {
        error = "versions_es.txt contiene más de un token npc,es,N.";
        return false;
    }

    if (!int.TryParse(matches[0].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version)
        || version <= 0)
    {
        error = "Version npc,es inválida.";
        return false;
    }

    token = string.Create(CultureInfo.InvariantCulture, $"npc,es,{version}");
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

static void Extract(
    IReadOnlyList<Avm1Action> actions,
    Dictionary<string, Dictionary<int, string>> setMemberNumeric,
    Dictionary<string, int> setMemberOther,
    Dictionary<int, string> setVariableNumeric,
    Dictionary<string, string> setVariableNamed,
    Dictionary<string, List<string>> samplesByPath)
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
            case Avm1Opcode.InitObject:
            {
                var n = Pop().Int ?? 0;
                var props = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < n; i++)
                {
                    var val = Pop();
                    var key = Pop().AsName() ?? "?";
                    props[key] = val.Str ?? val.AsName() ?? "";
                }

                var nameProp = props.TryGetValue("n", out var nm) ? nm : null;
                Push(StackVal.Object(props, nameProp));
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
            case Avm1Opcode.SetMember:
            {
                var value = Pop();
                var name = Pop();
                var obj = Pop();
                var path = obj.Path ?? obj.AsName() ?? "?";
                var key = name.Int ?? (int.TryParse(name.AsName(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null);
                if (key is int id)
                {
                    // Prefer object.n when value is InitObject; else plain string.
                    string? text = null;
                    if (value.Props is not null && value.Props.TryGetValue("n", out var nText))
                        text = nText;
                    else if (!string.IsNullOrEmpty(value.NameText))
                        text = value.NameText;
                    else if (value.Str is not null && value.Props is null && !value.Str.StartsWith("init", StringComparison.Ordinal))
                        text = value.Str;

                    if (text is not null)
                    {
                        if (!setMemberNumeric.TryGetValue(path, out var map))
                        {
                            map = new Dictionary<int, string>();
                            setMemberNumeric[path] = map;
                        }

                        map.TryAdd(id, text);
                        if (!samplesByPath.TryGetValue(path, out var list))
                        {
                            list = new List<string>();
                            samplesByPath[path] = list;
                        }

                        if (list.Count < 12)
                        {
                            var propKeys = value.Props is null
                                ? ""
                                : " props=[" + string.Join(",", value.Props.Keys) + "]";
                            list.Add($"{path}[{id}] = {Truncate(text.Replace('\n', ' '), 60)}{propKeys}");
                        }
                    }
                    else
                    {
                        var label = path + "." + (name.AsName() ?? id.ToString(CultureInfo.InvariantCulture));
                        setMemberOther[label] = setMemberOther.GetValueOrDefault(label) + 1;
                    }
                }
                else
                {
                    var label = path + "." + (name.AsName() ?? name.Int?.ToString() ?? "?");
                    setMemberOther[label] = setMemberOther.GetValueOrDefault(label) + 1;
                }

                break;
            }
            case Avm1Opcode.SetVariable:
            {
                var value = Pop();
                var name = Pop();
                if (name.Int is int id && value.Str is not null)
                    setVariableNumeric.TryAdd(id, value.Str);
                else if (name.AsName() is string nm)
                    setVariableNamed.TryAdd(nm, value.Str ?? value.Int?.ToString(CultureInfo.InvariantCulture) ?? value.AsName() ?? "");
                break;
            }
            default:
                break;
        }
    }
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

static int CountReassignedHints(SortedSet<int> ids)
{
    if (ids.Count == 0) return 0;
    var min = ids.Min;
    var max = ids.Max;
    // Cap absurd ranges
    if (max - min > 500_000) return -1;
    return (max - min + 1) - ids.Count;
}

static bool LooksLatin1Ok(string s)
{
    try
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(s);
        var round = Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
        return round == s;
    }
    catch
    {
        return false;
    }
}

static bool LooksUtf8Mojibake(string s) =>
    s.Contains('Ã') || s.Contains('Â') || s.Contains("â€™", StringComparison.Ordinal);

static string Combine(string dir, string file) => dir.TrimEnd('/') + "/" + file.TrimStart('/');
static string NormalizeDir(string path)
{
    var p = path.Replace('\\', '/').Trim();
    if (!p.EndsWith('/')) p += "/";
    return p;
}

static string BlankTo(string? value, string fallback) =>
    string.IsNullOrWhiteSpace(value) ? fallback : value;

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

static void Dump(string title, string body)
{
    Console.WriteLine("=== " + title + " ===");
    Console.WriteLine(body);
    Console.WriteLine();
}

internal readonly struct StackVal
{
    public string? Path { get; init; }
    public string? Str { get; init; }
    public int? Int { get; init; }
    public IReadOnlyDictionary<string, string>? Props { get; init; }
    public string? NameText { get; init; }

    public static StackVal Obj(string path) => new() { Path = path, Str = path };
    public static StackVal Text(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return new StackVal { Str = s, Int = n };
        return new StackVal { Str = s };
    }
    public static StackVal Number(int n) => new() { Int = n, Str = n.ToString(CultureInfo.InvariantCulture) };
    public static StackVal Unk(string s) => new() { Str = s };
    public static StackVal Object(Dictionary<string, string> props, string? nameText) =>
        new() { Props = props, NameText = nameText, Str = nameText ?? "object", Path = "object" };
    public string? AsName() => Path ?? Str;
}
