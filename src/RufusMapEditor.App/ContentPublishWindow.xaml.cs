using System.Windows;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class ContentPublishWindow : Window
{
    private readonly ContentPublishViewModel _vm;

    public ContentPublishWindow()
        : this(new ContentPublishViewModel())
    {
    }

    public ContentPublishWindow(ContentPublishViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _vm;
        _vm.RequestClose = ok =>
        {
            DialogResult = ok;
            Close();
        };
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _vm.InitializeAsync();
    }
}
