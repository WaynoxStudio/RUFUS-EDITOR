using System.Collections.Concurrent;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>Fake SFTP READ-ONLY para tests. AttemptWrite solo para verificar que sync no escribe.</summary>
public sealed class FakeLangSftpReadClient : ILangSftpReadClient
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dirs = new(StringComparer.Ordinal);
    private bool _connected;
    private int _writeAttempts;

    public int DownloadCount { get; private set; }

    public int WriteAttemptCount => _writeAttempts;

    public void SeedFile(string remotePath, string text) =>
        SeedFile(remotePath, Encoding.UTF8.GetBytes(text));

    public void SeedFile(string remotePath, byte[] bytes) =>
        _files[Normalize(remotePath)] = bytes;

    public void SeedDirectory(string remotePath) =>
        _dirs[NormalizeDir(remotePath)] = 0;

    public void AttemptWrite(string remotePath, byte[] bytes)
    {
        Interlocked.Increment(ref _writeAttempts);
        _files[Normalize(remotePath)] = bytes;
    }

    public void Connect() => _connected = true;

    public bool FileExists(string remotePath)
    {
        Ensure();
        return _files.ContainsKey(Normalize(remotePath));
    }

    public bool DirectoryExists(string remotePath)
    {
        Ensure();
        var dir = NormalizeDir(remotePath);
        if (_dirs.ContainsKey(dir))
            return true;
        // Any seeded file under this directory implies the directory is reachable.
        return _files.Keys.Any(f => f.StartsWith(dir, StringComparison.Ordinal));
    }

    public string ReadAllText(string remotePath)
    {
        Ensure();
        var path = Normalize(remotePath);
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException("Archivo remoto inexistente: " + path, path);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] DownloadBytes(string remotePath)
    {
        Ensure();
        var path = Normalize(remotePath);
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException("Archivo remoto inexistente: " + path, path);
        DownloadCount++;
        if (bytes.Length == 0)
            throw new InvalidOperationException("Descarga incompleta: archivo vacío (" + path + ").");
        return (byte[])bytes.Clone();
    }

    public long GetFileLength(string remotePath)
    {
        Ensure();
        var path = Normalize(remotePath);
        if (!_files.TryGetValue(path, out var bytes))
            throw new FileNotFoundException("Archivo remoto inexistente: " + path, path);
        return bytes.LongLength;
    }

    public void Dispose() => _connected = false;

    private void Ensure()
    {
        if (!_connected)
            throw new InvalidOperationException("Cliente SFTP no conectado.");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string NormalizeDir(string path)
    {
        var p = Normalize(path).TrimEnd('/') + "/";
        return p == "/" ? "/" : p;
    }
}
