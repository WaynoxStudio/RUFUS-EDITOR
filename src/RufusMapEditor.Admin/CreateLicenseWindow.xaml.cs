using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin;

public partial class CreateLicenseWindow : Window
{
    public CreateLicenseRequest? Request { get; private set; }

    public CreateLicenseWindow()
    {
        InitializeComponent();
        DurationCombo.SelectionChanged += (_, _) =>
        {
            var tag = (DurationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            CustomDaysBox.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var tag = (DurationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "30";
        int days;
        if (tag == "custom")
        {
            if (!int.TryParse(CustomDaysBox.Text.Trim(), out days) || days < 1)
            {
                MessageBox.Show("Indique días personalizados válidos.", "Crear licencia");
                return;
            }
        }
        else if (!int.TryParse(tag, out days))
        {
            days = 30;
        }

        if (!int.TryParse(DevicesBox.Text.Trim(), out var devices) || devices < 1)
        {
            MessageBox.Show("Dispositivos inválidos.", "Crear licencia");
            return;
        }

        if (!int.TryParse(SessionsBox.Text.Trim(), out var sessions) || sessions < 1)
        {
            MessageBox.Show("Sesiones inválidas.", "Crear licencia");
            return;
        }

        var daily = ParseOptionalLimit(DailyLimitBox.Text);
        var monthly = ParseOptionalLimit(MonthlyLimitBox.Text);
        if (daily is -1 || monthly is -1)
        {
            MessageBox.Show("Los límites IA deben ser enteros positivos o vacíos.", "Crear licencia");
            return;
        }

        Request = new CreateLicenseRequest
        {
            DurationDays = days,
            MaxDevices = devices,
            MaxConcurrentSessions = sessions,
            PermissionEditor = EditorCheck.IsChecked == true,
            PermissionAi = AiCheck.IsChecked == true,
            AiDailyLimit = daily,
            AiMonthlyLimit = monthly,
            DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
            AdminNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private static int? ParseOptionalLimit(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!int.TryParse(raw.Trim(), out var n) || n < 1)
            return -1;
        return n;
    }
}
