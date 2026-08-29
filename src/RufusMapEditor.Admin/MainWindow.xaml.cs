using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RufusMapEditor.Admin.Navigation;
using RufusMapEditor.Admin.Services;
using RufusMapEditor.Admin.Views;
using RufusMapEditor.App;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.Admin;

public partial class MainWindow : Window
{
    private readonly AdminWorkspace _workspace = new();
    private readonly AdminInfrastructureStatus _infra = new();
    private readonly Dictionary<AdminSection, FrameworkElement> _views = new();
    private LicensesView? _licensesView;
    private MapsEditorView? _mapsEditor;
    private ContentWorkspaceView? _contentWorkspace;
    private bool _autoConnectAttempted;
    private bool _infraCheckAttempted;
    private bool _navReady;
    private bool _preloadScheduled;
    private AdminSection? _loadingSection;

    public MainWindow()
    {
        InitializeComponent();
        _workspace.LoadPersistedConnection();
        _workspace.Changed += UpdateConnectionHeader;
        _infra.Changed += UpdateInfrastructureHeader;
        UpdateConnectionHeader();
        UpdateInfrastructureHeader();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_mapsEditor is not null && !_mapsEditor.TryConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        _contentWorkspace?.ViewModel.AiAssistant.CancelPendingGeneration();
        _mapsEditor?.DisposeWorkspace();
        _workspace.InvalidateAiSession();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _navReady = true;
        Navigate(AdminSection.Licenses);
        ScheduleHeavyModulePreload();

        if (_autoConnectAttempted)
            return;
        _autoConnectAttempted = true;

        if (!_workspace.HasCredentials)
        {
            Navigate(AdminSection.Settings);
            NavSettings.IsChecked = true;
            _ = ProbeInfrastructureQuietAsync();
            return;
        }

        _ = ConnectInBackgroundAsync();
    }

    private async Task ConnectInBackgroundAsync()
    {
        try
        {
            await _workspace.ConnectAndLoadAsync(showErrorDialog: false, this);
        }
        catch
        {
            // Header shows error state; no modal on auto-connect.
        }

        _ = ProbeInfrastructureQuietAsync();
    }

    private async Task ProbeInfrastructureQuietAsync()
    {
        if (_infraCheckAttempted)
            return;
        _infraCheckAttempted = true;
        try
        {
            await _infra.CheckAllAsync();
        }
        catch
        {
            // Quiet auto-check — header shows Error/NotConfigured; no modal.
        }
    }

    private async void CheckConnections_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_workspace.HasCredentials && !_workspace.IsConnected)
                await _workspace.ConnectAndLoadAsync(showErrorDialog: false, this);
            await _infra.CheckAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                AdminWorkspace.HumanizeError(ex),
                "RUFUS ADMIN",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_navReady || sender is not RadioButton { Tag: string tag })
            return;
        if (!Enum.TryParse<AdminSection>(tag, ignoreCase: true, out var section))
            return;
        Navigate(section);
    }

    public void Navigate(AdminSection section)
    {
        ContentHost.Margin = section is AdminSection.Maps or AdminSection.Content
            ? new Thickness(0)
            : new Thickness(28);

        if (IsHeavySection(section) && !_views.ContainsKey(section))
        {
            _loadingSection = section;
            ContentHost.Content = CreateLoadingPanel(section);
            SyncNav(section);
            _ = PresentHeavySectionWhenReadyAsync(section);
            return;
        }

        PresentSection(section);
        SyncNav(section);
    }

    private async Task PresentHeavySectionWhenReadyAsync(AdminSection section)
    {
        try
        {
            var view = await Dispatcher.InvokeAsync(() => GetOrCreateView(section));
            await WaitForSectionReadyAsync(section, view).ConfigureAwait(true);
            if (_loadingSection == section)
                PresentSection(section);
        }
        catch
        {
            if (_loadingSection == section)
            {
                ContentHost.Content = CreateLoadingPanel(section, failed: true);
                _loadingSection = null;
            }
        }
    }

    private void PresentSection(AdminSection section)
    {
        var view = GetOrCreateView(section);
        DetachFromPreloadHosts(view);
        ContentHost.Content = view;
        _loadingSection = null;
    }

    private static bool IsHeavySection(AdminSection section) =>
        section is AdminSection.Maps or AdminSection.Content;

    private static async Task WaitForSectionReadyAsync(AdminSection section, FrameworkElement view)
    {
        switch (section)
        {
            case AdminSection.Content when view is ContentWorkspaceView content:
                await content.EnsureInitializedAsync().ConfigureAwait(true);
                break;
            case AdminSection.Maps when view is MapsEditorView maps:
                await maps.Dispatcher.InvokeAsync(
                    () => maps.ViewModel.EnsureLibraryLoaded(),
                    DispatcherPriority.Background);
                break;
        }
    }

    private void ScheduleHeavyModulePreload()
    {
        if (_preloadScheduled)
            return;
        _preloadScheduled = true;

        _ = PreloadSectionAsync(AdminSection.Maps);
        _ = PreloadSectionAsync(AdminSection.Content);
    }

    private async Task PreloadSectionAsync(AdminSection section)
    {
        if (!IsHeavySection(section))
            return;

        // Let Licencias paint first.
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);

        if (_views.ContainsKey(section))
            return;

        FrameworkElement view = await Dispatcher.InvokeAsync(() =>
        {
            var created = GetOrCreateView(section);
            if (!ReferenceEquals(GetPreloadHost(section).Content, created))
                GetPreloadHost(section).Content = created;
            return created;
        });

        try
        {
            await WaitForSectionReadyAsync(section, view).ConfigureAwait(true);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(GetPreloadHost(section).Content, view)
                    && !ReferenceEquals(ContentHost.Content, view))
                {
                    GetPreloadHost(section).Content = null;
                }
            });
        }
    }

    private ContentControl GetPreloadHost(AdminSection section) =>
        section switch
        {
            AdminSection.Maps => MapsPreloadHost,
            AdminSection.Content => ContentPreloadHost,
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };

    private void DetachFromPreloadHosts(FrameworkElement view)
    {
        if (ReferenceEquals(MapsPreloadHost.Content, view))
            MapsPreloadHost.Content = null;
        if (ReferenceEquals(ContentPreloadHost.Content, view))
            ContentPreloadHost.Content = null;
    }

    private FrameworkElement CreateLoadingPanel(AdminSection section, bool failed = false)
    {
        var message = failed
            ? "No se pudo cargar el módulo. Inténtelo de nuevo."
            : section switch
            {
                AdminSection.Maps => "Cargando editor de mapas…",
                AdminSection.Content => "Cargando NPC y diálogos…",
                _ => "Cargando…",
            };

        return new Border
        {
            Background = (Brush)FindResource("Panel"),
            Child = new TextBlock
            {
                Text = message,
                FontSize = 16,
                Foreground = (Brush)FindResource("Text"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private FrameworkElement GetOrCreateView(AdminSection section)
    {
        if (_views.TryGetValue(section, out var existing))
            return existing;

        FrameworkElement view = section switch
        {
            AdminSection.Licenses => _licensesView ??= new LicensesView(_workspace, this),
            AdminSection.AiUsage => new AiUsageView(_workspace),
            AdminSection.Settings => new SettingsView(_workspace, this),
            AdminSection.Maps => CreateMapsEditor(),
            AdminSection.Content => CreateContentWorkspace(),
            AdminSection.Missions => new PlaceholderView("Misiones", AdminNavNotes.MissionsPlaceholder),
            _ => new PlaceholderView("RUFUS ADMIN", ""),
        };

        if (section == AdminSection.Licenses)
            _licensesView = (LicensesView)view;

        _views[section] = view;
        return view;
    }

    private MapsEditorView CreateMapsEditor()
    {
        _mapsEditor = new MapsEditorView(deferLibraryLoad: true);
        _mapsEditor.SetEmbeddedHost(true);
        return _mapsEditor;
    }

    private ContentWorkspaceView CreateContentWorkspace()
    {
        var ai = AiBackendGenerationServiceFactory.CreateForAdmin(_workspace.GetOrCreateAiSessionProvider());
        _contentWorkspace = new ContentWorkspaceView(new ContentWorkspaceViewModel(aiGeneration: ai));
        _contentWorkspace.SetEmbeddedHost(true);
        return _contentWorkspace;
    }

    private void SyncNav(AdminSection section)
    {
        foreach (var rb in FindVisualChildren<RadioButton>(this))
        {
            if (rb.GroupName != "AdminNav" || rb.Tag is not string tag)
                continue;
            if (Enum.TryParse<AdminSection>(tag, true, out var s) && s == section)
            {
                rb.IsChecked = true;
                break;
            }
        }
    }

    private void UpdateConnectionHeader()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateConnectionHeader);
            return;
        }

        if (_workspace.IsConnected)
        {
            BackendDot.Fill = (Brush)FindResource("Success");
            BackendLabel.Text = "Conectado";
            HostLabel.Text = _workspace.DisplayHost ?? "";
        }
        else
        {
            BackendDot.Fill = (Brush)FindResource("Danger");
            BackendLabel.Text = "Sin conexión";
            HostLabel.Text = _workspace.DisplayHost ?? "";
        }
    }

    private void UpdateInfrastructureHeader()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateInfrastructureHeader);
            return;
        }

        ApplyInfraDot(DbDot, _infra.DatabaseState);
        DbLabel.Text = ShortLabel(_infra.DatabaseState, database: true);
        DbDetail.Text = _infra.DatabaseDetail;

        ApplyInfraDot(SftpDot, _infra.SftpState);
        SftpLabel.Text = ShortLabel(_infra.SftpState, database: false);
        SftpDetail.Text = _infra.SftpDetail;
    }

    private void ApplyInfraDot(System.Windows.Shapes.Ellipse dot, SharedConnectionState state)
    {
        dot.Fill = state == SharedConnectionState.Connected
            ? (Brush)FindResource("Success")
            : (Brush)FindResource("Danger");
    }

    private static string ShortLabel(SharedConnectionState state, bool database) =>
        state switch
        {
            SharedConnectionState.Connected => database ? "Conectada" : "Conectado",
            SharedConnectionState.Error => "Error",
            SharedConnectionState.NotConfigured => "Sin configurar",
            _ => "Sin comprobar",
        };

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null)
            yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
