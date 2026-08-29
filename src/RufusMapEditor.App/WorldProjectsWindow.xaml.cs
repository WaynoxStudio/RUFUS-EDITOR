using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.World;

namespace RufusMapEditor.App;

public partial class WorldProjectsWindow : Window
{
    public string? SelectedPath { get; private set; }

    public WorldProjectsWindow(string geopositionsRoot, IReadOnlyList<WorldProjectInfo> projects)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        PathText.Text = geopositionsRoot;
        var items = new ObservableCollection<WorldProjectRow>(
            projects.Select(p => new WorldProjectRow(p)));
        ProjectsList.ItemsSource = items;
        if (items.Count > 0)
            ProjectsList.SelectedIndex = 0;
    }

    private void Open_Click(object sender, RoutedEventArgs e) => TryAcceptSelection();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ProjectsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => TryAcceptSelection();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = $"Mundo RUFUS (*{RufworldFormat.FileExtension})|*{RufworldFormat.FileExtension}",
            Title = "Abrir mundo",
        };
        if (dlg.ShowDialog(this) != true) return;
        SelectedPath = dlg.FileName;
        DialogResult = true;
    }

    private void TryAcceptSelection()
    {
        if (ProjectsList.SelectedItem is not WorldProjectRow row)
        {
            MessageBox.Show(this, "Selecciona un proyecto de la lista o usa Examinar archivo.", Title);
            return;
        }

        SelectedPath = row.FilePath;
        DialogResult = true;
    }

    private sealed class WorldProjectRow
    {
        public WorldProjectRow(WorldProjectInfo info)
        {
            Name = info.Name;
            FilePath = info.FilePath;
            ModifiedLocal = info.ModifiedUtc.ToLocalTime().ToString("g");
        }

        public string Name { get; }
        public string FilePath { get; }
        public string ModifiedLocal { get; }
    }
}
