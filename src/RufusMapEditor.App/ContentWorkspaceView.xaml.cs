using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.Services;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App;

public partial class ContentWorkspaceView : UserControl
{
    private readonly ContentWorkspaceViewModel _vm;
    private Task? _initializeTask;

    public ContentWorkspaceView()
        : this(new ContentWorkspaceViewModel())
    {
    }

    public ContentWorkspaceView(ContentWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await EnsureInitializedAsync();
    }

    /// <summary>Idempotent init (catalog + drafts). Safe to call from ADMIN preload.</summary>
    public Task EnsureInitializedAsync()
    {
        return _initializeTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        await VisualLibraryBootstrap.EnsureItemsAsync().ConfigureAwait(true);
        await _vm.InitializeAsync().ConfigureAwait(true);
    }

    public ContentWorkspaceViewModel ViewModel => _vm;

    /// <summary>ADMIN.UI.3 — when embedded, Close leaves host; otherwise closes owning Window.</summary>
    public bool IsEmbeddedHost { get; private set; }

    public void SetEmbeddedHost(bool embedded)
    {
        IsEmbeddedHost = embedded;
        if (CloseButton is not null)
            CloseButton.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
        // ADMIN.UI.3.1 — BD/SFTP status lives in Admin shell header (same AppSettings).
        if (LocalConnectionPanel is not null)
            LocalConnectionPanel.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
        if (LocalConnectionDetails is not null)
            LocalConnectionDetails.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (IsEmbeddedHost)
            return;
        Window.GetWindow(this)?.Close();
    }

    private void LocationField_Changed(object sender, TextChangedEventArgs e)
    {
        _vm.Npc.NotifyLocationEdited();
        _vm.NotifyLocationsUiChanged();
    }

    private void OrientationPicker_Edited(object? sender, EventArgs e)
    {
        _vm.Npc.NotifyLocationEdited();
        _vm.NotifyLocationsUiChanged();
    }

    private void AddLocation_Click(object sender, RoutedEventArgs e) =>
        _vm.NotifyLocationsUiChanged();

    private void RemoveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NpcLocationDraft loc })
            return;
        _vm.Npc.SelectedLocation = loc;
        if (_vm.Npc.RemoveLocationCommand.CanExecute(null))
            _vm.Npc.RemoveLocationCommand.Execute(null);
        _vm.NotifyLocationsUiChanged();
    }
}
