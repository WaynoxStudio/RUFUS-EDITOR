using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RufusMapEditor.Licensing.Client;

/// <summary>
/// Local ADMIN connection (BaseUrl + secret). Secret must never be stored in plaintext.
/// </summary>
public sealed class AdminConnectionState
{
    public string BaseUrl { get; set; } = "";
    public string AdminSecret { get; set; } = "";
}

public interface IAdminConnectionStore
{
    Task SaveAsync(AdminConnectionState state, CancellationToken ct = default);
    Task<AdminConnectionState?> LoadAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// DPAPI-protected ADMIN connection under %LocalAppData%\RufusMapEditor\admin-connection.bin.
/// Bound to the Windows user — copying dist-admin to another PC does not transport the secret.
/// Does not store OPENAI_API_KEY or RUFUS_AI_ACCESS_TOKEN.
/// </summary>
public sealed class DpapiAdminConnectionStore : IAdminConnectionStore
{
    public const string FileName = "admin-connection.bin";
    private readonly string _path;

    public DpapiAdminConnectionStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, FileName);
    }

    public string StorePath => _path;

    public Task SaveAsync(AdminConnectionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.BaseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(state));
        if (string.IsNullOrWhiteSpace(state.AdminSecret))
            throw new ArgumentException("AdminSecret is required.", nameof(state));

        var payload = new AdminConnectionPayload
        {
            BaseUrl = state.BaseUrl.Trim().TrimEnd('/'),
            AdminSecret = state.AdminSecret,
        };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
        return Task.CompletedTask;
    }

    public Task<AdminConnectionState?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return Task.FromResult<AdminConnectionState?>(null);
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var payload = JsonSerializer.Deserialize<AdminConnectionPayload>(Encoding.UTF8.GetString(bytes));
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.BaseUrl)
                || string.IsNullOrWhiteSpace(payload.AdminSecret))
                return Task.FromResult<AdminConnectionState?>(null);

            return Task.FromResult<AdminConnectionState?>(new AdminConnectionState
            {
                BaseUrl = payload.BaseUrl.Trim().TrimEnd('/'),
                AdminSecret = payload.AdminSecret,
            });
        }
        catch
        {
            return Task.FromResult<AdminConnectionState?>(null);
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }

    private sealed class AdminConnectionPayload
    {
        public string BaseUrl { get; set; } = "";
        public string AdminSecret { get; set; } = "";
    }
}

public static class AdminConnectionMessages
{
    public const string InvalidCredential =
        "La credencial de RUFUS ADMIN ya no es válida.";
}
