using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class ContentMissionEditorWindow : Window
{
    private readonly ContentMissionEditorViewModel _vm;

    public ContentMissionEditorWindow()
        : this(new ContentMissionEditorViewModel())
    {
    }

    public ContentMissionEditorWindow(ContentMissionEditorViewModel viewModel)
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

    private void Field_Changed(object sender, RoutedEventArgs e) => _vm.NotifyEdited();

    private void Rewards_Changed(object sender, TextChangedEventArgs e)
    {
        _vm.ApplyRewardsFromUi();
        _vm.NotifyEdited();
    }
}
