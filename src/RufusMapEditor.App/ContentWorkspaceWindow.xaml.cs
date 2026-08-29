using System.Windows;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App;

/// <summary>
/// USER Hub wrapper — hosts shared <see cref="ContentWorkspaceView"/> (ADMIN.UI.3).
/// </summary>
public partial class ContentWorkspaceWindow : Window
{
    public ContentWorkspaceWindow()
    {
        InitializeComponent();
    }

    public ContentWorkspaceWindow(ContentWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        if (viewModel is not null)
            Content = new ContentWorkspaceView(viewModel);
    }

    public ContentWorkspaceView WorkspaceView =>
        Content as ContentWorkspaceView ?? Workspace;
}
