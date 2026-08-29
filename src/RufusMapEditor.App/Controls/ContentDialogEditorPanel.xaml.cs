using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class ContentDialogEditorPanel : UserControl
{
    public ContentDialogEditorPanel()
    {
        InitializeComponent();
    }

    private ContentDialogEditorViewModel? Vm => DataContext as ContentDialogEditorViewModel;

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is not null)
            Vm.SelectedNode = e.NewValue;
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e) =>
        Vm?.NotifyTextEdited();
}
