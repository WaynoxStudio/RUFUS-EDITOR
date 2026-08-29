namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>AI.2 — internal creative guidance for one attitude preset (or custom).</summary>
public sealed class AiPersonalityProfile
{
    public AiPersonalityProfile(string label, string internalGuidance, bool isCustom = false)
    {
        Label = label ?? "";
        InternalGuidance = internalGuidance ?? "";
        IsCustom = isCustom;
    }

    /// <summary>Visible UI label (Amable, Gruñón, …) or "Personalizada".</summary>
    public string Label { get; }

    /// <summary>Creative instructions injected into the composed prompt.</summary>
    public string InternalGuidance { get; }

    public bool IsCustom { get; }
}
