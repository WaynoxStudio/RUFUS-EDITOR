using System.Text.RegularExpressions;
using RufusMapEditor.LegacyCompatibility.MapCrypto;
using RufusMapEditor.LegacyCompatibility.Swf;

var sqlPath = args[0];
var ids = new[] { 10420, 10111, 7411, 10425, 10429, 10421, 10422 };
var flasm = @"C:\Users\rubez\Desktop\RUFUS\Astria Map Editor 1\Flasm\flasm.exe";
var mapsDir = @"C:\Users\rubez\Desktop\RUFUS RETRO\resources\app\retroclient\data\maps";
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "RufusMapEditor.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }

    dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "RufusMapEditor.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

var art = Path.Combine(FindRepoRoot(), "tests", "artifacts", "crypto");
Directory.CreateDirectory(art);

foreach (var id in ids)
{
    string? line = null;
    using (var r = new StreamReader(sqlPath))
    {
        string? l;
        var prefix = $"INSERT INTO `maps` VALUES ({id},";
        while ((l = r.ReadLine()) != null)
            if (l.StartsWith(prefix, StringComparison.Ordinal)) { line = l; break; }
    }
    if (line is null) { Console.WriteLine($"{id}\tMISSING_IN_DB"); continue; }
    var m = Regex.Match(line, @"^INSERT INTO `maps` VALUES \((\d+), '([^']*)', (\d+), (\d+), '([^']*)', '([^']*)', '");
    if (!m.Success) { Console.WriteLine($"{id}\tPARSE_FAIL"); continue; }
    var date = m.Groups[2].Value;
    var key = m.Groups[6].Value;
    var after = line.Substring(m.Length);
    var end = after.IndexOf("', '", StringComparison.Ordinal);
    var dbMd = after.Substring(0, end);
    File.WriteAllText(Path.Combine(art, $"{id}_db_key.txt"), key);
    File.WriteAllText(Path.Combine(art, $"{id}_db_date.txt"), date);
    File.WriteAllText(Path.Combine(art, $"{id}_db_mapData.txt"), dbMd);

    var swf = Directory.GetFiles(mapsDir, $"{id}_*.swf").FirstOrDefault();
    if (swf is null) { Console.WriteLine($"{id}\t{date}\tkeyLen={key.Length}\tNO_SWF"); continue; }
    var meta = FlasmSwfMetadataReader.Read(swf, flasm, includeMapData: true);
    var kind = LegacyMapCrypto.LooksEncrypted(meta.MapData) ? "HEX" : "PLAIN";
    if (string.IsNullOrEmpty(key))
    {
        Console.WriteLine($"{id}\t{date}\tkey=EMPTY\tswf={Path.GetFileName(swf)}\tmd={meta.MapData.Length}\t{kind}\tDECRYPT=SKIP");
        continue;
    }
    try
    {
        var dec = LegacyMapCrypto.Decrypt(meta.MapData, key);
        var okDec = dec == dbMd;
        var re = LegacyMapCrypto.Encrypt(dbMd, key);
        var okEnc = re == meta.MapData;
        Console.WriteLine($"{id}\t{date}\tkeyLen={key.Length}\tswf={Path.GetFileName(swf)}\tenc={meta.MapData.Length}\tdecOK={okDec}\treencOK={okEnc}\tdbPlain={dbMd.Length}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{id}\tERROR\t{ex.Message}");
    }
}
