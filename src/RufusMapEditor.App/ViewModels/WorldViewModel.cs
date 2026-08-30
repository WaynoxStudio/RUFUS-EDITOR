using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Editing;
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
    private readonly Stack<(WorldLayoutSnapshot Snap, long Seq)> _worldUndo = new();
    private readonly Stack<(WorldLayoutSnapshot Snap, long Seq)> _worldRedo = new();
    private bool _suppressWorldUndo;
    private bool _showInfo = true;
    private bool _showMapBounds = true;
    private bool _showMapIds;
    private bool _showSeams;
    private bool _isMultiMapEditMode;
    private HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private string? _clipboardDocumentKey;
    private readonly MapPickerFilterState _mapPickerFilter;
    private int _lastAdjacentDx;
    private int _lastAdjacentDy;

    public WorldViewModel(
        AstriaLibraryService library,
        WorldThumbnailCache thumbs,
        MapPreviewCache mapPreviews,
        MultiMapEditService multiMap,
        Func<MapDocument, string, Task> openMapInEditor,
        Func<IReadOnlyList<int>> libraryMapIds,
        Action<string> setStatus,
        MapPickerFilterState? mapPickerFilter = null)
    {
        _library = library;
        _thumbs = thumbs;
        _mapPreviews = mapPreviews;
        _multiMap = multiMap;
        _openMapInEditor = openMapInEditor;
        _libraryMapIds = libraryMapIds;
        _setStatus = setStatus;
        _mapPickerFilter = mapPickerFilter ?? new MapPickerFilterState();

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
        FindDuplicateMapIdsCommand = new RelayCommand(FindDuplicateMapIds, () => _world is not null);
        UndoWorldCommand = new RelayCommand(UndoWorld, () => _worldUndo.Count > 0);
        RedoWorldCommand = new RelayCommand(RedoWorld, () => _worldRedo.Count > 0);
        InsertRowAboveSelectionCommand = new RelayCommand(
            () => InsertRowRelative(above: true),
            () => CanInsertDeleteAtSelection());
        InsertRowBelowSelectionCommand = new RelayCommand(
            () => InsertRowRelative(above: false),
            () => CanInsertDeleteAtSelection());
        InsertColumnLeftSelectionCommand = new RelayCommand(
            () => InsertColumnRelative(left: true),
            () => CanInsertDeleteAtSelection());
        InsertColumnRightSelectionCommand = new RelayCommand(
            () => InsertColumnRelative(left: false),
            () => CanInsertDeleteAtSelection());
        DeleteRowAtSelectionCommand = new RelayCommand(DeleteRowAtSelection, () => CanInsertDeleteAtSelection());
        DeleteColumnAtSelectionCommand = new RelayCommand(DeleteColumnAtSelection, () => CanInsertDeleteAtSelection());
    }

    public event Action? RequestOverlayRedraw;

    public event Action? WorldChanged;
    public event Action? RequestRedraw;
    public event Action? RequestFitAll;

    /// <summary>Dirty flag / label / path changed — floating window title.</summary>
    public event Action? PresentationChanged;

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
    public RelayCommand FindDuplicateMapIdsCommand { get; }
    public RelayCommand UndoWorldCommand { get; }
    public RelayCommand RedoWorldCommand { get; }
    public RelayCommand InsertRowAboveSelectionCommand { get; }
    public RelayCommand InsertRowBelowSelectionCommand { get; }
    public RelayCommand InsertColumnLeftSelectionCommand { get; }
    public RelayCommand InsertColumnRightSelectionCommand { get; }
    public RelayCommand DeleteRowAtSelectionCommand { get; }
    public RelayCommand DeleteColumnAtSelectionCommand { get; }

    public string UndoWorldLabel => _worldUndo.Count > 0 ? "Deshacer (mundo)" : "Deshacer";
    public string RedoWorldLabel => _worldRedo.Count > 0 ? "Rehacer (mundo)" : "Rehacer";
    public bool CanUndoWorld => _worldUndo.Count > 0;
    public bool CanRedoWorld => _worldRedo.Count > 0;
    public long TopLayoutUndoSequence => _worldUndo.Count > 0 ? _worldUndo.Peek().Seq : long.MinValue;
    public long TopLayoutRedoSequence => _worldRedo.Count > 0 ? _worldRedo.Peek().Seq : long.MinValue;

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

    /// <summary>True when this VM is the MAPA combinado mosaic, not a MUNDO project.</summary>
    public bool IsScratchCombined { get; private set; }

    /// <summary>{Library}/Géopositions — carpeta de proyectos de mundo (paridad Astria).</summary>
    public string? GeopositionsPath =>
        string.IsNullOrWhiteSpace(_library.RootPath)
            ? null
            : GeopositionsStore.GetRoot(_library.RootPath);

    public string CurrentWorldLabel =>
        _world is null
            ? "Sin mundo abierto"
            : string.IsNullOrWhiteSpace(_world.FilePath)
                ? $"{_world.Name} (sin guardar)"
                : _world.Name;

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

    public void NotifyLibraryRootChanged()
    {
        OnPropertyChanged(nameof(GeopositionsPath));
        ImportGeoCommand.RaiseCanExecuteChanged();
    }

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

    public void DispatchMultiMapCellClick(
        WorldCellRef cell,
        bool isDrag,
        bool ctrl,
        double? mapLocalX = null,
        double? mapLocalY = null) =>
        _editorHost?.HandleMultiMapCellClick(cell, isDrag, ctrl, mapLocalX, mapLocalY);

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
        if (_editorHost is not null)
        {
            _editorHost.RequestNewWorldSession();
            return;
        }

        var dlg = new WorldGridSizeWindow { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        ApplyNewWorld(
            dlg.ResultGridWidth,
            dlg.ResultGridHeight,
            dlg.ResultOriginX,
            dlg.ResultOriginY);
    }

    public void OpenWorld()
    {
        if (_editorHost is not null)
        {
            _editorHost.RequestOpenWorldSession();
            return;
        }

        if (!TryGetLibraryRoot(out var libraryRoot))
            return;

        var geoRoot = GeopositionsStore.EnsureRoot(libraryRoot);
        var projects = GeopositionsStore.ListProjects(libraryRoot);
        var pick = new WorldProjectsWindow(geoRoot, projects) { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() != true || string.IsNullOrWhiteSpace(pick.SelectedPath))
            return;

        LoadWorldFromPath(pick.SelectedPath);
    }

    /// <summary>Creates a blank world in this VM (no discard prompt).</summary>
    public void ApplyNewWorld(int gridWidth, int gridHeight, int originX, int originY)
    {
        ExitMultiMapEdit(force: true);
        SetWorld(_editor.CreateNew(
            gridWidth: gridWidth,
            gridHeight: gridHeight,
            originX: originX,
            originY: originY));
        _selectedKeys.Clear();
        _mosaicMode = false;
        _showInfo = true;
        OnPropertyChanged(nameof(MosaicMode));
        OnPropertyChanged(nameof(ShowInfoOverlay));
        SyncTray();
        StatusText = $"Nuevo mundo ({gridWidth}×{gridHeight})";
        WorldChanged?.Invoke();
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
        NotifyPresentation();
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
            NotifyPresentation();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "Mundo");
        }
    }

    public void ImportGeo()
    {
        if (_editorHost is not null)
        {
            _editorHost.RequestImportGeoWorldSession();
            return;
        }

        var dlg = new OpenFileDialog
        {
            Filter = "Géoposition legado (*.geo)|*.geo",
            Title = "Importar .geo (solo lectura)",
        };
        if (dlg.ShowDialog() != true) return;
        ApplyImportGeo(dlg.FileName);
    }

    /// <summary>Imports a legacy .geo into this VM (no discard prompt).</summary>
    public void ApplyImportGeo(string fileName)
    {
        try
        {
            var imported = AstriaGeoImporter.Import(fileName);
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
            NotifyPresentation();
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
        if (!TryGetLibraryRoot(out var libraryRoot))
            return;

        var geoRoot = GeopositionsStore.EnsureRoot(libraryRoot);
        var suggested = string.IsNullOrWhiteSpace(_world.Name) || _world.Name == "Nuevo mundo"
            ? "Mundo"
            : _world.Name;
        var nameDlg = new WorldNameInputWindow(
            "Elige un nombre para este mundo (proyecto). Cada nombre es un proyecto distinto, como en Astria.",
            suggested,
            geoRoot) { Owner = Application.Current.MainWindow };
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.ResultName))
            return;

        var projectName = nameDlg.ResultName;
        var path = GeopositionsStore.ProjectFilePath(libraryRoot, projectName);
        if (File.Exists(path) &&
            !string.Equals(_world.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            var overwrite = MessageBox.Show(
                $"Ya existe el proyecto «{projectName}».\n\n¿Sobrescribir?",
                "Géopositions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        _world.Name = projectName;
        _world.FilePath = path;
        WriteWorld(path);
        OnPropertyChanged(nameof(GeopositionsPath));
        OnPropertyChanged(nameof(CurrentWorldLabel));
    }

    private void WriteWorld(string path)
    {
        if (_world is null) return;

        path = GeopositionsStore.EnsureProjectFolderLayout(path);
        _world.FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var dto = RufworldSerializer.FromWorld(_world);
        RufworldIo.SaveAtomic(path, RufworldSerializer.Serialize(dto));
        _world.IsDirty = false;
        _autosave.Delete(_world.WorldId);

        var pngNote = "";
        try
        {
            if (_library.IsLoaded)
            {
                WorldPreviewExporter.Export(_world, _library, path);
                pngNote = " + PNG";
            }
        }
        catch (Exception ex)
        {
            pngNote = $" (PNG: {ex.Message})";
        }

        StatusText = $"Guardado: {GeopositionsStore.FolderName}\\{Path.GetFileNameWithoutExtension(path)}\\{Path.GetFileName(path)}{pngNote}";
        _setStatus(StatusText);
        NotifyPresentation();
    }

    private bool TryGetLibraryRoot(out string libraryRoot)
    {
        libraryRoot = _library.RootPath ?? "";
        if (!string.IsNullOrWhiteSpace(libraryRoot))
            return true;

        MessageBox.Show(
            "Carga primero la biblioteca (Library) para usar la carpeta Géopositions.",
            "Mundo",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
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
            prompt: "Selecciona un mapa para añadir al mundo:",
            persistState: _mapPickerFilter) { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() != true || pick.SelectedMapId is not int mapId) return;

        if (_world.Placements.Any(p =>
                _world.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == mapId))
        {
            var positions = _world.Placements
                .Where(p => _world.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == mapId)
                .Select(p => $"({p.WorldX},{p.WorldY})")
                .ToList();
            MessageBox.Show(
                $"Aviso: Map ID {mapId} ya está colocado en este mundo.\n\nPosiciones actuales: {string.Join(", ", positions)}\n\nSe colocará otra copia igual.",
                "Mapa duplicado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // Continúa: el aviso no bloquea la colocación.
        }

        PushWorldUndo();
        MapDocument doc;
        try
        {
            doc = _library.LoadMapDocument(mapId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cargar el mapa {mapId}:\n{ex.Message}", "Mundo");
            return;
        }
        PlaceLoadedMapDocument(doc, worldX, worldY);
    }

    /// <summary>Coloca un mapa de Library en el mundo (p. ej. drag desde la lista MAPAS).</summary>
    public bool PlaceLibraryMapAt(int mapId, int? worldX = null, int? worldY = null)
    {
        if (_world is null || !_library.IsLoaded || mapId <= 0)
            return false;

        if (_world.Placements.Any(p =>
                _world.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == mapId))
        {
            var positions = _world.Placements
                .Where(p => _world.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == mapId)
                .Select(p => $"({p.WorldX},{p.WorldY})")
                .ToList();
            MessageBox.Show(
                $"Aviso: Map ID {mapId} ya está colocado en este mundo.\n\nPosiciones actuales: {string.Join(", ", positions)}\n\nSe colocará otra copia igual.",
                "Mapa duplicado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        PushWorldUndo();
        MapDocument doc;
        try
        {
            doc = _library.LoadMapDocument(mapId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cargar el mapa {mapId}:\n{ex.Message}", "Mundo");
            return false;
        }

        return PlaceLoadedMapDocument(doc, worldX, worldY);
    }

    private bool PlaceLoadedMapDocument(MapDocument doc, int? worldX, int? worldY)
    {
        if (_world is null) return false;
        var key = _editor.AddDocument(_world, doc, WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        var pos = worldX is int x && worldY is int y ? (X: x, Y: y) : FindFreeOrigin();
        if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, pos.X, pos.Y))
        {
            MessageBox.Show("La posición está fuera de la cuadrícula del mundo.", "Mundo");
            _world.UnplacedDocumentKeys.Add(key);
            SyncTray();
            return false;
        }

        _editor.PlaceAt(_world, key, pos.X, pos.Y);
        SyncTray();
        SelectKey(key);
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
        StatusText = $"Mapa {doc.Id} colocado en ({pos.X},{pos.Y})";
        return true;
    }

    public bool IsCellEmpty(int x, int y) =>
        _world is not null && _editor.FindPlacementAt(_world, x, y) is null;

    public bool CanPlaceAt(int x, int y) =>
        _world is not null &&
        (IsScratchCombined || !_world.HasGrid || WorldGeometry.IsInGrid(_world, x, y));

    private void SetWorld(WorldDocument? world)
    {
        IsScratchCombined = false;
        _world = world;
        _worldUndo.Clear();
        _worldRedo.Clear();
        RaiseWorldUndoRedo();
        OnPropertyChanged(nameof(World));
        OnPropertyChanged(nameof(GeopositionsPath));
        SaveWorldCommand.RaiseCanExecuteChanged();
        SaveWorldAsCommand.RaiseCanExecuteChanged();
        AddMapCommand.RaiseCanExecuteChanged();
        NotifyPresentation();
    }

    private sealed record WorldLayoutSnapshot(
        IReadOnlyList<(string Key, int X, int Y)> Placements,
        IReadOnlyList<string> UnplacedKeys,
        int OriginX,
        int OriginY,
        int GridWidth,
        int GridHeight);

    private void PushWorldUndo()
    {
        if (_suppressWorldUndo || _world is null) return;
        _worldUndo.Push((CaptureWorldLayout(), CombinedHistoryClock.Next()));
        _worldRedo.Clear();
        RaiseWorldUndoRedo();
    }

    private WorldLayoutSnapshot CaptureWorldLayout()
    {
        if (_world is null)
            return new WorldLayoutSnapshot(Array.Empty<(string, int, int)>(), Array.Empty<string>(), 0, 0, 0, 0);

        return new WorldLayoutSnapshot(
            _world.Placements.Select(p => (p.DocumentKey, p.WorldX, p.WorldY)).ToList(),
            _world.UnplacedDocumentKeys.ToList(),
            _world.OriginX,
            _world.OriginY,
            _world.GridWidth,
            _world.GridHeight);
    }

    private void RestoreWorldLayout(WorldLayoutSnapshot snap)
    {
        if (_world is null) return;
        _world.Placements.Clear();
        foreach (var (key, x, y) in snap.Placements)
        {
            if (!_world.Documents.ContainsKey(key)) continue;
            _world.Placements.Add(new WorldMapPlacement { DocumentKey = key, WorldX = x, WorldY = y });
        }

        _world.UnplacedDocumentKeys.Clear();
        foreach (var key in snap.UnplacedKeys)
        {
            if (_world.Documents.ContainsKey(key) && !_world.UnplacedDocumentKeys.Contains(key))
                _world.UnplacedDocumentKeys.Add(key);
        }

        // Documents that exist but are neither placed nor in snap unplaced → tray
        foreach (var key in _world.Documents.Keys)
        {
            if (_world.Placements.Any(p => p.DocumentKey == key)) continue;
            if (!_world.UnplacedDocumentKeys.Contains(key))
                _world.UnplacedDocumentKeys.Add(key);
        }

        _world.OriginX = snap.OriginX;
        _world.OriginY = snap.OriginY;
        _world.GridWidth = snap.GridWidth;
        _world.GridHeight = snap.GridHeight;
        TouchWorld();
        SyncTray();
        RequestRedraw?.Invoke();
    }

    private void UndoWorld()
    {
        if (_world is null || _worldUndo.Count == 0) return;
        var current = CaptureWorldLayout();
        var (snap, seq) = _worldUndo.Pop();
        _worldRedo.Push((current, seq));
        _suppressWorldUndo = true;
        try
        {
            RestoreWorldLayout(snap);
        }
        finally
        {
            _suppressWorldUndo = false;
        }

        StatusText = "Deshacer (mundo)";
        RaiseWorldUndoRedo();
    }

    private void RedoWorld()
    {
        if (_world is null || _worldRedo.Count == 0) return;
        var current = CaptureWorldLayout();
        var (snap, seq) = _worldRedo.Pop();
        _worldUndo.Push((current, seq));
        _suppressWorldUndo = true;
        try
        {
            RestoreWorldLayout(snap);
        }
        finally
        {
            _suppressWorldUndo = false;
        }

        StatusText = "Rehacer (mundo)";
        RaiseWorldUndoRedo();
    }

    private void RaiseWorldUndoRedo()
    {
        OnPropertyChanged(nameof(UndoWorldLabel));
        OnPropertyChanged(nameof(RedoWorldLabel));
        OnPropertyChanged(nameof(CanUndoWorld));
        OnPropertyChanged(nameof(CanRedoWorld));
        UndoWorldCommand.RaiseCanExecuteChanged();
        RedoWorldCommand.RaiseCanExecuteChanged();
        _editorHost?.NotifyMosaicUndoRedoChanged();
    }

    public void PlaceExistingAt(string documentKey, int x, int y, bool promptIfOccupied = true)
    {
        if (_world is null) return;
        if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, x, y))
        {
            MessageBox.Show("La posición está fuera de la cuadrícula del mundo.", "Mundo");
            return;
        }

        PushWorldUndo();
        var result = _editor.PlaceAt(_world, documentKey, x, y);
        if (result == WorldMoveResult.Occupied)
        {
            if (promptIfOccupied)
            {
                var swap = MessageBox.Show(
                    "Posición ocupada. ¿Intercambiar mapas?",
                    "Mover mapa",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (swap != MessageBoxResult.Yes)
                    return;
            }

            _editor.PlaceAt(_world, documentKey, x, y, swapIfOccupied: true);
        }

        SyncTray();
        RequestRedraw?.Invoke();
    }

    /// <summary>
    /// Moves all selected placements so that <paramref name="anchorKey"/> lands on (targetX, targetY).
    /// </summary>
    public void MoveSelectionAnchoredAt(string anchorKey, int targetX, int targetY)
    {
        if (_world is null) return;
        if (!TryGetPlacementCoords(anchorKey, out var ax, out var ay))
        {
            PlaceExistingAt(anchorKey, targetX, targetY, promptIfOccupied: false);
            return;
        }

        var dx = targetX - ax;
        var dy = targetY - ay;
        if (dx == 0 && dy == 0) return;

        var keys = _selectedKeys.Contains(anchorKey)
            ? _selectedKeys.ToList()
            : new List<string> { anchorKey };

        var moves = new List<(string Key, int X, int Y, int NX, int NY)>();
        foreach (var key in keys)
        {
            if (!TryGetPlacementCoords(key, out var x, out var y))
                continue;
            moves.Add((key, x, y, x + dx, y + dy));
        }

        if (moves.Count == 0) return;

        PushWorldUndo();

        foreach (var m in moves)
        {
            if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, m.NX, m.NY))
            {
                if (IsScratchCombined)
                {
                    ExpandGridToward(m.NX, m.NY);
                    continue;
                }

                if (_worldUndo.Count > 0) _worldUndo.Pop();
                RaiseWorldUndoRedo();
                MessageBox.Show(
                    $"El bloque saldría de la cuadrícula (destino ({m.NX},{m.NY})).",
                    "Mover mapas",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var movingKeys = moves.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        // Outsiders on destinations swap into the vacated cells of the movers.
        var displaced = new List<(string Key, int X, int Y)>();
        foreach (var m in moves)
        {
            var occ = _editor.FindPlacementAt(_world, m.NX, m.NY);
            if (occ is null || movingKeys.Contains(occ.DocumentKey))
                continue;
            displaced.Add((occ.DocumentKey, m.X, m.Y));
        }

        // Detach then reattach to avoid self-collisions while shifting.
        foreach (var m in moves)
            _world.Placements.RemoveAll(p => p.DocumentKey == m.Key);
        foreach (var d in displaced)
            _world.Placements.RemoveAll(p => p.DocumentKey == d.Key);

        foreach (var m in moves)
        {
            _world.Placements.Add(new WorldMapPlacement
            {
                DocumentKey = m.Key,
                WorldX = m.NX,
                WorldY = m.NY,
            });
        }

        foreach (var d in displaced)
        {
            _world.Placements.Add(new WorldMapPlacement
            {
                DocumentKey = d.Key,
                WorldX = d.X,
                WorldY = d.Y,
            });
        }

        if (IsScratchCombined)
            EnsureCombinedNeighborSlots();

        TouchWorld();
        SyncTray();
        StatusText = displaced.Count > 0
            ? (moves.Count == 1
                ? $"Mapas intercambiados → ({moves[0].NX},{moves[0].NY})"
                : $"{moves.Count} mapas movidos · {displaced.Count} intercambiados (Δ{dx},{dy})")
            : moves.Count == 1
                ? $"Mapa movido → ({moves[0].NX},{moves[0].NY})"
                : $"{moves.Count} mapas movidos (Δ{dx},{dy})";
        RequestRedraw?.Invoke();
        if (IsScratchCombined)
            _editorHost?.RefreshCombinedMapChips();
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
        var scratch = IsScratchCombined;
        var question = scratch
            ? $"¿Quitar {_selectedKeys.Count} mapa(s) del combinado?\nNo borra archivos."
            : $"Quitar {_selectedKeys.Count} mapa(s) del mundo?\n(No borra archivos)";
        var title = scratch ? "Quitar del combinado" : "Quitar del mundo";
        if (MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        PushWorldUndo();
        foreach (var key in _selectedKeys.ToList())
            _editor.RemoveFromWorld(_world, key);
        _selectedKeys.Clear();
        if (scratch)
            CompactCombinedPlacements();
        if (scratch && _world.Placements.Count > 0)
            SelectKey(_world.Placements[0].DocumentKey);
        SyncTray();
        RequestRedraw?.Invoke();
        if (scratch)
            _editorHost?.NotifyCombinedLayoutChanged();
    }

    /// <summary>Removes a single placed map from the world (moves it to the tray).</summary>
    public void RemoveMap(string documentKey)
    {
        if (_world is null || string.IsNullOrEmpty(documentKey)) return;
        if (!_world.Documents.TryGetValue(documentKey, out var entry)) return;
        if (_world.Placements.All(p => p.DocumentKey != documentKey)) return;

        var scratch = IsScratchCombined;
        var question = scratch
            ? $"¿Quitar el mapa {entry.Document.Id} del combinado?\nNo borra archivos."
            : $"Quitar el mapa {entry.Document.Id} del mundo?\n(No borra archivos)";
        var title = scratch ? "Quitar del combinado" : "Quitar del mundo";
        if (MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        PushWorldUndo();
        _editor.RemoveFromWorld(_world, documentKey);
        _selectedKeys.Remove(documentKey);
        if (scratch)
            CompactCombinedPlacements();
        if (scratch && _world.Placements.Count > 0)
            SelectKey(_world.Placements[0].DocumentKey);
        SyncTray();
        RequestRedraw?.Invoke();
        if (scratch)
            _editorHost?.NotifyCombinedLayoutChanged();
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

    public void RequestViewRedraw() => RequestRedraw?.Invoke();

    public void ReloadPlacedMapsFromLibrary(int mapId, Func<int, MapDocument> loadFresh)
    {
        if (_world is null || mapId <= 0) return;
        MapDocument? template = null;
        var any = false;
        foreach (var p in _world.Placements.ToList())
        {
            if (!_world.Documents.TryGetValue(p.DocumentKey, out var entry))
                continue;
            if (entry.Document.Id != mapId)
                continue;

            template ??= loadFresh(mapId);
            var copy = MapDocumentDuplicator.DeepCopy(template, mapId);
            entry.Document = copy;
            _thumbs.Invalidate(copy);
            any = true;
        }

        if (!any) return;
        TouchWorld();
        RequestRedraw?.Invoke();
        StatusText = $"Mapa {mapId} actualizado desde Library";
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
        _editorHost?.PrepareEnterMultiMapEdit(this);
        // Editable = all placed maps so painting works everywhere; yellow selection is independent.
        var editable = new HashSet<string>(
            _world.Placements.Select(p => p.DocumentKey),
            StringComparer.Ordinal);
        if (editable.Count == 0)
            editable = new HashSet<string>(_selectedKeys, StringComparer.Ordinal);

        MosaicMode = true;
        ShowInfoOverlay = false;
        if (!_multiMap.Enter(_world, editable)) return;
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

    /// <summary>Replaces the yellow map selection (ESTE ÍTEM / Guardar seleccionado scope).</summary>
    public void SetSelectedKeys(IEnumerable<string> keys)
    {
        _selectedKeys = new HashSet<string>(keys, StringComparer.Ordinal);
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        RaiseSelectionGridCommands();
        RequestRedraw?.Invoke();
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
        RaiseSelectionGridCommands();
        RequestRedraw?.Invoke();
    }

    /// <summary>Adds a map to the selection without clearing others (MAPA combinado multi-scope).</summary>
    public void EnsureKeySelected(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_selectedKeys.Add(key))
        {
            RequestRedraw?.Invoke();
            return;
        }

        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        RaiseSelectionGridCommands();
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
        RaiseSelectionGridCommands();
        RequestRedraw?.Invoke();
    }

    public void ClearSelection()
    {
        _selectedKeys.Clear();
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        RaiseSelectionGridCommands();
        RequestRedraw?.Invoke();
    }

    public string? HitTestDocumentKey(double worldX, double worldY)
    {
        if (_world is null) return null;
        var mosaic = _mosaicMode || IsMultiMapEditMode;
        var entries = _world.Placements
            .Where(p => _world.Documents.ContainsKey(p.DocumentKey))
            .Select(p => (p.WorldX, p.WorldY, _world.Documents[p.DocumentKey].Document));
        var hit = WorldGeometry.HitTestWorldCell(worldX, worldY, entries, mosaic);
        return hit is null ? null : _editor.FindDocumentKeyAt(_world, hit.Value.WorldX, hit.Value.WorldY);
    }

    public (int X, int Y)? HitTestGridCell(double worldX, double worldY)
    {
        if (_world is null || !_world.HasGrid) return null;
        var mosaic = _mosaicMode || IsMultiMapEditMode;
        return WorldGeometry.HitTestGridSlot(worldX, worldY, _world, mosaic);
    }

    /// <summary>
    /// Places open MAPA documents into a mosaic world grid (H or V).
    /// Shares the same <see cref="MapDocument"/> instances so saves stay independent per map file.
    /// Returns placed document keys.
    /// </summary>
    public IReadOnlyList<string> CombineFromDocuments(
        IReadOnlyList<MapDocument> maps,
        bool horizontal,
        bool replaceWorld,
        bool enterMultiMapEdit,
        bool scratchCombined = false)
    {
        if (maps.Count == 0)
            return Array.Empty<string>();

        if (replaceWorld || _world is null)
        {
            if (_world is not null && !IsScratchCombined && !ConfirmDiscard())
                return Array.Empty<string>();

            ExitMultiMapEdit(force: true);
            var gw = horizontal ? Math.Max(1, maps.Count) : 1;
            var gh = horizontal ? 1 : Math.Max(1, maps.Count);
            var label = maps.Count == 1
                ? $"Mapa {maps[0].Id}"
                : $"Combinado {maps[0].Id}…{maps[^1].Id}";
            SetWorld(_editor.CreateNew(label, gw, gh, originX: 0, originY: 0));
            IsScratchCombined = scratchCombined;
        }
        else if (_world.HasGrid)
        {
            // Ensure grid can fit the strip we need when appending.
            EnsureGridFitsStrip(maps.Count, horizontal);
        }

        if (_world is null)
            return Array.Empty<string>();

        MosaicMode = true;
        ShowInfoOverlay = false;

        var keys = new List<string>(maps.Count);
        var start = replaceWorld || _world.Placements.Count == 0
            ? (X: _world.OriginX, Y: _world.OriginY)
            : FindFreeOrigin();

        for (var i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            var key = FindOrAddSharedDocument(map);
            var x = horizontal ? start.X + i : start.X;
            var y = horizontal ? start.Y : start.Y + i;

            if (_world.HasGrid && !WorldGeometry.IsInGrid(_world, x, y))
            {
                // Fall back to next free cell if strip doesn't fit.
                var free = FindFreeOrigin();
                x = free.X;
                y = free.Y;
            }

            _editor.PlaceAt(_world, key, x, y, swapIfOccupied: true);
            keys.Add(key);
            _thumbs.Invalidate(map);
        }

        _selectedKeys = keys.Count > 0
            ? new HashSet<string>(StringComparer.Ordinal) { keys[0] }
            : new HashSet<string>(StringComparer.Ordinal);
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        SyncTray();
        TouchWorld();
        StatusText = horizontal
            ? $"Combinados {keys.Count} mapas en horizontal (mosaico)"
            : $"Combinados {keys.Count} mapas en vertical (mosaico)";
        _setStatus(StatusText);
        WorldChanged?.Invoke();
        RequestRedraw?.Invoke();

        if (scratchCombined)
            IsScratchCombined = true;

        if (enterMultiMapEdit && keys.Count > 0)
            EnterMultiMapEdit();

        if (IsScratchCombined)
            EnsureCombinedNeighborSlots();

        // Encajar después de casillas "+" y con el viewport ya medido.
        RequestFitAll?.Invoke();

        return keys;
    }

    /// <summary>
    /// Copia la disposición de un mundo real al mosaico scratch del combinado (mismas celdas).
    /// </summary>
    public IReadOnlyList<string> ImportWorldLayoutAsScratch(WorldDocument source)
    {
        if (source.Placements.Count == 0)
            return Array.Empty<string>();

        ExitMultiMapEdit(force: true);
        var firstId = source.Placements
            .Select(p => source.Documents.TryGetValue(p.DocumentKey, out var e) ? e.Document.Id : 0)
            .FirstOrDefault(id => id != 0);
        var label = string.IsNullOrWhiteSpace(source.Name)
            ? (firstId != 0 ? $"Desde mundo · {firstId}…" : "Desde mundo")
            : $"Desde · {source.Name}";

        var gw = Math.Max(1, source.HasGrid ? source.GridWidth : source.Placements.Max(p => p.WorldX) - source.Placements.Min(p => p.WorldX) + 1);
        var gh = Math.Max(1, source.HasGrid ? source.GridHeight : source.Placements.Max(p => p.WorldY) - source.Placements.Min(p => p.WorldY) + 1);
        var ox = source.HasGrid ? source.OriginX : source.Placements.Min(p => p.WorldX);
        var oy = source.HasGrid ? source.OriginY : source.Placements.Min(p => p.WorldY);

        SetWorld(_editor.CreateNew(label, gw, gh, originX: ox, originY: oy));
        IsScratchCombined = true;
        MosaicMode = true;
        ShowInfoOverlay = false;
        ShowMapBounds = true;

        if (_world is null)
            return Array.Empty<string>();

        var keys = new List<string>(source.Placements.Count);
        foreach (var p in source.Placements.OrderBy(p => p.WorldY).ThenBy(p => p.WorldX))
        {
            if (!source.Documents.TryGetValue(p.DocumentKey, out var entry))
                continue;

            var key = FindOrAddSharedDocument(entry.Document);
            ExpandGridToward(p.WorldX, p.WorldY);
            _editor.PlaceAt(_world, key, p.WorldX, p.WorldY, swapIfOccupied: true);
            keys.Add(key);
            _thumbs.Invalidate(entry.Document);
        }

        _selectedKeys = keys.Count > 0
            ? new HashSet<string>(StringComparer.Ordinal) { keys[0] }
            : new HashSet<string>(StringComparer.Ordinal);
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        EnterMultiMapEditCommand.RaiseCanExecuteChanged();
        SyncTray();
        TouchWorld();
        StatusText = $"Mundo → combinado · {keys.Count} mapas";
        _setStatus(StatusText);
        WorldChanged?.Invoke();
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();

        if (keys.Count > 0)
            EnterMultiMapEdit();

        EnsureCombinedNeighborSlots();
        return keys;
    }

    public IReadOnlyList<MapDocument> GetPlacedMapsInReadingOrder()
    {
        if (_world is null)
            return Array.Empty<MapDocument>();

        return _world.Placements
            .Where(p => _world.Documents.ContainsKey(p.DocumentKey))
            .OrderBy(p => p.WorldY)
            .ThenBy(p => p.WorldX)
            .Select(p => _world.Documents[p.DocumentKey].Document)
            .ToList();
    }

    public void ResetScratchCombined()
    {
        ExitMultiMapEdit(force: true);
        _selectedKeys.Clear();
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        SetWorld(null);
        StatusText = "Sin mundo";
        RequestRedraw?.Invoke();
    }

    /// <summary>Expands the scratch grid so every placed map has empty neighbor slots for "+" adds.</summary>
    public void EnsureCombinedNeighborSlots()
    {
        if (_world is null || !IsScratchCombined || _world.Placements.Count == 0)
            return;

        foreach (var p in _world.Placements.ToList())
        {
            ExpandGridToward(p.WorldX + 1, p.WorldY);
            ExpandGridToward(p.WorldX - 1, p.WorldY);
            ExpandGridToward(p.WorldX, p.WorldY + 1);
            ExpandGridToward(p.WorldX, p.WorldY - 1);
        }
    }

    public IReadOnlyList<(int X, int Y)> EnumerateCombinedAddSlots()
    {
        if (_world is null || !IsScratchCombined)
            return Array.Empty<(int, int)>();

        EnsureCombinedNeighborSlots();
        var occupied = _world.Placements.Select(p => (p.WorldX, p.WorldY)).ToHashSet();
        var slots = new HashSet<(int X, int Y)>();
        foreach (var p in _world.Placements)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var x = p.WorldX + dx;
                var y = p.WorldY + dy;
                if (!occupied.Contains((x, y)))
                    slots.Add((x, y));
            }
        }

        return slots.OrderBy(s => s.Y).ThenBy(s => s.X).ToList();
    }

    public bool IsCombinedAddSlot(int x, int y) =>
        IsScratchCombined && EnumerateCombinedAddSlots().Any(s => s.X == x && s.Y == y);

    /// <summary>Places a map document into a combined-mosaic cell (always a new placement; same Map ID allowed).</summary>
    public string? PlaceNewMapAt(MapDocument map, int x, int y)
    {
        if (_world is null) return null;

        PushWorldUndo();
        // Nueva entrada siempre: el mismo Map ID puede repetirse (como en MUNDO).
        var key = _editor.AddDocument(_world, map, WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
        ExpandGridToward(x, y);
        if (!_world.HasGrid || !WorldGeometry.IsInGrid(_world, x, y))
            ExpandGridToward(x, y);

        var occ = _editor.FindPlacementAt(_world, x, y);
        if (occ is not null)
        {
            var free = WorldGeometry.FindAdjacentFree(x, y, _editor.OccupiedCells(_world));
            if (free is null)
            {
                if (_worldUndo.Count > 0) _worldUndo.Pop();
                RaiseWorldUndoRedo();
                StatusText = $"Celda ({x},{y}) ocupada";
                return null;
            }

            x = free.Value.X;
            y = free.Value.Y;
            ExpandGridToward(x, y);
        }

        _editor.PlaceAt(_world, key, x, y);
        SelectKey(key);
        if (IsMultiMapEditMode)
            _multiMap.EnsureEditable(key);
        EnsureCombinedNeighborSlots();
        _thumbs.Invalidate(map);
        SyncTray();
        TouchWorld();
        StatusText = $"Añadido mapa {map.Id} en ({x},{y})";
        _setStatus(StatusText);
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
        return key;
    }

    /// <summary>
    /// Tamaño dominante del mosaico (primera colocación con mapa). Null si aún no hay mapas reales.
    /// </summary>
    public (int Width, int Height)? GetWorkingMapSize()
    {
        if (_world is null) return null;
        foreach (var p in _world.Placements.OrderBy(p => p.WorldY).ThenBy(p => p.WorldX))
        {
            if (!_world.Documents.TryGetValue(p.DocumentKey, out var entry))
                continue;
            var m = entry.Document;
            if (m.Width > 0 && m.Height > 0)
                return (m.Width, m.Height);
        }

        return null;
    }

    public string? GetWorkingMapSizeLabel()
    {
        if (GetWorkingMapSize() is not { } size)
            return null;
        var name = size.Width == BlankMapFactory.MedioWidth && size.Height == BlankMapFactory.MedioHeight
            ? "Medio"
            : size.Width == BlankMapFactory.GrandeWidth && size.Height == BlankMapFactory.GrandeHeight
                ? "Grande"
                : "Personalizado";
        return $"{name} ({size.Width}×{size.Height})";
    }

    public bool MatchesWorkingMapSize(int width, int height)
    {
        if (GetWorkingMapSize() is not { } size)
            return true;
        return size.Width == width && size.Height == height;
    }

    public int CountPlacedMapId(int mapId)
    {
        if (_world is null || mapId == 0) return 0;
        var n = 0;
        foreach (var p in _world.Placements)
        {
            if (_world.Documents.TryGetValue(p.DocumentKey, out var entry) && entry.Document.Id == mapId)
                n++;
        }

        return n;
    }

    public bool HasPlacedMapId(int mapId)
    {
        if (_world is null) return false;
        foreach (var p in _world.Placements)
        {
            if (_world.Documents.TryGetValue(p.DocumentKey, out var entry) &&
                entry.Document.Id == mapId)
                return true;
        }

        return false;
    }

    /// <summary>True when the placed maps form a horizontal strip (or a single map).</summary>
    public bool IsCombinedStripHorizontal()
    {
        if (_world is null || _world.Placements.Count <= 1)
            return true;
        var minX = _world.Placements.Min(p => p.WorldX);
        var maxX = _world.Placements.Max(p => p.WorldX);
        var minY = _world.Placements.Min(p => p.WorldY);
        var maxY = _world.Placements.Max(p => p.WorldY);
        return maxX - minX >= maxY - minY;
    }

    public int? CombinedAnchorMapId()
    {
        if (_world is null) return null;
        if (_selectedKeys.Count == 1 &&
            _world.Documents.TryGetValue(_selectedKeys.First(), out var selected))
            return selected.Document.Id;
        var last = _world.Placements.LastOrDefault();
        if (last is not null && _world.Documents.TryGetValue(last.DocumentKey, out var entry))
            return entry.Document.Id;
        return null;
    }

    public CombinedAddChoice SuggestedCombinedAddChoice()
    {
        if (_lastAdjacentDx < 0) return CombinedAddChoice.Left;
        if (_lastAdjacentDx > 0) return CombinedAddChoice.Right;
        if (_lastAdjacentDy < 0) return CombinedAddChoice.Up;
        if (_lastAdjacentDy > 0) return CombinedAddChoice.Down;
        return IsCombinedStripHorizontal() ? CombinedAddChoice.Right : CombinedAddChoice.Down;
    }

    /// <summary>
    /// Inserts a map glued to the selected (or last) combined map in the given direction.
    /// Same Map ID may appear more than once (new placement).
    /// </summary>
    public string? InsertDocumentAdjacent(MapDocument map, int dx, int dy)
    {
        if (_world is null)
        {
            return CombineFromDocuments(new[] { map }, horizontal: dx != 0, replaceWorld: true, enterMultiMapEdit: true)
                .FirstOrDefault();
        }

        int x;
        int y;
        if (!TryGetCombinedAnchorCell(out var ax, out var ay))
        {
            x = _world.OriginX;
            y = _world.OriginY;
        }
        else
        {
            x = ax + dx;
            y = ay + dy;
            var occ = _editor.OccupiedCells(_world);
            while (occ.Contains((x, y)))
            {
                x += dx;
                y += dy;
            }
        }

        _lastAdjacentDx = dx;
        _lastAdjacentDy = dy;
        return PlaceNewMapAt(map, x, y);
    }

    private bool TryGetCombinedAnchorCell(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_world is null) return false;
        if (_selectedKeys.Count == 1 && TryGetPlacementCoords(_selectedKeys.First(), out x, out y))
            return true;
        var last = _world.Placements.LastOrDefault();
        if (last is null) return false;
        x = last.WorldX;
        y = last.WorldY;
        return true;
    }

    /// <summary>Closes empty cells so remaining maps sit together, then fits the scratch grid.</summary>
    public void CompactCombinedPlacements()
    {
        if (_world is null || _world.Placements.Count == 0)
            return;

        var list = _world.Placements;
        bool moved;
        do
        {
            moved = false;
            var minX = list.Min(p => p.WorldX);
            var minY = list.Min(p => p.WorldY);
            var occ = list.Select(p => (p.WorldX, p.WorldY)).ToHashSet();
            foreach (var p in list.OrderBy(p => p.WorldY).ThenBy(p => p.WorldX).ToList())
            {
                while (p.WorldY > minY && !occ.Contains((p.WorldX, p.WorldY - 1)))
                {
                    occ.Remove((p.WorldX, p.WorldY));
                    p.WorldY--;
                    occ.Add((p.WorldX, p.WorldY));
                    moved = true;
                }

                while (p.WorldX > minX && !occ.Contains((p.WorldX - 1, p.WorldY)))
                {
                    occ.Remove((p.WorldX, p.WorldY));
                    p.WorldX--;
                    occ.Add((p.WorldX, p.WorldY));
                    moved = true;
                }
            }
        } while (moved);

        var originDx = _world.OriginX - list.Min(p => p.WorldX);
        var originDy = _world.OriginY - list.Min(p => p.WorldY);
        if (originDx != 0 || originDy != 0)
        {
            foreach (var p in list)
            {
                p.WorldX += originDx;
                p.WorldY += originDy;
            }
        }

        if (_world.HasGrid)
        {
            _world.GridWidth = Math.Max(1, list.Max(p => p.WorldX) - _world.OriginX + 1);
            _world.GridHeight = Math.Max(1, list.Max(p => p.WorldY) - _world.OriginY + 1);
        }

        EnsureCombinedNeighborSlots();
        TouchWorld();
        RequestFitAll?.Invoke();
    }

    /// <summary>Adds one open map next to the current selection (or first free cell). Same Map ID allowed.</summary>
    public string? AppendDocumentAdjacent(MapDocument map, bool preferHorizontal)
    {
        if (_world is null)
        {
            return CombineFromDocuments(new[] { map }, preferHorizontal, replaceWorld: true, enterMultiMapEdit: false)
                .FirstOrDefault();
        }

        int x, y;
        if (_selectedKeys.Count == 1 &&
            TryGetPlacementCoords(_selectedKeys.First(), out var sx, out var sy))
        {
            var occ = _editor.OccupiedCells(_world);
            var candidate = preferHorizontal ? (sx + 1, sy) : (sx, sy + 1);
            if (!occ.Contains(candidate) &&
                (!_world.HasGrid || WorldGeometry.IsInGrid(_world, candidate.Item1, candidate.Item2)))
            {
                x = candidate.Item1;
                y = candidate.Item2;
            }
            else
            {
                var adj = WorldGeometry.FindAdjacentFree(sx, sy, occ);
                if (adj is null)
                {
                    var free = FindFreeOrigin();
                    x = free.X;
                    y = free.Y;
                }
                else
                {
                    x = adj.Value.X;
                    y = adj.Value.Y;
                }
            }
        }
        else
        {
            var free = FindFreeOrigin();
            x = free.X;
            y = free.Y;
        }

        return PlaceNewMapAt(map, x, y);
    }

    private string FindOrAddSharedDocument(MapDocument map)
    {
        if (_world is null) throw new InvalidOperationException("No world");

        foreach (var (key, entry) in _world.Documents)
        {
            if (entry.Document.Id == map.Id)
            {
                // Prefer the live MAPA instance so edits stay in sync.
                entry.Document = map;
                return key;
            }
        }

        return _editor.AddDocument(_world, map, WorldMapOrigin.Library, WorldMapPublicationState.FromLibrary);
    }

    /// <summary>
    /// Moves the current combined arrangement into a normal world grid
    /// (same structure as Archivo → Mundo → Nuevo mundo).
    /// </summary>
    public bool TransferCombinedToWorldGrid(int gridWidth, int gridHeight, int originX, int originY)
    {
        if (_world is null || _world.Placements.Count == 0)
            return false;

        var ordered = _world.Placements
            .Select(p => (p, entry: _world.Documents[p.DocumentKey]))
            .OrderBy(t => t.p.WorldY)
            .ThenBy(t => t.p.WorldX)
            .Select(t => t.entry.Document)
            .ToList();

        if (ordered.Count == 0)
            return false;

        // Keep live MapDocument instances; rebuild world shell with user grid.
        ExitMultiMapEdit(force: true);
        SetWorld(_editor.CreateNew(
            name: ordered.Count == 1 ? $"Mapa {ordered[0].Id}" : $"Combinado {ordered[0].Id}…",
            gridWidth: gridWidth,
            gridHeight: gridHeight,
            originX: originX,
            originY: originY));

        MosaicMode = false;
        ShowInfoOverlay = true;
        ShowMapBounds = true;

        var keys = new List<string>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var map = ordered[i];
            var key = FindOrAddSharedDocument(map);
            var x = originX + i;
            var y = originY;
            if (!WorldGeometry.IsInGrid(_world!, x, y))
            {
                // Wrap to next row if the strip exceeds width.
                var col = i % Math.Max(1, gridWidth);
                var row = i / Math.Max(1, gridWidth);
                x = originX + col;
                y = originY + row;
                if (!WorldGeometry.IsInGrid(_world!, x, y))
                    ExpandGridToward(x, y);
            }

            _editor.PlaceAt(_world!, key, x, y, swapIfOccupied: true);
            keys.Add(key);
            _thumbs.Invalidate(map);
        }

        _selectedKeys = new HashSet<string>(keys, StringComparer.Ordinal);
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
        SyncTray();
        TouchWorld();
        StatusText = $"Enviado a MUNDO · cuadrícula {gridWidth}×{gridHeight}";
        _setStatus(StatusText);
        WorldChanged?.Invoke();
        RequestRedraw?.Invoke();
        RequestFitAll?.Invoke();
        return true;
    }

    private void EnsureGridFitsStrip(int count, bool horizontal)
    {
        if (_world is null || !_world.HasGrid) return;
        if (horizontal && _world.GridWidth < count)
            _world.GridWidth = count;
        if (!horizontal && _world.GridHeight < count)
            _world.GridHeight = count;
        TouchWorld();
    }

    private void ExpandGridToward(int x, int y)
    {
        if (_world is null || !_world.HasGrid) return;
        if (x < _world.OriginX)
        {
            var d = _world.OriginX - x;
            _world.OriginX = x;
            _world.GridWidth += d;
        }

        if (y < _world.OriginY)
        {
            var d = _world.OriginY - y;
            _world.OriginY = y;
            _world.GridHeight += d;
        }

        if (x >= _world.OriginX + _world.GridWidth)
            _world.GridWidth = x - _world.OriginX + 1;
        if (y >= _world.OriginY + _world.GridHeight)
            _world.GridHeight = y - _world.OriginY + 1;
        TouchWorld();
    }

    public bool ConfirmDiscard()
    {
        if (IsScratchCombined)
            return true;
        if (_world is not { IsDirty: true }) return true;

        var name = string.IsNullOrWhiteSpace(_world.Name) ? "mundo" : _world.Name;
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(
                $"El mundo «{name}» tiene cambios sin guardar.\n\n" +
                "Sí = Guardar y continuar\n" +
                "No = Descartar cambios\n" +
                "Cancelar = Seguir con este mundo",
                "Mundo",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning)
            : MessageBox.Show(
                owner,
                $"El mundo «{name}» tiene cambios sin guardar.\n\n" +
                "Sí = Guardar y continuar\n" +
                "No = Descartar cambios\n" +
                "Cancelar = Seguir con este mundo",
                "Mundo",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.No)
            return true;

        SaveWorld();
        return _world is not { IsDirty: true };
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
        NotifyPresentation();
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CurrentWorldLabel));
        PresentationChanged?.Invoke();
    }

    public bool CanExpandGrid() =>
        _world is not null && _world.HasGrid && !IsMultiMapEditMode;

    public bool CanShrinkGrid(WorldGridEdge edge) =>
        _world is not null && !IsMultiMapEditMode && _editor.CanShrinkGrid(_world, edge);

    private bool CanInsertDeleteAtSelection() =>
        _world is not null
        && _world.HasGrid
        && !IsMultiMapEditMode
        && TryGetSelectionAnchorCell(out _, out _);

    private bool TryGetSelectionAnchorCell(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_world is null || _selectedKeys.Count == 0) return false;
        var key = _selectedKeys.Count == 1
            ? _selectedKeys.First()
            : _selectedKeys.FirstOrDefault(k => TryGetPlacementCoords(k, out _, out _)) ?? _selectedKeys.First();
        return TryGetPlacementCoords(key, out x, out y);
    }

    private void RaiseSelectionGridCommands()
    {
        InsertRowAboveSelectionCommand.RaiseCanExecuteChanged();
        InsertRowBelowSelectionCommand.RaiseCanExecuteChanged();
        InsertColumnLeftSelectionCommand.RaiseCanExecuteChanged();
        InsertColumnRightSelectionCommand.RaiseCanExecuteChanged();
        DeleteRowAtSelectionCommand.RaiseCanExecuteChanged();
        DeleteColumnAtSelectionCommand.RaiseCanExecuteChanged();
        FindDuplicateMapIdsCommand.RaiseCanExecuteChanged();
    }

    private void InsertRowRelative(bool above)
    {
        if (_world is null || !TryGetSelectionAnchorCell(out _, out var y)) return;
        InsertGridRowAt(above ? y : y + 1);
    }

    private void InsertColumnRelative(bool left)
    {
        if (_world is null || !TryGetSelectionAnchorCell(out var x, out _)) return;
        InsertGridColumnAt(left ? x : x + 1);
    }

    private void DeleteRowAtSelection()
    {
        if (_world is null || !TryGetSelectionAnchorCell(out _, out var y)) return;
        DeleteGridRowAt(y);
    }

    private void DeleteColumnAtSelection()
    {
        if (_world is null || !TryGetSelectionAnchorCell(out var x, out _)) return;
        DeleteGridColumnAt(x);
    }

    public void InsertGridRowAt(int y)
    {
        if (_world is null || !_world.HasGrid || IsMultiMapEditMode) return;
        PushWorldUndo();
        if (_editor.InsertRowAt(_world, y) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            return;
        }
        StatusText = $"Fila insertada en Y={y} · cuadrícula {_world.GridWidth}×{_world.GridHeight}";
        AfterGridResized();
    }

    public void InsertGridColumnAt(int x)
    {
        if (_world is null || !_world.HasGrid || IsMultiMapEditMode) return;
        PushWorldUndo();
        if (_editor.InsertColumnAt(_world, x) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            return;
        }
        StatusText = $"Columna insertada en X={x} · cuadrícula {_world.GridWidth}×{_world.GridHeight}";
        AfterGridResized();
    }

    public void DeleteGridRowAt(int y)
    {
        if (_world is null || !_world.HasGrid || IsMultiMapEditMode) return;
        if (y < _world.OriginY || y >= _world.OriginY + _world.GridHeight) return;

        var onRow = _world.Placements.Count(p => p.WorldY == y);
        if (onRow > 0 && !ConfirmRemoveMapsFromAxis("fila", onRow))
            return;

        PushWorldUndo();
        if (_editor.DeleteRowAt(_world, y, out var removed) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            MessageBox.Show("No se puede quitar esa fila (mínimo 1).", "Mundo");
            return;
        }

        ForgetRemovedKeys(removed);
        StatusText = removed.Count > 0
            ? $"Fila Y={y} eliminada ({removed.Count} mapa(s) → bandeja)"
            : $"Fila Y={y} eliminada · {_world.GridWidth}×{_world.GridHeight}";
        AfterGridResized();
    }

    public void DeleteGridColumnAt(int x)
    {
        if (_world is null || !_world.HasGrid || IsMultiMapEditMode) return;
        if (x < _world.OriginX || x >= _world.OriginX + _world.GridWidth) return;

        var onCol = _world.Placements.Count(p => p.WorldX == x);
        if (onCol > 0 && !ConfirmRemoveMapsFromAxis("columna", onCol))
            return;

        PushWorldUndo();
        if (_editor.DeleteColumnAt(_world, x, out var removed) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            MessageBox.Show("No se puede quitar esa columna (mínimo 1).", "Mundo");
            return;
        }

        ForgetRemovedKeys(removed);
        StatusText = removed.Count > 0
            ? $"Columna X={x} eliminada ({removed.Count} mapa(s) → bandeja)"
            : $"Columna X={x} eliminada · {_world.GridWidth}×{_world.GridHeight}";
        AfterGridResized();
    }

    private static bool ConfirmRemoveMapsFromAxis(string axis, int count)
    {
        var noun = count == 1 ? "mapa" : "mapas";
        var confirm = MessageBox.Show(
            $"Eliminar esta {axis} quitará {count} {noun} de la cuadrícula (irán a Mapas locales).\n\n¿Continuar?",
            $"Quitar {axis}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return confirm == MessageBoxResult.Yes;
    }

    private void ForgetRemovedKeys(IReadOnlyList<string> removed)
    {
        var changed = false;
        foreach (var key in removed)
            changed |= _selectedKeys.Remove(key);
        if (!changed) return;
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectedKeys));
    }

    private void AfterGridResized()
    {
        SyncTray();
        RaiseSelectionGridCommands();
        ExpandGridCommand.RaiseCanExecuteChanged();
        ShrinkGridCommand.RaiseCanExecuteChanged();
        RequestRedraw?.Invoke();
    }

    public void FindDuplicateMapIds()
    {
        if (_world is null) return;

        var groups = _world.Placements
            .Select(p =>
            {
                _world.Documents.TryGetValue(p.DocumentKey, out var entry);
                return (p.WorldX, p.WorldY, MapId: entry?.Document.Id ?? 0);
            })
            .Where(t => t.MapId > 0)
            .GroupBy(t => t.MapId)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .ToList();

        if (groups.Count == 0)
        {
            MessageBox.Show(
                "No hay Map IDs duplicados en la cuadrícula.",
                "Buscar duplicados",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusText = "Sin Map IDs duplicados";
            return;
        }

        var lines = groups.Select(g =>
        {
            var pos = string.Join(", ", g.Select(t => $"({t.WorldX},{t.WorldY})"));
            return $"Map ID {g.Key} → {pos} ({g.Count()} veces)";
        });
        var body = string.Join("\n", lines);
        MessageBox.Show(
            body,
            "Map IDs duplicados",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        StatusText = $"{groups.Count} Map ID(s) duplicado(s)";
    }

    public void ExpandGrid(WorldGridEdge edge)
    {
        if (_world is null) return;
        PushWorldUndo();
        if (_editor.ExpandGrid(_world, edge) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            return;
        }
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

        PushWorldUndo();
        if (_editor.ShrinkGrid(_world, edge, out var removed) != WorldGridResizeResult.Ok)
        {
            if (_worldUndo.Count > 0) _worldUndo.Pop();
            RaiseWorldUndoRedo();
            return;
        }
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
