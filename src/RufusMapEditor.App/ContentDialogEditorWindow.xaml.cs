using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class ContentDialogEditorWindow : Window
{
    private readonly ContentDialogEditorViewModel _vm;

    public ContentDialogEditorWindow()
        : this(new ContentDialogEditorViewModel())
    {
    }

    public ContentDialogEditorWindow(ContentDialogEditorViewModel viewModel)
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

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _vm.SelectedNode = e.NewValue;
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.NotifyTextEdited();
    }
}
