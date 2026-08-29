using Renci.SshNet;
using Renci.SshNet.Common;

namespace RufusMapEditor.LegacyCompatibility.LangMaps;

/// <summary>Cliente SFTP SSH.NET solo-lectura. Nunca llama Upload/Delete/Rename/Write.</summary>
internal sealed class SshNetLangSftpReadClient : ILangSftpReadClient
{
    private readonly LangSftpSettings _settings;
    private readonly string _password;
    private SftpClient? _client;

    public SshNetLangSftpReadClient(LangSftpSettings settings, string plainPassword)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _password = plainPassword ?? "";
    }

    public int WriteAttemptCount => 0;

    public void Connect()
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
            throw new InvalidOperationException("Host SFTP vacío.");
        if (string.IsNullOrWhiteSpace(_settings.User))
            throw new InvalidOperationException("Usuario SFTP vacío.");

        var port = _settings.Port <= 0 ? 22 : _settings.Port;
        var connection = new ConnectionInfo(
            _settings.Host.Trim(),
            port,
            _settings.User.Trim(),
            new PasswordAuthenticationMethod(_settings.User.Trim(), _password))
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        _client?.Dispose();
        _client = new SftpClient(connection) { OperationTimeout = TimeSpan.FromSeconds(30) };

        try
        {
            _client.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            throw new InvalidOperationException("Autenticación SFTP fallida.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new InvalidOperationException("Conexión SFTP fallida: " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Conexión SFTP fallida: " + Sanitize(ex.Message), ex);
        }

        if (!_client.IsConnected)
            throw new InvalidOperationException("Conexión SFTP fallida: no conectado.");
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
        EnsureConnected();
        var path = Normalize(remotePath);
        if (!_client!.Exists(path))
            throw new FileNotFoundException("Archivo remoto inexistente: " + path, path);

        try
        {
            using var ms = new MemoryStream();
            _client.DownloadFile(path, ms);
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error al leer archivo remoto: " + Sanitize(ex.Message), ex);
        }
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
                throw new InvalidOperationException("Descarga incompleta: archivo vacío (" + path + ").");
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
            throw new InvalidOperationException("Error al obtener tamaño remoto: " + Sanitize(ex.Message), ex);
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
            throw new ArgumentException("Ruta remota vacía.", nameof(path));
        return path.Replace('\\', '/');
    }

    private static string Sanitize(string message) =>
        message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "credenciales o permiso denegado"
            : message;
}
