using System.Windows;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin;

public partial class AiSettingsWindow : Window
{
    public UpdateAiSettingsRequest? Request { get; private set; }

    public AiSettingsWindow(AdminLicenseDetailDto current)
    {
        InitializeComponent();
        AiCheck.IsChecked = current.PermissionAi;
        DailyBox.Text = current.AiDailyLimit?.ToString() ?? "";
        MonthlyBox.Text = current.AiMonthlyLimit?.ToString() ?? "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int? daily = ParseOptionalPositive(DailyBox.Text);
        int? monthly = ParseOptionalPositive(MonthlyBox.Text);
        if (daily is -1 || monthly is -1)
        {
            MessageBox.Show("Los límites deben ser enteros positivos o vacíos.", "ASISTENTE IA");
            return;
        }

        Request = new UpdateAiSettingsRequest
        {
            PermissionAi = AiCheck.IsChecked == true,
            AiDailyLimit = daily,
            AiMonthlyLimit = monthly,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static int? ParseOptionalPositive(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!int.TryParse(raw.Trim(), out var n) || n < 1)
            return -1;
        return n;
    }
}
