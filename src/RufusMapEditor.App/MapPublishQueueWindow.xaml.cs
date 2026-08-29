using System.Windows;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

public partial class MapPublishQueueWindow : Window
{
    public MapPublishQueueWindow(MapPublishQueueViewModel vm)
    {
        InitializeComponent();
        Services.ThemeService.ApplyToWindow(this);
        DataContext = vm;
        vm.Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
