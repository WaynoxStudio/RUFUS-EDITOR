using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private const double SidebarExpandedWidth = 192;
    private const double SidebarCollapsedWidth = 28;
    private static readonly TimeSpan SidebarAnimDuration = TimeSpan.FromMilliseconds(220);

    private readonly AdminWorkspace _workspace = new();
    private readonly AdminInfrastructureStatus _infra = new();
    private readonly Dictionary<AdminSection, FrameworkElement> _views = new();
    private MapsEditorView? _mapsEditor;
    private ContentWorkspaceView? _contentWorkspace;
    private bool _autoConnectAttempted;
    private bool _infraCheckAttempted;
    private bool _navReady;
    private bool _warmCacheScheduled;
    private AdminSection? _loadingSection;
    private bool _sidebarExpanded = true;
    private bool _syncingThemeToggle;
    private AppSettings? _settings;

    public MainWindow()
    {
        InitializeComponent();
        _workspace.Changed += UpdateConnectionHeader;
        _infra.Changed += UpdateInfrastructureHeader;
        ThemeService.ThemeChanged += SyncThemeToggle;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ThemeService.ThemeChanged -= SyncThemeToggle;
        if (_mapsEditor is not null && !_mapsEditor.TryConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        if (_contentWorkspace is not null && !_contentWorkspace.ViewModel.ConfirmLeaveIfDirty())
        {
            e.Cancel = true;
            return;
        }

        _contentWorkspace?.ViewModel.AiAssistant.CancelPendingGeneration();
        _mapsEditor?.DisposeWorkspace();
        _workspace.InvalidateAiSession();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = AppSettingsStore.Load();
        }
        catch
        {
            _settings = null;
        }

        // Fijo abrir/cerrar (sin hover). Por defecto abierto; respeta preferencia guardada.
        var startExpanded = !(_settings?.UiLayout.AdminSidebarCollapsed ?? false);
        SetSidebarExpanded(startExpanded, persist: false);
        SyncThemeToggle();

        try
        {
            await _workspace.LoadPersistedConnectionAsync().ConfigureAwait(true);
        }
        catch
        {
            // Best-effort; Settings can reconfigure.
        }

        UpdateConnectionHeader();
        UpdateInfrastructureHeader();

        _navReady = true;
        Navigate(_workspace.HasCredentials ? AdminSection.Maps : AdminSection.Settings);
        ScheduleBackgroundWarmCache();

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

        // Let UI settle before optional BD/SFTP probes.
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);

        try
        {
            await _infra.CheckAllAsync();
        }
        catch
        {
            // Quiet auto-check — header shows Error/NotConfigured; no modal.
        }
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        => SetSidebarExpanded(!_sidebarExpanded, persist: true, animate: true);

    private void SetSidebarExpanded(bool expanded, bool persist, bool animate = false)
    {
        _sidebarExpanded = expanded;
        var toWidth = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;

        // Swap content early so the big arrow is the correct glyph while animating.
        if (expanded)
        {
            SidebarCollapsedRail.Visibility = Visibility.Collapsed;
            SidebarExpanded.Visibility = Visibility.Visible;
        }

        if (animate)
        {
            AnimateSidebarWidth(toWidth, onCompleted: () =>
            {
                if (!expanded)
                {
                    SidebarExpanded.Visibility = Visibility.Collapsed;
                    SidebarCollapsedRail.Visibility = Visibility.Visible;
                }
            });
        }
        else
        {
            SidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            SidebarHost.Width = toWidth;
            SidebarExpanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SidebarCollapsedRail.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        }

        if (!persist)
            return;

        try
        {
            _settings ??= AppSettingsStore.Load();
            _settings.UiLayout.AdminSidebarCollapsed = !expanded;
            AppSettingsStore.Save(_settings);
        }
        catch
        {
            // Preferencia no crítica.
        }
    }

    private void AnimateSidebarWidth(double toWidth, Action? onCompleted = null)
    {
        SidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
        var from = SidebarHost.ActualWidth > 1 ? SidebarHost.ActualWidth : SidebarHost.Width;
        var anim = new DoubleAnimation(from, toWidth, SidebarAnimDuration)
        {
            EasingFunction = new CubicEase
            {
                EasingMode = toWidth > from ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            SidebarHost.Width = toWidth;
            onCompleted?.Invoke();
        };
        SidebarHost.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void SyncThemeToggle()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(SyncThemeToggle);
            return;
        }

        if (ThemeToggle is null)
            return;

        _syncingThemeToggle = true;
        ThemeToggle.IsChecked = ThemeService.IsDarkEffective;
        _syncingThemeToggle = false;
    }

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingThemeToggle || ThemeToggle is null)
            return;

        var preference = ThemeToggle.IsChecked == true
            ? ThemePreference.Dark
            : ThemePreference.Light;

        var settings = AppSettingsStore.Load();
        settings.Theme = preference;
        AppSettingsStore.Save(settings);
        ThemeService.SetPreference(preference);
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
                await Task.Yield();
                await maps.Dispatcher.InvokeAsync(
                    () => maps.ViewModel.EnsureLibraryLoaded(),
                    DispatcherPriority.Background);
                break;
        }
    }

    /// <summary>
    /// After initial nav is responsive, warm heavy modules off-screen (memory cache).
    /// Does not block initial startup.
    /// </summary>
    private void ScheduleBackgroundWarmCache()
    {
        if (_warmCacheScheduled)
            return;
        _warmCacheScheduled = true;
        _ = WarmHeavyModulesWhenIdleAsync();
    }

    private async Task WarmHeavyModulesWhenIdleAsync()
    {
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(2000);

        foreach (var section in new[] { AdminSection.Content, AdminSection.Maps })
        {
            if (_views.ContainsKey(section))
                continue;

            try
            {
                await WarmSectionAsync(section).ConfigureAwait(true);
            }
            catch
            {
                // Warm cache is best-effort.
            }

            await Task.Delay(400);
        }
    }

    private async Task WarmSectionAsync(AdminSection section)
    {
        if (!IsHeavySection(section))
            return;

        var view = await Dispatcher.InvokeAsync(() =>
        {
            var created = GetOrCreateView(section);
            if (!ReferenceEquals(GetPreloadHost(section).Content, created))
                GetPreloadHost(section).Content = created;
            return created;
        }, DispatcherPriority.Background);

        await WaitForSectionReadyAsync(section, view).ConfigureAwait(true);

        await Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(GetPreloadHost(section).Content, view)
                && !ReferenceEquals(ContentHost.Content, view))
            {
                GetPreloadHost(section).Content = null;
            }
        }, DispatcherPriority.Background);
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
            AdminSection.Settings => new SettingsView(_workspace, this),
            AdminSection.Maps => CreateMapsEditor(),
            AdminSection.Content => CreateContentWorkspace(),
            // Missions live inside ContentWorkspaceView (NPC/diálogos/misiones).
            AdminSection.Missions => CreateContentWorkspace(),
            _ => new PlaceholderView("RUFUS ADMIN", ""),
        };

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

    private void SyncNav(AdminSection section) => SetNavChecked("AdminNav", section);

    private void SetNavChecked(string groupName, AdminSection section)
    {
        foreach (var rb in FindVisualChildren<RadioButton>(this))
        {
            if (rb.GroupName != groupName || rb.Tag is not string tag)
                continue;
            if (Enum.TryParse<AdminSection>(tag, true, out var s))
                rb.IsChecked = s == section;
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
