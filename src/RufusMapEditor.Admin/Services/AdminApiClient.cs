using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin.Services;

public sealed class AdminApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public AdminApiClient(string baseUrl, string adminSecret)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminSecret);
    }

    public async Task<IReadOnlyList<AdminLicenseListItemDto>> ListAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync("v1/admin/licenses", ct);
        await EnsureAdminSuccessAsync(res, ct);
        var list = await res.Content.ReadFromJsonAsync<List<AdminLicenseListItemDto>>(Json, ct);
        return list ?? new List<AdminLicenseListItemDto>();
    }

    public async Task<AdminLicenseDetailDto?> GetAsync(long id, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync($"v1/admin/licenses/{id}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureAdminSuccessAsync(res, ct);
        return await res.Content.ReadFromJsonAsync<AdminLicenseDetailDto>(Json, ct);
    }

    public async Task<CreateLicenseResponse> CreateAsync(CreateLicenseRequest req, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("v1/admin/licenses", req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Create failed ({(int)res.StatusCode}): {body}");
        return JsonSerializer.Deserialize<CreateLicenseResponse>(body, Json)
               ?? throw new InvalidOperationException("Empty create response");
    }

    public Task ExtendAsync(long id, int days, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/extend", new ExtendLicenseRequest { ExtraDays = days }, ct);

    public Task SuspendAsync(long id, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/suspend", null, ct);

    public Task ReactivateAsync(long id, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/reactivate", null, ct);

    public Task RevokeAsync(long id, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/revoke", null, ct);

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        using var res = await _http.DeleteAsync($"v1/admin/licenses/{id}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Licencia no encontrada.");
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Delete failed ({(int)res.StatusCode}): {text}");
        }
    }

    public Task UpdateDisplayNameAsync(long id, string? displayName, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/display-name", new UpdateDisplayNameRequest { DisplayName = displayName }, ct);

    public Task ResetDeviceAsync(long id, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/reset-device", null, ct);

    public Task TerminateSessionAsync(long id, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/terminate-session", null, ct);

    public Task UpdateAiSettingsAsync(long id, UpdateAiSettingsRequest req, CancellationToken ct = default) =>
        PostActionAsync($"v1/admin/licenses/{id}/ai-settings", req, ct);

    /// <summary>ADMIN.AI.1 — POST /v1/admin/ai-session (Admin secret auth; returns temporary AI Bearer).</summary>
    public async Task<AdminAiSessionResponse> CreateAiSessionAsync(CancellationToken ct = default)
    {
        using var res = await _http.PostAsync("v1/admin/ai-session", content: null, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"No se pudo obtener sesión IA ADMIN ({(int)res.StatusCode}).");
        return JsonSerializer.Deserialize<AdminAiSessionResponse>(text, Json)
               ?? throw new InvalidOperationException("Respuesta de sesión IA ADMIN vacía.");
    }

    /// <summary>ADMIN.USAGE.1 — GET /v1/admin/ai-usage (aggregated token metrics only).</summary>
    public async Task<AdminAiUsageStatsDto> GetAiUsageStatsAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync("v1/admin/ai-usage", ct);
        await EnsureAdminSuccessAsync(res, ct);
        return await res.Content.ReadFromJsonAsync<AdminAiUsageStatsDto>(Json, ct)
               ?? throw new InvalidOperationException("Respuesta de uso IA vacía.");
    }

    private async Task PostActionAsync(string path, object? body, CancellationToken ct)
    {
        using var res = body is null
            ? await _http.PostAsync(path, content: null, ct)
            : await _http.PostAsJsonAsync(path, body, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Admin action failed ({(int)res.StatusCode}): {text}");
    }

    private static async Task EnsureAdminSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        var text = await res.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Admin request failed ({(int)res.StatusCode}): {text}");
    }
}
