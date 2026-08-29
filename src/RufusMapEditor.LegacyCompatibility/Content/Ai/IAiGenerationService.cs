namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.4A — abstract AI generation service (WPF-free).
/// Receives AI.1 request + AI.2 package; returns AI.3-validated result via RUFUS backend.
/// </summary>
public interface IAiGenerationService
{
    AiGenerationServiceStatus Status { get; }
    bool IsConfigured { get; }

    Task<AiServiceCallResult> GenerateNameAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default);

    Task<AiServiceCallResult> GenerateDialogueAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default);

    Task<AiServiceCallResult> GenerateConversationAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default);

    /// <summary>Unified entry: dispatches by request.Action.</summary>
    Task<AiServiceCallResult> GenerateAsync(
        AiCreativeRequest request,
        AiPromptPackage package,
        CancellationToken cancellationToken = default);
}
