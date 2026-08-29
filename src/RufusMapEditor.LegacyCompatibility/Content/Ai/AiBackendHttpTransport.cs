using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A / AI.6C — shared HttpClient transport for the RUFUS AI backend.
/// One client instance; never points at OpenAI; never sends OpenAI API keys.
/// May send Authorization: Bearer with the RUFUS access token only.
/// </summary>
public sealed class AiBackendHttpTransport : IAiBackendTransport, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public AiBackendHttpTransport()
        : this(CreateSharedClient(), ownsClient: true)
    {
    }

    public AiBackendHttpTransport(HttpClient httpClient, bool ownsClient = false)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient();
        // Timeout enforced per-request via CancellationToken; keep HttpClient long-lived.
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    public async Task<AiBackendTransportResult> PostJsonAsync(
        Uri endpoint,
        string jsonBody,
        TimeSpan timeout,
        AiBackendRequestAuth auth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (IsOpenAiHost(endpoint))
            return AiBackendTransportResult.Fail(
                AiServiceCallResult.CodeUnavailable,
                "Destino OpenAI no permitido desde el editor. Usa el backend RUFUS.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            if (auth.HasBearer)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.BearerToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            return AiBackendTransportResult.Success((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AiBackendTransportResult.Fail(AiServiceCallResult.CodeCancelled, "Conexión cancelada.");
        }
        catch (OperationCanceledException)
        {
            return AiBackendTransportResult.Fail(AiServiceCallResult.CodeTimeout, "Timeout del backend IA.");
        }
        catch (HttpRequestException ex)
        {
            return AiBackendTransportResult.Fail(
                AiServiceCallResult.CodeUnavailable,
                "Backend no disponible: " + ex.Message);
        }
    }

    private static bool IsOpenAiHost(Uri endpoint) =>
        endpoint.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase)
        || endpoint.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
