namespace RufusMapEditor.Licensing.Model;

/// <summary>SQL aggregate row over <c>rufus_ai_usage_events</c> (read-only).</summary>
public readonly record struct AiUsageTokenTotals(
    long Generations,
    long InputTokens,
    long OutputTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

public readonly record struct AiUsageActionTotals(
    string Action,
    long Generations,
    long InputTokens,
    long OutputTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

public readonly record struct AiUsageDayTotals(
    string DayUtc,
    long Generations,
    long InputTokens,
    long OutputTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

public readonly record struct AiUsageMonthTotals(
    string MonthUtc,
    long Generations,
    long InputTokens,
    long OutputTokens)
{
    public long TotalTokens => InputTokens + OutputTokens;
}
