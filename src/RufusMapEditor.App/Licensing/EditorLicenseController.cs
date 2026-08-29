using System.Reflection;
using System.Windows;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.App.Licensing;

/// <summary>LIC.5 / LIC.7 / LIC.7P.2 — WPF license orchestration when enforcement is active.</summary>
public sealed class EditorLicenseController : IDisposable
{
    private readonly EditorLicenseSessionService _service;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private bool _invalidationHandled;
    private bool _statusFresh = true;

    public EditorLicenseController(EditorLicenseSessionService service)
    {
        _service = service;
    }

    public LicenseSessionLocalState? CurrentSession { get; private set; }

    /// <summary>False when the last backend refresh failed due to network (local cache may be stale).</summary>
    public bool StatusFresh => _statusFresh;

    public string StatusLabel { get; private set; } = "Licencia: —";

    public event Action? StatusChanged;

    /// <summary>Raised on UI thread when backend explicitly invalidates the session.</summary>
    public event Action<string>? SessionInvalidated;

    public static EditorLicenseController CreateDefault()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        var service = new EditorLicenseSessionService(
            HttpLicenseClient.CreateDefault(),
            new DpapiLicenseSessionStore(),
            new WindowsMachineGuidDeviceIdProvider(),
            LicenseLeaseOptions.FromEnvironment(),
            clientVersion: version);
        return new EditorLicenseController(service);
    }

    public EditorLicenseSessionService Service => _service;

    public async Task<bool> EnsureAuthorizedAsync(Window? owner = null)
    {
        LicenseRuntimeGate.Clear();
        RufusLog.Info("LIC → inicio validación");
        var resume = await _service.TryResumeAsync();
        if (resume.Outcome == LicenseGateOutcome.Authorized && resume.Session is not null)
        {
            ApplyAuthorized(resume.Session);
            RufusLog.Ok("LIC → sesión OK");
            return true;
        }

        if (resume.Outcome == LicenseGateOutcome.Denied && !string.IsNullOrWhiteSpace(resume.UserMessage))
            RufusLog.Warn("LIC → session invalid");

        return await ShowActivationLoopAsync(resume.UserMessage, owner);
    }

    public Task<bool> ReauthorizeAsync(Window? owner = null) =>
        ShowActivationLoopAsync(initialError: LicenseRuntimeGate.BlockMessage, owner);

    /// <summary>
    /// LIC.7P.2 — revalidate against /session (via heartbeat path) and refresh visible state.
    /// Does not invent offline validity.
    /// </summary>
    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        LicenseGateResult result;
        try
        {
            result = await _service.HeartbeatAsync(ct);
        }
        catch (Exception ex)
        {
            RufusLog.Warn("LIC → refresh red ERROR");
            RufusLog.Debug("LIC → refresh ex: " + ex.GetType().Name);
            MarkStale();
            return;
        }

        HandleHeartbeatResult(result);
    }

    public async Task LogoutAsync()
    {
        StopHeartbeat();
        RufusLog.Info("LIC → logout manual");
        await _service.LogoutBestEffortAsync();
        await _service.ClearLocalAsync();
        CurrentSession = null;
        _statusFresh = true;
        StatusLabel = "Licencia: Sin sesión";
        LicenseRuntimeGate.Clear();
        StatusChanged?.Invoke();
    }

    public void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatCts = new CancellationTokenSource();
        var ct = _heartbeatCts.Token;
        var delay = TimeSpan.FromSeconds(_service.HeartbeatSeconds);
        _heartbeatTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (ct.IsCancellationRequested)
                    break;

                LicenseGateResult result;
                try
                {
                    result = await _service.HeartbeatAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    RufusLog.Warn("LIC → heartbeat red ERROR");
                    RufusLog.Debug("LIC → heartbeat ex: " + ex.GetType().Name);
                    await Application.Current.Dispatcher.InvokeAsync(MarkStale);
                    continue;
                }

                await Application.Current.Dispatcher.InvokeAsync(() => HandleHeartbeatResult(result));
            }
        }, ct);
    }

    public void StopHeartbeat()
    {
        try
        {
            _heartbeatCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _heartbeatCts = null;
        _heartbeatTask = null;
    }

    public async Task ShutdownBestEffortAsync()
    {
        StopHeartbeat();
        RufusLog.Info("LIC → logout best-effort");
        try
        {
            await _service.LogoutBestEffortAsync();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Verify DPAPI store after activation; fails closed if persistence missing.</summary>
    public async Task<bool> VerifyLocalSessionPersistedAsync(CancellationToken ct = default)
    {
        var local = await _service.LoadLocalAsync(ct);
        return local is not null && !string.IsNullOrWhiteSpace(local.SessionToken);
    }

    public void RefreshStatusFromSession()
    {
        if (CurrentSession is null)
        {
            StatusLabel = "Licencia: Sin sesión";
            StatusChanged?.Invoke();
            return;
        }

        UpdateStatusLabel(denied: null);
        StatusChanged?.Invoke();
    }

    private async Task<bool> ShowActivationLoopAsync(string? initialError, Window? owner)
    {
        while (true)
        {
            var dlg = new LicenseActivationWindow(_service, initialError)
            {
                Owner = owner,
            };
            initialError = null;
            var ok = dlg.ShowDialog() == true && dlg.AuthorizedSession is not null;
            if (!ok)
                return false;

            ApplyAuthorized(dlg.AuthorizedSession!);
            if (!await VerifyLocalSessionPersistedAsync())
            {
                RufusLog.Warn("LIC → store vacío tras activación");
                initialError = "No se pudo guardar la sesión local. Comprueba permisos de usuario en este equipo.";
                await _service.ClearLocalAsync();
                continue;
            }

            RufusLog.Ok("LIC → sesión OK (store verificado)");
            return true;
        }
    }

    private void HandleHeartbeatResult(LicenseGateResult result)
    {
        switch (result.Outcome)
        {
            case LicenseGateOutcome.Authorized when result.Session is not null:
                CurrentSession = result.Session;
                _statusFresh = true;
                UpdateStatusLabel(denied: null);
                RufusLog.Ok("LIC → heartbeat OK");
                StatusChanged?.Invoke();
                break;

            case LicenseGateOutcome.TransientNetwork:
                if (result.Session is not null)
                    CurrentSession = result.Session;
                MarkStale();
                RufusLog.Warn("LIC → heartbeat red ERROR");
                break;

            case LicenseGateOutcome.Denied:
            case LicenseGateOutcome.NeedsActivation:
                CurrentSession = null;
                _statusFresh = true;
                StatusLabel = StatusForDenied(result.ErrorCode);
                RufusLog.Warn("LIC → session invalid");
                StatusChanged?.Invoke();
                if (!_invalidationHandled)
                {
                    _invalidationHandled = true;
                    StopHeartbeat();
                    var message = string.IsNullOrWhiteSpace(result.UserMessage)
                        ? LicenseUserMessages.SessionInvalid
                        : result.UserMessage;
                    LicenseRuntimeGate.Block(message);
                    SessionInvalidated?.Invoke(message);
                }
                break;
        }
    }

    private void MarkStale()
    {
        _statusFresh = false;
        UpdateStatusLabel(denied: null);
        StatusChanged?.Invoke();
    }

    private void UpdateStatusLabel(string? denied)
    {
        StatusLabel = LicenseStatusDisplay.FormatHubLabel(
            CurrentSession,
            _statusFresh,
            DateTimeOffset.UtcNow,
            deniedStatusLabel: denied);
    }

    private void ApplyAuthorized(LicenseSessionLocalState session)
    {
        _invalidationHandled = false;
        CurrentSession = session;
        _statusFresh = true;
        UpdateStatusLabel(denied: null);
        LicenseRuntimeGate.Clear();
        StatusChanged?.Invoke();
    }

    private static string StatusForDenied(string? code) => code switch
    {
        LicenseErrorCodes.LicenseSuspended => "Licencia: Suspendida",
        LicenseErrorCodes.LicenseExpired => "Licencia: Caducada",
        LicenseErrorCodes.LicenseRevoked => "Licencia: Revocada",
        _ => "Licencia: Inválida",
    };

    public void Dispose() => StopHeartbeat();
}
