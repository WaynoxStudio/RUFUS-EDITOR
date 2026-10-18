using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class WorldNameInputWindow : Window
{
    private readonly string _libraryRoot;
    private readonly string _geoRoot;
    private readonly HashSet<string> _existingNames;
    private bool _suppressSelectionSync;

    public string? ResultName { get; private set; }

    public WorldNameInputWindow(string prompt, string suggestedName, string geopositionsRoot)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        PromptText.Text = prompt;
        _geoRoot = geopositionsRoot;
        _libraryRoot = GuessLibraryRoot(geopositionsRoot);

        var projects = string.IsNullOrWhiteSpace(_libraryRoot)
            ? new List<WorldProjectRow>()
            : GeopositionsStore.ListProjects(_libraryRoot)
                .Select(p => new WorldProjectRow(p.Name, p.ModifiedUtc.ToLocalTime()))
                .ToList();

        _existingNames = new HashSet<string>(
            projects.Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        ExistingList.ItemsSource = projects;
        EmptyProjectsText.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        NameBox.Text = string.IsNullOrWhiteSpace(suggestedName) ? "Mundo" : suggestedName.Trim();
        UpdateHintAndButton();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private static string GuessLibraryRoot(string geopositionsRoot)
    {
        try
        {
            var full = Path.GetFullPath(geopositionsRoot);
            var parent = Path.GetDirectoryName(full);
            return parent ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void ExistingList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync) return;
        if (ExistingList.SelectedItem is not WorldProjectRow row) return;
        NameBox.Text = row.Name;
        UpdateHintAndButton();
    }

    private void ExistingList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ExistingList.SelectedItem is WorldProjectRow)
            Ok_Click(sender, e);
    }

    private void NameBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateHintAndButton();
        SyncListSelectionFromName();
    }

    private void SyncListSelectionFromName()
    {
        var name = GeopositionsStore.SanitizeProjectName(NameBox.Text);
        _suppressSelectionSync = true;
        try
        {
            if (ExistingList.ItemsSource is IEnumerable<WorldProjectRow> rows)
            {
                var match = rows.FirstOrDefault(r =>
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                ExistingList.SelectedItem = match;
            }
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private void UpdateHintAndButton()
    {
        var name = GeopositionsStore.SanitizeProjectName(NameBox.Text);
        var exists = _existingNames.Contains(name);
        OkButton.Content = exists ? "Reemplazar" : "Guardar";
        HintText.Text = exists
            ? $"Se reemplazará el proyecto existente:\n{_geoRoot}\\{name}\\{name}.rufworld\n(+ {name}.png y {name}_Mode.png)"
            : $"Se guardará en:\n{_geoRoot}\\<nombre>\\<nombre>.rufworld\n(+ <nombre>.png y <nombre>_Mode.png)";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Introduce un nombre para el mundo.", Title);
            return;
        }

        var name = GeopositionsStore.SanitizeProjectName(NameBox.Text);
        if (_existingNames.Contains(name))
        {
            var overwrite = MessageBox.Show(
                this,
                $"Ya existe el proyecto «{name}».\n\n¿Sobrescribir?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        ResultName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class WorldProjectRow
    {
        public WorldProjectRow(string name, DateTime modifiedLocal)
        {
            Name = name;
            ModifiedLocal = modifiedLocal.ToString("g", CultureInfo.CurrentCulture);
        }

        public string Name { get; }
        public string ModifiedLocal { get; }
    }
}
