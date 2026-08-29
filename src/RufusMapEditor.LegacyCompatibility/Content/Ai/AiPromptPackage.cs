namespace RufusMapEditor.LegacyCompatibility.Content.Ai;

/// <summary>
/// AI.2 — composed creative prompt package ready for a future model call.
/// Separates master rules, dynamic context, and concrete task.
/// </summary>
public sealed class AiPromptPackage
{
    public required AiCreativeRequest SourceRequest { get; init; }

    /// <summary>Master RUFUS style + creative-only technical bans.</summary>
    public required string MasterInstructions { get; init; }

    /// <summary>Role, personality, narrative context, length, extra instruction, NPC name.</summary>
    public required string DynamicContext { get; init; }

    /// <summary>Action-specific task (name / dialog / conversation).</summary>
    public required string TaskInstructions { get; init; }

    /// <summary>Full prompt = master + context + task (for future API).</summary>
    public required string FullPrompt { get; init; }

    // --- Debug / preview summaries ---
    public required string TaskSummary { get; init; }
    public required string RoleSummary { get; init; }
    public required string PersonalityLabel { get; init; }
    public required string PersonalityGuidance { get; init; }
    public required string ContextSummary { get; init; }
    public required string LengthSummary { get; init; }
    public required string AdditionalInstructionSummary { get; init; }
    public required string MasterRulesSummary { get; init; }
}
