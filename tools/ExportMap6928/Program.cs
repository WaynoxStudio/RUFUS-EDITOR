using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Swf;

var library = @"c:\Users\rubez\Desktop\RUFUS EDITOR\Library";
var rufmapPath = Path.Combine(library, "Maps", "6928", "6928.rufmap");
var clientMaps = @"C:\Users\rubez\AppData\Roaming\Jeifus Launchers\rufus\cliente\resources\app\retroclient\data\maps";
var destName = "6928_0903231037X.swf";
var destPath = Path.Combine(clientMaps, destName);

var json = File.ReadAllText(rufmapPath);
var dto = RufmapSerializer.DeserializeDto(json);
var loaded = RufmapSerializer.ToDocument(dto);
var map = loaded.Document;
map.Id = 6928;

Console.WriteLine($"map {map.Id} {map.Width}x{map.Height} bg={map.BackgroundId} mapData={map.MapData.Length}");

var flasm = SwfMapExporter.ResolveFlasmExe(library)
    ?? throw new InvalidOperationException("flasm.exe not found");
var blank = SwfMapExporter.ResolveBlankSwf(library)
    ?? throw new InvalidOperationException("blank.swf not found");

var outTmp = Path.Combine(Path.GetTempPath(), "6928_rufus_export.swf");
if (File.Exists(outTmp)) File.Delete(outTmp);

var result = SwfMapExporter.Export(new SwfExportRequest
{
    Document = map,
    DestinationSwfPath = outTmp,
    FlasmExePath = flasm,
    BlankSwfTemplatePath = blank,
});

if (!result.Success)
    throw new InvalidOperationException(result.ErrorMessage ?? "export failed");

Console.WriteLine($"exported {result.OutputBytes} bytes");

var bak = destPath + $".bak_push_{DateTime.Now:yyyyMMdd_HHmmss}";
File.Copy(destPath, bak, overwrite: true);
File.Copy(outTmp, destPath, overwrite: true);
Console.WriteLine($"installed -> {destPath}");
Console.WriteLine($"backup -> {bak}");

// quick verify object ids in exported mapData
var md = result.MapDataExported;
const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
var idx = alphabet.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);
var counts = new Dictionary<int, int>();
for (var i = 0; i + 10 <= md.Length; i += 10)
{
    var cell = md.AsSpan(i, 10);
    Span<int> n = stackalloc int[10];
    for (var k = 0; k < 10; k++) n[k] = idx[cell[k]];
    var o1 = ((n[0] & 4) << 11) + ((n[4] & 1) << 12) + (n[5] << 6) + n[6];
    var o2 = ((n[0] & 2) << 12) + ((n[7] & 1) << 12) + (n[8] << 6) + n[9];
    foreach (var id in new[] { o1, o2 })
    {
        if (id is 9101 or 9102 or 9103)
            counts[id] = counts.GetValueOrDefault(id) + 1;
    }
}
Console.WriteLine("object counts in export: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
