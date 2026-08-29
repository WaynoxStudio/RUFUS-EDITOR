using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>
/// Cliente SFTP SSH.NET para publicacion LANG.
/// Upload solo si no existe; replace via rename; nunca Delete.
/// </summary>
internal sealed class SshNetLangSftpPublishClient : ILangSftpPublishClient
{
    private readonly LangSftpSettings _settings;
    private readonly string _password;
    private SftpClient? _client;
    private int _writeAttempts;

    public SshNetLangSftpPublishClient(LangSftpSettings settings, string plainPassword)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _password = plainPassword ?? "";
    }

    public int WriteAttemptCount => _writeAttempts;
    public int DeleteAttemptCount => 0;

    public void Connect()
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
            throw new InvalidOperationException("Host SFTP vacio.");
        if (string.IsNullOrWhiteSpace(_settings.User))
            throw new InvalidOperationException("Usuario SFTP vacio.");

        var port = _settings.Port <= 0 ? 22 : _settings.Port;
        var connection = new ConnectionInfo(
            _settings.Host.Trim(),
            port,
            _settings.User.Trim(),
            new PasswordAuthenticationMethod(_settings.User.Trim(), _password))
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        _client?.Dispose();
        _client = new SftpClient(connection) { OperationTimeout = TimeSpan.FromSeconds(120) };

        try
        {
            _client.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            throw new InvalidOperationException("Autenticacion SFTP fallida.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new InvalidOperationException("Conexion SFTP fallida: " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Conexion SFTP fallida: " + Sanitize(ex.Message), ex);
        }

        if (!_client.IsConnected)
            throw new InvalidOperationException("Conexion SFTP fallida: no conectado.");
    }

    public bool FileExists(string remotePath)
    {
        EnsureConnected();
        try
        {
            return _client!.Exists(Normalize(remotePath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al comprobar archivo remoto: " + Sanitize(ex.Message), ex);
        }
    }

    public bool DirectoryExists(string remotePath)
    {
        EnsureConnected();
        try
        {
            var path = Normalize(remotePath).TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/";
            if (!_client!.Exists(path))
                return false;
            return _client.GetAttributes(path).IsDirectory;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al comprobar directorio remoto: " + Sanitize(ex.Message), ex);
        }
    }

    public string ReadAllText(string remotePath)
    {
        var bytes = DownloadBytes(remotePath);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] DownloadBytes(string remotePath)
    {
        EnsureConnected();
        var path = Normalize(remotePath);
        if (!_client!.Exists(path))
            throw new FileNotFoundException("Archivo remoto inexistente: " + path, path);

        try
        {
            using var ms = new MemoryStream();
            _client.DownloadFile(path, ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                throw new InvalidOperationException("Descarga incompleta: archivo vacio (" + path + ").");
            return bytes;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al descargar archivo remoto: " + Sanitize(ex.Message), ex);
        }
    }

    public long GetFileLength(string remotePath)
    {
        EnsureConnected();
        var path = Normalize(remotePath);
        try
        {
            return _client!.GetAttributes(path).Size;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al obtener tamano remoto: " + Sanitize(ex.Message), ex);
        }
    }

    public void UploadNewFile(string remotePath, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureConnected();
        var path = Normalize(remotePath);
        if (_client!.Exists(path))
            throw new InvalidOperationException("Archivo remoto ya existe (no se sobrescribe): " + path);

        try
        {
            using var ms = new MemoryStream(content, writable: false);
            _client.UploadFile(ms, path, canOverride: false);
            Interlocked.Increment(ref _writeAttempts);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al subir archivo remoto: " + Sanitize(ex.Message), ex);
        }

        if (!_client.Exists(path))
            throw new InvalidOperationException("Upload fallo: archivo remoto no aparece tras la subida.");
    }

    public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureConnected();
        var finalPath = Normalize(remotePath);
        var backupPath = Normalize(backupRemotePath);
        var tempPath = finalPath + ".rufus-tmp";

        if (_client!.Exists(tempPath))
            throw new InvalidOperationException("Temp remoto ya existe (abortado): " + tempPath);
        if (_client.Exists(backupPath))
            throw new InvalidOperationException(
                "Prev efímero remoto ya existe (abortado, limpiar manualmente): " + backupPath);
        if (!_client.Exists(finalPath))
            throw new InvalidOperationException("Archivo a reemplazar inexistente: " + finalPath);

        try
        {
            using (var ms = new MemoryStream(content, writable: false))
            {
                _client.UploadFile(ms, tempPath, canOverride: false);
                Interlocked.Increment(ref _writeAttempts);
            }

            using (var verify = new MemoryStream())
            {
                _client.DownloadFile(tempPath, verify);
                var remoteTmp = verify.ToArray();
                if (remoteTmp.Length != content.Length || !remoteTmp.AsSpan().SequenceEqual(content))
                    throw new InvalidOperationException("Contenido del temp remoto no coincide con el local.");
            }

            _client.RenameFile(finalPath, backupPath);
            Interlocked.Increment(ref _writeAttempts);

            try
            {
                _client.RenameFile(tempPath, finalPath);
                Interlocked.Increment(ref _writeAttempts);
            }
            catch (Exception renameFinalEx)
            {
                try
                {
                    if (!_client.Exists(finalPath) && _client.Exists(backupPath))
                        _client.RenameFile(backupPath, finalPath);
                }
                catch
                {
                    /* ignore secondary */
                }

                throw new InvalidOperationException(
                    "Fallo al activar el nuevo versions_es (rename temp→final). " +
                    "Se intento restaurar el original. Verificar estado remoto manualmente. " +
                    Sanitize(renameFinalEx.Message),
                    renameFinalEx);
            }

            // CONT.8 — eliminar prev efímero (no cuenta como DeleteAttemptCount / no borra SWF).
            try
            {
                if (_client.Exists(backupPath))
                    _client.DeleteFile(backupPath);
            }
            catch (Exception cleanupEx)
            {
                throw new InvalidOperationException(
                    "versions_es actualizado pero no se pudo eliminar el prev efímero (" + backupPath + "): " +
                    Sanitize(cleanupEx.Message),
                    cleanupEx);
            }

            if (_client.Exists(tempPath))
            {
                try { _client.DeleteFile(tempPath); } catch { /* ignore */ }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error en sustitucion atomica remota: " + Sanitize(ex.Message), ex);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_client?.IsConnected == true)
                _client.Disconnect();
        }
        catch
        {
            /* ignore */
        }

        _client?.Dispose();
        _client = null;
    }

    private void EnsureConnected()
    {
        if (_client is null || !_client.IsConnected)
            throw new InvalidOperationException("Cliente SFTP no conectado.");
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Ruta remota vacia.", nameof(path));
        return path.Replace('\\', '/');
    }

    private static string Sanitize(string message) =>
        message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "credenciales o permiso denegado"
            : message;
}
