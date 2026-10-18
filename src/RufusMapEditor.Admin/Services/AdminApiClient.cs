using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.AdminAuth;

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

    /// <summary>POST /v1/admin/ai-session (Admin secret auth; returns temporary AI Bearer).</summary>
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

    public async Task ProbeConnectionAsync(CancellationToken ct = default)
    {
        using var res = await _http.PostAsync("v1/admin/ai-session", content: null, ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(AdminConnectionMessages.InvalidCredential);
        if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.ServiceUnavailable)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Admin request failed ({(int)res.StatusCode}): {text}");
        }
    }
}
