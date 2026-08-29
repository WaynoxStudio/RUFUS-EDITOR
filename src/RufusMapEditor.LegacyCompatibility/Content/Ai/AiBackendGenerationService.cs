namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A / AI.6C — talks only to the RUFUS AI backend (never OpenAI, never OpenAI API keys).
/// When BackendUrl is empty: NotConfigured — no HTTP.
/// Sends Authorization: Bearer with RUFUS_AI_ACCESS_TOKEN via <see cref="IAiBackendAccessTokenProvider"/>.
/// </summary>
public sealed class AiBackendGenerationService : IAiGenerationService
{
    public const string NotConfiguredUserMessage =
        "El servicio IA de RUFUS todavía no está configurado.";

    public const string UnauthorizedUserMessage =
        "No autorizado para utilizar el servicio IA de RUFUS.";

    private readonly AiBackendSettings _settings;
    private readonly IAiBackendTransport _transport;
    private readonly IAiBackendAccessTokenProvider _accessTokenProvider;
    private AiGenerationServiceStatus _status = AiGenerationServiceStatus.NotConfigured;
    private readonly object _statusLock = new();

    public AiBackendGenerationService(
        AiBackendSettings settings,
        IAiBackendTransport transport,
        IAiBackendAccessTokenProvider accessTokenProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _accessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        RefreshStatusIdle();
    }

    /// <summary>Default: empty settings + env token provider (still NotConfigured until URL set).</summary>
    public AiBackendGenerationService()
        : this(new AiBackendSettings(), new AiBackendHttpTransport(), new EnvironmentAiBackendAccessTokenProvider())
    {
    }

    /// <summary>Convenience for tests that do not care about auth provider wiring.</summary>
    public AiBackendGenerationService(AiBackendSettings settings, IAiBackendTransport transport)
        : this(settings, transport, new StaticAiBackendAccessTokenProvider("test-rufus-access-token"))
    {
    }

    public AiBackendSettings Settings => _settings;

    public IAiBackendAccessTokenProvider AccessTokenProvider => _accessTokenProvider;

    public AiGenerationServiceStatus Status
    {
        get { lock (_statusLock) return _status; }
        private set { lock (_statusLock) _status = value; }
    }

    public bool IsConfigured => _settings.IsConfigured;

    public Task<AiServiceCallResult> GenerateNameAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(AiCreativeAction.GenerarNombre, request, package, cancellationToken);

    public Task<AiServiceCallResult> GenerateDialogueAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(AiCreativeAction.GenerarDialogo, request, package, cancellationToken);

    public Task<AiServiceCallResult> GenerateConversationAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(AiCreativeAction.GenerarConversacion, request, package, cancellationToken);

    public Task<AiServiceCallResult> GenerateAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GenerateCoreAsync(request.Action, request, package, cancellationToken);
    }

    private async Task<AiServiceCallResult> GenerateCoreAsync(
        AiCreativeAction expectedAction,
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);

        if (request.Action != expectedAction)
        {
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeWrongAction,
                "La acción del request no coincide con el método invocado.");
        }

        AiGenerationActivityLog.Info(AiCreativeRequestPreview.FormatAction(expectedAction));

        var outbound = AiBackendRequestBuilder.Build(request, package);

        if (!_settings.IsConfigured || string.IsNullOrWhiteSpace(_settings.BackendUrl))
        {
            Status = AiGenerationServiceStatus.NotConfigured;
            AiGenerationActivityLog.Backend("no configurado");
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeNotConfigured,
                NotConfiguredUserMessage,
                outbound);
        }

        if (!Uri.TryCreate(_settings.BackendUrl, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            Status = AiGenerationServiceStatus.Error;
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeNotConfigured,
                NotConfiguredUserMessage,
                outbound);
        }

        // AI.6B.2 / AI.6C — no automatic HTTP downgrade for the public VPS host.
        if (IsProtectedHttpsHost(endpoint) && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            Status = AiGenerationServiceStatus.Error;
            AiGenerationActivityLog.Error(expectedAction, "https requerido");
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeUnavailable,
                "El backend IA RUFUS requiere HTTPS.",
                outbound);
        }

        if (_accessTokenProvider is IAiBackendAccessTokenAsync asyncTokens)
        {
            try
            {
                await asyncTokens.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Status = AiGenerationServiceStatus.Available;
                return AiServiceCallResult.Fail(
                    expectedAction,
                    AiServiceCallResult.CodeCancelled,
                    "Conexión cancelada.",
                    outbound);
            }
            catch (Exception ex)
            {
                Status = AiGenerationServiceStatus.Error;
                AiGenerationActivityLog.Error(expectedAction, "sesión IA: " + (ex.Message.Length > 80 ? ex.Message[..80] : ex.Message));
                return AiServiceCallResult.Fail(
                    expectedAction,
                    AiServiceCallResult.CodeUnauthorized,
                    UnauthorizedUserMessage,
                    outbound);
            }
        }

        var accessToken = _accessTokenProvider.TryGetAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Status = AiGenerationServiceStatus.Error;
            AiGenerationActivityLog.Error(expectedAction, "no autorizado");
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeUnauthorized,
                UnauthorizedUserMessage,
                outbound);
        }

        Status = AiGenerationServiceStatus.Generating;
        AiGenerationActivityLog.Backend("enviando generación");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = AiBackendRequestBuilder.Serialize(outbound);
            var retried = false;
            while (true)
            {
                var auth = AiBackendRequestAuth.Bearer(accessToken!);
                var transportResult = await _transport
                    .PostJsonAsync(endpoint, json, _settings.Timeout, auth, cancellationToken)
                    .ConfigureAwait(false);

                if (transportResult.ErrorCode == AiServiceCallResult.CodeCancelled)
                {
                    Status = AiGenerationServiceStatus.Available;
                    return AiServiceCallResult.Fail(
                        expectedAction,
                        AiServiceCallResult.CodeCancelled,
                        "Conexión cancelada.",
                        outbound);
                }

                if (transportResult.ErrorCode == AiServiceCallResult.CodeTimeout)
                {
                    Status = AiGenerationServiceStatus.Error;
                    AiGenerationActivityLog.Error(expectedAction, "timeout");
                    return AiServiceCallResult.Fail(
                        expectedAction,
                        AiServiceCallResult.CodeTimeout,
                        "Timeout del backend IA.",
                        outbound);
                }

                var unauthorized = transportResult.StatusCode == 401
                    || transportResult.ErrorCode == AiServiceCallResult.CodeUnauthorized;

                if (unauthorized && !retried)
                {
                    var refreshed = false;
                    if (_accessTokenProvider is IAiBackendAccessTokenAsync asyncRefresh)
                    {
                        refreshed = await asyncRefresh
                            .RefreshAfterUnauthorizedAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (_accessTokenProvider is IAiBackendAccessTokenRefresh syncRefresh)
                    {
                        refreshed = syncRefresh.TryRefreshAfterUnauthorized();
                    }

                    if (refreshed)
                    {
                        accessToken = _accessTokenProvider.TryGetAccessToken();
                        if (!string.IsNullOrWhiteSpace(accessToken))
                        {
                            retried = true;
                            AiGenerationActivityLog.Backend("renovando sesión IA");
                            continue;
                        }
                    }
                }

                if (unauthorized)
                {
                    Status = AiGenerationServiceStatus.Error;
                    AiGenerationActivityLog.Error(expectedAction, "no autorizado");
                    return AiServiceCallResult.Fail(
                        expectedAction,
                        AiServiceCallResult.CodeUnauthorized,
                        UnauthorizedUserMessage,
                        outbound);
                }

                // AI.4C — parse JSON envelope for both 2xx and error statuses (e.g. 503 AI_NOT_CONFIGURED).
                if (!string.IsNullOrWhiteSpace(transportResult.Body))
                {
                    var parsed = AiBackendResponseParser.ParseAndValidate(
                        expectedAction,
                        transportResult.Body,
                        outbound);

                    if (parsed.Success
                        || parsed.ErrorCode is not AiServiceCallResult.CodeCorruptJson)
                    {
                        // Backend may return UNAUTHORIZED in JSON with non-401; normalize message.
                        if (!parsed.Success
                            && (parsed.ErrorCode is AiServiceCallResult.CodeUnauthorized or "UNAUTHORIZED"))
                        {
                            if (!retried)
                            {
                                var refreshedJson = false;
                                if (_accessTokenProvider is IAiBackendAccessTokenAsync asyncRefreshJson)
                                {
                                    refreshedJson = await asyncRefreshJson
                                        .RefreshAfterUnauthorizedAsync(cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                else if (_accessTokenProvider is IAiBackendAccessTokenRefresh syncRefreshJson)
                                {
                                    refreshedJson = syncRefreshJson.TryRefreshAfterUnauthorized();
                                }

                                if (refreshedJson)
                                {
                                    accessToken = _accessTokenProvider.TryGetAccessToken();
                                    if (!string.IsNullOrWhiteSpace(accessToken))
                                    {
                                        retried = true;
                                        AiGenerationActivityLog.Backend("renovando sesión IA");
                                        continue;
                                    }
                                }
                            }

                            Status = AiGenerationServiceStatus.Error;
                            AiGenerationActivityLog.Error(expectedAction, "no autorizado");
                            return AiServiceCallResult.Fail(
                                expectedAction,
                                AiServiceCallResult.CodeUnauthorized,
                                UnauthorizedUserMessage,
                                outbound);
                        }

                        if (!parsed.Success && IsAiPermissionDenied(parsed.ErrorCode))
                        {
                            Status = AiGenerationServiceStatus.Error;
                            AiGenerationActivityLog.Error(expectedAction, "IA no permitida");
                            return AiServiceCallResult.Fail(
                                expectedAction,
                                "AI_NOT_ALLOWED",
                                "Tu licencia no incluye acceso al Asistente IA.",
                                outbound);
                        }

                        if (!parsed.Success && IsAiQuotaDenied(parsed.ErrorCode))
                        {
                            Status = AiGenerationServiceStatus.Error;
                            AiGenerationActivityLog.Error(expectedAction, "cuota IA");
                            return AiServiceCallResult.Fail(
                                expectedAction,
                                parsed.ErrorCode ?? "AI_QUOTA_EXCEEDED",
                                QuotaUserMessage(parsed.ErrorCode, parsed.ErrorMessage),
                                outbound);
                        }

                        Status = parsed.Success
                            ? AiGenerationServiceStatus.Available
                            : AiGenerationServiceStatus.Error;
                        if (parsed.Success)
                            AiGenerationActivityLog.Response("OK · respuesta recibida");
                        else
                            AiGenerationActivityLog.Error(expectedAction, parsed.ErrorCode ?? "error");
                        return parsed;
                    }
                }

                if (!transportResult.Ok
                    || transportResult.ErrorCode == AiServiceCallResult.CodeUnavailable
                    || transportResult.ErrorCode == AiServiceCallResult.CodeHttpError)
                {
                    Status = AiGenerationServiceStatus.Error;
                    AiGenerationActivityLog.Error(
                        expectedAction,
                        transportResult.ErrorMessage ?? "HTTP error");
                    return AiServiceCallResult.Fail(
                        expectedAction,
                        AiServiceCallResult.CodeUnavailable,
                        "Backend IA no disponible.",
                        outbound);
                }

                Status = AiGenerationServiceStatus.Error;
                return AiServiceCallResult.Fail(
                    expectedAction,
                    AiServiceCallResult.CodeCorruptJson,
                    AiBackendResponseParser.InvalidUserMessage + " (JSON corrupto)",
                    outbound);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = AiGenerationServiceStatus.Available;
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeCancelled,
                "Conexión cancelada.",
                outbound);
        }
        catch (Exception ex)
        {
            Status = AiGenerationServiceStatus.Error;
            AiGenerationActivityLog.Error(expectedAction, ex.Message);
            return AiServiceCallResult.Fail(
                expectedAction,
                AiServiceCallResult.CodeUnavailable,
                "Backend no disponible.",
                outbound);
        }
    }

    public void RefreshStatusIdle()
    {
        Status = _settings.IsConfigured
            ? AiGenerationServiceStatus.Available
            : AiGenerationServiceStatus.NotConfigured;
    }

    private static bool IsProtectedHttpsHost(Uri endpoint) =>
        endpoint.Host.Equals("vmi3502135.contaboserver.net", StringComparison.OrdinalIgnoreCase);

    private static bool IsAiPermissionDenied(string? code) =>
        string.Equals(code, "AI_NOT_ALLOWED", StringComparison.OrdinalIgnoreCase);

    private static bool IsAiQuotaDenied(string? code) =>
        code is "AI_QUOTA_EXCEEDED" or "AI_QUOTA_DAILY_EXCEEDED" or "AI_QUOTA_MONTHLY_EXCEEDED";

    private static string QuotaUserMessage(string? code, string? backendMessage)
    {
        if (!string.IsNullOrWhiteSpace(backendMessage)
            && backendMessage.Contains("límite", StringComparison.OrdinalIgnoreCase))
            return backendMessage;

        return code switch
        {
            "AI_QUOTA_DAILY_EXCEEDED" => "Has alcanzado el límite diario de generaciones IA de tu licencia.",
            "AI_QUOTA_MONTHLY_EXCEEDED" => "Has alcanzado el límite mensual de generaciones IA de tu licencia.",
            _ => "Has alcanzado el límite de generaciones IA de tu licencia.",
        };
    }
}
