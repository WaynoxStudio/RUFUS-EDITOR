using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

namespace RufusMapEditor.App;

public partial class ItemPickerWindow : Window
{
    private readonly VisualLibraryService _lib;
    private readonly ObservableCollection<ItemPickerRowVm> _visible = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly ItemIconMissingPreviewSyncService _sync = new();
    private string _query = "";
    private int? _typeFilter;
    private bool _syncStarted;

    public ItemCatalogEntry? SelectedEntry { get; private set; }
    public int SelectedQuantity { get; private set; } = 1;

    public ItemPickerWindow(VisualLibraryService library, string? initialQuery = null, int initialQty = 1)
    {
        InitializeComponent();
        _lib = library;
        ItemIconPreviewService.Shared.EnsureConfigured();
        if (!string.IsNullOrWhiteSpace(library.ClipsRoot))
            ItemIconPreviewService.Shared.Configure(ItemIconPreviewService.Shared.LibraryRoot, library.ClipsRoot);
        ItemIconPreviewService.Shared.RefreshCollisionIndex(library.Items);
        ResultList.ItemsSource = _visible;
        StatusText.Text = library.StatusItems;
        QtyBox.Text = initialQty > 0 ? initialQty.ToString(CultureInfo.InvariantCulture) : "1";

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

        _sync.PreviewCompleted += OnPreviewCompleted;
        Closed += (_, _) =>
        {
            _sync.PreviewCompleted -= OnPreviewCompleted;
            _sync.CancelPending();
            _sync.Dispose();
        };

        RefreshList();
        BeginMissingPreviewSync();
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

        // Prioritize currently filtered/visible missing previews.
        EnqueueVisibleMissing(highPriority: true);
    }

    private void BeginMissingPreviewSync()
    {
        var clips = ItemIconPreviewService.Shared.ClipsRoot ?? _lib.ClipsRoot;
        var clipsValid = ClipsRootPaths.Validate(clips).IsValid;
        if (!clipsValid)
        {
            RufusLog.Info("ItemPicker auto-sync: ClipsRoot inválido — solo previews existentes.");
            return;
        }

        ItemIconPreviewService.Shared.EnsureConfigured();
        var libraryRoot = ItemIconPreviewService.Shared.LibraryRoot
                          ?? RufusLibraryPaths.TryResolveEffectiveLibrary(out _);
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            RufusLog.Warn("ItemPicker auto-sync: Library no resuelta.");
            return;
        }

        EnqueueVisibleMissing(highPriority: true);
        // Low priority: remaining catalog missing (deduped by PreviewKey inside service).
        _sync.EnqueueMissing(_lib.Items, ItemIconPreviewService.Shared, clips, highPriority: false);

        if (!_syncStarted)
        {
            _syncStarted = true;
            _sync.Start(libraryRoot, clips!);
        }
    }

    private void EnqueueVisibleMissing(bool highPriority)
    {
        var clips = ItemIconPreviewService.Shared.ClipsRoot ?? _lib.ClipsRoot;
        if (!ClipsRootPaths.Validate(clips).IsValid)
            return;
        _sync.EnqueueMissing(_visible.Select(r => r.Entry), ItemIconPreviewService.Shared, clips, highPriority);
    }

    private void OnPreviewCompleted(ItemIconMissingPreviewSyncService.ItemIconPreviewSyncResult result)
    {
        if (!result.Success)
            return;

        void Apply()
        {
            foreach (var row in _visible)
            {
                var key = ItemIconPreviewService.Shared.ResolvePreviewKey(row.GfxId, row.TypeId);
                if (key is null)
                    continue;
                if (!string.Equals(key.Value.FileStem, result.PreviewStem, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key.Value.Key, result.PreviewKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                row.NotifyPreviewReady();
            }
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.BeginInvoke(Apply, DispatcherPriority.Background);
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

public sealed class ItemPickerRowVm : INotifyPropertyChanged
{
    private bool _hasPreparedIcon;
    private bool _showMissingIconHint;
    private string _missingIconHint = "";
    private int _iconReloadToken;

    public ItemPickerRowVm(ItemCatalogEntry entry)
    {
        Entry = entry;
        GfxId = entry.GfxId;
        TypeId = entry.TypeId;
        Title = entry.Nombre;
        Subtitle =
            $"Item ID: {entry.ItemId} · Nivel {entry.Level} · {entry.Category} · GFX: {entry.GfxId}";

        RefreshIconFlags();

        var resolved = ItemIconPreviewService.Shared.ResolvePreview(entry.GfxId, entry.TypeId);
        var pngPath = resolved.FullPath ?? "(sin PNG)";
        var previewKey = resolved.PreviewKey?.Key ?? entry.IconRelativePath;
        ToolTipText =
            $"Item ID: {entry.ItemId}\n" +
            $"GFX: {entry.GfxId}\n" +
            $"Type ID: {entry.TypeId}\n" +
            $"SWF: {previewKey}\n" +
            $"PNG: {pngPath}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ItemCatalogEntry Entry { get; }
    public int GfxId { get; }
    public int TypeId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ToolTipText { get; }

    public bool HasPreparedIcon
    {
        get => _hasPreparedIcon;
        private set => SetField(ref _hasPreparedIcon, value);
    }

    public bool ShowMissingIconHint
    {
        get => _showMissingIconHint;
        private set => SetField(ref _showMissingIconHint, value);
    }

    public string MissingIconHint
    {
        get => _missingIconHint;
        private set => SetField(ref _missingIconHint, value);
    }

    /// <summary>Bumped when a generated preview becomes available so LazyItemIconImage reloads.</summary>
    public int IconReloadToken
    {
        get => _iconReloadToken;
        private set => SetField(ref _iconReloadToken, value);
    }

    public void NotifyPreviewReady()
    {
        RefreshIconFlags();
        IconReloadToken++;
    }

    private void RefreshIconFlags()
    {
        HasPreparedIcon = ItemIconPreviewService.Shared.HasPreview(Entry);
        ShowMissingIconHint = !HasPreparedIcon;
        MissingIconHint = ShowMissingIconHint ? "Icono no disponible" : "";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
