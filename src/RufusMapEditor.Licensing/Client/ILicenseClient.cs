using System.Net.Http.Json;
using System.Text.Json;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.Licensing.Client;

public interface ILicenseClient
{
    Task<LicenseOperationClientResult> ActivateAsync(ActivateLicenseRequest request, CancellationToken ct = default);
    /// <summary>POST /v1/license/session — validate / renew (same semantics as heartbeat in V1).</summary>
    Task<LicenseOperationClientResult> ValidateSessionAsync(HeartbeatRequest request, CancellationToken ct = default);
    Task<LicenseOperationClientResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);
    Task<LicenseOperationClientResult> LogoutAsync(LogoutRequest request, CancellationToken ct = default);
}

public sealed class LicenseOperationClientResult
{
    public bool Success { get; init; }
    public SessionSuccessResponse? Session { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }

    /// <summary>True when failure is network/timeout, not an explicit license decision.</summary>
    public bool IsTransientNetworkError =>
        string.Equals(ErrorCode, LicenseErrorCodes.NetworkUnavailable, StringComparison.Ordinal);
}

/// <summary>
/// HTTPS license client. Base URL: <see cref="BaseUrlEnvironmentVariable"/>, else production default.
/// </summary>
public sealed class HttpLicenseClient : ILicenseClient
{
    public const string BaseUrlEnvironmentVariable = "RUFUS_LICENSE_API_BASE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public HttpLicenseClient(HttpClient http) => _http = http;

    public static HttpLicenseClient CreateDefault(string? baseUrl = null)
    {
        var url = (baseUrl
                   ?? Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable)
                   ?? LicenseApiDefaults.ProductionBaseUrl).Trim().TrimEnd('/');
        var http = new HttpClient
        {
            BaseAddress = string.IsNullOrWhiteSpace(url) ? null : new Uri(url + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        return new HttpLicenseClient(http);
    }

    public Task<LicenseOperationClientResult> ActivateAsync(ActivateLicenseRequest request, CancellationToken ct = default) =>
        PostAsync(LicenseApiRoutes.Activate.TrimStart('/'), request, ct);

    public Task<LicenseOperationClientResult> ValidateSessionAsync(HeartbeatRequest request, CancellationToken ct = default) =>
        PostAsync(LicenseApiRoutes.Validate.TrimStart('/'), request, ct);

    public Task<LicenseOperationClientResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default) =>
        PostAsync(LicenseApiRoutes.Heartbeat.TrimStart('/'), request, ct);

    public Task<LicenseOperationClientResult> LogoutAsync(LogoutRequest request, CancellationToken ct = default) =>
        PostAsync(LicenseApiRoutes.Logout.TrimStart('/'), request, ct);

    private async Task<LicenseOperationClientResult> PostAsync<T>(string relative, T body, CancellationToken ct)
    {
        if (_http.BaseAddress is null)
        {
            return new LicenseOperationClientResult
            {
                Success = false,
                ErrorCode = LicenseErrorCodes.InvalidRequest,
                Message = "License API base URL not configured (RUFUS_LICENSE_API_BASE).",
            };
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(relative, body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                // Logout returns {success:true} without a full session payload.
                if (relative.Contains("logout", StringComparison.OrdinalIgnoreCase))
                    return new LicenseOperationClientResult { Success = true };

                var session = JsonSerializer.Deserialize<SessionSuccessResponse>(raw, JsonOptions);
                if (session is null)
                    return new LicenseOperationClientResult { Success = false, ErrorCode = LicenseErrorCodes.InvalidRequest };
                return new LicenseOperationClientResult { Success = true, Session = session };
            }

            LicenseErrorResponse? err = null;
            try
            {
                err = JsonSerializer.Deserialize<LicenseErrorResponse>(raw, JsonOptions);
            }
            catch
            {
                // ignore
            }

            return new LicenseOperationClientResult
            {
                Success = false,
                ErrorCode = err?.ErrorCode ?? LicenseErrorCodes.InvalidRequest,
                Message = err?.Message,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new LicenseOperationClientResult
            {
                Success = false,
                ErrorCode = LicenseErrorCodes.NetworkUnavailable,
                Message = LicenseUserMessages.NetworkLost,
            };
        }
    }
}

/// <summary>In-process client for tests — wraps LicenseAuthService.</summary>
public sealed class InProcessLicenseClient : ILicenseClient
{
    private readonly Services.LicenseAuthService _auth;

    public InProcessLicenseClient(Services.LicenseAuthService auth) => _auth = auth;

    public async Task<LicenseOperationClientResult> ActivateAsync(ActivateLicenseRequest request, CancellationToken ct = default)
    {
        var r = await _auth.ActivateAsync(request, ct);
        return Map(r);
    }

    public Task<LicenseOperationClientResult> ValidateSessionAsync(HeartbeatRequest request, CancellationToken ct = default) =>
        HeartbeatAsync(request, ct);

    public async Task<LicenseOperationClientResult> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        var r = await _auth.HeartbeatAsync(request, ct);
        return Map(r);
    }

    public async Task<LicenseOperationClientResult> LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var r = await _auth.LogoutAsync(request, ct);
        return Map(r);
    }

    private static LicenseOperationClientResult Map(Services.LicenseOperationResult r) => new()
    {
        Success = r.Success,
        Session = r.Session,
        ErrorCode = r.ErrorCode,
        Message = r.Message,
    };
}
