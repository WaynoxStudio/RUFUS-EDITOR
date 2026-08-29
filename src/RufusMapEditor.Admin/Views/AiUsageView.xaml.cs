using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin.Views;

public partial class AiUsageView : UserControl
{
    private readonly AdminWorkspace _workspace;
    private bool _loading;

    public AiUsageView(AdminWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_loading)
            return;
        _loading = true;
        StatusText.Text = "Cargando…";
        try
        {
            if (!_workspace.IsConnected)
            {
                ClearMetrics();
                StatusText.Text = "Error de conexión — conecte el backend en Ajustes.";
                return;
            }

            var client = _workspace.RequireClient();
            var stats = await client.GetAiUsageStatsAsync();
            Apply(stats);
            StatusText.Text = $"Actualizado · {stats.AsOfUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        }
        catch (Exception ex)
        {
            ClearMetrics();
            StatusText.Text = $"Error de conexión — {AdminWorkspace.HumanizeError(ex)}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void Apply(AdminAiUsageStatsDto stats)
    {
        BindBucket(stats.Today, TodayGenText, TodayInText, TodayOutText, TodayTotalText);
        BindBucket(stats.Month, MonthGenText, MonthInText, MonthOutText, MonthTotalText);
        BindBucket(stats.AllTime, AllGenText, AllInText, AllOutText, AllTotalText);

        AvgGlobalText.Text = FormatAverages(stats.AllTime);

        var ordered = stats.ByAction
            .OrderBy(a => ActionSort(a.Action))
            .ThenBy(a => a.Action, StringComparer.Ordinal)
            .ToList();

        AvgByActionText.Text = ordered.Count == 0
            ? "Sin eventos"
            : string.Join("\n", ordered.Select(a =>
                $"{a.Label}: {FormatAvg(a.AvgTotalTokens)}"));

        AvgByActionDetailText.Text = ordered.Count == 0
            ? "—"
            : string.Join("\n", ordered.Select(a =>
                $"{a.Label}: {FormatAvg(a.AvgInputTokens)} in · {FormatAvg(a.AvgOutputTokens)} out"));

        ByActionList.ItemsSource = ordered.Select(a => new ActionRow
        {
            Label = string.IsNullOrWhiteSpace(a.Label) ? a.Action : a.Label,
            GenerationsText = FormatInt(a.Generations),
            InputText = FormatInt(a.InputTokens),
            OutputText = FormatInt(a.OutputTokens),
            TotalText = FormatInt(a.TotalTokens),
        }).ToList();

        var maxDay = stats.Daily.Count == 0 ? 1L : Math.Max(1, stats.Daily.Max(d => d.TotalTokens));
        DailyList.ItemsSource = stats.Daily
            .OrderByDescending(d => d.Day)
            .Select(d => new DayRow
            {
                Day = d.Day,
                Summary = $"{FormatInt(d.Generations)} gen · {FormatInt(d.TotalTokens)} tok",
                BarWidth = Math.Max(2, 180.0 * d.TotalTokens / maxDay),
            }).ToList();

        MonthlyList.ItemsSource = stats.Monthly
            .OrderByDescending(m => m.Month)
            .Select(m => new MonthRow
            {
                Month = m.Month,
                Summary = $"{FormatInt(m.Generations)} gen · in {FormatInt(m.InputTokens)} · out {FormatInt(m.OutputTokens)} · Σ {FormatInt(m.TotalTokens)}",
            }).ToList();

        ScopeNoteText.Text = string.IsNullOrWhiteSpace(stats.TelemetryNote)
            ? $"Alcance: {stats.TelemetryScope}"
            : stats.TelemetryNote;
    }

    private void ClearMetrics()
    {
        foreach (var tb in new[]
                 {
                     TodayGenText, TodayInText, TodayOutText, TodayTotalText,
                     MonthGenText, MonthInText, MonthOutText, MonthTotalText,
                     AllGenText, AllInText, AllOutText, AllTotalText,
                     AvgGlobalText, AvgByActionText, AvgByActionDetailText,
                 })
            tb.Text = "—";
        ByActionList.ItemsSource = null;
        DailyList.ItemsSource = null;
        MonthlyList.ItemsSource = null;
        ScopeNoteText.Text = "";
    }

    private static void BindBucket(
        AdminAiUsageBucketDto b,
        TextBlock gen,
        TextBlock input,
        TextBlock output,
        TextBlock total)
    {
        gen.Text = FormatInt(b.Generations);
        input.Text = FormatInt(b.InputTokens);
        output.Text = FormatInt(b.OutputTokens);
        total.Text = FormatInt(b.TotalTokens);
    }

    private static string FormatAverages(AdminAiUsageBucketDto b)
    {
        if (b.Generations <= 0)
            return "Sin generaciones";
        return $"{FormatAvg(b.AvgInputTokens)} in · {FormatAvg(b.AvgOutputTokens)} out · {FormatAvg(b.AvgTotalTokens)} total";
    }

    private static string FormatInt(long n) =>
        n.ToString("N0", CultureInfo.GetCultureInfo("es-ES"));

    private static string FormatAvg(double? v) =>
        v is null ? "—" : Math.Round(v.Value, 1).ToString("N1", CultureInfo.GetCultureInfo("es-ES"));

    private static int ActionSort(string action) => action switch
    {
        AiUsageStoredActions.GenerateName => 0,
        AiUsageStoredActions.GenerateDialogue => 1,
        AiUsageStoredActions.GenerateConversation => 2,
        _ => 9,
    };

    private sealed class ActionRow
    {
        public string Label { get; init; } = "";
        public string GenerationsText { get; init; } = "";
        public string InputText { get; init; } = "";
        public string OutputText { get; init; } = "";
        public string TotalText { get; init; } = "";
    }

    private sealed class DayRow
    {
        public string Day { get; init; } = "";
        public string Summary { get; init; } = "";
        public double BarWidth { get; init; }
    }

    private sealed class MonthRow
    {
        public string Month { get; init; } = "";
        public string Summary { get; init; } = "";
    }
}
