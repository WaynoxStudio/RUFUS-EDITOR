namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.4A — builds versioned backend requests from AI.1 request + AI.2 prompt package.</summary>
public static class AiBackendRequestBuilder
{
    public static AiBackendGenerateRequest Build(AiCreativeRequest request, AiPromptPackage package)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);

        return new AiBackendGenerateRequest
        {
            Version = AiBackendGenerateRequest.CurrentVersion,
            Action = AiBackendWireActions.ToWire(request.Action),
            CreativeRequest = new AiBackendCreativeRequestDto
            {
                Role = request.Role ?? "",
                Attitude = request.Attitude ?? "",
                NarrativeContext = request.NarrativeContext ?? "",
                AdditionalInstruction = request.AdditionalInstruction ?? "",
                Length = FormatLength(request.Length),
                Style = request.Style ?? "",
                CurrentNpcName = request.CurrentNpcName ?? ""
            },
            Prompt = new AiBackendPromptDto
            {
                Master = package.MasterInstructions ?? "",
                Context = package.DynamicContext ?? "",
                Task = package.TaskInstructions ?? ""
            }
        };
    }

    public static string Serialize(AiBackendGenerateRequest request) =>
        AiResponseSerializer.Serialize(request);

    private static string FormatLength(AiTextLength length) => length switch
    {
        AiTextLength.Corta => "corta",
        AiTextLength.Media => "media",
        AiTextLength.Larga => "larga",
        _ => "corta"
    };
}
