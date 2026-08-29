using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App;

/// <summary>ADMIN.UI.4B — pick Area ID from estaticos.areas (not subareas).</summary>
public partial class AreaPickerWindow : Window
{
    private readonly IAreasReadRepository _repo;
    private readonly ObservableCollection<AreaCatalogEntry> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private string _query = "";

    public AreaCatalogEntry? SelectedEntry { get; private set; }

    public AreaPickerWindow(IAreasReadRepository repo, string? initialQuery = null)
    {
        InitializeComponent();
        _repo = repo;
        ResultList.ItemsSource = _visible;
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
        };
        if (!string.IsNullOrWhiteSpace(initialQuery))
            SearchBox.Text = initialQuery;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text ?? "";
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "Buscando áreas (no subáreas)…";
            var hits = await _repo.SearchAsync(_query, 80).ConfigureAwait(true);
            _visible.Clear();
            foreach (var h in hits)
                _visible.Add(h);
            CountText.Text = $"{_visible.Count} área(s)";
            StatusText.Text = "Fuente: estaticos.areas · id / nombre / superarea";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        TryAccept();

    private void Ok_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void TryAccept()
    {
        if (ResultList.SelectedItem is not AreaCatalogEntry entry)
            return;
        SelectedEntry = entry;
        DialogResult = true;
        Close();
    }
}
