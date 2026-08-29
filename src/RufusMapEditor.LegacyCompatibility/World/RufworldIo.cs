using System.Text;

namespace RufusMapEditor.LegacyCompatibility.World;

public static class RufworldIo
{
    public static void SaveAtomic(string destinationPath, string json, bool writeBackup = true)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Path required", nameof(destinationPath));
        if (!destinationPath.EndsWith(RufworldFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"La ruta debe terminar en {RufworldFormat.FileExtension}", nameof(destinationPath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var full = Path.GetFullPath(destinationPath);
        var temp = full + ".tmp";
        var backup = full + ".bak";

        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }

        if (new FileInfo(temp).Length == 0)
        {
            try { File.Delete(temp); } catch { /* ignore */ }
            throw new IOException("El archivo temporal de guardado quedó vacío.");
        }

        if (File.Exists(full))
            File.Replace(temp, full, writeBackup ? backup : null, ignoreMetadataErrors: true);
        else
            File.Move(temp, full);
    }

    public static string LoadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontró: {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
