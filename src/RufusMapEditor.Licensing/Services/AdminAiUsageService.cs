using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Services;

/// <summary>
/// ADMIN.USAGE.1 — read-only aggregation of <c>rufus_ai_usage_events</c>.
/// Does not write usage, change quotas, or call OpenAI.
/// </summary>
public sealed class AdminAiUsageService
{
    public const string TelemetryScopeUserLicenseEventsOnly = "user_license_events_only";

    public const string TelemetryNoteText =
        "Métricas basadas solo en eventos reales de rufus_ai_usage_events " +
        "(generaciones USER con licencia). La telemetría de generaciones IA ADMIN " +
        "no está registrada actualmente (DATO PENDIENTE DE CONFIRMAR).";

    private readonly ILicenseUnitOfWork _db;
    private readonly IServerClock _clock;

    public AdminAiUsageService(ILicenseUnitOfWork db, IServerClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AdminAiUsageStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc));
        var daySeriesFrom = todayStart.AddDays(-29);
        var monthSeriesFrom = new DateTimeOffset(new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc)).AddMonths(-11);

        var today = await _db.AiUsage.AggregateTokensAsync(todayStart, todayStart.AddDays(1), ct);
        var month = await _db.AiUsage.AggregateTokensAsync(monthStart, monthStart.AddMonths(1), ct);
        var all = await _db.AiUsage.AggregateTokensAsync(null, null, ct);
        var byAction = await _db.AiUsage.AggregateByActionAsync(null, null, ct);
        var daily = await _db.AiUsage.AggregateByDayAsync(daySeriesFrom, todayStart.AddDays(1), ct);
        var monthly = await _db.AiUsage.AggregateByMonthAsync(monthSeriesFrom, monthStart.AddMonths(1), ct);

        return new AdminAiUsageStatsDto
        {
            AsOfUtc = now,
            TelemetryScope = TelemetryScopeUserLicenseEventsOnly,
            TelemetryNote = TelemetryNoteText,
            Today = ToBucket(today),
            Month = ToBucket(month),
            AllTime = ToBucket(all),
            ByAction = byAction.Select(ToActionDto).ToList(),
            Daily = daily.Select(d => new AdminAiUsageDayDto
            {
                Day = d.DayUtc,
                Generations = d.Generations,
                InputTokens = d.InputTokens,
                OutputTokens = d.OutputTokens,
                TotalTokens = d.TotalTokens,
            }).ToList(),
            Monthly = monthly.Select(m => new AdminAiUsageMonthDto
            {
                Month = m.MonthUtc,
                Generations = m.Generations,
                InputTokens = m.InputTokens,
                OutputTokens = m.OutputTokens,
                TotalTokens = m.TotalTokens,
            }).ToList(),
        };
    }

    internal static AdminAiUsageBucketDto ToBucket(AiUsageTokenTotals t)
    {
        var dto = new AdminAiUsageBucketDto
        {
            Generations = t.Generations,
            InputTokens = t.InputTokens,
            OutputTokens = t.OutputTokens,
            TotalTokens = t.TotalTokens,
        };
        ApplyAverages(dto, t.Generations, t.InputTokens, t.OutputTokens, t.TotalTokens);
        return dto;
    }

    internal static AdminAiUsageByActionDto ToActionDto(AiUsageActionTotals t)
    {
        var dto = new AdminAiUsageByActionDto
        {
            Action = t.Action,
            Label = LabelFor(t.Action),
            Generations = t.Generations,
            InputTokens = t.InputTokens,
            OutputTokens = t.OutputTokens,
            TotalTokens = t.TotalTokens,
        };
        ApplyAverages(dto, t.Generations, t.InputTokens, t.OutputTokens, t.TotalTokens);
        return dto;
    }

    private static void ApplyAverages(
        AdminAiUsageBucketDto dto,
        long generations,
        long input,
        long output,
        long total)
    {
        if (generations <= 0)
            return;
        dto.AvgInputTokens = (double)input / generations;
        dto.AvgOutputTokens = (double)output / generations;
        dto.AvgTotalTokens = (double)total / generations;
    }

    private static void ApplyAverages(
        AdminAiUsageByActionDto dto,
        long generations,
        long input,
        long output,
        long total)
    {
        if (generations <= 0)
            return;
        dto.AvgInputTokens = (double)input / generations;
        dto.AvgOutputTokens = (double)output / generations;
        dto.AvgTotalTokens = (double)total / generations;
    }

    internal static string LabelFor(string action) => action switch
    {
        AiUsageStoredActions.GenerateName => "Generar nombre",
        AiUsageStoredActions.GenerateDialogue => "Generar diálogo",
        AiUsageStoredActions.GenerateConversation => "Generar conversación",
        _ => action,
    };
}
