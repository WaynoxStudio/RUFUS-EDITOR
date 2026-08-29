using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Rufmap;

/// <summary>
/// Atomic .rufmap writer: temp → flush → replace (optional single .bak).
/// </summary>
public static class RufmapIo
{
    public static void SaveAtomic(string destinationPath, string json, bool writeBackup = true)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Path required", nameof(destinationPath));
        if (!destinationPath.EndsWith(RufmapFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"La ruta debe terminar en {RufmapFormat.FileExtension}", nameof(destinationPath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var full = Path.GetFullPath(destinationPath);
        var temp = full + ".tmp";
        var backup = full + ".bak";

        // Write temp fully
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }

        // Sanity: non-empty
        var info = new FileInfo(temp);
        if (info.Length == 0)
        {
            try { File.Delete(temp); } catch { /* ignore */ }
            throw new IOException("El archivo temporal de guardado quedó vacío.");
        }

        if (File.Exists(full))
        {
            var backupPath = writeBackup ? backup : null;
            File.Replace(temp, full, backupPath, ignoreMetadataErrors: true);
            // Keep only one .bak (File.Replace already replaced previous backup content)
        }
        else
        {
            File.Move(temp, full);
        }
    }

    public static string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public static RufmapLoadResult LoadFile(string path)
    {
        var json = ReadAllText(path);
        return RufmapSerializer.LoadFromJson(json);
    }
}
