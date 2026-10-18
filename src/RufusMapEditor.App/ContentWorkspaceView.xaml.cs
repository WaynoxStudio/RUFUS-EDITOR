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
        VisualLibraryBootstrap.TryLoadCachedItems();
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
        if (!_vm.ConfirmLeaveIfDirty())
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

    /// <summary>Al enfocar Mapa/Celda con valor 0, vaciar para escribir sin borrar a mano.</summary>
    private void ZeroDefaultIntField_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Text: "0" } box)
            box.Clear();
    }

    private void ZeroDefaultIntField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        if (string.IsNullOrWhiteSpace(box.Text))
            box.Text = "0";
    }

    private void CollapseNpcList_Click(object sender, RoutedEventArgs e) =>
        _vm.NpcListExpanded = false;

    private void ExpandNpcList_Click(object sender, RoutedEventArgs e) =>
        _vm.NpcListExpanded = true;

    private void LibraryCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResolveLibraryCardFromSender(sender) is not { } card)
            return;

        _vm.Library.SelectedEntry = card.Entry;

        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0
            && card.CanMultiSelect)
        {
            _vm.Library.ToggleChecked(card);
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2 && _vm.Library.OpenCommand.CanExecute(null))
            _vm.Library.OpenCommand.Execute(null);
    }

    private void LibraryOpen_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ResolveLibraryEntryFromSender(sender) is not { } entry)
            return;
        _vm.Library.SelectedEntry = entry;
        if (_vm.Library.OpenCommand.CanExecute(null))
            _vm.Library.OpenCommand.Execute(null);
    }

    private void LibraryDuplicate_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ResolveLibraryEntryFromSender(sender) is not { } entry)
            return;
        _vm.Library.SelectedEntry = entry;
        if (_vm.Library.DuplicateCommand.CanExecute(null))
            _vm.Library.DuplicateCommand.Execute(null);
    }

    private void LibraryMore_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { ContextMenu: { } menu } btn)
            return;
        if (ResolveLibraryEntryFromSender(sender) is { } entry)
        {
            _vm.Library.SelectedEntry = entry;
            menu.DataContext = entry;
        }

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LibraryRemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        var entry = (sender as FrameworkElement)?.DataContext as ContentLibraryEntry
                    ?? _vm.Library.SelectedEntry;
        if (entry is null || !entry.CanRemove)
            return;
        _vm.Library.SelectedEntry = entry;
        if (_vm.Library.RemoveCommand.CanExecute(null))
            _vm.Library.RemoveCommand.Execute(null);
    }

    private static ContentLibraryCardVm? ResolveLibraryCardFromSender(object sender)
    {
        for (DependencyObject? d = sender as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: ContentLibraryCardVm card })
                return card;
        }

        return null;
    }

    private static ContentLibraryEntry? ResolveLibraryEntryFromSender(object sender)
    {
        for (DependencyObject? d = sender as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: ContentLibraryEntry entry })
                return entry;
            if (d is FrameworkElement { DataContext: ContentLibraryCardVm card })
                return card.Entry;
        }

        return null;
    }
}
