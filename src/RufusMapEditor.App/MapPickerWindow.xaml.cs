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
    private CancellationTokenSource? _loadCts;

    public int? SelectedMapId { get; private set; }

    public MapPickerWindow(
        AstriaLibraryService library,
        MapPreviewCache previews,
        IEnumerable<int> mapIds,
        int? current,
        string? title = null,
        string? prompt = null)
    {
        InitializeComponent();
        _library = library;
        _previews = previews;

        if (!string.IsNullOrWhiteSpace(title))
            Title = title;
        if (!string.IsNullOrWhiteSpace(prompt))
            PromptText.Text = prompt;

        foreach (var id in mapIds.OrderBy(x => x))
            _allItems.Add(new MapPickerItemVm(id));

        ApplyFilter("");
        MapGrid.ItemsSource = _visibleItems;

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
        Closed += (_, _) => _loadCts?.Cancel();
    }

    private void ApplyFilter(string query)
    {
        _visibleItems.Clear();
        var q = query.Trim();
        foreach (var item in _allItems)
        {
            if (q.Length == 0 || item.MapId.ToString().Contains(q, StringComparison.Ordinal))
                _visibleItems.Add(item);
        }
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var selectedId = (MapGrid.SelectedItem as MapPickerItemVm)?.MapId;
        ApplyFilter(SearchBox.Text);
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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (MapGrid.SelectedItem is MapPickerItemVm item)
        {
            SelectedMapId = item.MapId;
            DialogResult = true;
        }
    }

    private void MapGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MapGrid.SelectedItem is MapPickerItemVm)
            Ok_Click(sender, e);
    }
}
