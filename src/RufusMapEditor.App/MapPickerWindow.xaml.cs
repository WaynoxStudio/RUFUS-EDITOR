using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class MapPickerWindow : Window
{
    private readonly AstriaLibraryService _library;
    private readonly MapPreviewCache _previews;
    private readonly List<MapPickerItemVm> _allItems = new();
    private readonly ObservableCollection<MapPickerItemVm> _visibleItems = new();
    private readonly MapPickerFilterState? _persistState;
    private CancellationTokenSource? _loadCts;
    private bool _suppressFilterEvents = true;

    public int? SelectedMapId { get; private set; }
    public bool NewMapRequested { get; private set; }

    public MapPickerWindow(
        AstriaLibraryService library,
        MapPreviewCache previews,
        IEnumerable<int> mapIds,
        int? current,
        string? title = null,
        string? prompt = null,
        MapPickerFilterState? persistState = null,
        bool allowNewMap = false)
    {
        InitializeComponent();
        _library = library;
        _previews = previews;
        _persistState = persistState;

        if (!string.IsNullOrWhiteSpace(title))
            Title = title;
        if (!string.IsNullOrWhiteSpace(prompt))
            PromptText.Text = prompt;

        NewMapButton.Visibility = allowNewMap ? Visibility.Visible : Visibility.Collapsed;

        foreach (var id in mapIds)
            _allItems.Add(new MapPickerItemVm(id));

        _suppressFilterEvents = true;
        try
        {
            if (_persistState is not null)
            {
                SearchBox.Text = _persistState.SearchText ?? "";
                RangeFromBox.Text = _persistState.RangeFromText ?? "";
                RangeToBox.Text = _persistState.RangeToText ?? "";
                SortBox.SelectedIndex = _persistState.Ascending ? 0 : 1;
            }
            else
            {
                SortBox.SelectedIndex = 0;
            }
        }
        finally
        {
            ApplyFilter();
            MapGrid.ItemsSource = _visibleItems;
            _suppressFilterEvents = false;
        }

        if (current is int selectedId)
        {
            var item = _visibleItems.FirstOrDefault(x => x.MapId == selectedId)
                       ?? _allItems.FirstOrDefault(x => x.MapId == selectedId);
            if (item is not null)
                MapGrid.SelectedItem = item;
        }
        else if (_visibleItems.Count > 0)
        {
            MapGrid.SelectedIndex = 0;
        }

        Loaded += async (_, _) => await LoadThumbnailsAsync();
        Closed += (_, _) =>
        {
            _loadCts?.Cancel();
            SavePersistState();
        };
    }

    private bool IsAscending =>
        SortBox.SelectedItem is ComboBoxItem { Tag: "asc" }
        || SortBox.SelectedIndex <= 0;

    private void SavePersistState()
    {
        if (_persistState is null) return;
        _persistState.SearchText = SearchBox.Text ?? "";
        _persistState.Ascending = IsAscending;
        _persistState.RangeFromText = RangeFromBox.Text ?? "";
        _persistState.RangeToText = RangeToBox.Text ?? "";
        _persistState.NotifyChanged();
    }

    private void ApplyFilter()
    {
        _visibleItems.Clear();

        var q = (SearchBox.Text ?? "").Trim();
        TryParseBound(RangeFromBox.Text, out var fromId);
        TryParseBound(RangeToBox.Text, out var toId);

        IEnumerable<MapPickerItemVm> query = _allItems;
        if (fromId is int min)
            query = query.Where(x => x.MapId >= min);
        if (toId is int max)
            query = query.Where(x => x.MapId <= max);
        if (q.Length > 0)
            query = query.Where(x => x.MapId.ToString().Contains(q, StringComparison.Ordinal));

        query = IsAscending
            ? query.OrderBy(x => x.MapId)
            : query.OrderByDescending(x => x.MapId);

        foreach (var item in query)
            _visibleItems.Add(item);
    }

    private static void TryParseBound(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (int.TryParse(text.Trim(), out var n))
            value = n;
    }

    private async Task LoadThumbnailsAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        using var gate = new SemaphoreSlim(4);
        var tasks = _allItems.Select(async item =>
        {
            if (token.IsCancellationRequested) return;

            var cached = _previews.TryGetCached(item.MapId);
            if (cached is not null)
            {
                item.Thumbnail = cached.Image;
                item.IsLoading = false;
                return;
            }

            await gate.WaitAsync(token);
            try
            {
                var preview = await _previews.GetOrRenderAsync(_library, item.MapId);
                if (token.IsCancellationRequested) return;
                item.Thumbnail = preview?.Image;
            }
            catch
            {
                // Preview opcional; el ID sigue siendo seleccionable.
            }
            finally
            {
                item.IsLoading = false;
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Dialog closed while loading.
        }
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents || MapGrid is null || SearchBox is null || SortBox is null)
            return;

        var selectedId = (MapGrid.SelectedItem as MapPickerItemVm)?.MapId;
        ApplyFilter();
        SavePersistState();

        if (selectedId is int id)
        {
            var item = _visibleItems.FirstOrDefault(x => x.MapId == id);
            MapGrid.SelectedItem = item ?? (_visibleItems.Count > 0 ? _visibleItems[0] : null);
        }
        else if (_visibleItems.Count > 0 && MapGrid.SelectedItem is null)
        {
            MapGrid.SelectedIndex = 0;
        }
    }

    private void NewMap_Click(object sender, RoutedEventArgs e)
    {
        NewMapRequested = true;
        SelectedMapId = null;
        SavePersistState();
        DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (MapGrid.SelectedItem is MapPickerItemVm item)
        {
            NewMapRequested = false;
            SelectedMapId = item.MapId;
            SavePersistState();
            DialogResult = true;
        }
    }

    private void MapGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MapGrid.SelectedItem is MapPickerItemVm)
            Ok_Click(sender, e);
    }
}
