using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.ViewModels;

public sealed class WorldViewModel : ViewModelBase
{
    private readonly AstriaLibraryService _library;
    private readonly WorldEditorService _editor = new();
    private readonly WorldThumbnailCache _thumbs;
    private readonly MapPreviewCache _mapPreviews;
    private readonly MultiMapEditService _multiMap;
    private readonly WorldAutosaveStore _autosave = new();
    private readonly Func<MapDocument, string, Task> _openMapInEditor;
    private readonly Func<IReadOnlyList<int>> _libraryMapIds;
    private readonly Action<string> _setStatus;
    private MainViewModel? _editorHost;

    private WorldDocument? _world;
    private string _statusText = "Sin mundo";
    private string _hoverText = "";
    private bool _mosaicMode;
    private bool _showInfo = true;
    private bool _showMapBounds = true;
    private bool _showMapIds;
    private bool _showSeams;
    private bool _isMultiMapEditMode;
    private HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private string? _clipboardDocumentKey;

    public WorldViewModel(
        AstriaLibraryService library,
        WorldThumbnailCache thumbs,
        MapPreviewCache mapPreviews,
        MultiMapEditService multiMap,
        Func<MapDocument, string, Task> openMapInEditor,
        Func<IReadOnlyList<int>> libraryMapIds,
        Action<string> setStatus)
    {
        _library = library;
        _thumbs = thumbs;
        _mapPreviews = mapPreviews;
        _multiMap = multiMap;
        _openMapInEditor = openMapInEditor;
        _libraryMapIds = libraryMapIds;
        _setStatus = setStatus;

        _multiMap.ThumbnailInvalidateRequested += key => NotifyMapEdited(key);
        _multiMap.StateChanged += () =>
        {
            OnPropertyChanged(nameof(IsMultiMapEditMode));
            OnPropertyChanged(nameof(MultiMapStatusText));
            OnPropertyChanged(nameof(ModifiedMapCount));
            RequestOverlayRedraw?.Invoke();
        };

        NewWorldCommand = new RelayCommand(NewWorld);
        OpenWorldCommand = new RelayCommand(OpenWorld);
        ImportGeoCommand = new RelayCommand(ImportGeo, () => _library.IsLoaded);
        SaveWorldCommand = new RelayCommand(SaveWorld, () => _world is not null);
        SaveWorldAsCommand = new RelayCommand(SaveWorldAs, () => _world is not null);
        AddMapCommand = new RelayCommand(() => AddMapFromLibrary(), () => _world is not null && _library.IsLoaded);
        FitAllCommand = new RelayCommand(() => RequestFitAll?.Invoke());
        ToggleMosaicCommand = new RelayCommand(() => MosaicMode = !MosaicMode);
        DuplicateCommand = new RelayCommand(DuplicateSelected, () => HasSingleSelection);
        RemoveCommand = new RelayCommand(RemoveSelected, () => _selectedKeys.Count > 0);
        CopyCommand = new RelayCommand(CopySelected, () => HasSingleSelection);
        PasteCommand = new RelayCommand(PasteAtSelection, () => _clipboardDocumentKey is not null);
        EnterMultiMapEditCommand = new RelayCommand(EnterMultiMapEdit, CanEnterMultiMapEdit);
        ExitMultiMapEditCommand = new RelayCommand(() => ExitMultiMapEdit(), () => IsMultiMapEditMode);
        SaveModifiedMapsCommand = new RelayCommand(() => SaveModifiedMaps(), () => IsMultiMapEditMode && ModifiedMapCount > 0);
        ExpandGridCommand = new RelayCommand(p => ExpandGrid(ParseEdge(p)), _ => CanExpandGrid());
        ShrinkGridCommand = new RelayCommand(p => ShrinkGrid(ParseEdge(p)), p => CanShrinkGrid(ParseEdge(p)));
    }

    public event Action? RequestOverlayRedraw;

    public event Action? WorldChanged;
    public event Action? RequestRedraw;
    public event Action? RequestFitAll;

    public RelayCommand NewWorldCommand { get; }
    public RelayCommand OpenWorldCommand { get; }
    public RelayCommand ImportGeoCommand { get; }
    public RelayCommand SaveWorldCommand { get; }
    public RelayCommand SaveWorldAsCommand { get; }
    public RelayCommand AddMapCommand { get; }
    public RelayCommand FitAllCommand { get; }
    public RelayCommand ToggleMosaicCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand EnterMultiMapEditCommand { get; }
    public RelayCommand ExitMultiMapEditCommand { get; }
    public RelayCommand SaveModifiedMapsCommand { get; }
    public RelayCommand ExpandGridCommand { get; }
    public RelayCommand ShrinkGridCommand { get; }

    public MultiMapEditService MultiMap => _multiMap;
    public bool IsMultiMapEditMode => _isMultiMapEditMode;
    public int ModifiedMapCount => _multiMap.ModifiedMapCount;
    public string MultiMapStatusText => IsMultiMapEditMode
        ? $"EDICIÓN MULTIMAPA — {_selectedKeys.Count} mapa(s)"
        : "";

    public bool ShowMapBounds
    {
        get => _showMapBounds;
        set
        {
            if (!SetProperty(ref _showMapBounds, value)) return;
            RequestOverlayRedraw?.Invoke();
        }
    }

    public bool ShowMapIds
    {
        get => _showMapIds;
        set
        {
            if (!SetProperty(ref _showMapIds, value)) return;
            RequestOverlayRedraw?.Invoke();
        }
    }

    public bool ShowSeams
    {
        get => _showSeams;
        set
        {
            if (!SetProperty(ref _showSeams, value)) return;
            RequestOverlayRedraw?.Invoke();
        }
    }

    public WorldDocument? World => _world;
    public bool IsDirty => _world?.IsDirty == true;

    public bool MosaicMode
    {
        get => _mosaicMode;
        set
        {
            if (!SetProperty(ref _mosaicMode, value) || _world is null) return;
            _world.View.MosaicMode = value;
            TouchWorld();
            RequestRedraw?.Invoke();
        }
    }

    public bool ShowInfoOverlay
    {
        get => _showInfo;
        set
        {
            if (!SetProperty(ref _showInfo, value) || _world is null) return;
            _world.View.ShowInfoOverlay = value;
            RequestRedraw?.Invoke();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string HoverText
    {
        get => _hoverText;
        set => SetProperty(ref _hoverText, value);
    }

    public ObservableCollection<WorldTrayItemVm> TrayItems { get; } = new();

    public bool HasSingleSelection => _selectedKeys.Count == 1;
    public IReadOnlySet<string> SelectedKeys => _selectedKeys;

    public void MarkDirtyFromView()
    {
        if (_world is null) return;
        _world.IsDirty = true;
    }

    public void TryAutosave()
    {
        if (_world is null || !_world.IsDirty) return;
        try
        {
            var dto = RufworldSerializer.FromWorld(_world);
            var json = RufworldSerializer.Serialize(dto);
            _autosave.Write(_world.WorldId, json, new WorldAutosaveMeta
            {
                WorldId = _world.WorldId,
                WorldPath = _world.FilePath,
                DisplayName = _world.Name,
                SavedUtc = DateTimeOffset.UtcNow,
                HadWorldFile = !string.IsNullOrWhiteSpace(_world.FilePath),
            });
        }
        catch
        {
            /* ignore autosave failures */
        }
    }

    public MainViewModel? EditorHost => _editorHost;

    public void SetEditorHost(MainViewModel host) => _editorHost = host;

    public void DispatchMultiMapCellClick(WorldCellRef cell, bool isDrag, bool ctrl) =>
        _editorHost?.HandleMultiMapCellClick(cell, isDrag, ctrl);

    public void DispatchMultiMapBeginStroke() => _editorHost?.BeginMultiMapStroke();
    public void DispatchMultiMapFinishStroke() => _editorHost?.FinishMultiMapStroke();
    public void DispatchMultiMapContinueStroke(double wx, double wy) =>
        _editorHost?.ContinueMultiMapStroke(wx, wy);
    public void DispatchMultiMapBeginRectSelect(double wx, double wy) => _editorHost?.BeginMultiMapRectSelect(wx, wy);
    public void DispatchMultiMapUpdateRectSelect(double wx, double wy) => _editorHost?.UpdateMultiMapRectSelect(wx, wy);
    public void DispatchMultiMapEndRectSelect(double wx, double wy) => _editorHost?.EndMultiMapRectSelect(wx, wy);
    public void DispatchMultiMapHover(double wx, double wy) => _editorHost?.UpdateMultiMapHover(wx, wy);
    public void DispatchMultiMapClearHover() => _editorHost?.ClearMultiMapHover();
    public void DispatchMultiMapCopy() => _editorHost?.CopyMultiMapSelection();
    public void DispatchMultiMapPaste(WorldCellHit dest) => _editorHost?.PasteMultiMapAt(dest);
    public void DispatchMultiMapDelete() => _editorHost?.DeleteMultiMapSelection();
    public bool DispatchMultiMapReplace(int find, int replace) => _editorHost?.ApplyMultiMapReplace(find, replace) == true;

    public void NewWorld()
    {
        if (!ConfirmDiscard()) return;

        var dlg = new WorldGridSizeWindow { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        ExitMultiMapEdit(force: true);
        SetWorld(_editor.CreateNew(
            gridWidth: dlg.ResultGridWidth,
            gridHeight: dlg.ResultGridHeight,
            originX: dlg.ResultOriginX,
            originY: dlg.ResultOriginY));
        _selectedKeys.Clear();
        _mosaicMode = false;
        _showInfo = true;
        OnPropertyChanged(nameof(MosaicMode));
        OnPropertyChanged(nameof(ShowInfoOverlay));
        SyncTray();
        StatusText = $"Nuevo mundo ({dlg.ResultGridWidth}×{dlg.ResultGridHeight})";
        WorldChanged?.Invoke();
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
    }

    public void OpenWorld()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = $"Mundo RUFUS (*{RufworldFormat.FileExtension})|*{RufworldFormat.FileExtension}",
            Title = "Abrir mundo",
        };
        if (dlg.ShowDialog() != true) return;
        LoadWorldFromPath(dlg.FileName);
    }

    public void LoadWorldFromPath(string path)
    {
        try
        {
            var json = RufworldIo.LoadFile(path);
            SetWorld(RufworldSerializer.ToWorld(RufworldSerializer.Deserialize(json)));
            _world!.FilePath = path;
            _world.IsDirty = false;
            _mosaicMode = _world.View.MosaicMode;
            _showInfo = _world.View.ShowInfoOverlay;
            OnPropertyChanged(nameof(MosaicMode));
            OnPropertyChanged(nameof(ShowInfoOverlay));
            _selectedKeys.Clear();
            SyncTray();
            StatusText = Path.GetFileName(path);
            _autosave.Delete(_world.WorldId);
            WorldChanged?.Invoke();
            RequestRedraw?.Invoke();
            RequestFitAll?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "Mundo");
        }
    }

    public void ImportGeo()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Géoposition legado (*.geo)|*.geo",
            Title = "Importar .geo (solo lectura)",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var imported = AstriaGeoImporter.Import(dlg.FileName);
            SetWorld(_editor.CreateNew(imported.IslandName));
            var reserved = new HashSet<int>(_libraryMapIds());

            foreach (var cell in imported.Cells)
            {
                if (_world.Documents.Values.Any(e => e.Document.Id == cell.MapId))
                    continue;
                if (reserved.Contains(cell.MapId) && !_library.IsLoaded)
                    continue;

                MapDocument doc;
                try
                {
                    doc = _library.LoadMapDocument(cell.MapId);
                }
                catch
                {
                    continue;
                }

                var key = _editor.AddDocument(_world, doc, WorldMapOrigin.Imported, WorldMapPublicationState.FromLibrary);
                _editor.PlaceAt(_world, key, cell.X, cell.Y);
            }

            SyncTray();
            StatusText = $"Importado: {imported.IslandName} ({imported.Cells.Count} celdas)";
            WorldChanged?.Invoke();
            RequestRedraw?.Invoke();
            RequestFitAll?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Importación fallida:\n{ex.Message}", "Mundo");
        }
    }

    public void SaveWorld()
    {
        if (_world is null) return;
        if (string.IsNullOrWhiteSpace(_world.FilePath))
        {
            SaveWorldAs();
            return;
        }

        WriteWorld(_world.FilePath);
    }

    public void SaveWorldAs()
    {
        if (_world is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = $"Mundo RUFUS (*{RufworldFormat.FileExtension})|*{RufworldFormat.FileExtension}",
            FileName = (_world.Name ?? "mundo") + RufworldFormat.FileExtension,
        };
        if (dlg.ShowDialog() != true) return;
        _world.FilePath = dlg.FileName;
        WriteWorld(dlg.FileName);
    }

    private void WriteWorld(string path)
    {
        if (_world is null) return;
        var dto = RufworldSerializer.FromWorld(_world);
        RufworldIo.SaveAtomic(path, RufworldSerializer.Serialize(dto));
        _world.IsDirty = false;
        _autosave.Delete(_world.WorldId);
        StatusText = $"Guardado: {Path.GetFileName(path)}";
        _setStatus(StatusText);
    }

    public void AddMapFromLibrary(int? worldX = null, int? worldY = null)
    {
        if (_world is null || !_library.IsLoaded) return;
        var ids = _library.DiscoverMapIds();
        if (ids.Count == 0) return;
        var pick = new MapPickerWindow(
            _library,
            _mapPreviews,
            ids,
            ids[0],
            title: "Selección de mapa",
            prompt: "Selecciona un mapa para añadir al mundo:") { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() != true || pick.SelectedMapId is not int mapId) return;

        if (_world.Placements.Any(p =>
                _world.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == mapId))
        {
            MessageBox.Show($"Map ID {mapId} ya está colocado en este mundo.", "Mundo");
            return;
        }

        var doc = _library.LoadMapDocument(mapId);
        var key = _editor.AddDocument(_world, doc, WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        var pos = worldX is int x && worldY is int y ? (X: x, Y: y) : FindFreeOrigin();
        if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, pos.X, pos.Y))
        {
            MessageBox.Show("La posición está fuera de la cuadrícula del mundo.", "Mundo");
            _world.UnplacedDocumentKeys.Add(key);
            SyncTray();
            return;
        }

        _editor.PlaceAt(_world, key, pos.X, pos.Y);
        SyncTray();
        SelectKey(key);
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
    }

    public bool IsCellEmpty(int x, int y) =>
        _world is not null && _editor.FindPlacementAt(_world, x, y) is null;

    public bool CanPlaceAt(int x, int y) =>
        _world is not null && (!_world.HasGrid || WorldGeometry.IsInGrid(_world, x, y));

    private void SetWorld(WorldDocument? world)
    {
        _world = world;
        OnPropertyChanged(nameof(World));
        OnPropertyChanged(nameof(IsDirty));
        SaveWorldCommand.RaiseCanExecuteChanged();
        SaveWorldAsCommand.RaiseCanExecuteChanged();
        AddMapCommand.RaiseCanExecuteChanged();
    }

    public void PlaceExistingAt(string documentKey, int x, int y)
    {
        if (_world is null) return;
        if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, x, y))
        {
            MessageBox.Show("La posición está fuera de la cuadrícula del mundo.", "Mundo");
            return;
        }

        var result = _editor.PlaceAt(_world, documentKey, x, y);
        if (result == WorldMoveResult.Occupied)
        {
            var swap = MessageBox.Show(
                "Posición ocupada. ¿Intercambiar mapas?",
                "Mover mapa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (swap == MessageBoxResult.Yes)
                _editor.PlaceAt(_world, documentKey, x, y, swapIfOccupied: true);
            else
                return;
        }

        SyncTray();
        RequestRedraw?.Invoke();
    }


    public bool TryGetPlacementCoords(string documentKey, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_world is null) return false;
        var placement = _world.Placements.FirstOrDefault(p => p.DocumentKey == documentKey);
        if (placement is null) return false;
        x = placement.WorldX;
        y = placement.WorldY;
        return true;
    }

    /// <summary>Opens a dialog to type new world X/Y for a placed map.</summary>
    public void PromptChangeCoordinates(string? documentKey = null)
    {
        if (_world is null) return;
        var key = documentKey ?? (_selectedKeys.Count == 1 ? _selectedKeys.First() : null);
        if (key is null) return;
        if (!TryGetPlacementCoords(key, out var curX, out var curY)) return;

        var mapId = _world.Documents.TryGetValue(key, out var entry) ? entry.Document.Id : 0;
        var dlg = new WorldCoordInputWindow(
            $"Mapa {mapId} — introduce las nuevas coordenadas del mundo:",
            curX,
            curY)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true || dlg.ResultX is not int x || dlg.ResultY is not int y)
            return;
        if (x == curX && y == curY) return;
        PlaceExistingAt(key, x, y);
        SelectKey(key);
    }


    public void DuplicateSelected()
    {
        if (_world is null || !HasSingleSelection) return;
        DuplicateFromKey(_selectedKeys.First(), promptForId: true);
    }

    public void DuplicateFromKey(string sourceKey, bool promptForId = false)
    {
        if (_world is null) return;
        try
        {
            var reserved = _editor.CollectReservedMapIds(_world, _libraryMapIds());
            var sourceId = _world.Documents[sourceKey].Document.Id;
            var proposed = new LocalMapIdAllocator().ProposeNextId(sourceId, reserved);
            int newId;
            if (promptForId)
            {
                var dlg = new MapIdInputWindow(
                    $"Duplicar mapa {sourceId}\nNuevo Map ID local (pendiente validar BD):",
                    proposed)
                { Owner = Application.Current.MainWindow };
                if (dlg.ShowDialog() != true || dlg.ResultMapId is not int picked)
                    return;
                newId = picked;
                if (!new LocalMapIdAllocator().IsAvailable(newId, reserved))
                {
                    MessageBox.Show($"Map ID {newId} ya está en uso localmente.");
                    return;
                }
            }
            else
            {
                newId = proposed;
            }

            var dup = _editor.DuplicateMap(_world, sourceKey, newId);
            SyncTray();
            SelectKey(dup.DocumentKey);
            RequestRedraw?.Invoke();
            StatusText = $"Duplicado → ID {dup.MapId} (local, BD pendiente)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Duplicar mapa");
        }
    }

    public void RemoveSelected()
    {
        if (_world is null || _selectedKeys.Count == 0) return;
        if (MessageBox.Show(
                $"Quitar {_selectedKeys.Count} mapa(s) del mundo?\n(No borra archivos)",
                "Quitar del mundo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var key in _selectedKeys.ToList())
            _editor.RemoveFromWorld(_world, key);
        _selectedKeys.Clear();
        SyncTray();
        RequestRedraw?.Invoke();
    }

    /// <summary>Removes a single placed map from the world (moves it to the tray).</summary>
    public void RemoveMap(string documentKey)
    {
        if (_world is null || string.IsNullOrEmpty(documentKey)) return;
        if (!_world.Documents.TryGetValue(documentKey, out var entry)) return;
        if (_world.Placements.All(p => p.DocumentKey != documentKey)) return;

        if (MessageBox.Show(
                $"Quitar el mapa {entry.Document.Id} del mundo?\n(No borra archivos)",
                "Quitar del mundo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _editor.RemoveFromWorld(_world, documentKey);
        _selectedKeys.Remove(documentKey);
        SyncTray();
        RequestRedraw?.Invoke();
    }

    public void CopySelected()
    {
        if (!HasSingleSelection) return;
        _clipboardDocumentKey = _selectedKeys.First();
        StatusText = "Mapa copiado (duplicará contenido al pegar)";
    }

    public void PasteAtSelection() => PasteAtWorldCell(null);

    public void PasteAtWorldCell((int X, int Y)? cell)
    {
        if (_world is null || _clipboardDocumentKey is null) return;
        var pos = cell ?? FindFreeOrigin();
        DuplicateFromKey(_clipboardDocumentKey, promptForId: false);
        if (_selectedKeys.Count == 1)
            PlaceExistingAt(_selectedKeys.First(), pos.X, pos.Y);
    }

    public async Task OpenSelectedMapAsync()
    {
        if (_world is null || !HasSingleSelection) return;
        var key = _selectedKeys.First();
        if (!_world.Documents.TryGetValue(key, out var entry)) return;
        await _openMapInEditor(entry.Document, key);
    }

    public void NotifyMapEdited(string documentKey)
    {
        if (_world?.Documents.ContainsKey(documentKey) == true)
            _editor.MarkMapDocumentEdited(_world, documentKey);
        InvalidateThumbnail(documentKey);
    }

    public void NotifyMultiMapEdited(string documentKey) => NotifyMapEdited(documentKey);

    public void InvalidateThumbnail(string documentKey)
    {
        if (_world?.Documents.TryGetValue(documentKey, out var entry) == true)
            _thumbs.Invalidate(entry.Document);
        RequestRedraw?.Invoke();
    }

    public ImageSource? GetThumbnail(string documentKey, MapRenderOptions? renderOptions = null)
    {
        if (_world?.Documents.TryGetValue(documentKey, out var entry) != true)
            return null;
        return _thumbs.GetOrRender(_library, entry.Document, renderOptions);
    }

    public void EnterMultiMapEdit()
    {
        if (_world is null || _selectedKeys.Count == 0) return;
        MosaicMode = true;
        ShowInfoOverlay = false;
        if (!_multiMap.Enter(_world, _selectedKeys)) return;
        _isMultiMapEditMode = true;
        OnPropertyChanged(nameof(IsMultiMapEditMode));
        OnPropertyChanged(nameof(MultiMapStatusText));
        StatusText = MultiMapStatusText;
        RequestRedraw?.Invoke();
        RequestOverlayRedraw?.Invoke();
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        ExitMultiMapEditCommand.RaiseCanExecuteChanged();
        SaveModifiedMapsCommand.RaiseCanExecuteChanged();
    }

    public void ExitMultiMapEdit(bool force = false)
    {
        if (!_isMultiMapEditMode) return;
        if (!force && !_multiMap.Exit(confirmDiscard: true)) return;
        _multiMap.Exit(confirmDiscard: false);
        _isMultiMapEditMode = false;
        OnPropertyChanged(nameof(IsMultiMapEditMode));
        OnPropertyChanged(nameof(MultiMapStatusText));
        StatusText = _world is null ? "Sin mundo" : StatusText;
        RequestRedraw?.Invoke();
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        ExitMultiMapEditCommand.RaiseCanExecuteChanged();
        SaveModifiedMapsCommand.RaiseCanExecuteChanged();
    }

    public void SaveModifiedMaps()
    {
        var n = _multiMap.SaveModifiedMaps();
        if (n > 0)
        {
            StatusText = $"Guardados {n} mapa(s) modificados";
            _setStatus(StatusText);
            TouchWorld();
        }
        SaveModifiedMapsCommand.RaiseCanExecuteChanged();
    }

    private bool CanEnterMultiMapEdit() =>
        _world is not null && _selectedKeys.Count > 0 && !IsMultiMapEditMode;

    public IEnumerable<(WorldMapPlacement Placement, WorldMapEntry Entry)> EnumerateAllPlaced()
    {
        if (_world is null) yield break;
        foreach (var p in _world.Placements)
        {
            if (_world.Documents.TryGetValue(p.DocumentKey, out var entry))
                yield return (p, entry);
        }
    }

    public IEnumerable<(WorldMapPlacement Placement, WorldMapEntry Entry)> EnumeratePlaced() =>
        EnumerateAllPlaced();

    public void SelectKey(string key, bool additive = false)
    {
        if (!additive)
            _selectedKeys = new HashSet<string>(StringComparer.Ordinal) { key };
        else if (!_selectedKeys.Add(key))
            _selectedKeys.Remove(key);
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        RequestRedraw?.Invoke();
    }

    public void SelectInRect(double x0, double y0, double x1, double y1)
    {
        if (_world is null || IsMultiMapEditMode) return;
        _selectedKeys.Clear();
        var mosaic = _mosaicMode;
        foreach (var (p, entry) in EnumeratePlaced())
        {
            var (rx, ry, w, h) = WorldGeometry.GetMapRect(p.WorldX, p.WorldY, entry.Document, mosaic);
            if (rx + w >= x0 && rx <= x1 && ry + h >= y0 && ry <= y1)
                _selectedKeys.Add(entry.Key);
        }

        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        RequestRedraw?.Invoke();
    }

    public void ClearSelection()
    {
        _selectedKeys.Clear();
        OnPropertyChanged(nameof(HasSingleSelection));
        RequestRedraw?.Invoke();
    }

    public string? HitTestDocumentKey(double worldX, double worldY)
    {
        if (_world is null) return null;
        var entries = _world.Placements
            .Where(p => _world.Documents.ContainsKey(p.DocumentKey))
            .Select(p => (p.WorldX, p.WorldY, _world.Documents[p.DocumentKey].Document));
        var hit = WorldGeometry.HitTestWorldCell(worldX, worldY, entries, _mosaicMode);
        return hit is null ? null : _editor.FindDocumentKeyAt(_world, hit.Value.WorldX, hit.Value.WorldY);
    }

    public (int X, int Y)? HitTestGridCell(double worldX, double worldY)
    {
        if (_world is null || !_world.HasGrid) return null;
        return WorldGeometry.HitTestGridSlot(worldX, worldY, _world, _mosaicMode);
    }

    public bool ConfirmDiscard()
    {
        if (_world is not { IsDirty: true }) return true;
        return MessageBox.Show(
            "El mundo tiene cambios sin guardar. ¿Continuar?",
            "Mundo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public void PlaceTrayItem(string key)
    {
        if (_world is null) return;
        var pos = FindFreeOrigin();
        PlaceExistingAt(key, pos.X, pos.Y);
    }

    private (int X, int Y) FindFreeOrigin()
    {
        if (_world is null) return (0, 0);
        var occ = _editor.OccupiedCells(_world);

        if (_world.HasGrid)
        {
            foreach (var (x, y) in WorldGeometry.EnumerateGridCells(_world))
            {
                if (!occ.Contains((x, y)))
                    return (x, y);
            }

            return (_world.OriginX, _world.OriginY);
        }

        for (var r = 0; r <= 30; r++)
        {
            for (var x = -r; x <= r; x++)
            for (var y = -r; y <= r; y++)
            {
                if (Math.Abs(x) != r && Math.Abs(y) != r) continue;
                if (!occ.Contains((x, y)))
                    return (x, y);
            }
        }

        return (0, 0);
    }

    private void SyncTray()
    {
        TrayItems.Clear();
        if (_world is null) return;
        foreach (var key in _world.UnplacedDocumentKeys)
        {
            if (!_world.Documents.TryGetValue(key, out var entry)) continue;
            TrayItems.Add(new WorldTrayItemVm(key, entry.Document.Id, entry.PublicationState));
        }
    }

    private void TouchWorld()
    {
        if (_world is null) return;
        _world.IsDirty = true;
        _world.ModifiedUtc = DateTimeOffset.UtcNow;
    }

    public bool CanExpandGrid() =>
        _world is not null && _world.HasGrid && !IsMultiMapEditMode;

    public bool CanShrinkGrid(WorldGridEdge edge) =>
        _world is not null && !IsMultiMapEditMode && _editor.CanShrinkGrid(_world, edge);

    public void ExpandGrid(WorldGridEdge edge)
    {
        if (_world is null) return;
        if (_editor.ExpandGrid(_world, edge) != WorldGridResizeResult.Ok) return;
        StatusText = $"Cuadrícula {_world.GridWidth}×{_world.GridHeight}";
        SyncTray();
        ExpandGridCommand.RaiseCanExecuteChanged();
        ShrinkGridCommand.RaiseCanExecuteChanged();
        RequestRedraw?.Invoke();
    }

    public void ShrinkGrid(WorldGridEdge edge)
    {
        if (_world is null || !CanShrinkGrid(edge)) return;

        var onEdge = _editor.CountPlacementsOnEdge(_world, edge);
        if (onEdge > 0)
        {
            var kind = edge is WorldGridEdge.East or WorldGridEdge.West ? "columna" : "fila";
            var confirm = MessageBox.Show(
                $"Hay {onEdge} mapa(s) en esa {kind}. Se moverán a la bandeja.\n¿Continuar?",
                "Quitar " + kind,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        if (_editor.ShrinkGrid(_world, edge, out var removed) != WorldGridResizeResult.Ok) return;
        StatusText = removed.Count > 0
            ? $"Cuadrícula {_world.GridWidth}×{_world.GridHeight} ({removed.Count} mapa(s) a bandeja)"
            : $"Cuadrícula {_world.GridWidth}×{_world.GridHeight}";
        SyncTray();
        ExpandGridCommand.RaiseCanExecuteChanged();
        ShrinkGridCommand.RaiseCanExecuteChanged();
        RequestRedraw?.Invoke();
    }

    private static WorldGridEdge ParseEdge(object? parameter) =>
        parameter switch
        {
            WorldGridEdge e => e,
            string s when Enum.TryParse<WorldGridEdge>(s, ignoreCase: true, out var e) => e,
            _ => WorldGridEdge.East,
        };

    public sealed record WorldTrayItemVm(string Key, int MapId, WorldMapPublicationState State);
}
