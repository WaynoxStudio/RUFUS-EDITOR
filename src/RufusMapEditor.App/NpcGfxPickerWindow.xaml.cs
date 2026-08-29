using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App;

public partial class NpcGfxPickerWindow : Window
{
    private readonly NpcGfxCatalogService _catalog;
    private readonly ObservableCollection<NpcGfxPickerPairVm> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private string _query = "";
    private NpcGfxPickerRowVm? _selected;
    private int? _selectedGfxId;

    public int? SelectedGfxId { get; private set; }

    public NpcGfxPickerWindow(NpcGfxCatalogService catalog, int? initialGfxId = null, string? initialQuery = null)
    {
        InitializeComponent();
        _catalog = catalog;
        ResultList.ItemsSource = _visible;
        UpdateClipsBanner();
        StatusText.Text = catalog.Status;
        PreviewNote.Text =
            "Galería: artworks/big · Preview: sprites/staticR · Solo apariencias confirmadas en npcs_modelo.";
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
        _selectedGfxId = _selected?.GfxId;
        _visible.Clear();
        _selected = null;
        var hits = _catalog.Search(_query).Select(e => new NpcGfxPickerRowVm(e)).ToList();
        for (var i = 0; i < hits.Count; i += 2)
        {
            _visible.Add(new NpcGfxPickerPairVm(
                hits[i],
                i + 1 < hits.Count ? hits[i + 1] : null));
        }

        if (_selectedGfxId is int gfxId)
        {
            var row = hits.FirstOrDefault(h => h.GfxId == gfxId);
            if (row is not null)
                SelectRow(row);
        }

        var total = _catalog.Entries.Count;
        CountText.Text = total == 0
            ? "Catálogo vacío"
            : $"Mostrando {hits.Count} de {total}";
    }

    private void GfxCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NpcGfxPickerRowVm row })
            return;
        SelectRow(row);
        e.Handled = true;
    }

    private void SelectRow(NpcGfxPickerRowVm row)
    {
        _selected = row;
        _selectedGfxId = row.GfxId;
        PreviewSpriteImage.GfxId = row.GfxId;
        PreviewNameText.Text = row.DisplayName;
        PreviewGfxText.Text = row.GfxIdLabel;
        PreviewUsageText.Text = row.UsageSummary;
        foreach (var pair in _visible)
        {
            if (ReferenceEquals(pair.Left, row) || ReferenceEquals(pair.Right, row))
            {
                ResultList.SelectedItem = pair;
                break;
            }
        }
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
        {
            SelectedGfxId = _selected.GfxId;
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "Selecciona una apariencia.", "Apariencias NPC");
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestorTag<NpcGfxPickerRowVm>(e.OriginalSource as DependencyObject) is { } row)
        {
            SelectRow(row);
            Ok_Click(sender, e);
        }
        else if (_selected is not null)
        {
            Ok_Click(sender, e);
        }
    }

    private static T? FindAncestorTag<T>(DependencyObject? start) where T : class
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { Tag: T tagged })
                return tagged;
        }

        return null;
    }
}

public sealed class NpcGfxPickerPairVm
{
    public NpcGfxPickerPairVm(NpcGfxPickerRowVm left, NpcGfxPickerRowVm? right)
    {
        Left = left;
        Right = right;
    }

    public NpcGfxPickerRowVm Left { get; }
    public NpcGfxPickerRowVm? Right { get; }
}

public sealed class NpcGfxPickerRowVm
{
    public NpcGfxPickerRowVm(NpcGfxCatalogEntry entry)
    {
        Entry = entry;
        GfxId = entry.GfxId;
        DisplayName = entry.DisplayName;
        GfxIdLabel = entry.GfxIdLabel;
        UsageSummary = entry.UsageSummary;
    }

    public NpcGfxCatalogEntry Entry { get; }
    public int GfxId { get; }
    public string DisplayName { get; }
    public string GfxIdLabel { get; }
    public string UsageSummary { get; }
}
