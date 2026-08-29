using System.Collections.Concurrent;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// Fake SFTP de publicacion para tests.
/// Simula UploadNewFile / ReplaceFileAtomically sin red. DeleteAttemptCount siempre 0
/// (la limpieza del prev efímero de versions_es no cuenta).
/// </summary>
public sealed class FakeLangSftpPublishClient : ILangSftpPublishClient
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dirs = new(StringComparer.Ordinal);
    private bool _connected;
    private int _writeAttempts;

    public int WriteAttemptCount => _writeAttempts;
    public int DeleteAttemptCount => 0;
    public int UploadNewCount { get; private set; }
    public int AtomicReplaceCount { get; private set; }
    public int EphemeralCleanupCount { get; private set; }

    public void SeedFile(string remotePath, string text) =>
        SeedFile(remotePath, Encoding.UTF8.GetBytes(text));

    public void SeedFile(string remotePath, byte[] bytes) =>
        _files[Normalize(remotePath)] = bytes;

    public void SeedDirectory(string remotePath) =>
        _dirs[NormalizeDir(remotePath)] = 0;

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
        if (bytes.Length == 0)
            throw new InvalidOperationException("Descarga incompleta: archivo vacio (" + path + ").");
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

    public void UploadNewFile(string remotePath, byte[] content)
    {
        Ensure();
        ArgumentNullException.ThrowIfNull(content);
        var path = Normalize(remotePath);
        if (_files.ContainsKey(path))
            throw new InvalidOperationException("Archivo remoto ya existe (no se sobrescribe): " + path);
        _files[path] = (byte[])content.Clone();
        UploadNewCount++;
        Interlocked.Increment(ref _writeAttempts);
    }

    public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath)
    {
        Ensure();
        ArgumentNullException.ThrowIfNull(content);
        var finalPath = Normalize(remotePath);
        var backupPath = Normalize(backupRemotePath);
        var tempPath = finalPath + ".rufus-tmp";

        if (_files.ContainsKey(tempPath))
            throw new InvalidOperationException("Temp remoto ya existe (abortado): " + tempPath);
        if (_files.ContainsKey(backupPath))
            throw new InvalidOperationException("Prev efímero remoto ya existe (abortado): " + backupPath);
        if (!_files.TryGetValue(finalPath, out var current))
            throw new InvalidOperationException("Archivo a reemplazar inexistente: " + finalPath);

        _files[tempPath] = (byte[])content.Clone();
        Interlocked.Increment(ref _writeAttempts);

        _files[backupPath] = current;
        _files.TryRemove(finalPath, out _);
        Interlocked.Increment(ref _writeAttempts);

        _files[finalPath] = (byte[])content.Clone();
        _files.TryRemove(tempPath, out _);
        Interlocked.Increment(ref _writeAttempts);

        // CONT.8 — no dejar prev permanente.
        if (_files.TryRemove(backupPath, out _))
            EphemeralCleanupCount++;

        AtomicReplaceCount++;
    }

    /// <summary>No limpia el almacén: permite aserciones post-Publish (el servicio hace Dispose).</summary>
    public void Dispose()
    {
        _connected = false;
    }

    public bool PeekExists(string remotePath) => _files.ContainsKey(Normalize(remotePath));

    public IEnumerable<string> PeekPaths() => _files.Keys.OrderBy(k => k, StringComparer.Ordinal);

    public string PeekText(string remotePath) =>
        Encoding.UTF8.GetString(_files[Normalize(remotePath)]);

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
