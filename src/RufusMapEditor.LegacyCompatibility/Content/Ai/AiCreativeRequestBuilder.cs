namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.1 — builds a clean creative request from UI-agnostic inputs.
/// Ready for a future OpenAI backend; no WPF coupling.
/// </summary>
public static class AiCreativeRequestBuilder
{
    public static AiCreativeRequest Build(
        AiCreativeAction action,
        string? rolePreset,
        string? customRole,
        string? attitudePreset,
        string? customAttitude,
        string? narrativeContext,
        string? additionalInstruction,
        AiTextLength length = AiTextLength.Corta,
        string? currentNpcName = null)
    {
        return new AiCreativeRequest
        {
            Action = action,
            Role = ResolveRole(rolePreset, customRole),
            Attitude = ResolveAttitude(attitudePreset, customAttitude),
            NarrativeContext = (narrativeContext ?? "").Trim(),
            AdditionalInstruction = (additionalInstruction ?? "").Trim(),
            Length = length,
            Style = AiCreativeStyle.RufusDofusRetro,
            CurrentNpcName = (currentNpcName ?? "").Trim()
        };
    }

    public static string ResolveRole(string? rolePreset, string? customRole)
    {
        var preset = (rolePreset ?? "").Trim();
        if (string.Equals(preset, AiCreativePresets.RoleCustomLabel, StringComparison.OrdinalIgnoreCase))
            return (customRole ?? "").Trim();
        return preset;
    }

    public static string ResolveAttitude(string? attitudePreset, string? customAttitude)
    {
        var preset = (attitudePreset ?? "").Trim();
        if (string.Equals(preset, AiCreativePresets.AttitudeCustomLabel, StringComparison.OrdinalIgnoreCase))
            return (customAttitude ?? "").Trim();
        return preset;
    }
}
