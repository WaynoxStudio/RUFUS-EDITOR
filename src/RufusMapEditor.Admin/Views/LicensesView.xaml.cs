using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.Admin.Helpers;
using RufusMapEditor.Admin.Services;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin.Views;

public partial class LicensesView : UserControl
{
    private readonly AdminWorkspace _workspace;
    private readonly Window _owner;
    private AdminLicenseListItemDto? _selected;
    private string _filter = "all";
    private long? _pendingSelectId;

    public LicensesView(AdminWorkspace workspace, Window owner)
    {
        InitializeComponent();
        _workspace = workspace;
        _owner = owner;
        _workspace.Changed += OnWorkspaceChanged;
        Loaded += (_, _) => ApplyFilter();
        Unloaded += (_, _) => _workspace.Changed -= OnWorkspaceChanged;
        ClearDetail();
    }

    private void OnWorkspaceChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnWorkspaceChanged);
            return;
        }

        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _filter = tag;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var q = (SearchBox.Text ?? "").Trim();
        IEnumerable<AdminLicenseListItemDto> items = _workspace.Licenses;

        if (!string.Equals(_filter, "all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => string.Equals(x.Status, _filter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q))
        {
            items = items.Where(x =>
                (x.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                || x.CodeDisplayHint.Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.LicenseId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.Status.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = items.OrderByDescending(x => x.LicenseId).ToList();
        var keepId = _pendingSelectId ?? _selected?.LicenseId;
        LicensesGrid.ItemsSource = list;
        if (keepId is { } id)
            SelectById(id);
        _pendingSelectId = null;
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _workspace.ConnectAndLoadAsync(showErrorDialog: true, _owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "RUFUS ADMIN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public Task CreateLicenseAsync() => CreateInternalAsync();

    private async void Create_Click(object sender, RoutedEventArgs e) => await CreateInternalAsync();

    private async Task CreateInternalAsync()
    {
        try
        {
            var client = _workspace.RequireClient();
            var dlg = new CreateLicenseWindow { Owner = _owner };
            if (dlg.ShowDialog() != true || dlg.Request is null)
                return;
            var created = await client.CreateAsync(dlg.Request);
            var show = new LicenseCreatedWindow(created.LicenseCode) { Owner = _owner };
            show.ShowDialog();
            _pendingSelectId = created.LicenseId;
            await _workspace.ReloadLicensesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "Crear licencia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectById(long id)
    {
        if (LicensesGrid.ItemsSource is not IEnumerable<AdminLicenseListItemDto> items)
            return;
        foreach (var item in items)
        {
            if (item.LicenseId == id)
            {
                LicensesGrid.SelectedItem = item;
                LicensesGrid.ScrollIntoView(item);
                break;
            }
        }
    }

    private async void LicensesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = LicensesGrid.SelectedItem as AdminLicenseListItemDto;
        UpdateSelectionActions(_selected);
        if (_selected is null)
        {
            ClearDetail();
            return;
        }

        try
        {
            var client = _workspace.RequireClient();
            var d = await client.GetAsync(_selected.LicenseId);
            if (d is null)
            {
                ClearDetail();
                DetailTitle.Text = "No encontrada";
                return;
            }

            BindDetail(d);
        }
        catch (Exception ex)
        {
            ClearDetail();
            DetailTitle.Text = AdminWorkspace.HumanizeError(ex);
        }
    }

    private void BindDetail(AdminLicenseDetailDto d)
    {
        UpdateSelectionActions(_selected);
        var namePart = string.IsNullOrWhiteSpace(d.DisplayName) ? "" : d.DisplayName + " · ";
        DetailTitle.Text = $"{namePart}Licencia #{d.LicenseId}";
        DetailNameBox.Text = d.DisplayName ?? "";
        DetailHint.Text = string.IsNullOrWhiteSpace(d.CodeDisplayHint) ? "" : $"Hint …{d.CodeDisplayHint}";
        DetailQuickActions.Visibility = Visibility.Visible;
        DetailBadge.Visibility = Visibility.Visible;
        DetailBadge.Background = AdminUiFormat.StatusBrush(d.Status);
        DetailBadgeText.Text = AdminUiFormat.StatusLabel(d.Status);
        DetailExpires.Text = AdminUiFormat.FormatExpires(d.ExpiresAt);
        DetailDuration.Text = d.DurationDays is { } days ? $"{days} día(s)" : "—";
        DetailDevices.Text = $"{d.DevicesBound} / {d.MaxDevices}";
        DetailSessions.Text = $"{d.ActiveSessions} / {d.MaxConcurrentSessions}";
        DetailEditor.Text = d.PermissionEditor ? "Permitido" : "No permitido";
        DetailAiPerm.Text = d.PermissionAi ? "Permitido" : "No permitido";
        DetailAiToday.Text = AdminUiFormat.FormatQuota(d.AiUsageToday, d.AiDailyLimit);
        DetailAiMonth.Text = AdminUiFormat.FormatQuota(d.AiUsageMonth, d.AiMonthlyLimit);
        DetailDeviceId.Text = d.BoundDeviceIds.Count == 0
            ? "Sin dispositivo vinculado"
            : string.Join(", ", d.BoundDeviceIds.Select(AdminUiFormat.ShortDeviceId));
        DetailLastActivity.Text = AdminUiFormat.FormatDate(d.LastActivityAt);
        DetailNotes.Text = string.IsNullOrWhiteSpace(d.AdminNotes) ? "" : "Notas: " + d.AdminNotes;
    }

    private void ClearDetail()
    {
        UpdateSelectionActions(null);
        DetailTitle.Text = "Seleccione una licencia";
        DetailHint.Text = "Seleccione una fila de la tabla para editar, revocar, eliminar o liberar dispositivo.";
        DetailNameBox.Text = "";
        DetailQuickActions.Visibility = Visibility.Collapsed;
        DetailBadge.Visibility = Visibility.Collapsed;
        DetailExpires.Text = "—";
        DetailDuration.Text = "—";
        DetailDevices.Text = "—";
        DetailSessions.Text = "—";
        DetailEditor.Text = "—";
        DetailAiPerm.Text = "—";
        DetailAiToday.Text = "—";
        DetailAiMonth.Text = "—";
        DetailDeviceId.Text = "—";
        DetailLastActivity.Text = "—";
        DetailNotes.Text = "";
    }

    private async void AiSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("Seleccione una licencia.", "RUFUS ADMIN");
            return;
        }

        try
        {
            var client = _workspace.RequireClient();
            var detail = await client.GetAsync(_selected.LicenseId);
            if (detail is null)
                return;
            var dlg = new AiSettingsWindow(detail) { Owner = _owner };
            if (dlg.ShowDialog() != true || dlg.Request is null)
                return;
            await client.UpdateAiSettingsAsync(_selected.LicenseId, dlg.Request);
            _pendingSelectId = _selected.LicenseId;
            await _workspace.ReloadLicensesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "RUFUS ADMIN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Extend_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Extender", "¿Extender la licencia seleccionada?"))
            return;
        var days = PromptDays();
        if (days is null) return;
        await RunSelectedAsync(c => c.ExtendAsync(_selected!.LicenseId, days.Value));
    }

    private async void Suspend_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Suspender", "¿Suspender esta licencia? Las sesiones se cerrarán."))
            return;
        await RunSelectedAsync(c => c.SuspendAsync(_selected!.LicenseId));
    }

    private async void Reactivate_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Reactivar", "¿Reactivar esta licencia?"))
            return;
        await RunSelectedAsync(c => c.ReactivateAsync(_selected!.LicenseId));
    }

    private void UpdateSelectionActions(AdminLicenseListItemDto? item)
    {
        var hasSelection = item is not null;
        var isRevoked = item?.Status.Equals("Revoked", StringComparison.OrdinalIgnoreCase) == true;

        DeleteToolbarButton.IsEnabled = hasSelection;
        RevokeToolbarButton.IsEnabled = hasSelection && !isRevoked;
        ResetDeviceToolbarButton.IsEnabled = hasSelection && !isRevoked;
    }

    private async void SaveName_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("Seleccione una licencia.", "RUFUS ADMIN");
            return;
        }

        try
        {
            var client = _workspace.RequireClient();
            var name = string.IsNullOrWhiteSpace(DetailNameBox.Text) ? null : DetailNameBox.Text.Trim();
            await client.UpdateDisplayNameAsync(_selected.LicenseId, name);
            _pendingSelectId = _selected.LicenseId;
            await _workspace.ReloadLicensesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "Guardar nombre", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("Seleccione una licencia.", "RUFUS ADMIN");
            return;
        }

        var label = string.IsNullOrWhiteSpace(_selected.DisplayName)
            ? $"licencia #{_selected.LicenseId} (…{_selected.CodeDisplayHint})"
            : $"«{_selected.DisplayName}» (#{_selected.LicenseId})";
        if (!await ConfirmAsync("Eliminar licencia",
                $"¿Eliminar definitivamente {label}?\n\nSe borrará de la base de datos junto con sesiones y dispositivos vinculados. No se puede deshacer."))
            return;

        try
        {
            var client = _workspace.RequireClient();
            var deletedId = _selected.LicenseId;
            await client.DeleteAsync(deletedId);
            _selected = null;
            ClearDetail();
            await _workspace.ReloadLicensesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "Eliminar licencia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Revocar licencia",
                "¿Revocar definitivamente esta licencia?\n\nEl código dejará de funcionar y se cerrarán las sesiones. "
                + "Permanece en el listado como «Revocada» (filtro Revocadas). No se puede deshacer."))
            return;
        await RunSelectedAsync(c => c.RevokeAsync(_selected!.LicenseId));
    }

    private async void ResetDevice_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Liberar dispositivo",
                "¿Desvincular todos los dispositivos y cerrar sesiones?\n\nLibera el bloqueo del PC para que otra licencia pueda activarse en ese equipo."))
            return;
        await RunSelectedAsync(c => c.ResetDeviceAsync(_selected!.LicenseId));
    }

    private async void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Cerrar sesión", "¿Invalidar sesiones activas de esta licencia?"))
            return;
        await RunSelectedAsync(c => c.TerminateSessionAsync(_selected!.LicenseId));
    }

    private async Task RunSelectedAsync(Func<AdminApiClient, Task> action)
    {
        if (_selected is null)
        {
            MessageBox.Show("Seleccione una licencia.", "RUFUS ADMIN");
            return;
        }

        try
        {
            var client = _workspace.RequireClient();
            await action(client);
            _pendingSelectId = _selected.LicenseId;
            await _workspace.ReloadLicensesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(AdminWorkspace.HumanizeError(ex), "RUFUS ADMIN", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task<bool> ConfirmAsync(string title, string message)
    {
        var r = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(r == MessageBoxResult.Yes);
    }

    private int? PromptDays()
    {
        var dlg = new Window
        {
            Title = "Extender (días)",
            Owner = _owner,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("Bg"),
            Foreground = (System.Windows.Media.Brush)FindResource("Text"),
        };
        var box = new TextBox { Text = "30", Margin = new Thickness(16, 48, 16, 0), Padding = new Thickness(8, 6, 8, 6) };
        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            Margin = new Thickness(0, 0, 16, 14),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsDefault = true,
            Style = (Style)FindResource("PrimaryButton"),
        };
        int? result = null;
        ok.Click += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), out var d) && d > 0)
            {
                result = d;
                dlg.DialogResult = true;
            }
        };
        var root = new Grid();
        root.Children.Add(new TextBlock
        {
            Text = "Días a añadir:",
            Margin = new Thickness(16, 16, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
        });
        root.Children.Add(box);
        root.Children.Add(ok);
        dlg.Content = root;
        return dlg.ShowDialog() == true ? result : null;
    }
}
