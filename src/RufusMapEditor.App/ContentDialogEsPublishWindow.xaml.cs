using System.Windows;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class ContentDialogEsPublishWindow : Window
{
    public ContentDialogEsPublishWindow(ContentDialogEsPublishViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = ok =>
        {
            DialogResult = ok;
            Close();
        };
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
