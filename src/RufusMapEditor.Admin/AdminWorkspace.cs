using System.Windows;
using RufusMapEditor.Admin.Services;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin;

/// <summary>
/// Shared ADMIN session: DPAPI connection, API client, license cache, Admin AI session provider.
/// Views bind to this instead of duplicating connection logic.
/// </summary>
public sealed class AdminWorkspace
{
    private readonly IAdminConnectionStore _connectionStore;
    private AdminApiClient? _client;
    private string? _secret;
    private string _baseUrl = "";
    private AdminAiSessionAccessTokenProvider? _aiSessionProvider;

    public AdminWorkspace(IAdminConnectionStore? store = null)
    {
        _connectionStore = store ?? new DpapiAdminConnectionStore();
    }

    public bool IsConnected { get; private set; }
    public string BaseUrl => _baseUrl;
    public string? DisplayHost => TryGetDisplayHost(_baseUrl);
    public string StatusMessage { get; private set; } = "";
    public IReadOnlyList<AdminLicenseListItemDto> Licenses { get; private set; } =
        Array.Empty<AdminLicenseListItemDto>();

    public event Action? Changed;

    /// <summary>ADMIN.AI.1 — token provider for Content IA (in-memory; cleared on credential change).</summary>
    public AdminAiSessionAccessTokenProvider GetOrCreateAiSessionProvider()
    {
        _aiSessionProvider ??= new AdminAiSessionAccessTokenProvider(async ct =>
        {
            var client = RequireClient();
            return await client.CreateAiSessionAsync(ct).ConfigureAwait(false);
        });
        return _aiSessionProvider;
    }

    public void InvalidateAiSession() => _aiSessionProvider?.Invalidate();

    public void LoadPersistedConnection()
    {
        AdminConnectionState? saved = null;
        try
        {
            saved = _connectionStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // first run / corrupt
        }

        var envBase = Environment.GetEnvironmentVariable("RUFUS_ADMIN_API_BASE");
        var envSecret = Environment.GetEnvironmentVariable(AdminAuthOptions.EnvironmentVariable);

        if (saved is not null && !string.IsNullOrWhiteSpace(saved.BaseUrl))
            _baseUrl = saved.BaseUrl;
        else if (!string.IsNullOrWhiteSpace(envBase))
            _baseUrl = envBase.Trim();

        if (saved is not null && !string.IsNullOrWhiteSpace(saved.AdminSecret))
            _secret = saved.AdminSecret;
        else if (!string.IsNullOrWhiteSpace(envSecret))
            _secret = envSecret;

        IsConnected = false;
        StatusMessage = HasCredentials
            ? "Listo para conectar…"
            : "Configure el backend en Ajustes.";
        RaiseChanged();
    }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_secret);

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        ResetAiAndClient();
    }

    public void SetSecret(string? secret)
    {
        _secret = string.IsNullOrEmpty(secret) ? null : secret;
        ResetAiAndClient();
    }

    public void ClearSecretForChange()
    {
        _secret = null;
        ResetAiAndClient();
        StatusMessage = "Introduzca la nueva credencial y pulse Reconectar.";
        RaiseChanged();
    }

    /// <summary>Restore secret/URL from DPAPI without changing connection flag.</summary>
    public void RestoreCredentialsFromStore()
    {
        try
        {
            var saved = _connectionStore.LoadAsync().GetAwaiter().GetResult();
            if (saved is null)
                return;
            if (!string.IsNullOrWhiteSpace(saved.BaseUrl))
                _baseUrl = saved.BaseUrl;
            if (!string.IsNullOrWhiteSpace(saved.AdminSecret))
                _secret = saved.AdminSecret;
            RaiseChanged();
        }
        catch
        {
            // ignore
        }
    }

    public bool HasSecret => !string.IsNullOrWhiteSpace(_secret);

    public AdminApiClient RequireClient()
    {
        if (string.IsNullOrWhiteSpace(_secret))
            throw new InvalidOperationException("Indique el Admin Secret en Ajustes.");
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Indique la Base URL en Ajustes.");
        _client = new AdminApiClient(_baseUrl, _secret);
        return _client;
    }

    public async Task<bool> ConnectAndLoadAsync(bool showErrorDialog, Window? owner)
    {
        try
        {
            StatusMessage = "Cargando…";
            RaiseChanged();
            InvalidateAiSession();
            var client = RequireClient();
            var list = await client.ListAsync();
            Licenses = list;
            IsConnected = true;
            StatusMessage = $"{list.Count} licencia(s)";
            await PersistIfValidAsync();
            RaiseChanged();
            return true;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            InvalidateAiSession();
            StatusMessage = HumanizeError(ex);
            RaiseChanged();
            if (showErrorDialog)
            {
                MessageBox.Show(
                    StatusMessage,
                    "RUFUS ADMIN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }
    }

    public async Task ReloadLicensesAsync()
    {
        var client = RequireClient();
        var list = await client.ListAsync();
        Licenses = list;
        IsConnected = true;
        StatusMessage = $"{list.Count} licencia(s)";
        RaiseChanged();
    }

    public async Task PersistIfValidAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_secret))
            return;
        await _connectionStore.SaveAsync(new AdminConnectionState
        {
            BaseUrl = _baseUrl,
            AdminSecret = _secret,
        });
    }

    public static string HumanizeError(Exception ex)
    {
        if (ex.Message.Contains(AdminConnectionMessages.InvalidCredential, StringComparison.Ordinal)
            || ex.Message.Contains("401", StringComparison.Ordinal)
            || ex.Message.Contains("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
            return AdminConnectionMessages.InvalidCredential;
        return ex.Message;
    }

    public static string? TryGetDisplayHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;
        try
        {
            var uri = new Uri(baseUrl.Trim());
            return string.IsNullOrEmpty(uri.Host) ? baseUrl.Trim() : uri.Host;
        }
        catch
        {
            return baseUrl.Trim();
        }
    }

    private void ResetAiAndClient()
    {
        InvalidateAiSession();
        _aiSessionProvider = null;
        _client = null;
    }

    private void RaiseChanged() => Changed?.Invoke();
}
