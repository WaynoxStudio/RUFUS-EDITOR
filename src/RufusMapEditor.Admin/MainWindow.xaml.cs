using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private const double SidebarExpandedWidth = 220;
    private const double SidebarCollapsedWidth = 14;
    private static readonly TimeSpan SidebarAnimDuration = TimeSpan.FromMilliseconds(240);

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
    private DispatcherTimer? _sidebarCollapseTimer;
    private bool _sidebarExpanded;
    private bool _syncingThemeToggle;

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
        SetSidebarImmediate(expanded: false);
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

    private void SidebarHost_MouseEnter(object sender, MouseEventArgs e)
    {
        _sidebarCollapseTimer?.Stop();
        AnimateSidebar(expanded: true);
    }

    private void SidebarHost_MouseLeave(object sender, MouseEventArgs e)
    {
        _sidebarCollapseTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _sidebarCollapseTimer.Tick -= OnSidebarCollapseTick;
        _sidebarCollapseTimer.Tick += OnSidebarCollapseTick;
        _sidebarCollapseTimer.Stop();
        _sidebarCollapseTimer.Start();
    }

    private void OnSidebarCollapseTick(object? sender, EventArgs e)
    {
        _sidebarCollapseTimer?.Stop();
        AnimateSidebar(expanded: false);
    }

    private void SetSidebarImmediate(bool expanded)
    {
        StopSidebarAnimations();
        _sidebarExpanded = expanded;
        SidebarHost.Width = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
        SidebarExpanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SidebarExpanded.Opacity = 1;
        SidebarExpanded.IsHitTestVisible = expanded;
        SidebarArrow.Opacity = expanded ? 0 : 1;
        SidebarArrow.IsHitTestVisible = !expanded;
    }

    private void AnimateSidebar(bool expanded)
    {
        StopSidebarAnimations();
        _sidebarExpanded = expanded;

        var fromWidth = SidebarHost.ActualWidth > 0 ? SidebarHost.ActualWidth : SidebarHost.Width;
        var toWidth = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
        var easing = expanded
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseIn };

        if (expanded)
        {
            SidebarExpanded.Visibility = Visibility.Visible;
            SidebarExpanded.Opacity = 1;
            SidebarExpanded.IsHitTestVisible = true;
            SidebarArrow.Opacity = 0;
            SidebarArrow.IsHitTestVisible = false;
        }
        else
        {
            // Mantener texto visible; ClipToBounds recorta al reducir el ancho.
            SidebarExpanded.Opacity = 1;
            SidebarArrow.Opacity = 0;
            SidebarArrow.IsHitTestVisible = false;
        }

        var widthAnim = new DoubleAnimation(fromWidth, toWidth, SidebarAnimDuration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        };
        widthAnim.Completed += (_, _) =>
        {
            SidebarHost.Width = toWidth;
            _sidebarExpanded = expanded;
            if (expanded)
            {
                SidebarExpanded.Opacity = 1;
            }
            else
            {
                SidebarExpanded.Visibility = Visibility.Collapsed;
                SidebarExpanded.IsHitTestVisible = false;
                SidebarArrow.Opacity = 1;
                SidebarArrow.IsHitTestVisible = true;
            }
        };
        SidebarHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
    }

    private void StopSidebarAnimations()
    {
        SidebarHost.BeginAnimation(FrameworkElement.WidthProperty, null);
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
            AdminSection.Missions => new PlaceholderView("Misiones", AdminNavNotes.MissionsPlaceholder),
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
