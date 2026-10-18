using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App;

public partial class NpcGfxPickerWindow : Window
{
    private readonly NpcGfxCatalogService _catalog;
    private readonly ObservableCollection<NpcGfxPickerRowVm> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private string _query = "";
    private int? _selectedGfxId;

    public int? SelectedGfxId { get; private set; }

    public NpcGfxPickerWindow(NpcGfxCatalogService catalog, int? initialGfxId = null, string? initialQuery = null)
    {
        InitializeComponent();
        _catalog = catalog;
        ResultList.ItemsSource = _visible;
        UpdateClipsBanner();
        StatusText.Text = catalog.Status;
        PreviewNote.Text = "Galería de apariencias NPC · Solo se muestran apariencias confirmadas.";
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RefreshList();
        };
        if (!string.IsNullOrWhiteSpace(initialQuery))
            SearchBox.Text = initialQuery;
        else if (initialGfxId is int id)
            SearchBox.Text = id.ToString();
        RefreshList();
    }

    private void UpdateClipsBanner()
    {
        ClipsHintPanel.Visibility = _catalog.HasSpriteNames ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConfigureClips_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var dlg = new ClipsSettingsWindow(settings) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _catalog.SetClipsRoot(settings.ClipsRootPath);
        if (_catalog.IsLoaded)
            _catalog.ReloadSpriteMetadata(settings.ClipsRootPath);

        UpdateClipsBanner();
        StatusText.Text = _catalog.Status;
        RefreshList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text ?? "";
        _debounce.Stop();
        _debounce.Start();
    }

    private void RefreshList()
    {
        _selectedGfxId = (ResultList.SelectedItem as NpcGfxPickerRowVm)?.GfxId ?? _selectedGfxId;
        _visible.Clear();
        var hits = _catalog.Search(_query).Select(e => new NpcGfxPickerRowVm(e)).ToList();
        foreach (var row in hits)
            _visible.Add(row);

        if (_selectedGfxId is int gfxId)
        {
            var row = hits.FirstOrDefault(h => h.GfxId == gfxId);
            if (row is not null)
                ResultList.SelectedItem = row;
        }

        var total = _catalog.Entries.Count;
        CountText.Text = total == 0
            ? "Catálogo vacío"
            : $"Mostrando {hits.Count} de {total}";
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is NpcGfxPickerRowVm row)
            _selectedGfxId = row.GfxId;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is NpcGfxPickerRowVm row)
        {
            SelectedGfxId = row.GfxId;
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "Selecciona una apariencia.", "Apariencias NPC");
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is NpcGfxPickerRowVm)
            Ok_Click(sender, e);
    }
}

/// <summary>One gallery card = one confirmed GFX. UsageSummary stays on catalog (editor hints); not shown here.</summary>
public sealed class NpcGfxPickerRowVm
{
    public NpcGfxPickerRowVm(NpcGfxCatalogEntry entry)
    {
        Entry = entry;
        GfxId = entry.GfxId;
        DisplayName = entry.DisplayName;
        GfxIdLabel = entry.GfxIdLabel;
    }

    public NpcGfxCatalogEntry Entry { get; }
    public int GfxId { get; }
    public string DisplayName { get; }
    public string GfxIdLabel { get; }
}
