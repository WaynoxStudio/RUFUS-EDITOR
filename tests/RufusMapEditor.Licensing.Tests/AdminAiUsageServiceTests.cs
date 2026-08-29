using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Services;
using RufusMapEditor.Licensing.Sqlite;

namespace RufusMapEditor.Licensing.Tests;

public sealed class AdminAiUsageServiceTests
{
    private static async Task<(SqliteLicenseUnitOfWork db, FakeServerClock clock, AdminAiUsageService usage, long licenseId)> CreateAsync()
    {
        var db = SqliteLicenseUnitOfWork.CreateInMemory();
        var clock = new FakeServerClock(new DateTimeOffset(2026, 8, 26, 15, 30, 0, TimeSpan.Zero));
        var admin = new AdminLicenseService(db, clock);
        var created = await admin.CreateAsync(new CreateLicenseRequest
        {
            DurationDays = 30,
            MaxDevices = 1,
            MaxConcurrentSessions = 1,
            PermissionEditor = true,
            PermissionAi = true,
        });
        var usage = new AdminAiUsageService(db, clock);
        return (db, clock, usage, created.LicenseId);
    }

    private static Task SeedAsync(
        ILicenseUnitOfWork db,
        long licenseId,
        DateTimeOffset at,
        string action,
        int? input,
        int? output,
        bool ok = true) =>
        db.ExecuteInTransactionAsync(ct => db.AiUsage.AppendEventAsync(new AiUsageEventEntity
        {
            LicenseId = licenseId,
            SessionId = null,
            AtUtc = at,
            Action = action,
            Model = "test-model",
            InputTokens = input,
            OutputTokens = output,
            OpenAiSucceeded = ok,
        }, ct));

    [Fact]
    public async Task Zero_events_returns_zeros_and_null_averages()
    {
        var (db, _, usage, _) = await CreateAsync();
        await using (db)
        {
            var stats = await usage.GetStatsAsync();
            Assert.Equal(0, stats.Today.Generations);
            Assert.Equal(0, stats.Today.InputTokens);
            Assert.Equal(0, stats.Today.OutputTokens);
            Assert.Equal(0, stats.Today.TotalTokens);
            Assert.Null(stats.Today.AvgTotalTokens);
            Assert.Equal(0, stats.Month.Generations);
            Assert.Equal(0, stats.AllTime.Generations);
            Assert.Empty(stats.ByAction);
            Assert.Equal(AdminAiUsageService.TelemetryScopeUserLicenseEventsOnly, stats.TelemetryScope);
            Assert.Contains("ADMIN", stats.TelemetryNote, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Single_event_today_aggregates_input_output_total()
    {
        var (db, clock, usage, licenseId) = await CreateAsync();
        await using (db)
        {
            await SeedAsync(db, licenseId, clock.UtcNow, AiUsageStoredActions.GenerateName, 100, 40);
            var stats = await usage.GetStatsAsync();
            Assert.Equal(1, stats.Today.Generations);
            Assert.Equal(100, stats.Today.InputTokens);
            Assert.Equal(40, stats.Today.OutputTokens);
            Assert.Equal(140, stats.Today.TotalTokens);
            Assert.Equal(100, stats.Today.AvgInputTokens);
            Assert.Equal(40, stats.Today.AvgOutputTokens);
            Assert.Equal(140, stats.Today.AvgTotalTokens);
            Assert.Equal(1, stats.Month.Generations);
            Assert.Equal(1, stats.AllTime.Generations);
        }
    }

    [Fact]
    public async Task Multiple_events_split_today_month_history_and_by_action()
    {
        var (db, clock, usage, licenseId) = await CreateAsync();
        await using (db)
        {
            // Today
            await SeedAsync(db, licenseId, clock.UtcNow, AiUsageStoredActions.GenerateName, 10, 5);
            await SeedAsync(db, licenseId, clock.UtcNow.AddHours(-1), AiUsageStoredActions.GenerateDialogue, 200, 50);
            // Earlier this month
            await SeedAsync(db, licenseId, new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                AiUsageStoredActions.GenerateConversation, 1000, 300);
            // Previous month (history only)
            await SeedAsync(db, licenseId, new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                AiUsageStoredActions.GenerateName, 20, 10);

            var stats = await usage.GetStatsAsync();

            Assert.Equal(2, stats.Today.Generations);
            Assert.Equal(210, stats.Today.InputTokens);
            Assert.Equal(55, stats.Today.OutputTokens);
            Assert.Equal(265, stats.Today.TotalTokens);

            Assert.Equal(3, stats.Month.Generations);
            Assert.Equal(1210, stats.Month.InputTokens);
            Assert.Equal(355, stats.Month.OutputTokens);

            Assert.Equal(4, stats.AllTime.Generations);
            Assert.Equal(1230, stats.AllTime.InputTokens);
            Assert.Equal(365, stats.AllTime.OutputTokens);
            Assert.Equal(1595, stats.AllTime.TotalTokens);
            Assert.Equal(1230 / 4.0, stats.AllTime.AvgInputTokens);
            Assert.Equal(365 / 4.0, stats.AllTime.AvgOutputTokens);

            var name = Assert.Single(stats.ByAction, a => a.Action == AiUsageStoredActions.GenerateName);
            Assert.Equal("Generar nombre", name.Label);
            Assert.Equal(2, name.Generations);
            Assert.Equal(30, name.InputTokens);
            Assert.Equal(15, name.OutputTokens);
            Assert.Equal(22.5, name.AvgTotalTokens);

            var dialogue = Assert.Single(stats.ByAction, a => a.Action == AiUsageStoredActions.GenerateDialogue);
            Assert.Equal(1, dialogue.Generations);
            Assert.Equal(250, dialogue.AvgTotalTokens);

            var conversation = Assert.Single(stats.ByAction, a => a.Action == AiUsageStoredActions.GenerateConversation);
            Assert.Equal(1, conversation.Generations);
            Assert.Equal(1300, conversation.AvgTotalTokens);

            Assert.Contains(stats.Daily, d => d.Day == "2026-08-26" && d.Generations == 2);
            Assert.Contains(stats.Monthly, m => m.Month == "2026-08" && m.Generations == 3);
            Assert.Contains(stats.Monthly, m => m.Month == "2026-07" && m.Generations == 1);
        }
    }

    [Fact]
    public async Task Null_tokens_treated_as_zero_in_sums()
    {
        var (db, clock, usage, licenseId) = await CreateAsync();
        await using (db)
        {
            await SeedAsync(db, licenseId, clock.UtcNow, AiUsageStoredActions.GenerateName, null, null);
            var stats = await usage.GetStatsAsync();
            Assert.Equal(1, stats.Today.Generations);
            Assert.Equal(0, stats.Today.InputTokens);
            Assert.Equal(0, stats.Today.OutputTokens);
            Assert.Equal(0, stats.Today.AvgTotalTokens);
        }
    }

    [Fact]
    public async Task Response_dto_has_no_sensitive_fields()
    {
        var (db, clock, usage, licenseId) = await CreateAsync();
        await using (db)
        {
            await SeedAsync(db, licenseId, clock.UtcNow, AiUsageStoredActions.GenerateName, 1, 1);
            var stats = await usage.GetStatsAsync();
            var json = System.Text.Json.JsonSerializer.Serialize(stats);
            Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("licenseCode", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sessionToken", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OPENAI", json, StringComparison.Ordinal);
            Assert.DoesNotContain("RUFUS_ADMIN", json, StringComparison.Ordinal);
            Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test-model", json); // model not exposed in aggregate DTO
        }
    }
}
