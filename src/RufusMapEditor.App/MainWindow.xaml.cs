using System.ComponentModel;
using System.Windows;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

/// <summary>
/// USER Maps shell — thin Window host around shared <see cref="MapsEditorView"/>.
/// ADMIN.UI.2 hosts the same UserControl inside ContentHost.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = Editor.ViewModel;
        Closed += (_, _) => Editor.DisposeWorkspace();
    }

    public MainViewModel ViewModel => Editor.ViewModel;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!Editor.TryConfirmClose())
            e.Cancel = true;
    }
}
