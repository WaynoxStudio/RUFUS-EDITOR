namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.1/AI.2 — local stub only. Composes prompt package; no API, no keys, no network.
/// </summary>
public static class AiCreativeServiceStub
{
    public const string NotConnectedMessage = "Servicio IA no conectado todavía.";
    public const string NotConnectedShort = "IA todavía no conectada";

    /// <summary>
    /// Builds the creative request preview + composed prompt.
    /// Does not invent names, dialogue, or conversation text.
    /// </summary>
    public static AiCreativeStubResult Prepare(AiCreativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var package = AiPromptComposer.Compose(request);
        return new AiCreativeStubResult(
            Success: false,
            Message: NotConnectedMessage,
            ShortMessage: NotConnectedShort,
            Request: request,
            Package: package,
            Preview: AiCreativeRequestPreview.Format(request, package));
    }
}

/// <summary>Result of a local (non-API) prepare call.</summary>
public sealed record AiCreativeStubResult(
    bool Success,
    string Message,
    string ShortMessage,
    AiCreativeRequest Request,
    AiPromptPackage Package,
    string Preview);
