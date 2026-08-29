using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RufusMapEditor.Licensing.Client;

public sealed class LicenseSessionLocalState
{
    public string SessionToken { get; set; } = "";
    public DateTimeOffset LeaseExpiresAt { get; set; }
    public DateTimeOffset? LicenseExpiresAt { get; set; }
    public bool PermissionEditor { get; set; }
    public bool PermissionAi { get; set; }
    public string DeviceId { get; set; } = "";
    public int? AiDailyLimit { get; set; }
    public int? AiMonthlyLimit { get; set; }
    public int? AiUsageToday { get; set; }
    public int? AiUsageMonth { get; set; }
}

public interface ISessionStore
{
    Task SaveAsync(LicenseSessionLocalState state, CancellationToken ct = default);
    Task<LicenseSessionLocalState?> LoadAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// DPAPI-protected session blob under %LocalAppData%\RufusMapEditor\ — never under Library\ portable.
/// Does not store OPENAI_API_KEY or admin credentials.
/// </summary>
public sealed class DpapiLicenseSessionStore : ISessionStore
{
    private readonly string _path;

    public DpapiLicenseSessionStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "license-session.bin");
    }

    public Task SaveAsync(LicenseSessionLocalState state, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
        return Task.CompletedTask;
    }

    public Task<LicenseSessionLocalState?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return Task.FromResult<LicenseSessionLocalState?>(null);
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<LicenseSessionLocalState>(Encoding.UTF8.GetString(bytes));
            return Task.FromResult(state);
        }
        catch
        {
            return Task.FromResult<LicenseSessionLocalState?>(null);
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }
}

public sealed class MemorySessionStore : ISessionStore
{
    private LicenseSessionLocalState? _state;
    public Task SaveAsync(LicenseSessionLocalState state, CancellationToken ct = default)
    {
        _state = state;
        return Task.CompletedTask;
    }

    public Task<LicenseSessionLocalState?> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(_state);

    public Task ClearAsync(CancellationToken ct = default)
    {
        _state = null;
        return Task.CompletedTask;
    }
}
