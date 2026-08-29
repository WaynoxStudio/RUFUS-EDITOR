using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App;

public partial class MonsterPickerWindow : Window
{
    private readonly VisualLibraryService _lib;
    private readonly ObservableCollection<MonsterPickerPairVm> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private string _query = "";
    private MonsterPickerRowVm? _selectedMob;

    public MonsterCatalogEntry? SelectedEntry { get; private set; }

    public MonsterPickerWindow(VisualLibraryService library, string? initialQuery = null)
    {
        InitializeComponent();
        _lib = library;
        ResultList.ItemsSource = _visible;
        StatusText.Text = library.StatusMonsters;
        PreviewNote.Text =
            "Miniaturas desde clips/artworks/big/{gfx}.swf (caché Library/cache/artworks). " +
            "Carga diferida al hacer scroll.";
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RefreshList();
        };
        if (!string.IsNullOrWhiteSpace(initialQuery))
            SearchBox.Text = initialQuery;
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
        _visible.Clear();
        _selectedMob = null;
        var hits = _lib.SearchMonsters(_query).Select(m => new MonsterPickerRowVm(m)).ToList();
        for (var i = 0; i < hits.Count; i += 2)
        {
            _visible.Add(new MonsterPickerPairVm(
                hits[i],
                i + 1 < hits.Count ? hits[i + 1] : null));
        }

        var total = _lib.Monsters.Count;
        CountText.Text = total == 0
            ? "Catálogo vacío"
            : $"Mostrando {hits.Count} de {total}";
    }

    private void MonsterCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MonsterPickerRowVm mob })
            return;
        SelectMob(mob);
        e.Handled = true;
    }

    private void SelectMob(MonsterPickerRowVm mob)
    {
        _selectedMob = mob;
        // Highlight: select the pair row that contains the mob.
        foreach (var pair in _visible)
        {
            if (ReferenceEquals(pair.Left, mob) || ReferenceEquals(pair.Right, mob))
            {
                ResultList.SelectedItem = pair;
                break;
            }
        }
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep card selection; pair selection alone does not pick a mob.
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMob is not null)
        {
            SelectedEntry = _selectedMob.Entry;
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "Selecciona un monstruo.", "Monstruos");
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Prefer the card under the cursor.
        if (FindAncestorTag<MonsterPickerRowVm>(e.OriginalSource as DependencyObject) is { } mob)
        {
            SelectMob(mob);
            Ok_Click(sender, e);
        }
        else if (_selectedMob is not null)
            Ok_Click(sender, e);
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

public sealed class MonsterPickerPairVm
{
    public MonsterPickerPairVm(MonsterPickerRowVm left, MonsterPickerRowVm? right)
    {
        Left = left;
        Right = right;
    }

    public MonsterPickerRowVm Left { get; }
    public MonsterPickerRowVm? Right { get; }
}

public sealed class MonsterPickerRowVm
{
    public MonsterPickerRowVm(MonsterCatalogEntry entry)
    {
        Entry = entry;
        GfxId = entry.GfxId;
        Title = entry.Nombre;
        Subtitle =
            $"Mob ID: {entry.Id}\nGFX: {entry.GfxId}\nNiveles: {entry.LevelsDisplay}";
        PathHint = $"Archivo: {entry.ArtworkRelativePath}";
    }

    public MonsterCatalogEntry Entry { get; }
    public int GfxId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string PathHint { get; }
}
