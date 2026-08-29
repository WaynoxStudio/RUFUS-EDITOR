using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App;

public partial class ContentNpcEditorWindow : Window
{
    private readonly ContentNpcEditorViewModel _vm;

    public ContentNpcEditorWindow()
        : this(new ContentNpcEditorViewModel())
    {
    }

    public ContentNpcEditorWindow(ContentNpcEditorViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _vm.InitializeAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void LocationField_Changed(object sender, TextChangedEventArgs e) =>
        _vm.NotifyLocationEdited();

    private void OrientationPicker_Edited(object? sender, EventArgs e) =>
        _vm.NotifyLocationEdited();

    private void RemoveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NpcLocationDraft loc })
            return;
        _vm.SelectedLocation = loc;
        if (_vm.RemoveLocationCommand.CanExecute(null))
            _vm.RemoveLocationCommand.Execute(null);
    }
}
