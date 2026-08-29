using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.6B — read a locally cached dialog_es_*.swf. Never talks to SFTP.</summary>
public static class DialogEsLocalCache
{
    private static readonly Regex FileName = new(@"^dialog_es_(\d+)\.swf$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryLoadLatest(string? directory, out byte[] bytes, out string? path, out string? error)
    {
        bytes = Array.Empty<byte>();
        path = null;
        error = null;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error = "Sin caché local de dialog_es.";
            return false;
        }

        string? bestPath = null;
        var bestVer = -1;
        foreach (var file in Directory.EnumerateFiles(directory, "dialog_es_*.swf"))
        {
            var name = Path.GetFileName(file);
            var m = FileName.Match(name);
            if (!m.Success) continue;
            var ver = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (ver > bestVer)
            {
                bestVer = ver;
                bestPath = file;
            }
        }

        if (bestPath is null)
        {
            error = "No hay dialog_es_*.swf en la caché local.";
            return false;
        }

        bytes = File.ReadAllBytes(bestPath);
        path = bestPath;
        return true;
    }
}
