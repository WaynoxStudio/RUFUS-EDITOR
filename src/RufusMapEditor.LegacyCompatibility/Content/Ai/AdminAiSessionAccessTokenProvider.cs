using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// ADMIN.AI.1 / ADMIN.UI.3.1 — in-memory Admin AI session Bearer for /v1/ai/generate.
/// Network issue/refresh is async-only to avoid WPF UI-thread deadlocks
/// (<c>GetAwaiter().GetResult()</c> on UI was freezing Generar nombre).
/// </summary>
public sealed class AdminAiSessionAccessTokenProvider :
    IAiBackendAccessTokenProvider,
    IAiBackendAccessTokenRefresh,
    IAiBackendAccessTokenAsync
{
    private readonly Func<CancellationToken, Task<AdminAiSessionResponse>> _issueAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;
    private readonly TimeSpan _refreshSkew = TimeSpan.FromMinutes(2);

    public AdminAiSessionAccessTokenProvider(Func<CancellationToken, Task<AdminAiSessionResponse>> issueAsync) =>
        _issueAsync = issueAsync ?? throw new ArgumentNullException(nameof(issueAsync));

    public string? TryGetAccessToken()
    {
        // Cache only — never blocks. Call EnsureReadyAsync first from async generate path.
        if (string.IsNullOrWhiteSpace(_token))
            return null;
        if (DateTimeOffset.UtcNow >= _expiresAt - _refreshSkew)
            return null;
        return _token;
    }

    public void Invalidate()
    {
        _token = null;
        _expiresAt = default;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_token)
                && DateTimeOffset.UtcNow < _expiresAt - _refreshSkew)
                return;

            AiGenerationActivityLog.Backend("ADMIN AI · obteniendo sesión");
            var issued = await _issueAsync(cancellationToken).ConfigureAwait(false);
            if (issued is null || string.IsNullOrWhiteSpace(issued.AccessToken))
            {
                Invalidate();
                AiGenerationActivityLog.Backend("ADMIN AI · sesión no disponible");
                return;
            }

            _token = issued.AccessToken.Trim();
            _expiresAt = issued.ExpiresAt;
            AiGenerationActivityLog.Backend("ADMIN AI · sesión obtenida");
        }
        catch (Exception ex)
        {
            Invalidate();
            AiGenerationActivityLog.Backend($"ADMIN AI · error sesión: {Sanitize(ex)}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RefreshAfterUnauthorizedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Invalidate();
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            AiGenerationActivityLog.Backend("ADMIN AI · renovando sesión");
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(TryGetAccessToken());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sync refresh kept for interface compatibility. Must not run on the WPF UI thread.
    /// Prefer <see cref="RefreshAfterUnauthorizedAsync"/>.
    /// </summary>
    public bool TryRefreshAfterUnauthorized()
    {
        try
        {
            return RefreshAfterUnauthorizedAsync(CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return false;
        }
    }

    private static string Sanitize(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Length > 120)
            msg = msg[..120];
        return msg;
    }
}
