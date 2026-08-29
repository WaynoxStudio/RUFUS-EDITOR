using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App;

public partial class ItemPickerWindow : Window
{
    private readonly VisualLibraryService _lib;
    private readonly ObservableCollection<ItemPickerRowVm> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private string _query = "";
    private int? _typeFilter;

    public ItemCatalogEntry? SelectedEntry { get; private set; }
    public int SelectedQuantity { get; private set; } = 1;

    public ItemPickerWindow(VisualLibraryService library, string? initialQuery = null, int initialQty = 1)
    {
        InitializeComponent();
        _lib = library;
        ResultList.ItemsSource = _visible;
        StatusText.Text = library.StatusItems;
        QtyBox.Text = initialQty > 0 ? initialQty.ToString() : "1";
        PreviewNote.Text =
            "Icono SWF: DATO PENDIENTE DE CONFIRMAR (sin rasterizador). " +
            "Si el archivo falta → «Icono no disponible»; el Item ID se conserva.";

        TypeFilter.Items.Add(new TypeOption(null, "Todas las categorías"));
        foreach (var t in library.Items.Select(i => (i.TypeId, i.Category)).Distinct().OrderBy(x => x.Category))
            TypeFilter.Items.Add(new TypeOption(t.TypeId, t.Category));
        TypeFilter.SelectedIndex = 0;

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

    private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _typeFilter = (TypeFilter.SelectedItem as TypeOption)?.Id;
        RefreshList();
    }

    private void RefreshList()
    {
        _visible.Clear();
        foreach (var it in _lib.SearchItems(_query, _typeFilter, take: 250))
            _visible.Add(new ItemPickerRowVm(it));
        CountText.Text = $"{_visible.Count} resultados (máx. 250)";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not ItemPickerRowVm row)
        {
            MessageBox.Show(this, "Selecciona un objeto.", "Objetos");
            return;
        }

        if (!int.TryParse(QtyBox.Text, out var qty) || qty < 1)
            qty = 1;
        SelectedEntry = row.Entry;
        SelectedQuantity = qty;
        DialogResult = true;
    }

    private void ResultList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is ItemPickerRowVm)
            Ok_Click(sender, e);
    }

    private sealed record TypeOption(int? Id, string Label)
    {
        public override string ToString() => Label;
    }
}

public sealed class ItemPickerRowVm
{
    public ItemPickerRowVm(ItemCatalogEntry entry)
    {
        Entry = entry;
        Title = entry.Nombre;
        Subtitle =
            $"Item ID: {entry.ItemId} · Nivel {entry.Level} · {entry.Category} · GFX: {entry.GfxId}";
        IconStatus = entry.IconExists
            ? $"Icono: ✓ {entry.IconRelativePath}"
            : $"Icono no disponible · {entry.IconRelativePath}";
    }

    public ItemCatalogEntry Entry { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string IconStatus { get; }
}
