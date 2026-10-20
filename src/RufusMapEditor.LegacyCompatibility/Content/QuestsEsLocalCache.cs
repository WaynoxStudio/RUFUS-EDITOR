using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Read a locally cached quests_es_*.swf. Never talks to SFTP.</summary>
public static class QuestsEsLocalCache
{
    private static readonly Regex FileName = new(
        @"^quests_es_(\d+)\.swf$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryLoadLatest(string? directory, out byte[] bytes, out string? path, out string? error)
    {
        bytes = Array.Empty<byte>();
        path = null;
        error = null;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error = "Sin caché local de quests_es.";
            return false;
        }

        string? bestPath = null;
        var bestVer = -1;
        foreach (var file in Directory.EnumerateFiles(directory, "quests_es_*.swf"))
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
            error = "No hay quests_es_*.swf en la caché local.";
            return false;
        }

        bytes = File.ReadAllBytes(bestPath);
        path = bestPath;
        return true;
    }

    public static bool TryParseLatest(
        string? directory,
        out QuestsEsSnapshot? snapshot,
        out string? path,
        out string? error)
    {
        snapshot = null;
        if (!TryLoadLatest(directory, out var bytes, out path, out error))
            return false;

        try
        {
            snapshot = QuestsEsParser.Parse(bytes);
            return true;
        }
        catch (Exception ex)
        {
            error = "Caché quests_es ilegible: " + ex.Message;
            return false;
        }
    }
}
