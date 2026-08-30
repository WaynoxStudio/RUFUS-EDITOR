using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Portable;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;
using RufusMapEditor.Rendering.Package;
using System.Diagnostics;

namespace RufusMapEditor.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private int _gfxColumns = GfxCatalogLayout.DefaultColumns;
    private const int MaxRecents = 24;
    private const string FavoritesFolder = "Favoritos";
    private const string RecentsFolder = "Recientes";

    private readonly AstriaLibraryService _library = new();
    private readonly AppSettings _settings;
    private readonly GfxThumbnailCache _thumbs = new();
    private readonly GfxOverlayCache _overlayCache = new();
    private readonly WorldThumbnailCache _worldThumbs = new();
    private readonly MapPreviewCache _mapPreviews = new();
    private readonly SemaphoreSlim _mapThumbGate = new(3);
    private readonly AutosaveStore _autosave = new();
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _gfxSearchDebounce;
    private readonly MapPickerFilterState _mapListFilter = new();
    private FileSystemWatcher? _imagesWatcher;
    private DispatcherTimer? _imagesReloadDebounce;
    private int _imagesReloadGeneration;

    /// <summary>True when hosted inside ADMIN ContentHost — Exit must not close Application.MainWindow.</summary>
    public bool IsEmbeddedHost { get; set; }

    private Dictionary<(GfxCategory Category, string FolderKey), IReadOnlyList<GfxResource>>? _folderResourceIndex;
    private Dictionary<(GfxCategory Category, int Id), GfxResource>? _resourceByIdIndex;
    private string _lastVisibleGfxFilterKey = "";
    private int _catalogRefreshCount;
    private InspectorLayerHighlight _highlightedInspectorLayer = InspectorLayerHighlight.None;

    private MapEditSession? _session;
    private string _libraryStatusMessage = "No hay biblioteca RUFUS configurada.";
    private string _statusText = "Listo";
    private string _zoomText = "100%";
    private string _cellIdText = "—";
    private string _coordsText = "—";
    private string _renderTimeText = "—";
    private string _editLatencyText = "—";
    private string _windowTitle = "RUFUS Map Editor";
    private string _gfxSearch = "";
    private string _activeLayerLabel = "Capa: SUELO";
    private string _selectedGfxLabel = "Gfx: —";
    private string _catalogHeaderTitle = "CATÁLOGO · SUELOS";
    private string _catalogHeaderDestination = "Destino: SUELO";
    private string _brushTypeLabel = "Suelo";
    private string _brushTargetLabel = "Suelo";
    private string _brushGfxIdLabel = "—";
    private string _brushFlipLabel = "No";
    private string _brushRotationLabel = "0";
    private string _brushDimensionsLabel = "";
    private string? _effectiveLibraryPath;
    private LibrarySource _librarySource;
    private bool _isLoading;
    private bool _isRendering;
    private bool _hasLibrary;
    private bool _showGrid;
    private bool _showDebugInfo;
    private bool _showBackgroundLayer = true;
    private bool _showGroundLayer = true;
    private bool _showObject1Layer = true;
    private bool _showObject2Layer = true;
    private bool _showCellIds;
    private bool _showUnwalkableMarkers = true;
    private bool _showLosBlockMarkers = true;
    private bool _showFightMarkers = true;
    private bool _showMapExportLimit;
    private double _viewportZoom = 1.0;
    private MapViewVisibilitySettings? _visibilityRestore;
    private bool _suppressProp;
    private int? _selectedMapId;
    private int? _hoveredCellId;
    private int? _primarySelectedCellId;
    private int? _selectedGfxId;
    private MapDocument? _currentMap;
    private ImageSource? _mapImage;
    private ImageSource? _previewGround;
    private ImageSource? _previewObject1;
    private ImageSource? _previewObject2;
    private ImageSource? _brushPreview;
    private IsoHitTester? _hitTester;
    private IReadOnlyList<string> _resourceWarnings = Array.Empty<string>();
    private FlasmSwfMetadataReader.SwfMapMetadata? _swfMeta;
    private readonly List<OpenMapDocument> _openDocuments = new();
    private OpenMapDocument? _activeDocument;
    private int _nextCascadeIndex;
    private readonly List<OpenWorldSession> _openWorlds = new();
    private OpenWorldSession? _activeWorldSession;
    private WorldViewModel _worldShell = null!;
    private WorldViewModel _combinedMapsVm = null!;
    private int _nextWorldCascadeIndex;
    private bool _maximizeNextWorldWindow;
    private bool _maximizeNextMapWindow;
    private int _nextTempMapId = -1;
    private EditorTool _tool = EditorTool.Select;
    private PaintLayer _paintLayer = PaintLayer.Ground;
    private string? _selectedFolder;
    private GfxCategory _selectedCategory = GfxCategory.Ground;
    private bool _showUnifiedFavorites;
    private bool _brushFlip;
    private int _brushRotation;
    private int _cellModeOverlayRevision;
    private int _fixedMobsOverlayRevision;
    private int _lastPaintedCell = -1;
    private bool _strokeIsErase;
    /// <summary>When set, erase strokes only clear cells whose active layer GFX matches this id.</summary>
    private int? _eraseMatchGfxId;
    /// <summary>When true for the current erase stroke, only cells matching <see cref="_eraseMatchGfxId"/> are cleared.</summary>
    private bool _eraseRequireMatch;
    private bool _eraseOnlySelectedGfx;
    private double? _lastStrokeContentX;
    private double? _lastStrokeContentY;
    private List<SelectionMovePiece>? _movePieces;
    private IReadOnlyList<SelectionMovePreviewItem> _movePreviewItems = Array.Empty<SelectionMovePreviewItem>();
    private bool _isMovingSelection;
    private MovementType? _editMovement = MovementType.Walkable;
    private MovementDisplayItem? _editMovementItem;
    private bool? _editLos = true;
    private int? _editFightCell;
    private FightCellDisplayItem? _editFightCellItem;
    private bool? _editIo;
    private int? _editGroundLevel = 7;
    private int? _editGroundSlope = 1;
    private bool? _editFlipG;
    private bool? _editFlipO1;
    private bool? _editFlipO2;
    private int? _editRotG;
    private int? _editRotO1;
    private int _renderGeneration;
    private bool _rectSelecting;
    private double _rectX0;
    private double _rectY0;
    private double _rectX1;
    private double _rectY1;
    /// <summary>MAP-AREA.1 — snapshot of selection when a Keep/Add rect drag starts.</summary>
    private HashSet<int>? _rectSelectBase;
    private bool _keepAddSelection;
    private IReadOnlyList<int> _selectedCellIds = Array.Empty<int>();
    private string? _worldEditingDocumentKey;
    private bool _openedMapFromWorld;
    private int _workspaceTabIndex;
    /// <summary>When true, several maps stay yellow and ESTE ÍTEM applies to all of them.</summary>
    private bool _combinedMapsMultiSelect;
    private bool _pasteArmed;
    private bool _multiMapStrokeIsErase;
    private bool _multiMapEraseMatchBrush;
    private double? _inspectContentX;
    private double? _inspectContentY;
    private static readonly PaintLayer[] InspectLayerOrder =
    {
        PaintLayer.Object2,
        PaintLayer.Object1,
        PaintLayer.Ground,
    };

    private bool _libraryLoadCompleted;
    private readonly bool _deferLibraryLoad;

    public MainViewModel(bool deferLibraryLoad = false)
    {
        _deferLibraryLoad = deferLibraryLoad;
        _settings = AppSettingsStore.Load();
        _settings.MapListFilter ??= new MapListFilterSettings();
        _mapListFilter.LoadFrom(_settings.MapListFilter);
        _mapListFilter.Changed += OnMapListFilterChangedFromPicker;
        MapIds = new ObservableCollection<int>();
        MapListItems = new ObservableCollection<MapPickerItemVm>();
        FolderTree = new ObservableCollection<FolderNodeVm>();
        VisibleGfxRows = new ObservableCollection<GfxRowVm>();
        MovementDisplayOptions = new ObservableCollection<MovementDisplayItem>(MovementDisplayItem.StandardOptions);
        FightCellDisplayOptions = new ObservableCollection<FightCellDisplayItem>(FightCellDisplayItem.Options);
        RotationOptions = new ObservableCollection<int>(new[] { 0, 1, 2, 3 });

        SelectLibraryCommand = new RelayCommand(SelectLibrary);
        OpenMapDialogCommand = new RelayCommand(OpenMapDialog, () => HasLibrary);
        NewMapCommand = new RelayCommand(() => _ = NewMapAsync(), () => HasLibrary);
        ClearMapListFilterCommand = new RelayCommand(ClearMapListFilter);
        CloseMapCommand = new RelayCommand(
            () =>
            {
                if (_activeDocument is null) return;
                if (_activeDocument.IsDirty && !ConfirmDiscardMapOnly()) return;
                CloseDocument(_activeDocument);
            },
            () => CurrentMap is not null);
        ExitCommand = new RelayCommand(ExitOrCloseHost);
        FitMapCommand = new RelayCommand(() => RequestFitMap?.Invoke(), () => MapImage is not null);
        Zoom100Command = new RelayCommand(() => RequestZoom100?.Invoke(), () => MapImage is not null);
        ResetPanelsCommand = new RelayCommand(() => RequestResetPanels?.Invoke());

        UndoCommand = new RelayCommand(Undo, CanUndoNow);
        RedoCommand = new RelayCommand(Redo, CanRedoNow);
        CopyCommand = new RelayCommand(CopySelection, CanCopyNow);
        PasteCommand = new RelayCommand(PasteSelection, CanPasteNow);
        DuplicateCommand = new RelayCommand(DuplicateSelection, () => HasSelection);
        CombineOpenMapsCommand = new RelayCommand(CombineOpenMaps, () => _openDocuments.Count >= 2);
        AppendActiveMapToWorldCommand = new RelayCommand(
            AppendActiveMapToCombined,
            () => CurrentMap is not null && IsMapCombinedMode);
        ExitMapCombinedModeCommand = new RelayCommand(ExitMapCombinedMode, () => IsMapCombinedMode);
        MinimizeCombinedMapsCommand = new RelayCommand(
            () => SetMapCombinedMinimized(true),
            () => IsMapCombinedMode && !IsMapCombinedMinimized);
        RestoreCombinedMapsCommand = new RelayCommand(
            () => SetMapCombinedMinimized(false),
            () => IsMapCombinedMode && IsMapCombinedMinimized);
        SelectAllCombinedMapChipsCommand = new RelayCommand(
            SelectAllCombinedMapChips,
            () => IsMapCombinedMode && CombinedMapChips.Count > 0);
        ClearCombinedMapChipsKeepFirstCommand = new RelayCommand(
            ClearCombinedMapChipsKeepFirst,
            () => IsMapCombinedMode && CombinedMapChips.Count > 0);
        SendCombinedToWorldCommand = new RelayCommand(
            SendCombinedToWorld,
            () => IsMapCombinedMode && CombinedMaps.World is not null && CombinedMaps.World.Placements.Count > 0);
        SendWorldToCombinedCommand = new RelayCommand(
            SendWorldToCombined,
            CanSendWorldToCombined);
        SaveSelectedCombinedMapCommand = new RelayCommand(
            () => _ = SaveSelectedCombinedMapAsync(),
            () => IsMapCombinedMode && CombinedMaps.HasSingleSelection);
        SaveAllCombinedMapsCommand = new RelayCommand(
            () => _ = SaveAllCombinedMapsAsync(),
            () => IsMapCombinedMode && HasAnyDirtyCombinedMap);
        AddCombinedMapsToPublishQueueCommand = new RelayCommand(
            () => _ = AddCombinedMapsToPublishQueueAsync(),
            () => IsMapCombinedMode && CombinedMaps.World is { Placements.Count: > 0 });
        ExportCombinedWorldCommand = new RelayCommand(
            () => MosaicHost.SaveWorldAsCommand.Execute(null),
            () => MosaicHost.World is not null);

        SetToolSelectCommand = new RelayCommand(() => Tool = EditorTool.Select);
        SetToolRectSelectCommand = new RelayCommand(() => Tool = EditorTool.RectSelect);
        SetToolPaintCommand = new RelayCommand(() => Tool = EditorTool.Paint);
        SetToolEraseCommand = new RelayCommand(() => Tool = EditorTool.Erase);
        SetToolEyedropperCommand = new RelayCommand(() => Tool = EditorTool.Eyedropper);
        SetToolUnwalkableCommand = new RelayCommand(() => Tool = EditorTool.Unwalkable);
        SetToolLineOfSightCommand = new RelayCommand(() => Tool = EditorTool.LineOfSight);
        SetToolFightCell1Command = new RelayCommand(() => Tool = EditorTool.FightCell1);
        SetToolFightCell2Command = new RelayCommand(() => Tool = EditorTool.FightCell2);
        SetToolPanCommand = new RelayCommand(() => Tool = EditorTool.Pan);
        CycleBrushRotationCommand = new RelayCommand(CycleBrushRotation, () => PaintLayer != PaintLayer.Object2);

        SetLayerGroundCommand = new RelayCommand(() => PaintLayer = PaintLayer.Ground);
        SetLayerObject1Command = new RelayCommand(
            () => PaintLayer = PaintLayer.Object1,
            () => AreGfxObjectLayersEnabled);
        SetLayerObject2Command = new RelayCommand(
            () => PaintLayer = PaintLayer.Object2,
            () => AreGfxObjectLayersEnabled);

        ClearGroundCommand = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Ground), () => HasSelection);
        ClearObject1Command = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Object1), () => HasSelection);
        ClearObject2Command = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Object2), () => HasSelection);
        ClearActiveLayerCommand = new RelayCommand(ClearActiveLayer, () => HasSelection);
        ApplyBrushToSelectionCommand = new RelayCommand(
            ApplyBrushToSelection,
            () => HasSelection && (SelectedGfxId is int || FocusGfxId is int));
        DeleteSelectedGfxOnActiveLayerCommand = new RelayCommand(
            () => DeleteSelectedGfxOnLayers(new[] { PaintLayer }, wholeMap: true),
            () => CanMassEditSelectedGfx);
        DeleteSelectedGfxOnObject1Command = new RelayCommand(
            () => DeleteSelectedGfxOnLayers(new[] { PaintLayer.Object1 }, wholeMap: true),
            () => CanMassEditSelectedGfx);
        DeleteSelectedGfxOnObject2Command = new RelayCommand(
            () => DeleteSelectedGfxOnLayers(new[] { PaintLayer.Object2 }, wholeMap: true),
            () => CanMassEditSelectedGfx);
        DeleteSelectedGfxOnAllLayersCommand = new RelayCommand(
            () => DeleteSelectedGfxOnLayers(
                new[] { PaintLayer.Ground, PaintLayer.Object1, PaintLayer.Object2 }, wholeMap: true),
            () => CanMassEditSelectedGfx);
        DeleteSelectedGfxInSelectionCommand = new RelayCommand(
            () => DeleteSelectedGfxOnLayers(new[] { PaintLayer }, wholeMap: false),
            () => CanMassEditSelectedGfx && HasSelection);
        RotateSelectedGfxOnActiveLayerCommand = new RelayCommand(
            () => RotateSelectedGfxInstances(PaintLayer, delta: 1, wholeMap: true),
            () => CanMassRotateSelectedGfx);
        RotateSelectedGfxInSelectionCommand = new RelayCommand(
            () => RotateSelectedGfxInstances(PaintLayer, delta: 1, wholeMap: false),
            () => CanMassRotateSelectedGfx && HasSelection);
        SelectAllFocusGfxCommand = new RelayCommand(SelectAllFocusGfx, () => CanUseFocusGfx);
        DeleteAllFocusGfxCommand = new RelayCommand(
            () => DeleteFocusGfxOnActiveLayer(),
            () => CanUseFocusGfx);
        FlipAllFocusGfxCommand = new RelayCommand(
            () => FlipFocusGfxInstances(wholeMap: true),
            () => CanUseFocusGfx);
        RotateAllFocusGfxCommand = new RelayCommand(
            () => RotateFocusGfx(delta: 1, wholeMap: true),
            () => CanUseFocusGfx);
        MoveFocusGfxToGroundCommand = new RelayCommand(
            () => MoveFocusGfxToLayer(PaintLayer.Ground),
            () => CanMoveFocusGfxTo(PaintLayer.Ground));
        MoveFocusGfxToObject1Command = new RelayCommand(
            () => MoveFocusGfxToLayer(PaintLayer.Object1),
            () => CanMoveFocusGfxTo(PaintLayer.Object1));
        MoveFocusGfxToObject2Command = new RelayCommand(
            () => MoveFocusGfxToLayer(PaintLayer.Object2),
            () => CanMoveFocusGfxTo(PaintLayer.Object2));
        ClearMapSelectionCommand = new RelayCommand(ClearMapSelection, () => HasSelection);
        ToggleFavoriteCommand = new RelayCommand(() =>
        {
            if (SelectedGfxId is int id && FindVisibleGfxItem(id) is GfxItemVm item)
                ToggleFavorite(item);
        }, () => SelectedGfxId is not null);
        LocateGroundInCatalogCommand = new RelayCommand(() => LocateLayerInCatalog(PaintLayer.Ground), () => CanLocateLayer(PaintLayer.Ground));
        LocateObject1InCatalogCommand = new RelayCommand(() => LocateLayerInCatalog(PaintLayer.Object1), () => CanLocateLayer(PaintLayer.Object1));
        LocateObject2InCatalogCommand = new RelayCommand(() => LocateLayerInCatalog(PaintLayer.Object2), () => CanLocateLayer(PaintLayer.Object2));
        SelectInspectorGroundCommand = new RelayCommand(() => SelectInspectorLayer(InspectorLayerHighlight.Ground), () => HasSingleCellSelection);
        SelectInspectorObject1Command = new RelayCommand(() => SelectInspectorLayer(InspectorLayerHighlight.Object1), () => HasSingleCellSelection);
        SelectInspectorObject2Command = new RelayCommand(() => SelectInspectorLayer(InspectorLayerHighlight.Object2), () => HasSingleCellSelection);
        CopyCellMapDataCodeCommand = new RelayCommand(CopyCellMapDataCode, () => CanCopyCellMapData);
        CopyFullMapDataCommand = new RelayCommand(CopyFullMapData, () => CurrentMap is not null);

        OpenBackgroundPickerCommand = new RelayCommand(OpenBackgroundPicker, () => CurrentMap is not null && HasLibrary);
        OpenAppearanceCommand = new RelayCommand(OpenAppearance);
        OpenDatabaseSettingsCommand = new RelayCommand(OpenDatabaseSettings);
        PublishToDatabaseCommand = new RelayCommand(() => _ = PublishToDatabaseAsync(), () => CurrentMap is not null);
        SyncMetadataFromDatabaseCommand = new RelayCommand(() => _ = SyncMetadataFromDatabaseAsync(), () => CurrentMap is not null);
        GenerateLocalLangMapsCommand = new RelayCommand(OpenGenerateLocalLangMaps, () => CurrentMap is not null);
        OpenLangSftpSettingsCommand = new RelayCommand(OpenLangSftpSettings);
        OpenClipsSettingsCommand = new RelayCommand(OpenClipsSettings);
        OpenLicenseStatusCommand = new RelayCommand(OpenLicenseStatus);
        SyncRemoteLangCommand = new RelayCommand(() => _ = SyncRemoteLangAsync());
        PublishRemoteLangCommand = new RelayCommand(OpenPublishRemoteLang, () => CurrentMap is not null);
        MapMonsters = new MapMonstersEditorViewModel(
            getMapId: () => CurrentMap?.Id,
            getCellCount: () => CurrentMap?.Cells.Count,
            onFixedMobsChanged: () => FixedMobsOverlayRevision++);

        MapPublishQueue = new MapPublishQueueViewModel(
            getLibraryRoot: () => _library.RootPath ?? _effectiveLibraryPath,
            isMapDirty: id => FindOpenDocument(id)?.IsDirty == true,
            openMapAsync: LoadMapAsync,
            getCurrentMap: () => CurrentMap,
            saveCurrentAsync: SaveOfficialMapAsync,
            getSettings: () => _settings,
            loadMapDocument: id => _library.LoadMapDocument(id),
            reportStatus: msg => StatusText = msg);

        ThemeService.ThemeChanged += OnAppThemeChanged;
        if (App.License is not null)
            App.License.StatusChanged += OnLicenseStatusChanged;
        SoloBackgroundCommand = new RelayCommand(() => SoloLayer(SoloLayerTarget.Background));
        SoloGroundCommand = new RelayCommand(() => SoloLayer(SoloLayerTarget.Ground));
        SoloObject1Command = new RelayCommand(() => SoloLayer(SoloLayerTarget.Object1));
        SoloObject2Command = new RelayCommand(() => SoloLayer(SoloLayerTarget.Object2));
        RestoreVisibilityCommand = new RelayCommand(RestoreLayerVisibility, () => _visibilityRestore is not null);
        ResetLayoutCommand = new RelayCommand(ResetLayout);

        ToggleMapsPanelCommand = new RelayCommand(() => ShowMapsPanel = !ShowMapsPanel);
        ToggleInspectorPanelCommand = new RelayCommand(() => ShowInspectorPanel = !ShowInspectorPanel);
        FocusMonstersPanelCommand = new RelayCommand(() =>
        {
            ShowInspectorPanel = true;
            MapMonsters.FocusPanel();
            _ = MapMonsters.EnsureCatalogAsync(refreshDb: true);
            _ = MapMonsters.LoadNaturalMobsForCurrentMapAsync();
        });
        ToggleCatalogPanelCommand = new RelayCommand(() => ShowCatalogPanel = !ShowCatalogPanel);
        ToggleCategoriesPanelCommand = new RelayCommand(() => ShowCategoriesPanel = !ShowCategoriesPanel);
        ToggleBrushPanelCommand = new RelayCommand(() => ShowBrushPanel = !ShowBrushPanel);
        ToggleToolBarCommand = new RelayCommand(() => ShowToolBar = !ShowToolBar);
        ToggleStatusBarCommand = new RelayCommand(() => ShowStatusBar = !ShowStatusBar);

        SaveCommand = new RelayCommand(async () =>
        {
            if (MosaicHost.IsMultiMapEditMode)
                await SaveMultiMapModifiedAsync();
            else
                await SaveAsync();
        }, () => CurrentMap is not null || (MosaicHost.IsMultiMapEditMode && MultiMap.ModifiedMapCount > 0));
        SaveAsCommand = new RelayCommand(() => _ = SaveAsAsync(), () => CurrentMap is not null);
        OpenProjectCommand = new RelayCommand(() => _ = OpenProjectAsync());
        ExportSwfCommand = new RelayCommand(() => _ = ExportSwfAsync(), () => CurrentMap is not null);
        ExportPackageCommand = new RelayCommand(() => _ = ExportPackageAsync(), () => CurrentMap is not null);
        OpenMapFolderCommand = new RelayCommand(OpenMapFolder, () => CurrentMap is not null);
        RevertToSavedCommand = new RelayCommand(() => _ = RevertToSavedAsync(), () =>
            CurrentMap is not null && !string.IsNullOrWhiteSpace(_session?.FilePath));
        ReloadOriginalCommand = new RelayCommand(ReloadOriginal, () =>
            CurrentMap is not null && IsAstriaImport);

        _gfxSearchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _gfxSearchDebounce.Tick += (_, _) =>
        {
            _gfxSearchDebounce.Stop();
            RefreshVisibleGfx(force: true);
        };

        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, _settings.AutosaveIntervalSeconds)),
        };
        _autosaveTimer.Tick += (_, _) =>
        {
            TryAutosave();
            foreach (var session in _openWorlds)
                session.Vm.TryAutosave();
            if (_activeWorldSession is null)
                _worldShell.TryAutosave();
        };
        _autosaveTimer.Start();

        RecentProjects = new ObservableCollection<string>(_settings.RecentProjects);

        MultiMap = new MultiMapEditService(_library, _worldThumbs);

        _worldShell = CreateWorldViewModel();
        _combinedMapsVm = CreateWorldViewModel();

        ApplyMapViewVisibilityFromSettings(_settings.MapViewVisibility);

        Logs = new LogConsoleViewModel(RufusLog.Current);
        Logs.IsExpanded = _settings.UiLayout.LogsExpanded;
        Logs.PanelHeight = _settings.UiLayout.LogsPanelHeight > 0
            ? _settings.UiLayout.LogsPanelHeight
            : UiLayoutSettings.DefaultLogsPanelHeight;

        if (!_deferLibraryLoad)
            TryLoadSavedLibrary();
        RefreshLayerLabels();
    }

    /// <summary>ADMIN.UI.2 — defer heavy library scan until preload or first show.</summary>
    public void EnsureLibraryLoaded()
    {
        if (_libraryLoadCompleted)
            return;
        TryLoadSavedLibrary();
    }

    public WorldViewModel World => _activeWorldSession?.Vm ?? _worldShell;

    /// <summary>MAPA combinado mosaic. Never a MUNDO session window.</summary>
    public WorldViewModel CombinedMaps => _combinedMapsVm;

    /// <summary>Combinado mosaic while that mode is on; otherwise the active world.</summary>
    private WorldViewModel MosaicHost =>
        IsMapCombinedMode && _combinedMapsVm is not null ? _combinedMapsVm : World;

    public IReadOnlyList<OpenWorldSession> OpenWorldSessions => _openWorlds;

    public event Action<OpenWorldSession>? WorldSessionOpened;
    public event Action<OpenWorldSession>? WorldSessionClosed;
    public event Action<OpenWorldSession>? WorldSessionActivated;

    public MultiMapEditService MultiMap { get; }

    public LogConsoleViewModel Logs { get; }

    public int WorkspaceTabIndex
    {
        get => _workspaceTabIndex;
        set
        {
            if (!SetProperty(ref _workspaceTabIndex, value)) return;
            OnPropertyChanged(nameof(IsWorldTab));
            OnPropertyChanged(nameof(UndoLabel));
            OnPropertyChanged(nameof(RedoLabel));
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            CopyCommand.RaiseCanExecuteChanged();
            PasteCommand.RaiseCanExecuteChanged();
            if (value == 1 && _worldEditingDocumentKey is not null)
                World.NotifyMapEdited(_worldEditingDocumentKey);
        }
    }

    public bool IsWorldTab => WorkspaceTabIndex == 1;

    private bool _isMapCombinedMode;
    private bool _isMapCombinedMinimized;

    /// <summary>
    /// MAPA tab shows glued open maps (mosaic) instead of separate floating windows.
    /// Optional later step: send this arrangement to the MUNDO tab.
    /// </summary>
    public bool IsMapCombinedMode
    {
        get => _isMapCombinedMode;
        private set
        {
            if (!SetProperty(ref _isMapCombinedMode, value)) return;
            if (!value)
                _isMapCombinedMinimized = false;
            NotifyCombinedChrome();
            ExitMapCombinedModeCommand?.RaiseCanExecuteChanged();
            SendCombinedToWorldCommand?.RaiseCanExecuteChanged();
            AppendActiveMapToWorldCommand?.RaiseCanExecuteChanged();
            SaveSelectedCombinedMapCommand?.RaiseCanExecuteChanged();
            SaveAllCombinedMapsCommand?.RaiseCanExecuteChanged();
            ExportCombinedWorldCommand?.RaiseCanExecuteChanged();
            SelectAllCombinedMapChipsCommand?.RaiseCanExecuteChanged();
            ClearCombinedMapChipsKeepFirstCommand?.RaiseCanExecuteChanged();
            MinimizeCombinedMapsCommand?.RaiseCanExecuteChanged();
            RestoreCombinedMapsCommand?.RaiseCanExecuteChanged();
            if (value)
                RefreshCombinedMapChips();
            else
                CombinedMapChips.Clear();
        }
    }

    /// <summary>Hides the mosaic overlay so floating map windows are usable again.</summary>
    public bool IsMapCombinedMinimized
    {
        get => _isMapCombinedMinimized;
        private set => SetMapCombinedMinimized(value);
    }

    public bool ShowFloatingMapWindows => !IsMapCombinedMode || IsMapCombinedMinimized;
    public bool ShowCombinedMapsViewport => IsMapCombinedMode && !IsMapCombinedMinimized;
    public bool ShowCombinedMinimizedBar => IsMapCombinedMode && IsMapCombinedMinimized;

    /// <summary>Combinar / Añadir: solo con ventanas flotantes (no encima del mosaico).</summary>
    public bool ShowMapCombineEntryActions => ShowFloatingMapWindows;

    /// <summary>Salir / Enviar en la barra MAPA: solo si el combinado está minimizado (si está a pantalla, van en el chrome).</summary>
    public bool ShowMapCombinedToolbarExtras => IsMapCombinedMode && IsMapCombinedMinimized;

    /// <summary>True when 2+ maps are checked in the combined toolbar.</summary>
    public bool IsCombinedMapsMultiSelect => _combinedMapsMultiSelect;

    public ObservableCollection<CombinedMapChipVm> CombinedMapChips { get; } = new();

    public string MapCombinedModeLabel
    {
        get
        {
            if (!IsMapCombinedMode)
                return "";

            var size = CombinedMaps.GetWorkingMapSizeLabel();
            var sizePrefix = size is null ? "Combinado" : $"Combinado · {size}";

            if (Tool is EditorTool.Paint or EditorTool.Erase)
                return $"{sizePrefix} · Clic = pintar · Clic derecho = borrar · Márgenes / Alt / Espacio = mover vista";

            return _combinedMapsMultiSelect
                ? $"{sizePrefix} · Varios mapas · Arrastrar selección = mover GFX · Arrastra mapa de la lista o + · Alt+arrastrar = intercambiar"
                : $"{sizePrefix} · Clic = celda · Arrastra mapa de la lista o + · Alt+arrastrar mapa = intercambiar · Márgenes = vista";
        }
    }

    private void SetMapCombinedMinimized(bool minimized)
    {
        if (!IsMapCombinedMode)
            minimized = false;
        if (_isMapCombinedMinimized == minimized) return;
        _isMapCombinedMinimized = minimized;
        NotifyCombinedChrome();
        MinimizeCombinedMapsCommand.RaiseCanExecuteChanged();
        RestoreCombinedMapsCommand.RaiseCanExecuteChanged();
        StatusText = minimized
            ? "Combinado minimizado · barra inferior · Restaurar para volver al mosaico"
            : "Combinado restaurado";
    }

    private void NotifyCombinedChrome()
    {
        OnPropertyChanged(nameof(IsMapCombinedMinimized));
        OnPropertyChanged(nameof(ShowFloatingMapWindows));
        OnPropertyChanged(nameof(ShowCombinedMapsViewport));
        OnPropertyChanged(nameof(ShowCombinedMinimizedBar));
        OnPropertyChanged(nameof(ShowMapCombineEntryActions));
        OnPropertyChanged(nameof(ShowMapCombinedToolbarExtras));
        OnPropertyChanged(nameof(IsCombinedMapsMultiSelect));
        OnPropertyChanged(nameof(MapCombinedModeLabel));
    }

    public event Action? RequestFitMap;
    public event Action? RequestZoom100;
    public event Action? RequestResetPanels;
    public event Action? RequestApplyLayout;
    public event Action<int>? ScrollCatalogToGfxId;

    /// <summary>Raised when a map document is opened into a new floating window.</summary>
    public event Action<OpenMapDocument>? DocumentOpened;
    /// <summary>Raised when a map document window is closed.</summary>
    public event Action<OpenMapDocument>? DocumentClosed;
    /// <summary>Raised when the active (focused) map document changes.</summary>
    public event Action<OpenMapDocument>? DocumentActivated;

    public OpenMapDocument? ActiveDocument => _activeDocument;
    public IReadOnlyList<OpenMapDocument> OpenDocuments => _openDocuments;

    public ObservableCollection<int> MapIds { get; }

    /// <summary>Sidebar map list with lazy-rendered thumbnails.</summary>
    public ObservableCollection<MapPickerItemVm> MapListItems { get; }
    public ObservableCollection<FolderNodeVm> FolderTree { get; }
    public ObservableCollection<GfxRowVm> VisibleGfxRows { get; }
    public ObservableCollection<MovementDisplayItem> MovementDisplayOptions { get; }
    public ObservableCollection<FightCellDisplayItem> FightCellDisplayOptions { get; }
    public ObservableCollection<int> RotationOptions { get; }
    public ObservableCollection<string> RecentProjects { get; }

    public bool IsAstriaImport =>
        _session?.Source?.Kind == "LegacyAstria" && string.IsNullOrWhiteSpace(_session.FilePath);

    public bool HasProjectFile => !string.IsNullOrWhiteSpace(_session?.FilePath);

    public RelayCommand SelectLibraryCommand { get; }
    public RelayCommand OpenMapDialogCommand { get; }
    public RelayCommand NewMapCommand { get; }
    public RelayCommand CloseMapCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand FitMapCommand { get; }
    public RelayCommand Zoom100Command { get; }
    public RelayCommand ResetPanelsCommand { get; }
    public RelayCommand ReloadOriginalCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand ExportSwfCommand { get; }
    public RelayCommand ExportPackageCommand { get; }
    public RelayCommand OpenMapFolderCommand { get; }
    public RelayCommand RevertToSavedCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand CombineOpenMapsCommand { get; }
    public RelayCommand AppendActiveMapToWorldCommand { get; }
    public RelayCommand ExitMapCombinedModeCommand { get; }
    public RelayCommand MinimizeCombinedMapsCommand { get; }
    public RelayCommand RestoreCombinedMapsCommand { get; }
    public RelayCommand SelectAllCombinedMapChipsCommand { get; }
    public RelayCommand ClearCombinedMapChipsKeepFirstCommand { get; }
    public RelayCommand SendCombinedToWorldCommand { get; }
    public RelayCommand SendWorldToCombinedCommand { get; }
    public RelayCommand SaveSelectedCombinedMapCommand { get; }
    public RelayCommand SaveAllCombinedMapsCommand { get; }
    public RelayCommand AddCombinedMapsToPublishQueueCommand { get; }
    public RelayCommand ExportCombinedWorldCommand { get; }
    public RelayCommand ClearMapListFilterCommand { get; }
    public RelayCommand SetToolSelectCommand { get; }
    public RelayCommand SetToolRectSelectCommand { get; }
    public RelayCommand SetToolPaintCommand { get; }
    public RelayCommand SetToolEraseCommand { get; }
    public RelayCommand SetToolEyedropperCommand { get; }
    public RelayCommand SetToolUnwalkableCommand { get; }
    public RelayCommand SetToolLineOfSightCommand { get; }
    public RelayCommand SetToolFightCell1Command { get; }
    public RelayCommand SetToolFightCell2Command { get; }
    public RelayCommand SetToolPanCommand { get; }
    public RelayCommand CycleBrushRotationCommand { get; }
    public RelayCommand SetLayerGroundCommand { get; }
    public RelayCommand SetLayerObject1Command { get; }
    public RelayCommand SetLayerObject2Command { get; }
    public RelayCommand ClearGroundCommand { get; }
    public RelayCommand ClearObject1Command { get; }
    public RelayCommand ClearObject2Command { get; }
    public RelayCommand ClearActiveLayerCommand { get; }
    public RelayCommand ApplyBrushToSelectionCommand { get; }
    public RelayCommand DeleteSelectedGfxOnActiveLayerCommand { get; }
    public RelayCommand DeleteSelectedGfxOnObject1Command { get; }
    public RelayCommand DeleteSelectedGfxOnObject2Command { get; }
    public RelayCommand DeleteSelectedGfxOnAllLayersCommand { get; }
    public RelayCommand DeleteSelectedGfxInSelectionCommand { get; }
    public RelayCommand RotateSelectedGfxOnActiveLayerCommand { get; }
    public RelayCommand RotateSelectedGfxInSelectionCommand { get; }
    public RelayCommand SelectAllFocusGfxCommand { get; }
    public RelayCommand DeleteAllFocusGfxCommand { get; }
    public RelayCommand FlipAllFocusGfxCommand { get; }
    public RelayCommand RotateAllFocusGfxCommand { get; }
    public RelayCommand MoveFocusGfxToGroundCommand { get; }
    public RelayCommand MoveFocusGfxToObject1Command { get; }
    public RelayCommand MoveFocusGfxToObject2Command { get; }
    public RelayCommand ClearMapSelectionCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand LocateGroundInCatalogCommand { get; }
    public RelayCommand LocateObject1InCatalogCommand { get; }
    public RelayCommand LocateObject2InCatalogCommand { get; }
    public RelayCommand SelectInspectorGroundCommand { get; }
    public RelayCommand SelectInspectorObject1Command { get; }
    public RelayCommand SelectInspectorObject2Command { get; }
    public RelayCommand CopyCellMapDataCodeCommand { get; }
    public RelayCommand CopyFullMapDataCommand { get; }
    public RelayCommand OpenBackgroundPickerCommand { get; }
    public RelayCommand OpenAppearanceCommand { get; }
    public RelayCommand OpenDatabaseSettingsCommand { get; }
    public RelayCommand PublishToDatabaseCommand { get; }
    public RelayCommand SyncMetadataFromDatabaseCommand { get; }
    public RelayCommand GenerateLocalLangMapsCommand { get; }
    public RelayCommand OpenLangSftpSettingsCommand { get; }
    public RelayCommand OpenClipsSettingsCommand { get; }
    public RelayCommand OpenLicenseStatusCommand { get; }
    public RelayCommand SyncRemoteLangCommand { get; }
    public RelayCommand PublishRemoteLangCommand { get; }

    /// <summary>LIB.4.5 — Monstruos del mapa (población natural mapas.mobs). Sin escritura BD en esta fase.</summary>
    public MapMonstersEditorViewModel MapMonsters { get; }

    /// <summary>MAP-BATCH.1 — cola de publicación de mapas (BD + un maps_es N+1).</summary>
    public MapPublishQueueViewModel MapPublishQueue { get; }

    public bool IsThemeSystem
    {
        get => _settings.Theme == ThemePreference.System;
        set { if (value) SetTheme(ThemePreference.System); }
    }

    public bool IsThemeLight
    {
        get => _settings.Theme == ThemePreference.Light;
        set { if (value) SetTheme(ThemePreference.Light); }
    }

    public bool IsThemeDark
    {
        get => _settings.Theme == ThemePreference.Dark;
        set { if (value) SetTheme(ThemePreference.Dark); }
    }

    /// <summary>Barra: sol/luna. True = oscuro, False = claro (sin modo Sistema).</summary>
    public bool IsDarkTheme
    {
        get => _settings.Theme == ThemePreference.Dark
               || (_settings.Theme == ThemePreference.System && ThemeService.IsDarkEffective);
        set => SetTheme(value ? ThemePreference.Dark : ThemePreference.Light);
    }

    public RelayCommand SoloBackgroundCommand { get; }
    public RelayCommand SoloGroundCommand { get; }
    public RelayCommand SoloObject1Command { get; }
    public RelayCommand SoloObject2Command { get; }
    public RelayCommand RestoreVisibilityCommand { get; }
    public RelayCommand ResetLayoutCommand { get; }
    public RelayCommand ToggleMapsPanelCommand { get; }
    public RelayCommand ToggleInspectorPanelCommand { get; }
    public RelayCommand FocusMonstersPanelCommand { get; }
    public RelayCommand ToggleCatalogPanelCommand { get; }
    public RelayCommand ToggleCategoriesPanelCommand { get; }
    public RelayCommand ToggleBrushPanelCommand { get; }
    public RelayCommand ToggleToolBarCommand { get; }
    public RelayCommand ToggleStatusBarCommand { get; }

    public string? EffectiveLibraryPath => _effectiveLibraryPath;
    public LibrarySource LibrarySource => _librarySource;

    public string CatalogHeaderTitle
    {
        get => _catalogHeaderTitle;
        private set => SetProperty(ref _catalogHeaderTitle, value);
    }

    public string CatalogHeaderDestination
    {
        get => _catalogHeaderDestination;
        private set => SetProperty(ref _catalogHeaderDestination, value);
    }

    public string BrushTypeLabel
    {
        get => _brushTypeLabel;
        private set => SetProperty(ref _brushTypeLabel, value);
    }

    public string BrushTargetLabel
    {
        get => _brushTargetLabel;
        private set => SetProperty(ref _brushTargetLabel, value);
    }

    public string BrushGfxIdLabel
    {
        get => _brushGfxIdLabel;
        private set => SetProperty(ref _brushGfxIdLabel, value);
    }

    public string BrushFlipLabel
    {
        get => _brushFlipLabel;
        private set => SetProperty(ref _brushFlipLabel, value);
    }

    public string BrushRotationLabel
    {
        get => _brushRotationLabel;
        private set => SetProperty(ref _brushRotationLabel, value);
    }

    public string BrushDimensionsLabel
    {
        get => _brushDimensionsLabel;
        private set => SetProperty(ref _brushDimensionsLabel, value);
    }

    public bool ShowMapsPanel
    {
        get => _settings.UiLayout.ShowMapsPanel;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowMapsPanel = v, value, nameof(ShowMapsPanel));
    }

    public bool ShowInspectorPanel
    {
        get => _settings.UiLayout.ShowInspectorPanel;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowInspectorPanel = v, value, nameof(ShowInspectorPanel));
    }

    public bool ShowCatalogPanel
    {
        get => _settings.UiLayout.ShowCatalogPanel;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowCatalogPanel = v, value, nameof(ShowCatalogPanel));
    }

    public bool ShowCategoriesPanel
    {
        get => _settings.UiLayout.ShowCategoriesPanel;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowCategoriesPanel = v, value, nameof(ShowCategoriesPanel));
    }

    public bool ShowBrushPanel
    {
        get => _settings.UiLayout.ShowBrushPanel;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowBrushPanel = v, value, nameof(ShowBrushPanel));
    }

    public bool ShowToolBar
    {
        get => _settings.UiLayout.ShowToolBar;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowToolBar = v, value, nameof(ShowToolBar));
    }

    public bool ShowStatusBar
    {
        get => _settings.UiLayout.ShowStatusBar;
        set => SetLayoutFlag(v => _settings.UiLayout.ShowStatusBar = v, value, nameof(ShowStatusBar));
    }

    public UiLayoutSettings UiLayout => _settings.UiLayout;

    public int GfxColumns
    {
        get => _gfxColumns;
        private set
        {
            if (value == _gfxColumns) return;
            _gfxColumns = value;
            OnPropertyChanged(nameof(GfxColumns));
        }
    }

    public int CatalogRefreshCount => _catalogRefreshCount;

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string MapWindowTitle
    {
        get
        {
            if (CurrentMap is null) return "Map";
            var dirty = IsDirty ? " *" : "";
            return $"Map {CurrentMap.Id}{dirty}";
        }
    }

    public string LibraryStatusMessage
    {
        get => _libraryStatusMessage;
        private set => SetProperty(ref _libraryStatusMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ZoomText
    {
        get => _zoomText;
        set => SetProperty(ref _zoomText, value);
    }

    public string CellIdText
    {
        get => _cellIdText;
        private set => SetProperty(ref _cellIdText, value);
    }

    public string CoordsText
    {
        get => _coordsText;
        private set => SetProperty(ref _coordsText, value);
    }

    public string RenderTimeText
    {
        get => _renderTimeText;
        private set => SetProperty(ref _renderTimeText, value);
    }

    public string EditLatencyText
    {
        get => _editLatencyText;
        private set => SetProperty(ref _editLatencyText, value);
    }

    public string ActiveLayerLabel
    {
        get => _activeLayerLabel;
        private set => SetProperty(ref _activeLayerLabel, value);
    }

    public string ActiveToolLabel => Tool switch
    {
        EditorTool.Unwalkable => "Celda: No transitable",
        EditorTool.LineOfSight => "Celda: Bloquear visión",
        EditorTool.FightCell1 => "Combate: Equipo 1",
        EditorTool.FightCell2 => "Combate: Equipo 2",
        EditorTool.Paint or EditorTool.Erase or EditorTool.Eyedropper =>
            $"GFX: {UiDisplayLabels.LayerTarget(PaintLayer)}",
        EditorTool.Select => SelectedGfxId is int
            ? $"Seleccionar · {UiDisplayLabels.LayerTarget(PaintLayer)}"
            : "Seleccionar",
        _ => "—",
    };

    public string SelectedGfxLabel
    {
        get => _selectedGfxLabel;
        private set => SetProperty(ref _selectedGfxLabel, value);
    }

    public string UndoLabel =>
        IsWorldTab
            ? World.UndoWorldLabel
            : IsMapCombinedMode && CombinedMaps.CanUndoWorld
                ? "Deshacer (combinado)"
                : MosaicHost.IsMultiMapEditMode && MultiMap.History.CanUndo
                    ? "Deshacer (multimap)"
                    : _session?.History.UndoName is string name ? $"Deshacer {name}" : "Deshacer";

    public string RedoLabel =>
        IsWorldTab
            ? World.RedoWorldLabel
            : IsMapCombinedMode && CombinedMaps.CanRedoWorld
                ? "Rehacer (combinado)"
                : MosaicHost.IsMultiMapEditMode && MultiMap.History.CanRedo
                    ? "Rehacer (multimap)"
                    : _session?.History.RedoName is string name ? $"Rehacer {name}" : "Rehacer";

    public bool CanUndo => CanUndoNow();
    public bool CanRedo => CanRedoNow();

    private bool CanUndoNow() =>
        IsWorldTab
            ? World.CanUndoWorld
            : (IsMapCombinedMode && CombinedMaps.CanUndoWorld)
              || (MosaicHost.IsMultiMapEditMode && MultiMap.History.CanUndo)
              || _session?.History.CanUndo == true;

    private bool CanRedoNow() =>
        IsWorldTab
            ? World.CanRedoWorld
            : (IsMapCombinedMode && CombinedMaps.CanRedoWorld)
              || (MosaicHost.IsMultiMapEditMode && MultiMap.History.CanRedo)
              || _session?.History.CanRedo == true;

    public void NotifyMosaicUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private bool CanCopyNow() =>
        IsWorldTab ? World.HasSingleSelection : HasSelection;

    private bool CanPasteNow() =>
        IsWorldTab
            ? World.PasteCommand.CanExecute(null)
            : SelectedGfxId is int
              || (_session?.Clipboard is not null && CurrentMap is not null)
              || (IsMapCombinedMode && MultiMap.HasClipboard);

    public string GfxSearch
    {
        get => _gfxSearch;
        set
        {
            if (SetProperty(ref _gfxSearch, value))
            {
                _gfxSearchDebounce.Stop();
                _gfxSearchDebounce.Start();
            }
        }
    }

    public InspectorLayerHighlight HighlightedInspectorLayer
    {
        get => _highlightedInspectorLayer;
        private set
        {
            if (!SetProperty(ref _highlightedInspectorLayer, value))
                return;
            OnPropertyChanged(nameof(IsInspectorGroundHighlighted));
            OnPropertyChanged(nameof(IsInspectorObject1Highlighted));
            OnPropertyChanged(nameof(IsInspectorObject2Highlighted));
        }
    }

    public bool IsInspectorGroundHighlighted => HighlightedInspectorLayer == InspectorLayerHighlight.Ground;
    public bool IsInspectorObject1Highlighted => HighlightedInspectorLayer == InspectorLayerHighlight.Object1;
    public bool IsInspectorObject2Highlighted => HighlightedInspectorLayer == InspectorLayerHighlight.Object2;

    public bool HasSingleCellSelection => SelectedCellIds.Count == 1;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    public bool IsIdle => !IsLoading;

    public bool HasLibrary
    {
        get => _hasLibrary;
        private set
        {
            if (SetProperty(ref _hasLibrary, value))
                OpenMapDialogCommand.RaiseCanExecuteChanged();
                NewMapCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (!SetProperty(ref _showGrid, value)) return;
            PersistMapViewVisibility();
        }
    }

    public bool ShowDebugInfo
    {
        get => _showDebugInfo;
        set => SetProperty(ref _showDebugInfo, value);
    }

    public bool ShowBackgroundLayer
    {
        get => _showBackgroundLayer;
        set { if (SetProperty(ref _showBackgroundLayer, value)) OnLayerVisibilityChanged(); }
    }

    public bool ShowGroundLayer
    {
        get => _showGroundLayer;
        set { if (SetProperty(ref _showGroundLayer, value)) OnLayerVisibilityChanged(); }
    }

    public bool ShowObject1Layer
    {
        get => _showObject1Layer;
        set { if (SetProperty(ref _showObject1Layer, value)) OnLayerVisibilityChanged(); }
    }

    public bool ShowObject2Layer
    {
        get => _showObject2Layer;
        set { if (SetProperty(ref _showObject2Layer, value)) OnLayerVisibilityChanged(); }
    }

    public bool ShowCellIds
    {
        get => _showCellIds;
        set
        {
            if (!SetProperty(ref _showCellIds, value)) return;
            OnPropertyChanged(nameof(ShowCellIdsEffective));
            PersistMapViewVisibility();
        }
    }

    public double ViewportZoom
    {
        get => _viewportZoom;
        set
        {
            if (!SetProperty(ref _viewportZoom, value)) return;
            OnPropertyChanged(nameof(ShowCellIdsEffective));
        }
    }

    public bool ShowCellIdsEffective => ShowCellIds && ViewportZoom >= 0.35;

    public bool ShowUnwalkableMarkers
    {
        get => _showUnwalkableMarkers;
        set
        {
            if (!SetProperty(ref _showUnwalkableMarkers, value)) return;
            BumpCellModeOverlayRevision();
            PersistMapViewVisibility();
        }
    }

    public bool ShowLosBlockMarkers
    {
        get => _showLosBlockMarkers;
        set
        {
            if (!SetProperty(ref _showLosBlockMarkers, value)) return;
            BumpCellModeOverlayRevision();
            PersistMapViewVisibility();
        }
    }

    public bool ShowFightMarkers
    {
        get => _showFightMarkers;
        set
        {
            if (!SetProperty(ref _showFightMarkers, value)) return;
            BumpCellModeOverlayRevision();
            PersistMapViewVisibility();
        }
    }

    public bool ShowMapExportLimit
    {
        get => _showMapExportLimit;
        set => SetProperty(ref _showMapExportLimit, value);
    }

    private void BumpCellModeOverlayRevision() => CellModeOverlayRevision++;

    /// <summary>Dirty state comes only from the document session history.</summary>
    public bool IsDirty =>
        _session?.IsDirty == true
        || _openWorlds.Any(s => s.Vm.IsDirty)
        || (_activeWorldSession is null && _worldShell.IsDirty);

    public int? SelectedMapId
    {
        get => _selectedMapId;
        set => SetProperty(ref _selectedMapId, value);
    }

    public int? HoveredCellId
    {
        get => _hoveredCellId;
        private set
        {
            if (SetProperty(ref _hoveredCellId, value))
            {
                if (!HasSelection)
                    CellIdText = value?.ToString() ?? "—";
            }
        }
    }

    /// <summary>Last (or first) clicked cell in the selection — used as paste/duplicate anchor.</summary>
    public int? PrimarySelectedCellId
    {
        get => _primarySelectedCellId;
        private set
        {
            if (SetProperty(ref _primarySelectedCellId, value))
            {
                OnPropertyChanged(nameof(SelectedCellId));
                CellIdText = value?.ToString()
                    ?? (HasSelection ? $"{SelectedCellIds.Count} celdas" : HoveredCellId?.ToString() ?? "—");
                RaiseFocusGfxUi();
            }
        }
    }

    /// <summary>Alias for overlay/viewport bindings that still use SelectedCellId.</summary>
    public int? SelectedCellId => PrimarySelectedCellId;

    public IReadOnlyList<int> SelectedCellIds
    {
        get => _selectedCellIds;
        private set
        {
            _selectedCellIds = value;
            OnPropertyChanged(nameof(SelectedCellIds));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasSelectedCellProp));
            RaiseSelectionCommands();
        }
    }

    public bool HasSelection => SelectedCellIds.Count > 0;
    public bool HasSelectedCellProp => HasSelection;

    /// <summary>MAP-AREA.1 — when true, new area selections are unioned into the current set.</summary>
    public bool KeepAddSelection
    {
        get => _keepAddSelection;
        set => SetProperty(ref _keepAddSelection, value);
    }

    public string SelectionSummaryLabel =>
        HasSelection
            ? $"Selección: {SelectionCount} celdas"
            : "Selección: 0 celdas";

    public string FillSelectionTooltip =>
        (SelectedGfxId ?? FocusGfxId) is not int gfxId
            ? "Selecciona un GFX del catálogo, o una celda con GFX (ESTE ÍTEM)."
            : HasSelection
                ? $"Rellenar {SelectionCount} celdas con GFX {gfxId} en {UiDisplayLabels.LayerTarget(PaintLayer)}"
                : "Selecciona un área primero.";

    public string ClearActiveLayerInSelectionTooltip =>
        HasSelection
            ? $"Vaciar {UiDisplayLabels.LayerTarget(PaintLayer)} en {SelectionCount} celdas"
            : "Selecciona un área primero.";

    public bool IsMovingSelection => _isMovingSelection;

    public IReadOnlyList<SelectionMovePreviewItem> MovePreviewItems => _movePreviewItems;

    public int MovePreviewOutsideCount =>
        _movePreviewItems.Count(p => p.IsOutside);

    public string SelectionMoveHint =>
        HasSelection
            ? "Arrastra la selección para mover GFX · Ctrl+clic añade · Fuera del mapa (rojo) = se elimina al soltar"
            : "Selecciona celdas (V / R) y arrástralas para mover los GFX";

    public bool CanMassEditSelectedGfx => CanUseFocusGfx;
    public bool CanMassRotateSelectedGfx =>
        CanMassEditSelectedGfx
        && (TryResolveFocusGfx(out _, out var layer)
            ? layer != PaintLayer.Object2
            : PaintLayer != PaintLayer.Object2);

    /// <summary>
    /// GFX the user is working on: from selected cell content (correct layer), else catalog brush.
    /// </summary>
    public int? FocusGfxId =>
        TryResolveFocusGfx(out var gfxId, out _) ? gfxId : null;

    public bool CanUseFocusGfx => CurrentMap is not null && FocusGfxId is int;

    public int FocusGfxCountOnActiveLayer
    {
        get
        {
            if (!TryResolveFocusGfx(out var gfxId, out var layer))
                return 0;

            if (IsMapCombinedMode && MultiMap.IsActive)
                return MultiMap.CountMatchingGfx(GetEsteItemMapKeys(), layer, gfxId);

            if (CurrentMap is null) return 0;
            var n = 0;
            foreach (var cell in CurrentMap.Cells)
            {
                if (GetLayerGfx(cell, layer) == gfxId)
                    n++;
            }

            return n;
        }
    }

    public string FocusGfxSummary
    {
        get
        {
            if (!TryResolveFocusGfx(out var id, out var layer))
                return "Selecciona una celda con un objeto (o elige un GFX en el catálogo)";

            var count = FocusGfxCountOnActiveLayer;
            var maps = IsMapCombinedMode ? GetEsteItemMapKeys().Count : 1;
            var scope = maps > 1
                ? $"{count} en {maps} mapas"
                : $"{count} en el mapa";
            var rotHint = layer == PaintLayer.Object2
                ? " · Capa 2: solo voltear (sin rotación en MapData)"
                : "";
            return $"Ítem GFX {id} · {UiDisplayLabels.LayerTarget(layer)} · {scope}{rotHint}";
        }
    }

    public string SelectAllFocusGfxLabel => "Seleccionar todos";

    public string ReplaceAllFocusGfxLabel => "Reemplazar todos…";

    public string DeleteAllFocusGfxLabel => "Borrar todos";

    public string RotateAllFocusGfxLabel =>
        TryResolveFocusGfx(out _, out var layer) && layer == PaintLayer.Object2
            ? "Rotar +90° (no disponible en Capa 2)"
            : "Rotar +90°";

    public string FlipAllFocusGfxLabel => "Voltear horizontalmente";

    public string MoveFocusGfxSectionLabel =>
        TryResolveFocusGfx(out _, out var layer)
            ? $"Mover a otra capa (ahora: {UiDisplayLabels.LayerTarget(layer)})"
            : "Mover a otra capa";

    public bool CanMoveFocusGfxToGround => CanMoveFocusGfxTo(PaintLayer.Ground);
    public bool CanMoveFocusGfxToObject1 => CanMoveFocusGfxTo(PaintLayer.Object1);
    public bool CanMoveFocusGfxToObject2 => CanMoveFocusGfxTo(PaintLayer.Object2);

    private bool CanMoveFocusGfxTo(PaintLayer target) =>
        CanUseFocusGfx
        && TryResolveFocusGfx(out _, out var source)
        && source != target;

    /// <summary>
    /// When true, Erase (and matching strokes) only remove the catalog brush GFX on the active
    /// layer (Suelo / Capa 1 / Capa 2). Default off — erase clears the whole active layer.
    /// </summary>
    public bool EraseOnlySelectedGfx
    {
        get => _eraseOnlySelectedGfx;
        set
        {
            if (!SetProperty(ref _eraseOnlySelectedGfx, value)) return;
            if (value)
            {
                StatusText = SelectedGfxId is int id
                    ? $"Borrar solo GFX {id} en {UiDisplayLabels.LayerTarget(PaintLayer)}"
                    : "Borrar solo GFX · elige un GFX del catálogo y la capa (Suelo / Capa 1 / Capa 2)";
            }
        }
    }

    public string EraseOnlySelectedGfxTooltip =>
        "Si está activo, la goma solo quita el GFX del pincel en la capa destino (Suelo / Capa 1 / Capa 2). Sin GFX seleccionado no borra nada. Si está desactivado, vacía toda la capa destino.";

    /// <summary>Al pintar un GFX, marcar la celda como no transitable. Solo manual; no se guarda al reiniciar.</summary>
    private bool _paintMarksUnwalkable;

    /// <summary>En combinado: al pintar cerca de un borde, replicar el GFX en mapas vecinos para que se vea entero.</summary>
    private bool _paintSeam;

    public bool PaintMarksUnwalkable
    {
        get => _paintMarksUnwalkable;
        set
        {
            if (!SetProperty(ref _paintMarksUnwalkable, value)) return;
            StatusText = value
                ? "Pintar + no transitable · al colocar el ítem se bloquea la casilla"
                : "Pintar normal (sin cambiar transitabilidad)";
        }
    }

    public bool PaintSeam
    {
        get => _paintSeam;
        set
        {
            if (!SetProperty(ref _paintSeam, value)) return;
            StatusText = value
                ? "Pintar en costura · el GFX se replica en mapas vecinos si cruza el borde"
                : "Pintar en costura desactivado";
        }
    }

    public string PaintSeamTooltip =>
        "En modo combinado: si el ítem sobresale hacia un mapa de arriba/abajo/izquierda/derecha, se coloca también en la celda de borde de ese mapa para que se vea completo al entrar en él.";

    public string MapListSearchText
    {
        get => _mapListFilter.SearchText;
        set
        {
            var v = value ?? "";
            if (_mapListFilter.SearchText == v) return;
            _mapListFilter.SearchText = v;
            OnPropertyChanged();
            ApplyMapListFilter();
            SaveMapListFilterSettings();
        }
    }

    public string MapListRangeFromText
    {
        get => _mapListFilter.RangeFromText;
        set
        {
            var v = value ?? "";
            if (_mapListFilter.RangeFromText == v) return;
            _mapListFilter.RangeFromText = v;
            OnPropertyChanged();
            ApplyMapListFilter();
            SaveMapListFilterSettings();
        }
    }

    public string MapListRangeToText
    {
        get => _mapListFilter.RangeToText;
        set
        {
            var v = value ?? "";
            if (_mapListFilter.RangeToText == v) return;
            _mapListFilter.RangeToText = v;
            OnPropertyChanged();
            ApplyMapListFilter();
            SaveMapListFilterSettings();
        }
    }

    public bool MapListAscending
    {
        get => _mapListFilter.Ascending;
        set
        {
            if (_mapListFilter.Ascending == value) return;
            _mapListFilter.Ascending = value;
            OnPropertyChanged();
            ApplyMapListFilter();
            SaveMapListFilterSettings();
        }
    }

    public string MassGfxPanelTitle =>
        FocusGfxId is int id
            ? $"Este ítem · GFX {id}"
            : "Este ítem";

    /// <summary>
    /// Picks the GFX for ESTE ÍTEM and the layer where it actually lives on the selection.
    /// Avoids counting the catalog brush on an empty destination layer (the «0 en el mapa» bug).
    /// </summary>
    private bool TryResolveFocusGfx(out int gfxId, out PaintLayer layer)
    {
        gfxId = 0;
        layer = PaintLayer;
        if (CurrentMap is null)
            return false;

        static bool TryFromCell(
            CellData cell,
            PaintLayer prefer,
            bool objectLayersEnabled,
            out int id,
            out PaintLayer foundLayer)
        {
            id = 0;
            foundLayer = prefer;
            var onPrefer = GetLayerGfx(cell, prefer);
            if (onPrefer > 0)
            {
                id = onPrefer;
                foundLayer = prefer;
                return true;
            }

            foreach (var candidate in new[] { PaintLayer.Object1, PaintLayer.Object2, PaintLayer.Ground })
            {
                if (!objectLayersEnabled && candidate != PaintLayer.Ground)
                    continue;
                var g = GetLayerGfx(cell, candidate);
                if (g <= 0) continue;
                id = g;
                foundLayer = candidate;
                return true;
            }

            return false;
        }

        if (PrimarySelectedCellId is int primaryId
            && primaryId >= 0 && primaryId < CurrentMap.Cells.Count
            && TryFromCell(CurrentMap.Cells[primaryId], PaintLayer, AreGfxObjectLayersEnabled, out gfxId, out layer))
            return true;

        foreach (var cellId in SelectedCellIds)
        {
            if (cellId < 0 || cellId >= CurrentMap.Cells.Count) continue;
            if (TryFromCell(CurrentMap.Cells[cellId], PaintLayer, AreGfxObjectLayersEnabled, out gfxId, out layer))
                return true;
        }

        // Catalog brush only when nothing is selected — never pretend an empty cell "has" the brush GFX.
        if (!HasSelection && SelectedGfxId is int brush && brush > 0)
        {
            gfxId = brush;
            layer = PaintLayer;
            return true;
        }

        return false;
    }

    public bool IsRectSelecting => _rectSelecting;

    public bool HasAnyDirtyCombinedMap =>
        _openDocuments.Any(d => d.IsDirty) || CombinedMaps.ModifiedMapCount > 0;

    public (double X0, double Y0, double X1, double Y1)? RectSelectBounds =>
        _rectSelecting ? (_rectX0, _rectY0, _rectX1, _rectY1) : null;

    public MapDocument? CurrentMap
    {
        get => _currentMap;
        private set
        {
            if (SetProperty(ref _currentMap, value))
            {
                CloseMapCommand.RaiseCanExecuteChanged();
                FitMapCommand.RaiseCanExecuteChanged();
                Zoom100Command.RaiseCanExecuteChanged();
                ReloadOriginalCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasMap));
                RefreshMapInspector();
                UpdateTitle();
            }
        }
    }

    public bool HasMap => CurrentMap is not null;

    public ImageSource? MapImage
    {
        get => _mapImage;
        private set
        {
            if (SetProperty(ref _mapImage, value))
            {
                if (_activeDocument is not null)
                    _activeDocument.MapImage = value;
                FitMapCommand.RaiseCanExecuteChanged();
                Zoom100Command.RaiseCanExecuteChanged();
            }
        }
    }

    public ImageSource? PreviewGround
    {
        get => _previewGround;
        private set => SetProperty(ref _previewGround, value);
    }

    public ImageSource? PreviewObject1
    {
        get => _previewObject1;
        private set => SetProperty(ref _previewObject1, value);
    }

    public ImageSource? PreviewObject2
    {
        get => _previewObject2;
        private set => SetProperty(ref _previewObject2, value);
    }

    public ImageSource? BrushPreview
    {
        get => _brushPreview;
        private set => SetProperty(ref _brushPreview, value);
    }

    public IsoHitTester? HitTester
    {
        get => _hitTester;
        private set => SetProperty(ref _hitTester, value);
    }

    public IReadOnlyList<string> ResourceWarnings
    {
        get => _resourceWarnings;
        private set
        {
            if (SetProperty(ref _resourceWarnings, value))
                OnPropertyChanged(nameof(HasResourceWarnings));
        }
    }

    public bool HasResourceWarnings => ResourceWarnings.Count > 0;

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (SetProperty(ref _tool, value))
            {
                FinishStroke();
                OnPropertyChanged(nameof(IsSelectTool));
                OnPropertyChanged(nameof(IsRectSelectTool));
                OnPropertyChanged(nameof(IsPaintTool));
                OnPropertyChanged(nameof(IsEraseTool));
                OnPropertyChanged(nameof(IsEyedropperTool));
                OnPropertyChanged(nameof(IsUnwalkableTool));
                OnPropertyChanged(nameof(IsLineOfSightTool));
                OnPropertyChanged(nameof(IsFightCell1Tool));
                OnPropertyChanged(nameof(IsFightCell2Tool));
                OnPropertyChanged(nameof(IsMobCellTool));
                OnPropertyChanged(nameof(IsPanTool));
                OnPropertyChanged(nameof(IsCellModeTool));
                OnPropertyChanged(nameof(AreGfxObjectLayersEnabled));
                SetLayerObject1Command.RaiseCanExecuteChanged();
                SetLayerObject2Command.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(MapCombinedModeLabel));
                // Fight placement is not a GFX layer — park paint target on Ground and lock C1/C2.
                if ((value is EditorTool.FightCell1 or EditorTool.FightCell2) &&
                    PaintLayer != PaintLayer.Ground)
                    PaintLayer = PaintLayer.Ground;
                StatusText = value switch
                {
                    EditorTool.Paint => BuildPaintBrushStatus(),
                    EditorTool.Erase => "Herramienta: Borrar GFX",
                    EditorTool.Eyedropper => "Herramienta: Cuentagotas",
                    EditorTool.RectSelect => "Herramienta: Selección rectangular",
                    EditorTool.Unwalkable => "Herramienta: No transitable",
                    EditorTool.LineOfSight => "Herramienta: Bloquear visión",
                    EditorTool.FightCell1 => "Herramienta: Combate — Equipo 1",
                    EditorTool.FightCell2 => "Herramienta: Combate — Equipo 2",
                    EditorTool.MobCell => "Herramienta: Grupos fijos (inactiva · LIB.4 aislado)",
                    EditorTool.Pan => "Herramienta: Mover vista (arrastra el mapa)",
                    _ => SelectedGfxId is int gfx
                        ? $"Seleccionar · GFX {gfx} en {UiDisplayLabels.LayerTarget(PaintLayer)} · Construir (B) para colocarlo"
                        : "Herramienta: Seleccionar",
                };
                OnPropertyChanged(nameof(ActiveToolLabel));
            }
        }
    }

    /// <summary>
    /// Capa 1 / Capa 2 (GFX) are irrelevant while placing fight cells — UI should disable them.
    /// </summary>
    public bool AreGfxObjectLayersEnabled =>
        Tool is not (EditorTool.FightCell1 or EditorTool.FightCell2);

    public bool IsSelectTool
    {
        get => Tool == EditorTool.Select;
        set { if (value) Tool = EditorTool.Select; }
    }

    public bool IsRectSelectTool
    {
        get => Tool == EditorTool.RectSelect;
        set { if (value) Tool = EditorTool.RectSelect; }
    }

    public bool IsPaintTool
    {
        get => Tool == EditorTool.Paint;
        set { if (value) Tool = EditorTool.Paint; }
    }

    public bool IsEraseTool
    {
        get => Tool == EditorTool.Erase;
        set { if (value) Tool = EditorTool.Erase; }
    }

    public bool IsEyedropperTool
    {
        get => Tool == EditorTool.Eyedropper;
        set { if (value) Tool = EditorTool.Eyedropper; }
    }

    public bool IsUnwalkableTool
    {
        get => Tool == EditorTool.Unwalkable;
        set { if (value) Tool = EditorTool.Unwalkable; }
    }

    public bool IsLineOfSightTool
    {
        get => Tool == EditorTool.LineOfSight;
        set { if (value) Tool = EditorTool.LineOfSight; }
    }

    public bool IsFightCell1Tool
    {
        get => Tool == EditorTool.FightCell1;
        set { if (value) Tool = EditorTool.FightCell1; }
    }

    public bool IsFightCell2Tool
    {
        get => Tool == EditorTool.FightCell2;
        set { if (value) Tool = EditorTool.FightCell2; }
    }

    public bool IsMobCellTool
    {
        get => Tool == EditorTool.MobCell;
        set { if (value) Tool = EditorTool.MobCell; }
    }

    public bool IsPanTool
    {
        get => Tool == EditorTool.Pan;
        set { if (value) Tool = EditorTool.Pan; }
    }

    public bool IsCellModeTool => Tool.IsCellModeTool();

    public int CellModeOverlayRevision
    {
        get => _cellModeOverlayRevision;
        private set => SetProperty(ref _cellModeOverlayRevision, value);
    }

    /// <summary>Bumped when mobs_fix markers for the open map change (LIB.4).</summary>
    public int FixedMobsOverlayRevision
    {
        get => _fixedMobsOverlayRevision;
        private set => SetProperty(ref _fixedMobsOverlayRevision, value);
    }

    public PaintLayer PaintLayer
    {
        get => _paintLayer;
        set
        {
            if (_paintLayer == value) return;
            var oldCategory = _paintLayer.ToGfxCategory();
            if (!SetProperty(ref _paintLayer, value)) return;

            FinishStroke();
            OnPropertyChanged(nameof(IsLayerGround));
            OnPropertyChanged(nameof(IsLayerObject1));
            OnPropertyChanged(nameof(IsLayerObject2));
            if (oldCategory != value.ToGfxCategory())
                SelectedGfxId = null;
            CycleBrushRotationCommand.RaiseCanExecuteChanged();
            SyncSelectedCategoryFromPaintLayer();
            RefreshLayerLabels();
            RefreshVisibleGfx();
            OnPropertyChanged(nameof(ActiveToolLabel));
            OnPropertyChanged(nameof(FillSelectionTooltip));
            OnPropertyChanged(nameof(ClearActiveLayerInSelectionTooltip));
            RaiseFocusGfxUi();
            RaiseMassGfxCommands();
        }
    }

    public GfxCategory SelectedCategory
    {
        get => _selectedCategory;
        private set
        {
            if (_selectedCategory == value) return;
            _selectedCategory = value;
            OnPropertyChanged(nameof(SelectedCategory));
            RefreshVisibleGfx();
        }
    }

    private void SyncSelectedCategoryFromPaintLayer() =>
        SelectedCategory = PaintLayer.ToGfxCategory();

    public bool IsLayerGround
    {
        get => PaintLayer == PaintLayer.Ground;
        set { if (value) PaintLayer = PaintLayer.Ground; }
    }

    public bool IsLayerObject1
    {
        get => PaintLayer == PaintLayer.Object1;
        set
        {
            if (value && AreGfxObjectLayersEnabled)
                PaintLayer = PaintLayer.Object1;
        }
    }

    public bool IsLayerObject2
    {
        get => PaintLayer == PaintLayer.Object2;
        set
        {
            if (value && AreGfxObjectLayersEnabled)
                PaintLayer = PaintLayer.Object2;
        }
    }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
                RefreshVisibleGfx();
        }
    }

    public int? SelectedGfxId
    {
        get => _selectedGfxId;
        set
        {
            if (SetProperty(ref _selectedGfxId, value))
            {
                UpdateGfxCatalogSelectionHighlight();
                RefreshLayerLabels();
                UpdateBrushPreview();
                ApplyBrushToSelectionCommand.RaiseCanExecuteChanged();
                ToggleFavoriteCommand.RaiseCanExecuteChanged();
                RaiseMassGfxCommands();
                OnPropertyChanged(nameof(IsSelectedGfxFavorite));
                OnPropertyChanged(nameof(FillSelectionTooltip));
                OnPropertyChanged(nameof(CanMassEditSelectedGfx));
                OnPropertyChanged(nameof(CanMassRotateSelectedGfx));
                RaiseFocusGfxUi();
            }
        }
    }

    public bool IsSelectedGfxFavorite =>
        SelectedGfxId is int id && IsFavorite(CategoryKey(SelectedCategory), id);

    public bool BrushFlip
    {
        get => _brushFlip;
        set
        {
            if (SetProperty(ref _brushFlip, value))
                RefreshLayerLabels();
        }
    }

    public int BrushRotation
    {
        get => _brushRotation;
        set
        {
            var clamped = Math.Clamp(value, 0, 3);
            if (SetProperty(ref _brushRotation, clamped))
            {
                RefreshLayerLabels();
                CycleBrushRotationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string InfoMapId { get; private set; } = "—";
    public string InfoWidth { get; private set; } = "—";
    public string InfoHeight { get; private set; } = "—";
    public string InfoCellCount { get; private set; } = "—";
    public string InfoBackground { get; private set; } = "—";
    public string InfoMusic { get; private set; } = "—";
    public string InfoAmbiance { get; private set; } = "—";
    public string InfoCapabilities { get; private set; } = "—";
    public string InfoOutdoor { get; private set; } = "—";

    private string _editRevision = "";
    private string _editWorldX = "";
    private string _editWorldY = "";
    private string _editBackground = "";

    public string EditBackground
    {
        get => _editBackground;
        set
        {
            if (_suppressProp || CurrentMap is null || _session is null)
            {
                SetProperty(ref _editBackground, value ?? "");
                return;
            }

            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                // Vacío = sin fondo (0)
                SetProperty(ref _editBackground, "0");
                ApplyBackgroundId(0);
                return;
            }

            if (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var next) || next < 0)
            {
                _editBackground = CurrentMap.BackgroundId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(EditBackground));
                return;
            }

            SetProperty(ref _editBackground, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplyBackgroundId(next);
        }
    }

    public string EditRevision
    {
        get => _editRevision;
        set
        {
            if (!SetProperty(ref _editRevision, value ?? "") || _suppressProp || CurrentMap is null || _session is null)
                return;
            var next = value?.Trim() ?? "";
            var before = CurrentMap.DateMap ?? "";
            if (string.Equals(before, next, StringComparison.Ordinal))
                return;
            CurrentMap.DateMap = next;
            _session.History.PushExecuted(new MapStringFieldEditCommand("Cambiar revisión", before, next, (m, v) => m.DateMap = v));
            AfterHistoryChange();
        }
    }

    public string EditWorldX
    {
        get => _editWorldX;
        set
        {
            // Parse before SetProperty: re-entering "0" when the box already shows "0" must still
            // mark WorldCoordinatesSet (0,0 is a valid explicit coordinate).
            if (_suppressProp || CurrentMap is null || _session is null)
            {
                SetProperty(ref _editWorldX, value ?? "");
                return;
            }

            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                SetProperty(ref _editWorldX, "");
                CurrentMap.WorldCoordinatesSet = false;
                AfterHistoryChange();
                return;
            }

            if (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var next))
            {
                _editWorldX = CurrentMap.WorldCoordinatesSet
                    ? CurrentMap.WorldX.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "";
                OnPropertyChanged(nameof(EditWorldX));
                return;
            }

            SetProperty(ref _editWorldX, trimmed);
            var before = CurrentMap.WorldX;
            var wasSet = CurrentMap.WorldCoordinatesSet;
            CurrentMap.WorldCoordinatesSet = true;
            if (wasSet && before == next)
            {
                AfterHistoryChange();
                return;
            }

            CurrentMap.WorldX = next;
            _session.History.PushExecuted(new MapIntFieldEditCommand("Cambiar X", before, next, (m, v) =>
            {
                m.WorldX = v;
                m.WorldCoordinatesSet = true;
            }));
            AfterHistoryChange();
        }
    }

    public string EditWorldY
    {
        get => _editWorldY;
        set
        {
            if (_suppressProp || CurrentMap is null || _session is null)
            {
                SetProperty(ref _editWorldY, value ?? "");
                return;
            }

            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                SetProperty(ref _editWorldY, "");
                CurrentMap.WorldCoordinatesSet = false;
                AfterHistoryChange();
                return;
            }

            if (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var next))
            {
                _editWorldY = CurrentMap.WorldCoordinatesSet
                    ? CurrentMap.WorldY.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "";
                OnPropertyChanged(nameof(EditWorldY));
                return;
            }

            SetProperty(ref _editWorldY, trimmed);
            var before = CurrentMap.WorldY;
            var wasSet = CurrentMap.WorldCoordinatesSet;
            CurrentMap.WorldCoordinatesSet = true;
            if (wasSet && before == next)
            {
                AfterHistoryChange();
                return;
            }

            CurrentMap.WorldY = next;
            _session.History.PushExecuted(new MapIntFieldEditCommand("Cambiar Y", before, next, (m, v) =>
            {
                m.WorldY = v;
                m.WorldCoordinatesSet = true;
            }));
            AfterHistoryChange();
        }
    }

    public string CellInfoId { get; private set; } = "—";
    public string CellInfoGround { get; private set; } = "—";
    public string CellInfoObject1 { get; private set; } = "—";
    public string CellInfoObject2 { get; private set; } = "—";
    public string CellInfoGroundDetail { get; private set; } = "";
    public string CellInfoObject1Detail { get; private set; } = "";
    public string CellInfoObject2Detail { get; private set; } = "";

    public string InfoMapDataLength { get; private set; } = "—";
    public string CellMapDataCodeActual { get; private set; } = "—";
    public string CellMapDataCodeSource { get; private set; } = "—";
    public string CellMapDataCodeSaved { get; private set; } = "—";
    public string CellMapDataDiffHint { get; private set; } = "";
    public string CellMapDataRange { get; private set; } = "—";
    public bool ShowSingleCellMapData { get; private set; }
    public bool CanCopyCellMapData =>
        ShowSingleCellMapData && CellMapDataCodeActual.Length == MapDataConstants.CharsPerCell;

    public MovementType? EditMovement
    {
        get => _editMovement;
        set
        {
            if (SetProperty(ref _editMovement, value) && !_suppressProp && value is MovementType mv)
                CommitSelection("Cambiar Movement", (_, c) => MapCellEditor.SetMovement(c, mv));
            if (!_suppressProp)
                SyncEditMovementItemFromRaw();
        }
    }

    public MovementDisplayItem? EditMovementItem
    {
        get => _editMovementItem;
        set
        {
            if (!SetProperty(ref _editMovementItem, value) || _suppressProp || value is null)
                return;
            _editMovement = (MovementType)(value.RawValue & 7);
            OnPropertyChanged(nameof(EditMovement));
            CommitSelection("Cambiar Movement", (_, c) => MapCellEditor.SetMovement(c, (MovementType)(value.RawValue & 7)));
        }
    }

    public bool? EditLos
    {
        get => _editLos;
        set
        {
            if (SetProperty(ref _editLos, value) && !_suppressProp && value is bool v)
                CommitSelection("Cambiar LoS", (_, c) => MapCellEditor.SetLineOfSight(c, v));
        }
    }

    public bool? EditBlocksVision
    {
        get => _editLos is null ? null : !_editLos.Value;
        set
        {
            if (value is null)
            {
                if (SetProperty(ref _editLos, null) && !_suppressProp) { }
                return;
            }
            EditLos = !value.Value;
        }
    }

    public int? EditFightCell
    {
        get => _editFightCell;
        set
        {
            if (SetProperty(ref _editFightCell, value) && !_suppressProp && value is int v)
            {
                CommitSelection("Cambiar combate", (_, c) =>
                {
                    if (v > 0 && c.Movement == MovementType.Unwalkable)
                        return;
                    MapCellEditor.SetFightCell(c, v);
                });
            }
            if (!_suppressProp)
                SyncEditFightCellItemFromRaw();
        }
    }

    public FightCellDisplayItem? EditFightCellItem
    {
        get => _editFightCellItem;
        set
        {
            if (!SetProperty(ref _editFightCellItem, value) || _suppressProp || value is null)
                return;
            _editFightCell = value.Value;
            OnPropertyChanged(nameof(EditFightCell));
            CommitSelection("Cambiar combate", (_, c) =>
            {
                if (value.Value > 0 && c.Movement == MovementType.Unwalkable)
                    return;
                MapCellEditor.SetFightCell(c, value.Value);
            });
        }
    }

    public bool? EditIo
    {
        get => _editIo;
        set
        {
            if (SetProperty(ref _editIo, value) && !_suppressProp && value is bool v)
                CommitSelection("Cambiar Interactive", (_, c) => MapCellEditor.SetInteractive(c, v));
        }
    }

    public int? EditGroundLevel
    {
        get => _editGroundLevel;
        set
        {
            var v = value is null ? (int?)null : Math.Clamp(value.Value, 0, 15);
            if (SetProperty(ref _editGroundLevel, v) && !_suppressProp && v is int level)
                CommitSelection("Cambiar GroundLevel", (_, c) => MapCellEditor.SetGroundLevel(c, level));
        }
    }

    public int? EditGroundSlope
    {
        get => _editGroundSlope;
        set
        {
            var v = value is null ? (int?)null : Math.Clamp(value.Value, 0, 15);
            if (SetProperty(ref _editGroundSlope, v) && !_suppressProp && v is int slope)
                CommitSelection("Cambiar GroundSlope", (_, c) => MapCellEditor.SetGroundSlope(c, slope));
        }
    }

    public bool? EditFlipG
    {
        get => _editFlipG;
        set
        {
            if (SetProperty(ref _editFlipG, value) && !_suppressProp && value is bool v)
                CommitSelection("Cambiar Flip Ground", (_, c) => MapCellEditor.SetFlip(c, MapCellEditor.Layer.Ground, v));
        }
    }

    public bool? EditFlipO1
    {
        get => _editFlipO1;
        set
        {
            if (SetProperty(ref _editFlipO1, value) && !_suppressProp && value is bool v)
                CommitSelection("Cambiar Flip Layer 1", (_, c) => MapCellEditor.SetFlip(c, MapCellEditor.Layer.Object1, v));
        }
    }

    public bool? EditFlipO2
    {
        get => _editFlipO2;
        set
        {
            if (SetProperty(ref _editFlipO2, value) && !_suppressProp && value is bool v)
                CommitSelection("Cambiar Flip Layer 2", (_, c) => MapCellEditor.SetFlip(c, MapCellEditor.Layer.Object2, v));
        }
    }

    public int? EditRotG
    {
        get => _editRotG;
        set
        {
            var v = value is null ? (int?)null : Math.Clamp(value.Value, 0, 3);
            if (SetProperty(ref _editRotG, v) && !_suppressProp && v is int rot)
                CommitSelection("Cambiar Rot Ground", (_, c) => MapCellEditor.SetRotation(c, MapCellEditor.Layer.Ground, rot));
        }
    }

    public int? EditRotO1
    {
        get => _editRotO1;
        set
        {
            var v = value is null ? (int?)null : Math.Clamp(value.Value, 0, 3);
            if (SetProperty(ref _editRotO1, v) && !_suppressProp && v is int rot)
                CommitSelection("Cambiar Rot Layer 1", (_, c) => MapCellEditor.SetRotation(c, MapCellEditor.Layer.Object1, rot));
        }
    }

    public void SelectFolderNode(FolderNodeVm? node)
    {
        if (node is null) return;

        if (node.IsUnifiedFavorites)
        {
            _showUnifiedFavorites = true;
            SelectedFolder = FavoritesFolder;
            return;
        }

        _showUnifiedFavorites = false;

        if (node.Category is GfxCategory category)
        {
            SelectedCategory = category;
            SyncPaintLayerFromCategory(category);
        }

        SelectedFolder = node.Name;
    }

    private void SyncPaintLayerFromCategory(GfxCategory category)
    {
        if (category == GfxCategory.Ground)
        {
            if (PaintLayer != PaintLayer.Ground)
                PaintLayer = PaintLayer.Ground;
            return;
        }

        if (PaintLayer == PaintLayer.Ground)
            PaintLayer = PaintLayer.Object1;
    }

    private static void ApplyFightPlacesFromDocument(MapDocument map) =>
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);

    public void CycleBrushRotation()
    {
        if (PaintLayer == PaintLayer.Object2) return;
        BrushRotation = (BrushRotation + 1) % 4;
    }

    public void SelectGfx(GfxItemVm? item)
    {
        if (item is null) return;

        // MAP-PAINT.1 — re-clicking the active GFX is idempotent (no catalog rebuild / scroll jump).
        if (SelectedGfxId == item.Id)
        {
            if (Tool is EditorTool.Select or EditorTool.RectSelect or EditorTool.Eyedropper)
                Tool = EditorTool.Paint;
            return;
        }

        SelectedGfxId = item.Id;
        PushRecent(CategoryKey(item.Resource.Category), item.Id);
        if (Tool is EditorTool.Select or EditorTool.RectSelect or EditorTool.Eyedropper)
            Tool = EditorTool.Paint;
    }

    /// <summary>
    /// MAP-PAINT.1 — in paint mode, right-click removes only the active brush GFX on the active layer.
    /// Keeps <see cref="SelectedGfxId"/> so the user can keep painting. Returns false = no-op.
    /// </summary>
    public bool TryEraseActiveBrushAtCell(int cellId)
    {
        if (CurrentMap is null || _session is null) return false;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return false;
        if (SelectedGfxId is not int brushId) return false;

        var cell = CurrentMap.Cells[cellId];
        if (GetLayerGfx(cell, PaintLayer) != brushId)
            return false;

        BeginMatchingEraseStroke(brushId);
        EraseCell(cellId, isDrag: false);
        StatusText = $"Retirado GFX {brushId} — sigue activo (arrastra para borrar más)";
        return true;
    }

    /// <summary>Erase stroke that only clears cells matching <paramref name="gfxId"/> on the active layer.</summary>
    public void BeginMatchingEraseStroke(int gfxId)
    {
        _eraseRequireMatch = true;
        _eraseMatchGfxId = gfxId;
        _strokeIsErase = true;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke($"Borrar GFX {gfxId}");
    }

    /// <summary>Starts a paint stroke (MouseDown). No-op for other tools.</summary>
    public void BeginPaintStroke()
    {
        _eraseRequireMatch = false;
        _eraseMatchGfxId = null;
        _strokeIsErase = false;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(PaintStrokeName);
    }

    /// <summary>Starts an erase stroke (MouseDown, typically right button).</summary>
    public void BeginEraseStroke()
    {
        ConfigureEraseMatchFromBrush();
        _strokeIsErase = true;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(
            _eraseRequireMatch && _eraseMatchGfxId is int id
                ? $"Borrar GFX {id}"
                : Tool.IsCellModeTool() ? CellModeEraseStrokeName : EraseStrokeName);

        if (_eraseRequireMatch && _eraseMatchGfxId is null)
            StatusText = "Borrar solo GFX · selecciona un GFX y la capa destino primero";
    }

    /// <summary>Starts a cell-mode paint stroke (MouseDown).</summary>
    public void BeginCellModeStroke()
    {
        _eraseRequireMatch = false;
        _eraseMatchGfxId = null;
        _strokeIsErase = false;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(CellModeStrokeName);
    }

    /// <summary>Starts a cell-mode erase stroke (MouseDown, right button).</summary>
    public void BeginCellModeEraseStroke()
    {
        _eraseRequireMatch = false;
        _eraseMatchGfxId = null;
        _strokeIsErase = true;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(CellModeEraseStrokeName);
    }

    /// <summary>Starts a paint/erase stroke (MouseDown). No-op for other tools.</summary>
    public void BeginStroke()
    {
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        if (_session is null) return;
        if (Tool == EditorTool.Paint)
        {
            _eraseRequireMatch = false;
            _eraseMatchGfxId = null;
            _strokeIsErase = false;
            _session.BeginStroke(PaintStrokeName);
        }
        else if (Tool == EditorTool.Erase)
        {
            ConfigureEraseMatchFromBrush();
            _strokeIsErase = true;
            _session.BeginStroke(
                _eraseRequireMatch && _eraseMatchGfxId is int id ? $"Borrar GFX {id}" : EraseStrokeName);
            if (_eraseRequireMatch && _eraseMatchGfxId is null)
                StatusText = "Borrar solo GFX · selecciona un GFX y la capa destino primero";
        }
        else if (Tool.IsCellModeTool())
        {
            _eraseRequireMatch = false;
            _eraseMatchGfxId = null;
            _strokeIsErase = false;
            _session.BeginStroke(CellModeStrokeName);
        }
    }

    private void ConfigureEraseMatchFromBrush()
    {
        _eraseRequireMatch = EraseOnlySelectedGfx;
        _eraseMatchGfxId = EraseOnlySelectedGfx && SelectedGfxId is int matchId ? matchId : null;
    }

    /// <summary>Ends the current stroke as one undo command (MouseUp).</summary>
    public void FinishStroke()
    {
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _eraseMatchGfxId = null;
        _eraseRequireMatch = false;
        if (_session is null) return;
        if (_session.EndStroke())
        {
            AfterHistoryChange();
            if (Tool.IsCellModeTool())
                CellModeOverlayRevision++;
            else
                _ = RerenderAsync();
        }
    }

    /// <summary>Continues paint/erase along pointer segment (fast drag interpolation).</summary>
    public void ContinueStroke(double contentX, double contentY)
    {
        if (CurrentMap is null || HitTester is null) return;

        if (_lastStrokeContentX is null || _lastStrokeContentY is null)
        {
            if (HoveredCellId is int id)
            {
                if (Tool.IsCellModeTool())
                    PaintCellMode(id, isDrag: true, erase: _strokeIsErase);
                else if (_strokeIsErase)
                    EraseCell(id, isDrag: true);
                else
                    PaintCell(id, isDrag: true);
            }
        }
        else
        {
            foreach (var cellId in IsoStrokeInterpolation.CellsAlongSegment(
                         HitTester, _lastStrokeContentX.Value, _lastStrokeContentY.Value, contentX, contentY))
            {
                if (Tool.IsCellModeTool())
                    PaintCellMode(cellId, isDrag: true, erase: _strokeIsErase);
                else if (_strokeIsErase)
                    EraseCell(cellId, isDrag: true);
                else
                    PaintCell(cellId, isDrag: true);
            }
        }

        _lastStrokeContentX = contentX;
        _lastStrokeContentY = contentY;
    }

    public void PaintCell(int cellId, bool isDrag)
    {
        if (CurrentMap is null || _session is null) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;
        if (isDrag && cellId == _lastPaintedCell) return;
        if (SelectedGfxId is not int gfxId)
        {
            if (!isDrag)
                StatusText = "Selecciona un GFX del catálogo para pintar.";
            return;
        }

        if (!ValidateSelectedGfxForActiveLayer(out var paintError))
        {
            if (!isDrag)
                StatusText = paintError;
            return;
        }

        var layer = PaintLayer.ToEditorLayer();
        var rot = PaintLayer == PaintLayer.Object2 ? (int?)null : BrushRotation;
        var flip = BrushFlip;
        var markBlocked = PaintMarksUnwalkable;
        _session.StrokeMutate(cellId, c =>
        {
            MapCellEditor.SetLayerGfx(c, layer, gfxId, flip, rot);
            if (markBlocked)
                MapCellEditor.SetMovement(c, MovementType.Unwalkable);
        });
        _lastPaintedCell = cellId;
        if (!isDrag)
        {
            _session.SetSelection(new[] { cellId });
            PrimarySelectedCellId = cellId;
            SyncSelectionFromSession();
            PushRecent(CategoryKey(PaintLayer.ToGfxCategory()), gfxId);
        }

        _ = RerenderAsync();
    }

    public void PaintCellMode(int cellId, bool isDrag, bool erase)
    {
        if (CurrentMap is null || _session is null || !Tool.IsCellModeTool()) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;
        if (isDrag && cellId == _lastPaintedCell) return;

        // Fight tools cannot place on unwalkable cells — warn clearly on click.
        if (!erase &&
            Tool is EditorTool.FightCell1 or EditorTool.FightCell2 &&
            CurrentMap.Cells[cellId].Movement == MovementType.Unwalkable)
        {
            _lastPaintedCell = cellId;
            var msg = $"Celda {cellId}: esta celda es no transitable.\nNo se puede colocar combate aquí.";
            StatusText = msg;
            if (!isDrag)
            {
                MessageBox.Show(
                    msg,
                    "Combate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return;
        }

        _session.StrokeMutate(cellId, cell =>
        {
            switch (Tool)
            {
                case EditorTool.Unwalkable:
                    if (erase)
                        MapCellEditor.SetMovement(cell, MovementType.Walkable);
                    else
                        MapCellEditor.SetMovement(cell, MovementType.Unwalkable);
                    break;
                case EditorTool.LineOfSight:
                    MapCellEditor.SetLineOfSight(cell, erase);
                    break;
                case EditorTool.FightCell1:
                case EditorTool.FightCell2:
                    if (erase)
                    {
                        // Right-click clears any fight placement (team 1 or 2),
                        // regardless of which fight tool is active.
                        if (cell.FightCell is 1 or 2)
                            MapCellEditor.SetFightCell(cell, 0);
                    }
                    else if (Tool == EditorTool.FightCell1)
                        MapCellEditor.SetFightCell(cell, 1);
                    else
                        MapCellEditor.SetFightCell(cell, 2);
                    break;
            }
        });

        _lastPaintedCell = cellId;
        if (!isDrag)
        {
            _session.SetSelection(new[] { cellId });
            PrimarySelectedCellId = cellId;
            SyncSelectionFromSession();
        }

        OnPropertyChanged(nameof(CellModeOverlayRevision));
    }


    /// <summary>
    /// Right-click on a GFX: delete that layer, load it into the brush, switch to construction (Paint).
    /// </summary>
    public void DeleteGfxAndEnterBuildMode(int cellId)
    {
        if (CurrentMap is null || _session is null) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;

        var cell = CurrentMap.Cells[cellId];
        var layer = ResolveGfxLayerForDelete(cell);
        if (layer is null)
        {
            Tool = EditorTool.Paint;
            StatusText = "Modo construcción";
            return;
        }

        var gfxId = GetLayerGfx(cell, layer.Value);
        PaintLayer = layer.Value;
        if (gfxId > 0)
            SelectedGfxId = gfxId;
        HighlightedInspectorLayer = InspectorLayerHighlight.None;

        Tool = EditorTool.Paint;
        BeginEraseStroke();
        EraseCell(cellId, isDrag: false);
        StatusText = gfxId > 0
            ? $"Eliminado GFX {gfxId} — modo construcción"
            : "Modo construcción";
    }

    private PaintLayer? ResolveGfxLayerForDelete(CellData cell)
    {
        if (HighlightedInspectorLayer != InspectorLayerHighlight.None)
        {
            var highlighted = HighlightedInspectorLayer switch
            {
                InspectorLayerHighlight.Ground => PaintLayer.Ground,
                InspectorLayerHighlight.Object1 => PaintLayer.Object1,
                _ => PaintLayer.Object2,
            };
            if (GetLayerGfx(cell, highlighted) > 0)
                return highlighted;
        }

        if (GetLayerGfx(cell, PaintLayer) > 0)
            return PaintLayer;
        if (GetLayerGfx(cell, PaintLayer.Object2) > 0)
            return PaintLayer.Object2;
        if (GetLayerGfx(cell, PaintLayer.Object1) > 0)
            return PaintLayer.Object1;
        if (GetLayerGfx(cell, PaintLayer.Ground) > 0)
            return PaintLayer.Ground;
        return null;
    }

    public void EraseCell(int cellId, bool isDrag)
    {
        if (CurrentMap is null || _session is null) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;
        if (isDrag && cellId == _lastPaintedCell) return;

        if (_eraseRequireMatch)
        {
            if (_eraseMatchGfxId is not int matchId)
                return;
            if (GetLayerGfx(CurrentMap.Cells[cellId], PaintLayer) != matchId)
                return;
        }

        var eraseLayer = PaintLayer.ToEditorLayer();
        _session.StrokeMutate(cellId, c => MapCellEditor.ClearLayer(c, eraseLayer));
        _lastPaintedCell = cellId;
        if (!isDrag)
        {
            _session.SetSelection(new[] { cellId });
            PrimarySelectedCellId = cellId;
            SyncSelectionFromSession();
        }

        _ = RerenderAsync();
    }

    public void HandleCellClick(int cellId, bool isDrag, bool ctrl = false, double? contentX = null, double? contentY = null)
    {
        if (CurrentMap is null || _session is null) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;

        _inspectContentX = contentX;
        _inspectContentY = contentY;

        switch (Tool)
        {
            case EditorTool.Select:
                if (isDrag) return;
                if (ctrl)
                {
                    _session.ToggleSelection(cellId);
                    PrimarySelectedCellId = cellId;
                }
                else if (KeepAddSelection)
                {
                    // MAP-AREA.1 — do not replace; add cell to the unique set.
                    if (!_session.Selection.Contains(cellId))
                        _session.UnionSelection(new[] { cellId });
                    PrimarySelectedCellId = cellId;
                }
                else
                {
                    _session.SetSelection(new[] { cellId });
                    PrimarySelectedCellId = cellId;
                }
                SyncSelectionFromSession();
                PreferPaintLayerWithGfxOnPrimaryCell();
                // LIB.4.5 — fixed-mob M marks disabled in normal flow.
                break;

            case EditorTool.RectSelect:
                // Handled by BeginRectSelect / UpdateRectSelect / EndRectSelect.
                break;

            case EditorTool.Paint:
                PaintCell(cellId, isDrag);
                break;

            case EditorTool.Erase:
                EraseCell(cellId, isDrag);
                break;

            case EditorTool.MobCell:
                // LIB.4.5 — tool M removed from normal UI; ignore if somehow activated.
                if (isDrag) return;
                break;

            case EditorTool.Unwalkable:
            case EditorTool.LineOfSight:
            case EditorTool.FightCell1:
            case EditorTool.FightCell2:
                PaintCellMode(cellId, isDrag, erase: false);
                break;

            case EditorTool.Eyedropper:
                if (!isDrag)
                    ApplyEyedropper(cellId);
                break;
        }
    }

    public void BeginRectSelect(double contentX, double contentY)
    {
        _rectSelecting = true;
        _rectX0 = _rectX1 = contentX;
        _rectY0 = _rectY1 = contentY;
        // MAP-AREA.1 — snapshot existing selection for Keep/Add union during this drag.
        _rectSelectBase = KeepAddSelection && _session is not null
            ? new HashSet<int>(_session.Selection)
            : null;
        OnPropertyChanged(nameof(IsRectSelecting));
        OnPropertyChanged(nameof(RectSelectBounds));
    }

    public void UpdateRectSelect(double contentX, double contentY)
    {
        if (!_rectSelecting || HitTester is null || _session is null) return;
        _rectX1 = contentX;
        _rectY1 = contentY;
        var cells = IsoSelection.CellsIntersectingRect(HitTester, _rectX0, _rectY0, _rectX1, _rectY1);
        if (_rectSelectBase is not null)
        {
            var union = new HashSet<int>(_rectSelectBase);
            foreach (var id in cells)
                union.Add(id);
            _session.SetSelection(union);
            PrimarySelectedCellId = cells.Count > 0 ? cells.Max() : PrimarySelectedCellId;
        }
        else
        {
            _session.SetSelection(cells);
            PrimarySelectedCellId = cells.Count > 0 ? cells.Max() : null;
        }

        SyncSelectionFromSession();
        OnPropertyChanged(nameof(RectSelectBounds));
    }

    public void EndRectSelect(double contentX, double contentY)
    {
        if (!_rectSelecting) return;
        UpdateRectSelect(contentX, contentY);
        _rectSelecting = false;
        _rectSelectBase = null;
        OnPropertyChanged(nameof(IsRectSelecting));
        OnPropertyChanged(nameof(RectSelectBounds));
    }

    public void UpdateHover(double contentX, double contentY)
    {
        CoordsText = $"{contentX:F0}, {contentY:F0}";
        if (HitTester is null || CurrentMap is null)
        {
            HoveredCellId = null;
            return;
        }

        HoveredCellId = HitTester.HitTest(contentX, contentY);
        UpdatePaintStatusBar();
    }

    public void ClearHover()
    {
        HoveredCellId = null;
        CoordsText = "—";
    }

    private void UpdatePaintStatusBar()
    {
        if (CurrentMap is null) return;
        if (Tool is not (EditorTool.Paint or EditorTool.Erase))
            return;

        var layer = UiDisplayLabels.LayerTarget(PaintLayer);
        var cell = HoveredCellId?.ToString() ?? "—";
        var gfx = SelectedGfxId is int g ? $"GFX {g}" : "GFX —";
        StatusText = Tool == EditorTool.Erase
            ? $"Map {CurrentMap.Id} | Cell {cell} | {layer} | Borrar"
            : $"Construcción · celda {cell} · {layer} · {gfx} · clic para colocar";
    }

    private string BuildPaintBrushStatus() =>
        SelectedGfxId is int id
            ? $"Construcción · GFX {id} · {UiDisplayLabels.LayerTarget(PaintLayer)} · el ítem sigue el cursor · clic para colocar"
            : "Herramienta: Construcción (GFX) — elige un GFX o copia uno con Ctrl+C";

    public void SetCatalogPanelWidth(double width)
    {
        var cols = GfxCatalogLayout.ComputeColumns(width);
        if (cols == GfxColumns) return;
        GfxColumns = cols;
        RefreshVisibleGfx(force: true);
    }

    public bool ConfirmDiscardIfDirty()
    {
        foreach (var doc in _openDocuments.ToList())
        {
            if (!doc.IsDirty) continue;
            ActivateDocument(doc);
            if (!ConfirmDiscardMapOnly())
                return false;
        }

        foreach (var session in _openWorlds.ToList())
        {
            if (!session.Vm.IsDirty) continue;
            ActivateWorldSession(session);
            if (!session.Vm.ConfirmDiscard())
                return false;
        }

        if (_activeWorldSession is null && _worldShell.IsDirty && !_worldShell.ConfirmDiscard())
            return false;

        return true;
    }

    private void ExitOrCloseHost()
    {
        if (IsEmbeddedHost)
        {
            // ADMIN.UI.2 — do not close RufusAdmin MainWindow.
            if (_activeDocument is not null)
            {
                if (_activeDocument.IsDirty && !ConfirmDiscardMapOnly())
                    return;
                CloseDocument(_activeDocument);
            }

            return;
        }

        Application.Current.MainWindow?.Close();
    }

    public bool ConfirmDiscardMapOnly()
    {
        if (_session?.IsDirty != true)
            return true;

        var result = MessageBox.Show(
            "Este mapa contiene cambios no guardados.\n\n¿Guardar el proyecto?",
            "RUFUS Map Editor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.No)
            return true;
        return SaveBlocking();
    }

    private bool SaveBlocking() => SaveOfficialMapAsync().GetAwaiter().GetResult();

    public void CheckRecoverableAutosavesOnStartup()
    {
        var list = _autosave.ListRecoverable();
        if (list.Count == 0)
            return;

        var entry = list[0];
        var info =
            $"Map ID: {entry.Meta.MapId}\n" +
            $"Nombre: {entry.Meta.DisplayName ?? "—"}\n" +
            $"Autosave: {entry.Meta.SavedUtc.ToLocalTime():g}\n" +
            (entry.Meta.HadProjectFile ? $"Proyecto: {entry.Meta.ProjectPath}" : "Sin .rufmap (nunca guardado)");

        var choice = MessageBox.Show(
            "Se encontró una sesión recuperable.\n\n" + info +
            "\n\nSí = Recuperar\nNo = Descartar\nCancelar = Ignorar ahora",
            "Recuperación",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
            return;
        if (choice == MessageBoxResult.No)
        {
            _autosave.Delete(entry.Meta.DocumentId);
            return;
        }

        _ = RecoverAutosaveAsync(entry);
    }

    private async Task RecoverAutosaveAsync(RecoverableAutosave entry)
    {
        if (!ConfirmDiscardIfDirty())
            return;

        try
        {
            IsLoading = true;
            var (map, session) = await Task.Run(() => ProjectPersistence.OpenAutosave(entry.AutosavePath, entry.Meta));
            await PresentLoadedDocumentAsync(map, session, fromAutosave: true);
            StatusText = "Sesión recuperada (cambios no guardados)";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo recuperar:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Official Map Save (9S.3): rebuilds Library\Maps\&lt;MapId&gt; with .rufmap + .png + _MapData.txt (+ optional _AME.swf).
    /// Ctrl+S / Guardar. Distinct from Export Package and from Autosave recovery.
    /// </summary>
    public async Task<bool> SaveAsync() => await SaveOfficialMapAsync();

    public async Task<bool> SaveOfficialMapAsync()
    {
        if (_session is null || CurrentMap is null)
            return false;

        if (!EnsureNoOfficialIdCollisionBeforeSave())
            return false;

        if (!TryAssignMapIdBeforeOfficialSave())
            return false;

        if (CurrentMap.Id <= 0)
        {
            MessageBox.Show(
                "MapId inválido. El documento debe tener un MapId > 0 antes de guardar.",
                "Guardar mapa",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var libraryRoot = _library.RootPath ?? _effectiveLibraryPath;
        if (string.IsNullOrWhiteSpace(libraryRoot) || !_library.IsLoaded || _library.Renderer is null)
        {
            MessageBox.Show(
                "Configure una biblioteca RUFUS (Library) antes de guardar el mapa oficial.",
                "Guardar mapa",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            _session.EndStroke();
            StatusText = $"Guardando mapa {CurrentMap.Id}...";
            IsLoading = true;

            var map = CurrentMap;
            var renderer = _library.Renderer;
            var documentId = _session.DocumentId;
            var source = _session.Source;
            var projectName = _session.ProjectName;
            var root = libraryRoot;

            var result = await Task.Run(() =>
            {
                var saver = new OfficialMapSave(renderer);
                return saver.Save(map, new OfficialMapSaveOptions
                {
                    LibraryRoot = root,
                    DocumentId = documentId,
                    Source = source,
                    ProjectName = projectName,
                    Progress = msg =>
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
                    },
                });
            });

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "Error al guardar el mapa oficial.",
                    "Guardar mapa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusText = "Guardado fallido";
                RufusLog.Error($"Guardado fallido mapa {CurrentMap.Id}: {result.ErrorMessage}");
                return false;
            }

            // Point session at official .rufmap and clear dirty only after CORE succeeded.
            if (!string.IsNullOrWhiteSpace(result.RufmapPath))
            {
                _session.FilePath = result.RufmapPath;
                _session.ProjectName = Path.GetFileNameWithoutExtension(result.RufmapPath);
                AppSettingsStore.TouchRecentProject(_settings, result.RufmapPath);
                RefreshRecentProjects();
            }

            _session.MarkSaved();
            _autosave.Delete(_session.DocumentId);

            // Ensure MapId appears in library list (official .rufmap discovery).
            if (!MapIds.Contains(result.MapId))
            {
                MapIds.Add(result.MapId);
                // keep sorted
                var sorted = MapIds.OrderBy(x => x).ToList();
                MapIds.Clear();
                foreach (var id in sorted)
                    MapIds.Add(id);
                SyncMapListItems();
            }

            _mapPreviews.Invalidate(result.MapId);
            RefreshMapListThumbnail(result.MapId);

            AfterHistoryChange();
            RevertToSavedCommand.RaiseCanExecuteChanged();
            ReloadOriginalCommand.RaiseCanExecuteChanged();
            OpenMapFolderCommand.RaiseCanExecuteChanged();
            NotifyWorldsAfterMapSaved(result.MapId);

            if (result.AmeSwfGenerated)
            {
                StatusText = $"Mapa {result.MapId} guardado";
            }
            else
            {
                StatusText = $"Mapa {result.MapId} guardado (SWF AME omitido)";
                // Discreet warning — not a blocking dialog every Ctrl+S unless useful once.
                // Keep MessageBox only when user might wonder why SWF is missing on first notice.
                if (!string.IsNullOrWhiteSpace(result.AmeSwfWarning))
                {
                    // Status already carries the fact; MessageBox on every save is too noisy.
                    // Spec: "Mostrar warning discreto" → status bar is enough.
                }
            }

            RufusLog.Ok($"Mapa {result.MapId} guardado");
            MapPublishQueue.SyncFingerprintAfterSave(result.MapId);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al guardar:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Guardado fallido";
            RufusLog.Error($"Guardado fallido: {ex.Message}");
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OpenMapFolder()
    {
        if (CurrentMap is null)
            return;

        var libraryRoot = _library.RootPath ?? _effectiveLibraryPath;
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            MessageBox.Show("No hay biblioteca RUFUS cargada.", "Abrir carpeta del mapa");
            return;
        }

        if (CurrentMap.Id <= 0 || !LibraryMapPaths.HasOfficialSave(libraryRoot, CurrentMap.Id))
        {
            MessageBox.Show(
                "Este mapa todavía no tiene un guardado oficial.",
                "Abrir carpeta del mapa",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dir = LibraryMapPaths.GetOfficialMapDirectory(libraryRoot, CurrentMap.Id);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la carpeta:\n{ex.Message}", "Abrir carpeta del mapa");
        }
    }

    public async Task<bool> SaveAsAsync()
    {
        if (_session is null || CurrentMap is null)
            return false;

        var dlg = new SaveFileDialog
        {
            Title = "Guardar proyecto RUFUS",
            Filter = "Proyecto RUFUS (*.rufmap)|*.rufmap",
            DefaultExt = "rufmap",
            AddExtension = true,
            FileName = _session.ProjectName ?? $"map_{CurrentMap.Id}",
        };
        if (dlg.ShowDialog() != true)
            return false;

        var path = dlg.FileName;
        if (!path.EndsWith(RufmapFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            path += RufmapFormat.FileExtension;

        // Refuse overwriting non-rufmap
        var ext = Path.GetExtension(path);
        if (ext.Equals(".sql", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".swf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ame", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("No se puede guardar como SQL/SWF/AME. Use .rufmap.", "RUFUS Map Editor");
            return false;
        }

        try
        {
            _session.EndStroke();
            await Task.Run(() => ProjectPersistence.SaveToPath(_session, path));
            _autosave.Delete(_session.DocumentId);
            AppSettingsStore.TouchRecentProject(_settings, path);
            RefreshRecentProjects();
            AfterHistoryChange();
            RevertToSavedCommand.RaiseCanExecuteChanged();
            ReloadOriginalCommand.RaiseCanExecuteChanged();
            StatusText = "Proyecto guardado";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al guardar:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public async Task ExportSwfAsync()
    {
        if (CurrentMap is null || _session is null)
            return;

        if (CurrentMap.Outdoor is null)
        {
            MessageBox.Show(
                "Metadata obligatoria ausente: bOutdoor (Outdoor).\n\n" +
                "DATO PENDIENTE DE CONFIRMAR — no se inventa un valor.\n" +
                "Cargue el mapa desde la biblioteca RUFUS (con SWF) o un .rufmap que incluya Outdoor.",
                "Exportar SWF",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var root = _library.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            MessageBox.Show("Configure una biblioteca RUFUS (necesaria para Flasm/blank.swf).", "Exportar SWF");
            return;
        }

        var flasm = SwfMapExporter.ResolveFlasmExe(root);
        var blank = SwfMapExporter.ResolveBlankSwf(root);
        if (flasm is null)
        {
            MessageBox.Show($"Flasm no encontrado bajo:\n{root}\\Flasm\\flasm.exe", "Exportar SWF");
            return;
        }
        if (blank is null)
        {
            MessageBox.Show($"Plantilla SWF no encontrada:\n{root}\\Flasm\\blank.swf", "Exportar SWF");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Exportar SWF",
            Filter = "Shockwave Flash (*.swf)|*.swf",
            DefaultExt = "swf",
            AddExtension = true,
            FileName = $"{CurrentMap.Id}_{CurrentMap.DateMap}.swf",
        };
        if (dlg.ShowDialog() != true)
            return;

        var path = dlg.FileName;
        if (!path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
            path += ".swf";

        // Never overwrite legacy reference install paths silently
        var astriaMaps = Path.Combine(root, "Maps");
        if (path.StartsWith(astriaMaps, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(Path.Combine(root, "Flasm"), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "No se permite exportar dentro de la instalación de referencia (solo lectura).\nElija otra carpeta.",
                "Exportar SWF",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (File.Exists(path))
        {
            var overwrite = MessageBox.Show(
                $"El archivo ya existe:\n{path}\n\n¿Sobrescribir?",
                "Exportar SWF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        var dirtyBefore = IsDirty;
        var undoBefore = _session.History.UndoCount;
        var redoBefore = _session.History.RedoCount;

        StatusText = "Exportando SWF...";
        IsLoading = true;
        SwfExportResult result;
        try
        {
            var map = CurrentMap;
            var flasmPath = flasm;
            var blankPath = blank;
            result = await Task.Run(() => SwfMapExporter.Export(new SwfExportRequest
            {
                Document = map,
                DestinationSwfPath = path,
                FlasmExePath = flasmPath,
                BlankSwfTemplatePath = blankPath,
            }));
        }
        finally
        {
            IsLoading = false;
        }

        // Export must not alter dirty / history
        _ = dirtyBefore;
        _ = undoBefore;
        _ = redoBefore;

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorMessage ?? "Error de exportación", "Exportar SWF",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Exportación SWF fallida";
            return;
        }

        var summary =
            $"SWF generado:\n{result.DestinationPath}\n\n" +
            $"Map ID: {CurrentMap.Id}\n" +
            $"MapData chars: {result.MapDataExported.Length}\n" +
            $"Bytes: {result.OutputBytes}\n" +
            $"Flasm: OK (exit {result.FlasmAssemble?.ExitCode})\n" +
            $"Read-back: OK\n" +
            $"Tiempo: {result.Elapsed.TotalMilliseconds:F0} ms";

        new ExportSwfResultWindow(summary) { Owner = Application.Current.MainWindow }.ShowDialog();
        StatusText = "SWF exportado";
        AfterHistoryChange(); // refresh title only; dirty unchanged
    }

    public async Task ExportPackageAsync()
    {
        if (CurrentMap is null || _session is null)
            return;

        if (CurrentMap.Id <= 0)
        {
            MessageBox.Show(
                "MapId inválido. El documento debe tener un MapId > 0 antes de exportar el paquete.",
                "Exportar paquete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!_library.IsLoaded || _library.Renderer is null)
        {
            MessageBox.Show(
                "Configure una biblioteca RUFUS (necesaria para renderizar PNG/ModeCell).",
                "Exportar paquete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var folderDlg = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta destino del paquete (se creará subcarpeta MapId)",
        };
        if (folderDlg.ShowDialog() != true)
            return;

        var parent = folderDlg.FolderName;
        var packageDir = Path.Combine(parent, CurrentMap.Id.ToString());
        if (Directory.Exists(packageDir))
        {
            var overwrite = MessageBox.Show(
                $"El paquete del mapa {CurrentMap.Id} ya existe:\n{packageDir}\n\nSe actualizarán sus archivos.\n¿Continuar?",
                "Exportar paquete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        var cellIds = MessageBox.Show(
            "¿Incluir IDs de celda en el PNG ModeCell?\n\n(Recomendado: Sí — imagen técnica determinista)",
            "Exportar paquete — ModeCell",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        var dirtyBefore = IsDirty;
        var undoBefore = _session.History.UndoCount;
        var redoBefore = _session.History.RedoCount;

        StatusText = "Exportando paquete...";
        IsLoading = true;
        MapPackageResult result;
        try
        {
            var map = CurrentMap;
            var renderer = _library.Renderer;
            var libraryRoot = _library.RootPath;
            var documentId = _session.DocumentId;
            var source = _session.Source;
            var projectName = _session.ProjectName;
            result = await Task.Run(() =>
            {
                var builder = new MapPackageBuilder(renderer);
                return builder.Build(map, new MapPackageOptions
                {
                    ParentDirectory = parent,
                    DocumentId = documentId,
                    Source = source,
                    ProjectName = projectName,
                    ShowCellIds = cellIds,
                    LibraryRootForSwf = libraryRoot,
                    Progress = msg =>
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
                    },
                });
            });
        }
        finally
        {
            IsLoading = false;
        }

        // Export must not alter dirty / history
        _ = dirtyBefore;
        _ = undoBefore;
        _ = redoBefore;
        AfterHistoryChange(); // refresh title only; dirty unchanged

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorMessage ?? "Error al exportar paquete", "Exportar paquete",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Exportación de paquete fallida";
            return;
        }

        var files = string.Join("\n", result.CoreFiles.Select(f => "  • " + f));
        var swfLine = result.LegacySwfGenerated
            ? $"  • Legacy\\{result.MapId}_AME.swf"
            : $"  • Legacy SWF: NO GENERADO\n    ({result.LegacySwfWarning})";

        var hashPreview = result.MapDataSha256.Length >= 16
            ? result.MapDataSha256[..16]
            : result.MapDataSha256;

        var summary =
            $"Paquete generado correctamente.\n\n" +
            $"Map ID: {result.MapId}\n" +
            $"Ruta:\n{result.PackageDirectory}\n\n" +
            $"Archivos:\n{files}\n{swfLine}\n\n" +
            $"PNG: {result.PngWidth}×{result.PngHeight}\n" +
            $"ModeCell: {result.ModeCellWidth}×{result.ModeCellHeight}\n" +
            $"MapData SHA256: {hashPreview}…";

        var open = MessageBox.Show(
            summary + "\n\n¿Abrir carpeta?",
            "Exportar paquete",
            MessageBoxButton.YesNo,
            result.LegacySwfGenerated ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (open == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = result.PackageDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la carpeta:\n{ex.Message}", "Exportar paquete");
            }
        }

        StatusText = result.LegacySwfGenerated
            ? "Paquete exportado"
            : "Paquete exportado (SWF AME omitido)";
    }

    public async Task OpenProjectAsync(string? path = null)
    {
        if (!ConfirmDiscardIfDirty())
            return;

        if (path is null)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Abrir proyecto RUFUS",
                Filter = "Proyecto RUFUS (*.rufmap)|*.rufmap|Todos|*.*",
            };
            if (dlg.ShowDialog() != true)
                return;
            path = dlg.FileName;
        }

        if (!File.Exists(path))
        {
            MessageBox.Show("El archivo no existe.", "RUFUS Map Editor");
            RefreshRecentProjects(removeMissing: true);
            return;
        }

        try
        {
            IsLoading = true;
            StatusText = "Abriendo proyecto...";
            var (map, session) = await Task.Run(() => ProjectPersistence.OpenFile(path));
            _maximizeNextMapWindow = true;
            await PresentLoadedDocumentAsync(map, session, fromAutosave: false);
            AppSettingsStore.TouchRecentProject(_settings, path);
            RefreshRecentProjects();
            _autosave.Delete(session.DocumentId);
            StatusText = $"Proyecto: {Path.GetFileName(path)}";
        }
        catch (RufmapException ex)
        {
            MessageBox.Show(ex.Message, "Proyecto no válido", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText = "Error al abrir proyecto";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Error al abrir proyecto";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task OpenRecentProjectAsync(string path) => await OpenProjectAsync(path);

    private async Task RevertToSavedAsync()
    {
        if (_session?.FilePath is null) return;
        var confirm = MessageBox.Show(
            "¿Revertir al último guardado?\nSe perderán los cambios no guardados.",
            "Revertir",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        var path = _session.FilePath;
        // Bypass dirty check — user confirmed
        try
        {
            IsLoading = true;
            var (map, session) = await Task.Run(() => ProjectPersistence.OpenFile(path));
            await PresentLoadedDocumentAsync(map, session, fromAutosave: false);
            _autosave.Delete(session.DocumentId);
            StatusText = "Revertido al último guardado";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo revertir:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void TryAutosave()
    {
        if (_session is null || CurrentMap is null) return;
        if (!_session.IsDirty) return;
        if (_session.IsStrokeOpen) return;

        try
        {
            var json = ProjectPersistence.BuildJson(_session);
            _autosave.Write(_session.DocumentId, json, new AutosaveMeta
            {
                DocumentId = _session.DocumentId,
                ProjectPath = _session.FilePath,
                MapId = CurrentMap.Id,
                DisplayName = _session.ProjectName ?? $"map_{CurrentMap.Id}",
                SavedUtc = DateTimeOffset.UtcNow,
                HadProjectFile = !string.IsNullOrWhiteSpace(_session.FilePath),
            });
            // Discrete status — don't spam; only if not already showing save message
            if (!StatusText.StartsWith("Proyecto guardado", StringComparison.Ordinal))
                StatusText = $"Autosave: {DateTime.Now:HH:mm}";
        }
        catch
        {
            // silent — autosave must not disrupt editing
        }
    }

    private void RefreshRecentProjects(bool removeMissing = false)
    {
        if (removeMissing)
        {
            _settings.RecentProjects.RemoveAll(p => !File.Exists(p));
            AppSettingsStore.Save(_settings);
        }

        RecentProjects.Clear();
        foreach (var p in _settings.RecentProjects)
            RecentProjects.Add(p);
    }

    private async Task PresentLoadedDocumentAsync(MapDocument map, MapEditSession session, bool fromAutosave)
    {
        var result = await Task.Run(() => _library.IsLoaded ? _library.Render(map, BuildRenderOptions()) : null);
        ImageSource? image = null;
        IReadOnlyList<string> warnings = Array.Empty<string>();
        var renderMs = 0.0;
        if (result is not null)
        {
            image = BitmapConversion.ToBitmapSource(result.Image);
            renderMs = result.Metrics.Render.TotalMilliseconds;
            var w = new List<string>();
            foreach (var g in result.MissingGfx)
            {
                if (g.StartsWith("Background:", StringComparison.Ordinal))
                    w.Add($"Recurso ausente: Background GfxID {g["Background:".Length..]}");
                else
                    w.Add($"Recurso ausente: {g}");
            }
            warnings = w;
            result.Image.Dispose();
        }

        session.CaptureLoadBaseline();
        var openDoc = new OpenMapDocument(map, session, session.HitTester, image, null)
        {
            CascadeIndex = _nextCascadeIndex++
        };
        // Replace existing window for same map id if recovering/opening project
        var prior = FindOpenDocument(map.Id);
        if (prior is not null)
        {
            _openDocuments.Remove(prior);
            DocumentClosed?.Invoke(prior);
        }
        _openDocuments.Add(openDoc);
        BindActiveDocument(openDoc, clearSelection: true);
        DocumentOpened?.Invoke(openDoc);
        RaiseCombineCommands();
        ResourceWarnings = warnings;
        RenderTimeText = result is null ? "—" : $"{renderMs:F0} ms";
        SelectedMapId = map.Id > 0 ? map.Id : null;
        RequestFitMap?.Invoke();
    }


    /// <summary>
    /// Counts how many cells would change from findId to replaceId for the given scope/layers.
    /// </summary>
    public int CountReplaceGfx(
        int findId,
        int replaceId,
        bool wholeMap,
        IReadOnlyList<PaintLayer> layers)
    {
        if (CurrentMap is null || findId == replaceId || layers.Count == 0)
            return 0;

        var cellIds = ResolveMassCellIds(wholeMap);
        if (cellIds.Count == 0) return 0;

        var count = 0;
        foreach (var id in cellIds)
        {
            var cell = CurrentMap.Cells[id];
            foreach (var layer in layers)
            {
                if (GetLayerGfx(cell, layer) == findId)
                    count++;
            }
        }

        return count;
    }

    /// <summary>Legacy count helper — selection + active layer only.</summary>
    public int ReplaceGfx(int findId, int replaceId) =>
        CountReplaceGfx(findId, replaceId, wholeMap: false, new[] { PaintLayer });

    /// <summary>Applies GFX replace as one undo command.</summary>
    public int ApplyReplace(
        int findId,
        int replaceId,
        bool wholeMap = false,
        IReadOnlyList<PaintLayer>? layers = null,
        int? forceRotation = null,
        bool? forceFlip = null)
    {
        if (_session is null || CurrentMap is null || findId == replaceId)
            return 0;

        layers ??= new[] { PaintLayer };
        var cellIds = ResolveMassCellIds(wholeMap);
        if (cellIds.Count == 0) return 0;

        var changed = 0;
        if (_session.Commit("Reemplazar GFX", cellIds, (_, c) =>
            {
                foreach (var paintLayer in layers)
                {
                    if (GetLayerGfx(c, paintLayer) != findId)
                        continue;
                    var editorLayer = paintLayer.ToEditorLayer();
                    var rot = paintLayer == PaintLayer.Object2
                        ? null
                        : forceRotation;
                    MapCellEditor.SetLayerGfx(c, editorLayer, replaceId, forceFlip, rot);
                    changed++;
                }
            }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        StatusText = changed > 0
            ? $"Reemplazadas {changed} instancias ({findId} → {replaceId})"
            : "Ninguna celda coincidía";
        return changed;
    }

    public int CountReplaceFocusAcrossCombinedMaps(int findId, IReadOnlyList<PaintLayer>? layers = null)
    {
        if (!IsMapCombinedMode || !MultiMap.IsActive || findId <= 0) return 0;
        layers ??= new[] { PaintLayer };
        var keys = GetEsteItemMapKeys();
        var total = 0;
        foreach (var layer in layers)
            total += MultiMap.CountMatchingGfx(keys, layer, findId);
        return total;
    }

    public int ApplyReplaceFocusAcrossCombinedMaps(
        int findId,
        int replaceId,
        IReadOnlyList<PaintLayer>? layers = null,
        int? forceRotation = null,
        bool? forceFlip = null)
    {
        if (!IsMapCombinedMode || !MultiMap.IsActive || findId <= 0 || findId == replaceId)
            return 0;

        layers ??= new[] { PaintLayer };
        var keys = GetEsteItemMapKeys();
        if (keys.Count == 0) return 0;

        var changed = 0;
        foreach (var layer in layers)
        {
            var editorLayer = layer.ToEditorLayer();
            changed += MultiMap.MutateMatchingGfx(
                keys,
                layer,
                findId,
                $"Reemplazar GFX {findId}→{replaceId}",
                c =>
                {
                    var rot = layer == PaintLayer.Object2 ? null : forceRotation;
                    MapCellEditor.SetLayerGfx(c, editorLayer, replaceId, forceFlip, rot);
                });
        }

        foreach (var key in keys)
        {
            MarkOpenDocumentDirtyFromMultiMap(key);
            MosaicHost.NotifyMapEdited(key);
        }

        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        RaiseCombineCommands();
        RaiseFocusGfxUi();
        StatusText = changed > 0
            ? $"Reemplazadas {changed} instancias ({findId} → {replaceId}) · {keys.Count} mapa(s)"
            : "Ninguna celda coincidía";
        return changed;
    }

    private IReadOnlyList<int> ResolveMassCellIds(bool wholeMap)
    {
        if (CurrentMap is null) return Array.Empty<int>();
        if (wholeMap)
        {
            var all = new int[CurrentMap.Cells.Count];
            for (var i = 0; i < all.Length; i++)
                all[i] = i;
            return all;
        }

        return SelectedCellIds;
    }

    private void SelectAllFocusGfx()
    {
        if (!TryResolveFocusGfx(out var gfxId, out var layer))
            return;

        if (PaintLayer != layer)
            PaintLayer = layer;
        SelectedGfxId = gfxId;

        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            var keys = GetEsteItemMapKeys();
            if (keys.Count == 0)
            {
                StatusText = "Ningún mapa en el alcance (Alt+clic para añadir mapas)";
                return;
            }

            var refs = MultiMap.FindMatchingCells(keys, layer, gfxId);
            if (refs.Count == 0)
            {
                StatusText = $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)} ({keys.Count} mapa(s))";
                RaiseFocusGfxUi();
                return;
            }

            MultiMap.SetSelection(refs);
            SyncUiFromMultiMapSelection();
            RaiseFocusGfxUi();
            RaiseSelectionCommands();
            MosaicHost.RequestViewRedraw();
            StatusText =
                $"Seleccionadas {refs.Count} celdas con GFX {gfxId} · {UiDisplayLabels.LayerTarget(layer)} · {keys.Count} mapa(s)";
            return;
        }

        if (_session is null || CurrentMap is null)
            return;

        var ids = new List<int>();
        for (var i = 0; i < CurrentMap.Cells.Count; i++)
        {
            if (GetLayerGfx(CurrentMap.Cells[i], layer) == gfxId)
                ids.Add(i);
        }

        if (ids.Count == 0)
        {
            StatusText = $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
            RaiseFocusGfxUi();
            return;
        }

        _session.SetSelection(ids);
        PrimarySelectedCellId = ids[^1];
        SyncSelectionFromSession();
        PushSessionSelectionToMultiMap();
        RaiseFocusGfxUi();
        RaiseSelectionCommands();
        StatusText = $"Seleccionadas {ids.Count} celdas con GFX {gfxId} · {UiDisplayLabels.LayerTarget(layer)}";
    }

    /// <summary>
    /// Select tool: pick the GFX under the click (Capa 2 → Capa 1 → Suelo) and load it
    /// into the brush without stamping. Paint / Erase keep the current brush and layer —
    /// painting must overwrite the cell, never swap the active GFX for what was underneath.
    /// </summary>
    private void PreferPaintLayerWithGfxOnPrimaryCell()
    {
        if (Tool is EditorTool.Select or EditorTool.RectSelect)
        {
            InspectPrimaryCellAfterSelect();
            return;
        }

        // Paint, Erase y herramientas de celda: no tocar SelectedGfxId ni PaintLayer.
        // En combinado SyncUiFromMultiMapSelection llamaba aquí tras cada pincelada y
        // sustituía el pincel por el GFX que había en la celda (el “sobrescrito”).
        RaiseFocusGfxUi();
    }

    /// <summary>
    /// Select-tool inspect without changing the selection (click on an already-selected cell).
    /// </summary>
    public void InspectCellGfx(int cellId, double? contentX = null, double? contentY = null)
    {
        if (CurrentMap is null || cellId < 0 || cellId >= CurrentMap.Cells.Count)
            return;

        _inspectContentX = contentX;
        _inspectContentY = contentY;
        if (TryPickInspectedGfx(cellId, out var gfxId, out var layer))
            ApplyInspectedGfxToBrush(cellId, gfxId, layer, announce: true);
        else
        {
            HighlightedInspectorLayer = InspectorLayerHighlight.None;
            StatusText = "Celda vacía";
            RaiseFocusGfxUi();
        }
    }

    private void InspectPrimaryCellAfterSelect()
    {
        if (PrimarySelectedCellId is not int cellId)
        {
            RaiseFocusGfxUi();
            return;
        }

        if (TryPickInspectedGfx(cellId, out var gfxId, out var layer))
        {
            ApplyInspectedGfxToBrush(cellId, gfxId, layer, announce: true);
            return;
        }

        HighlightedInspectorLayer = InspectorLayerHighlight.None;
        StatusText = "Celda vacía";
        RaiseFocusGfxUi();
    }

    private bool TryPickInspectedGfx(int cellId, out int gfxId, out PaintLayer layer)
    {
        gfxId = 0;
        layer = PaintLayer;
        if (CurrentMap is null || cellId < 0 || cellId >= CurrentMap.Cells.Count)
            return false;

        if (_inspectContentX is double x && _inspectContentY is double y)
        {
            foreach (var candidate in InspectLayerOrder)
            {
                if (!AreGfxObjectLayersEnabled && candidate != PaintLayer.Ground)
                    continue;
                if (!TryGetCellLayerVisual(cellId, candidate, out var visual))
                    continue;
                if (!visual.Bounds.Contains(x, y))
                    continue;
                gfxId = GetLayerGfx(CurrentMap.Cells[cellId], candidate);
                if (gfxId <= 0)
                    continue;
                layer = candidate;
                return true;
            }
        }

        var cell = CurrentMap.Cells[cellId];
        foreach (var candidate in InspectLayerOrder)
        {
            if (!AreGfxObjectLayersEnabled && candidate != PaintLayer.Ground)
                continue;
            var g = GetLayerGfx(cell, candidate);
            if (g <= 0)
                continue;
            gfxId = g;
            layer = candidate;
            return true;
        }

        return false;
    }

    private void ApplyInspectedGfxToBrush(int cellId, int gfxId, PaintLayer layer, bool announce)
    {
        if (CurrentMap is null || cellId < 0 || cellId >= CurrentMap.Cells.Count)
            return;

        var cell = CurrentMap.Cells[cellId];
        if (PaintLayer != layer)
            PaintLayer = layer;
        ApplyBrushTransformFromCell(cell, layer);
        if (SelectedGfxId != gfxId)
            SelectedGfxId = gfxId;
        HighlightedInspectorLayer = layer switch
        {
            PaintLayer.Ground => InspectorLayerHighlight.Ground,
            PaintLayer.Object1 => InspectorLayerHighlight.Object1,
            _ => InspectorLayerHighlight.Object2,
        };
        PasteCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveToolLabel));
        if (announce)
        {
            StatusText = Tool == EditorTool.Paint
                ? BuildPaintBrushStatus()
                : $"{UiDisplayLabels.LayerTarget(layer)} · GFX {gfxId} · Construir (B) para colocarlo";
        }

        RaiseFocusGfxUi();
        RefreshCellInspector();
    }

    private void ApplyBrushTransformFromCell(CellData cell, PaintLayer layer)
    {
        BrushFlip = GetLayerFlip(cell, layer);
        if (layer != PaintLayer.Object2)
            BrushRotation = GetLayerRotation(cell, layer);
    }

    private bool CaptureBrushFromSelection()
    {
        if (PrimarySelectedCellId is int cellId && TryPickInspectedGfx(cellId, out var pickedId, out var pickedLayer))
        {
            ApplyInspectedGfxToBrush(cellId, pickedId, pickedLayer, announce: false);
            return true;
        }

        if (TryResolveFocusGfx(out var focusId, out var focusLayer) && PrimarySelectedCellId is int id)
        {
            ApplyInspectedGfxToBrush(id, focusId, focusLayer, announce: false);
            return true;
        }

        return SelectedGfxId is int;
    }

    private void DeleteFocusGfxOnActiveLayer()
    {
        if (!TryResolveFocusGfx(out var gfxId, out var layer)) return;
        if (PaintLayer != layer)
            PaintLayer = layer;
        SelectedGfxId = gfxId;

        if (TryMutateFocusGfxAcrossCombinedMaps(
                gfxId,
                layer,
                $"Borrar GFX {gfxId}",
                c => MapCellEditor.ClearLayer(c, layer.ToEditorLayer()),
                out var deleted))
        {
            StatusText = deleted > 0
                ? $"Eliminadas {deleted} instancias de GFX {gfxId} · {UiDisplayLabels.LayerTarget(layer)}"
                : $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
            RaiseFocusGfxUi();
            return;
        }

        DeleteSelectedGfxOnLayers(new[] { layer }, wholeMap: true);
        RaiseFocusGfxUi();
    }

    private void RotateFocusGfx(int delta, bool wholeMap)
    {
        if (!TryResolveFocusGfx(out var gfxId, out var layer)) return;
        if (PaintLayer != layer)
            PaintLayer = layer;
        SelectedGfxId = gfxId;

        if (layer == PaintLayer.Object2)
        {
            StatusText =
                "Capa 2 no tiene rotación en MapData (formato clásico). Usa Voltear, o mueve el GFX a Capa 1 para rotarlo.";
            RaiseFocusGfxUi();
            return;
        }

        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            var editorLayer = layer.ToEditorLayer();
            if (TryMutateFocusGfxAcrossCombinedMaps(
                    gfxId,
                    layer,
                    $"Rotar GFX {gfxId}",
                    c =>
                    {
                        var current = GetLayerRotation(c, layer);
                        MapCellEditor.SetRotation(c, editorLayer, (current + delta) & 3);
                    },
                    out var rotated))
            {
                StatusText = rotated > 0
                    ? $"Rotadas {rotated} instancias de GFX {gfxId}"
                    : $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
                RaiseFocusGfxUi();
                return;
            }
        }

        RotateSelectedGfxInstances(layer, delta, wholeMap);
        RaiseFocusGfxUi();
    }

    private void FlipFocusGfxInstances(bool wholeMap)
    {
        if (!TryResolveFocusGfx(out var gfxId, out var layer)) return;
        if (PaintLayer != layer)
            PaintLayer = layer;
        SelectedGfxId = gfxId;

        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            var editorLayer = layer.ToEditorLayer();
            if (TryMutateFocusGfxAcrossCombinedMaps(
                    gfxId,
                    layer,
                    $"Voltear GFX {gfxId}",
                    c => MapCellEditor.SetFlip(c, editorLayer, !GetLayerFlip(c, layer)),
                    out var flipped))
            {
                StatusText = flipped > 0
                    ? $"Volteadas {flipped} instancias de GFX {gfxId}"
                    : $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
                RaiseFocusGfxUi();
                return;
            }
        }

        FlipSelectedGfxInstances(layer, wholeMap);
        RaiseFocusGfxUi();
    }

    private void MoveFocusGfxToLayer(PaintLayer target)
    {
        if (!TryResolveFocusGfx(out var gfxId, out var source))
            return;
        if (source == target)
        {
            StatusText = $"GFX {gfxId} ya está en {UiDisplayLabels.LayerTarget(target)}";
            RaiseFocusGfxUi();
            return;
        }

        var srcEdit = source.ToEditorLayer();
        var dstEdit = target.ToEditorLayer();
        var dstLabel = UiDisplayLabels.LayerTarget(target);
        var srcLabel = UiDisplayLabels.LayerTarget(source);
        var cmdName = $"Mover GFX {gfxId}: {srcLabel} → {dstLabel}";

        void Mutate(CellData c)
        {
            if (GetLayerGfx(c, source) != gfxId)
                return;

            var flip = GetLayerFlip(c, source);
            var rot = GetLayerRotation(c, source);
            if (target == PaintLayer.Object2)
                MapCellEditor.SetLayerGfx(c, dstEdit, gfxId, flip);
            else
                MapCellEditor.SetLayerGfx(c, dstEdit, gfxId, flip, rot);
            MapCellEditor.ClearLayer(c, srcEdit);
        }

        var moved = 0;
        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            if (TryMutateFocusGfxAcrossCombinedMaps(gfxId, source, cmdName, Mutate, out moved))
            {
                PaintLayer = target;
                SelectedGfxId = gfxId;
                StatusText = BuildMoveLayerStatus(gfxId, moved, srcLabel, dstLabel, source, target);
                RaiseFocusGfxUi();
                RefreshCellInspector();
                return;
            }
        }

        if (_session is null || CurrentMap is null)
            return;

        var cellIds = new List<int>();
        for (var i = 0; i < CurrentMap.Cells.Count; i++)
        {
            if (GetLayerGfx(CurrentMap.Cells[i], source) == gfxId)
                cellIds.Add(i);
        }

        if (cellIds.Count == 0)
        {
            StatusText = $"No hay GFX {gfxId} en {srcLabel}";
            RaiseFocusGfxUi();
            return;
        }

        if (_session.Commit(cmdName, cellIds, (_, c) =>
            {
                Mutate(c);
                moved++;
            }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        PaintLayer = target;
        SelectedGfxId = gfxId;
        StatusText = BuildMoveLayerStatus(gfxId, moved, srcLabel, dstLabel, source, target);
        RaiseFocusGfxUi();
        RefreshCellInspector();
    }

    private static string BuildMoveLayerStatus(
        int gfxId,
        int moved,
        string srcLabel,
        string dstLabel,
        PaintLayer source,
        PaintLayer target)
    {
        if (moved <= 0)
            return $"No hay GFX {gfxId} en {srcLabel}";

        var rotNote = source != PaintLayer.Object2 && target == PaintLayer.Object2
            ? " · rotación no se conserva en Capa 2"
            : "";
        return $"Movidas {moved} instancias de GFX {gfxId}: {srcLabel} → {dstLabel}{rotNote}";
    }

    /// <summary>Maps in scope for ESTE ÍTEM (yellow borders). Defaults to all selected world maps.</summary>
    private IReadOnlyList<string> GetEsteItemMapKeys()
    {
        if (IsMapCombinedMode && CombinedMaps.SelectedKeys.Count > 0)
            return CombinedMaps.SelectedKeys.ToList();

        if (CurrentMap is not null)
        {
            var key = FindWorldDocumentKeyForMap(CurrentMap);
            if (key is not null)
                return new[] { key };
        }

        return Array.Empty<string>();
    }

    private bool TryMutateFocusGfxAcrossCombinedMaps(
        int gfxId,
        PaintLayer layer,
        string commandName,
        Action<CellData> mutate,
        out int changed)
    {
        changed = 0;
        if (!IsMapCombinedMode || !MultiMap.IsActive)
            return false;

        var keys = GetEsteItemMapKeys();
        if (keys.Count == 0)
            return false;

        changed = MultiMap.MutateMatchingGfx(keys, layer, gfxId, commandName, mutate);
        foreach (var key in keys)
        {
            MarkOpenDocumentDirtyFromMultiMap(key);
            MosaicHost.NotifyMapEdited(key);
        }

        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        RaiseCombineCommands();
        return true;
    }

    private void DeleteSelectedGfxOnLayers(IReadOnlyList<PaintLayer> layers, bool wholeMap)
    {
        if (_session is null || CurrentMap is null)
            return;
        if ((FocusGfxId ?? SelectedGfxId) is not int gfxId)
            return;

        var cellIds = ResolveMassCellIds(wholeMap);
        if (cellIds.Count == 0)
        {
            StatusText = wholeMap ? "Mapa vacío" : "Selecciona celdas primero";
            return;
        }

        var changed = 0;
        var scope = wholeMap ? "mapa" : "selección";
        var layerLabel = layers.Count == 1
            ? UiDisplayLabels.LayerTarget(layers[0])
            : $"{layers.Count} capas";
        if (_session.Commit($"Borrar GFX {gfxId} ({layerLabel}, {scope})", cellIds, (_, c) =>
            {
                foreach (var layer in layers)
                {
                    if (GetLayerGfx(c, layer) != gfxId)
                        continue;
                    MapCellEditor.ClearLayer(c, layer.ToEditorLayer());
                    changed++;
                }
            }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        StatusText = changed > 0
            ? $"Eliminadas {changed} instancias de GFX {gfxId} · {layerLabel}"
            : $"No hay GFX {gfxId} en {layerLabel}";
        RaiseFocusGfxUi();
    }

    private void RotateSelectedGfxInstances(PaintLayer layer, int delta, bool wholeMap)
    {
        if (_session is null || CurrentMap is null)
            return;
        if ((FocusGfxId ?? SelectedGfxId) is not int gfxId)
            return;
        if (layer == PaintLayer.Object2)
        {
            StatusText = "Capa 2 no tiene rotación en MapData";
            return;
        }

        var cellIds = ResolveMassCellIds(wholeMap);
        if (cellIds.Count == 0)
        {
            StatusText = wholeMap ? "Mapa vacío" : "Selecciona celdas primero";
            return;
        }

        var editorLayer = layer.ToEditorLayer();
        var changed = 0;
        var scope = wholeMap ? "mapa" : "selección";
        if (_session.Commit($"Rotar GFX {gfxId} ({UiDisplayLabels.LayerTarget(layer)}, {scope})", cellIds,
                (_, c) =>
                {
                    if (GetLayerGfx(c, layer) != gfxId)
                        return;
                    var current = GetLayerRotation(c, layer);
                    MapCellEditor.SetRotation(c, editorLayer, (current + delta) & 3);
                    changed++;
                }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        StatusText = changed > 0
            ? $"Rotadas {changed} instancias de GFX {gfxId}"
            : $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
    }

    private void FlipSelectedGfxInstances(PaintLayer layer, bool wholeMap)
    {
        if (_session is null || CurrentMap is null)
            return;
        if ((FocusGfxId ?? SelectedGfxId) is not int gfxId)
            return;

        var cellIds = ResolveMassCellIds(wholeMap);
        if (cellIds.Count == 0)
        {
            StatusText = wholeMap ? "Mapa vacío" : "Selecciona celdas primero";
            return;
        }

        var editorLayer = layer.ToEditorLayer();
        var changed = 0;
        var scope = wholeMap ? "mapa" : "selección";
        if (_session.Commit($"Voltear GFX {gfxId} ({UiDisplayLabels.LayerTarget(layer)}, {scope})", cellIds,
                (_, c) =>
                {
                    if (GetLayerGfx(c, layer) != gfxId)
                        return;
                    MapCellEditor.SetFlip(c, editorLayer, !GetLayerFlip(c, layer));
                    changed++;
                }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        StatusText = changed > 0
            ? $"Volteadas {changed} instancias de GFX {gfxId}"
            : $"No hay GFX {gfxId} en {UiDisplayLabels.LayerTarget(layer)}";
    }

    private static int GetLayerRotation(CellData cell, PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => cell.GroundRotation,
        PaintLayer.Object1 => cell.Object1Rotation,
        _ => 0,
    };

    private static bool GetLayerFlip(CellData cell, PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => cell.FlipGround,
        PaintLayer.Object1 => cell.FlipObject1,
        PaintLayer.Object2 => cell.FlipObject2,
        _ => false,
    };

    private void RaiseFocusGfxUi()
    {
        OnPropertyChanged(nameof(FocusGfxId));
        OnPropertyChanged(nameof(CanUseFocusGfx));
        OnPropertyChanged(nameof(FocusGfxCountOnActiveLayer));
        OnPropertyChanged(nameof(FocusGfxSummary));
        OnPropertyChanged(nameof(SelectAllFocusGfxLabel));
        OnPropertyChanged(nameof(ReplaceAllFocusGfxLabel));
        OnPropertyChanged(nameof(DeleteAllFocusGfxLabel));
        OnPropertyChanged(nameof(FlipAllFocusGfxLabel));
        OnPropertyChanged(nameof(RotateAllFocusGfxLabel));
        OnPropertyChanged(nameof(MoveFocusGfxSectionLabel));
        OnPropertyChanged(nameof(CanMoveFocusGfxToGround));
        OnPropertyChanged(nameof(CanMoveFocusGfxToObject1));
        OnPropertyChanged(nameof(CanMoveFocusGfxToObject2));
        OnPropertyChanged(nameof(MassGfxPanelTitle));
        OnPropertyChanged(nameof(CanMassEditSelectedGfx));
        OnPropertyChanged(nameof(CanMassRotateSelectedGfx));
        SelectAllFocusGfxCommand.RaiseCanExecuteChanged();
        DeleteAllFocusGfxCommand.RaiseCanExecuteChanged();
        FlipAllFocusGfxCommand.RaiseCanExecuteChanged();
        RotateAllFocusGfxCommand.RaiseCanExecuteChanged();
        MoveFocusGfxToGroundCommand.RaiseCanExecuteChanged();
        MoveFocusGfxToObject1Command.RaiseCanExecuteChanged();
        MoveFocusGfxToObject2Command.RaiseCanExecuteChanged();
        ApplyBrushToSelectionCommand.RaiseCanExecuteChanged();
    }

    public void NotifyFocusGfxUi() => RaiseFocusGfxUi();

    private void RaiseMassGfxCommands()
    {
        DeleteSelectedGfxOnActiveLayerCommand.RaiseCanExecuteChanged();
        DeleteSelectedGfxOnObject1Command.RaiseCanExecuteChanged();
        DeleteSelectedGfxOnObject2Command.RaiseCanExecuteChanged();
        DeleteSelectedGfxOnAllLayersCommand.RaiseCanExecuteChanged();
        DeleteSelectedGfxInSelectionCommand.RaiseCanExecuteChanged();
        RotateSelectedGfxOnActiveLayerCommand.RaiseCanExecuteChanged();
        RotateSelectedGfxInSelectionCommand.RaiseCanExecuteChanged();
        RaiseFocusGfxUi();
    }

    public async Task LoadMapAsync(int mapId)
    {
        if (!HasLibrary || IsLoading)
            return;

        var existing = FindOpenDocument(mapId);
        if (existing is not null)
        {
            ActivateDocument(existing);
            return;
        }

        IsLoading = true;
        StatusText = "Cargando mapa...";
        HoveredCellId = null;
        FinishStroke();

        try
        {
            var (map, swf, result) = await Task.Run(() =>
            {
                var doc = _library.LoadMapDocument(mapId, out var meta);
                var render = _library.Render(doc, BuildRenderOptions());
                return (doc, meta, render);
            });

            ApplyRenderResult(map, swf, result);
            SelectedMapId = mapId;
            RequestFitMap?.Invoke();
            RufusLog.Info($"Mapa {mapId} cargado");
        }
        catch (Exception ex)
        {
            StatusText = "Error al cargar";
            RufusLog.Error($"Error al cargar mapa {mapId}: {ex.Message}");
            MessageBox.Show($"No se pudo abrir el mapa {mapId}:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void CloseMap()
    {
        if (_activeDocument is not null)
            CloseDocument(_activeDocument);
    }

    public OpenMapDocument? FindOpenDocument(int mapId) =>
        _openDocuments.FirstOrDefault(d => d.MapId == mapId && !IsMosaicBoundDocument(d))
        ?? _openDocuments.FirstOrDefault(d => d.MapId == mapId);

    private static bool IsMosaicBoundDocument(OpenMapDocument doc) =>
        doc.DocumentId.StartsWith("mosaic-", StringComparison.Ordinal);

    /// <summary>
    /// Ensures the inspector / ESTE ÍTEM use the mosaic MapDocument instance, never a
    /// floating window that only shares the same numeric MapId.
    /// </summary>
    public void FocusOpenMapFromWorldDocumentKey(string documentKey)
    {
        if (MosaicHost.World is null) return;
        if (!MosaicHost.World.Documents.TryGetValue(documentKey, out var entry)) return;

        var mosaicMap = entry.Document;
        var open = _openDocuments.FirstOrDefault(d => ReferenceEquals(d.Map, mosaicMap));

        if (open is null)
        {
            if (IsMapCombinedMode && !IsMapCombinedMinimized)
            {
                // Combinado a pantalla: enlazar la instancia del mosaico (sin ventana flotante).
                open = EnsureMosaicBoundDocument(mosaicMap, documentKey);
            }
            else
            {
                open = _openDocuments.FirstOrDefault(d => d.MapId == mosaicMap.Id && !IsMosaicBoundDocument(d))
                       ?? _openDocuments.FirstOrDefault(d => d.MapId == mosaicMap.Id);
            }
        }

        if (open is null && IsMapCombinedMode && !IsMapCombinedMinimized)
            open = EnsureMosaicBoundDocument(mosaicMap, documentKey);

        if (open is not null)
            ActivateDocument(open);
        RaiseCombineCommands();
    }

    private OpenMapDocument EnsureMosaicBoundDocument(MapDocument map, string documentKey)
    {
        var existing = _openDocuments.FirstOrDefault(d => ReferenceEquals(d.Map, map));
        if (existing is not null)
            return existing;

        var hit = new IsoHitTester(map.Width, map.Height);
        var linkedPath = MosaicHost.World?.Documents.TryGetValue(documentKey, out var entry) == true
            ? entry.LinkedRufmapPath
            : null;
        var session = new MapEditSession(map, hit)
        {
            DocumentId = $"mosaic-{documentKey}",
            CreatedUtc = DateTimeOffset.UtcNow,
            Source = new RufmapSourceDto
            {
                Kind = "CombinedMosaic",
                OriginalMapId = map.Id,
                LibraryPathHint = _library.RootPath,
            },
            ProjectName = MosaicHost.World?.Name,
            FilePath = linkedPath,
        };
        session.CaptureLoadBaseline();

        var openDoc = new OpenMapDocument(map, session, hit, mapImage: null, swfMeta: null)
        {
            CascadeIndex = _nextCascadeIndex++,
        };
        _openDocuments.Add(openDoc);
        // Sin DocumentOpened: no crear ventana flotante duplicada detrás del combinado.
        return openDoc;
    }

    private void RemoveMosaicBoundDocuments()
    {
        var silent = _openDocuments.Where(IsMosaicBoundDocument).ToList();
        if (silent.Count == 0) return;

        var wasActive = silent.Any(d => ReferenceEquals(d, _activeDocument));
        foreach (var doc in silent)
        {
            _autosave.Delete(doc.Session.DocumentId);
            _openDocuments.Remove(doc);
        }

        if (!wasActive) return;

        if (_openDocuments.Count == 0)
            ClearAllOpenDocumentsState();
        else
        {
            BindActiveDocument(_openDocuments[^1], clearSelection: false);
            DocumentActivated?.Invoke(_activeDocument!);
        }
    }

    public void ActivateDocument(OpenMapDocument doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (!_openDocuments.Contains(doc)) return;
        if (ReferenceEquals(_activeDocument, doc))
        {
            DocumentActivated?.Invoke(doc);
            return;
        }

        FinishStroke();
        BindActiveDocument(doc, clearSelection: false);
        DocumentActivated?.Invoke(doc);
        RequestFitMap?.Invoke();
    }

    private WorldViewModel CreateWorldViewModel()
    {
        var vm = new WorldViewModel(
            _library,
            _worldThumbs,
            _mapPreviews,
            MultiMap,
            OpenMapFromWorldAsync,
            () => MapIds.ToList(),
            s => StatusText = s,
            _mapListFilter);
        vm.SetEditorHost(this);
        return vm;
    }

    public void RequestNewWorldSession()
    {
        var dlg = new WorldGridSizeWindow { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var vm = CreateWorldViewModel();
        vm.ApplyNewWorld(
            dlg.ResultGridWidth,
            dlg.ResultGridHeight,
            dlg.ResultOriginX,
            dlg.ResultOriginY);
        OpenWorldSessionFromVm(vm);
    }

    public void RequestOpenWorldSession()
    {
        if (!TryGetWorldLibraryRoot(out var libraryRoot))
            return;

        var geoRoot = GeopositionsStore.EnsureRoot(libraryRoot);
        var projects = GeopositionsStore.ListProjects(libraryRoot);
        var pick = new WorldProjectsWindow(geoRoot, projects) { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() != true || string.IsNullOrWhiteSpace(pick.SelectedPath))
            return;

        var existing = FindWorldSessionByPath(pick.SelectedPath);
        if (existing is not null)
        {
            _maximizeNextWorldWindow = true;
            ActivateWorldSession(existing);
            WorkspaceTabIndex = 1;
            return;
        }

        var vm = CreateWorldViewModel();
        vm.LoadWorldFromPath(pick.SelectedPath);
        if (vm.World is null) return;
        OpenWorldSessionFromVm(vm);
    }

    public void RequestImportGeoWorldSession()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Géoposition legado (*.geo)|*.geo",
            Title = "Importar .geo (solo lectura)",
        };
        if (dlg.ShowDialog() != true) return;

        var vm = CreateWorldViewModel();
        vm.ApplyImportGeo(dlg.FileName);
        if (vm.World is null) return;
        OpenWorldSessionFromVm(vm);
    }

    private bool TryGetWorldLibraryRoot(out string libraryRoot)
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

    private OpenWorldSession? FindWorldSessionByPath(string path) =>
        _openWorlds.FirstOrDefault(s =>
            s.Vm.World?.FilePath is { Length: > 0 } fp
            && string.Equals(fp, path, StringComparison.OrdinalIgnoreCase));

    private void OpenWorldSessionFromVm(WorldViewModel vm)
    {
        var session = new OpenWorldSession(vm, _nextWorldCascadeIndex++);
        session.SyncSessionIdFromWorld();
        _openWorlds.Add(session);
        ActivateWorldSession(session);
        WorldSessionOpened?.Invoke(session);
        WorkspaceTabIndex = 1;
    }

    public bool ConsumeMaximizeNextWorldWindow()
    {
        var value = _maximizeNextWorldWindow;
        _maximizeNextWorldWindow = false;
        return value;
    }

    public bool ConsumeMaximizeNextMapWindow()
    {
        var value = _maximizeNextMapWindow;
        _maximizeNextMapWindow = false;
        return value;
    }

    /// <summary>
    /// Promotes the shell WorldViewModel into an open session when it gained content
    /// (e.g. MAPA combinado) without going through Nuevo/Abrir.
    /// </summary>
    public void EnsureActiveWorldSessionOpen()
    {
        if (_activeWorldSession is not null) return;
        if (_worldShell.World is null) return;

        var session = new OpenWorldSession(_worldShell, _nextWorldCascadeIndex++);
        session.SyncSessionIdFromWorld();
        _openWorlds.Add(session);
        _activeWorldSession = session;
        _worldShell = CreateWorldViewModel();
        OnPropertyChanged(nameof(World));
        WorldSessionOpened?.Invoke(session);
        WorldSessionActivated?.Invoke(session);
    }

    public void ActivateWorldSession(OpenWorldSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (!_openWorlds.Contains(session)) return;

        if (_activeWorldSession is not null
            && !ReferenceEquals(_activeWorldSession, session)
            && _activeWorldSession.Vm.IsMultiMapEditMode)
        {
            _activeWorldSession.Vm.ExitMultiMapEdit(force: true);
        }

        if (ReferenceEquals(_activeWorldSession, session))
        {
            WorldSessionActivated?.Invoke(session);
            OnPropertyChanged(nameof(World));
            return;
        }

        _activeWorldSession = session;
        OnPropertyChanged(nameof(World));
        WorldSessionActivated?.Invoke(session);
        RaiseCombineCommands();
    }

    public bool CloseWorldSession(OpenWorldSession session)
    {
        if (session is null || !_openWorlds.Contains(session)) return false;

        ActivateWorldSession(session);
        if (!session.Vm.ConfirmDiscard())
            return false;

        session.Vm.ExitMultiMapEdit(force: true);
        session.Detach();
        _openWorlds.Remove(session);
        WorldSessionClosed?.Invoke(session);

        if (ReferenceEquals(_activeWorldSession, session))
        {
            _activeWorldSession = _openWorlds.Count > 0 ? _openWorlds[^1] : null;
            OnPropertyChanged(nameof(World));
            if (_activeWorldSession is not null)
                WorldSessionActivated?.Invoke(_activeWorldSession);
        }

        RaiseCombineCommands();
        return true;
    }

    public void PrepareEnterMultiMapEdit(WorldViewModel requester)
    {
        foreach (var session in _openWorlds)
        {
            if (!ReferenceEquals(session.Vm, requester) && session.Vm.IsMultiMapEditMode)
                session.Vm.ExitMultiMapEdit(force: true);
        }

        if (!ReferenceEquals(_worldShell, requester) && _worldShell.IsMultiMapEditMode)
            _worldShell.ExitMultiMapEdit(force: true);
        if (!ReferenceEquals(_combinedMapsVm, requester) && _combinedMapsVm.IsMultiMapEditMode)
            _combinedMapsVm.ExitMultiMapEdit(force: true);
    }

    public void CloseDocument(OpenMapDocument doc)
    {
        if (doc is null || !_openDocuments.Contains(doc)) return;

        if (ReferenceEquals(_activeDocument, doc))
            FinishStroke();

        _autosave.Delete(doc.Session.DocumentId);

        var wasActive = ReferenceEquals(_activeDocument, doc);
        _openDocuments.Remove(doc);
        DocumentClosed?.Invoke(doc);
        RaiseCombineCommands();

        if (_openDocuments.Count == 0)
        {
            ClearAllOpenDocumentsState();
            return;
        }

        if (wasActive)
        {
            BindActiveDocument(_openDocuments[^1], clearSelection: false);
            DocumentActivated?.Invoke(_activeDocument!);
            RequestFitMap?.Invoke();
        }
    }

    private void BindActiveDocument(OpenMapDocument doc, bool clearSelection)
    {
        _activeDocument = doc;
        _session = doc.Session;
        _swfMeta = doc.SwfMeta;
        // Map before HitTester so overlay redraws never pair the new tester with the previous map.
        CurrentMap = doc.Map;
        HitTester = doc.HitTester;
        if (!ReferenceEquals(_mapImage, doc.MapImage))
        {
            _mapImage = doc.MapImage;
            OnPropertyChanged(nameof(MapImage));
            FitMapCommand.RaiseCanExecuteChanged();
            Zoom100Command.RaiseCanExecuteChanged();
        }

        SelectedMapId = doc.MapId;
        HoveredCellId = null;
        if (clearSelection)
            ClearSelectionUi();
        else
            SyncSelectionFromSession();

        ApplyFightPlacesFromDocument(doc.Map);
        BumpCellModeOverlayRevision();
        AfterHistoryChange();
        SaveCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        ExportSwfCommand.RaiseCanExecuteChanged();
        ExportPackageCommand.RaiseCanExecuteChanged();
        OpenMapFolderCommand.RaiseCanExecuteChanged();
        RevertToSavedCommand.RaiseCanExecuteChanged();
        ReloadOriginalCommand.RaiseCanExecuteChanged();
        UpdateTitle();
        MapMonsters.NotifyMapOrSelectionChanged();
    }

    private void ClearAllOpenDocumentsState()
    {
        _activeDocument = null;
        _session = null;
        CurrentMap = null;
        MapImage = null;
        HitTester = null;
        _swfMeta = null;
        HoveredCellId = null;
        ClearSelectionUi();
        ResourceWarnings = Array.Empty<string>();
        PreviewGround = PreviewObject1 = PreviewObject2 = null;
        RenderTimeText = "—";
        EditLatencyText = "—";
        CoordsText = "—";
        StatusText = HasLibrary ? $"{MapIds.Count} mapas" : "Sin biblioteca";
        RefreshMapInspector();
        RefreshCellInspector();
        AfterHistoryChange();
        UpdateTitle();
        MapMonsters.NotifyMapOrSelectionChanged();

        if (_openedMapFromWorld)
        {
            _openedMapFromWorld = false;
            if (_worldEditingDocumentKey is not null)
                World.NotifyMapEdited(_worldEditingDocumentKey);
            WorkspaceTabIndex = 1;
        }

        _worldEditingDocumentKey = null;
    }

    private async Task OpenMapFromWorldAsync(MapDocument map, string documentKey)
    {
        if (IsMapCombinedMode && CombinedMaps.World is not null)
        {
            if (CombinedMaps.HasPlacedMapId(map.Id))
            {
                await OpenMapFromWorldCoreAsync(map, documentKey);
                RevealCombinedAndFocusMap(map.Id);
                return;
            }

            await TryAddMapToCombinedWithPromptAsync(map, documentKey);
            return;
        }

        await OpenMapFromWorldCoreAsync(map, documentKey);
    }

    /// <summary>
    /// World mosaic click: if MAPA combinado is open, ask where to glue the map (or open it aparte).
    /// Cancel leaves the world selection as-is.
    /// </summary>
    public async Task TryOfferAddWorldSelectionToCombinedAsync(WorldViewModel source)
    {
        if (!IsMapCombinedMode || source.IsScratchCombined || source.World is null)
            return;
        if (!source.HasSingleSelection)
            return;

        var key = source.SelectedKeys.First();
        if (!source.World.Documents.TryGetValue(key, out var entry))
            return;
        if (CombinedMaps.HasPlacedMapId(entry.Document.Id))
        {
            // Aviso no bloqueante: se puede pegar otra copia al combinado.
        }

        await TryAddMapToCombinedWithPromptAsync(entry.Document, key);
    }

    private async Task<bool> TryAddMapToCombinedWithPromptAsync(MapDocument map, string documentKey)
    {
        if (!TryAcceptCombinedMapSize(map.Width, map.Height, map.Id > 0 ? map.Id : null))
            return false;

        var placeMap = MapDocumentDuplicator.DeepCopy(map, map.Id > 0 ? map.Id : _nextTempMapId--);
        if (map.Id > 0 && !TryForkDuplicateCombinedMapId(placeMap, map.Id))
            return false;

        var dlg = new AddMapToCombinedWindow(
            map.Id,
            CombinedMaps.CombinedAnchorMapId(),
            CombinedMaps.SuggestedCombinedAddChoice())
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true)
            return false;

        if (dlg.Choice == CombinedAddChoice.Independent)
        {
            await OpenMapFromWorldCoreAsync(map, documentKey);
            SetMapCombinedMinimized(true);
            WorkspaceTabIndex = 0;
            StatusText = $"Mapa {map.Id} abierto aparte · Restaurar para volver al combinado";
            return true;
        }

        var (dx, dy) = dlg.Choice switch
        {
            CombinedAddChoice.Left => (-1, 0),
            CombinedAddChoice.Right => (1, 0),
            CombinedAddChoice.Up => (0, -1),
            _ => (0, 1),
        };

        var key = CombinedMaps.InsertDocumentAdjacent(placeMap, dx, dy);
        if (key is null)
        {
            StatusText = "No se pudo pegar el mapa al combinado";
            return false;
        }

        EnsureMosaicBoundDocument(placeMap, key);
        RevealCombinedAndFocusMapKey(key);
        OnPropertyChanged(nameof(MapCombinedModeLabel));
        var side = dlg.Choice switch
        {
            CombinedAddChoice.Left => "izquierda",
            CombinedAddChoice.Right => "derecha",
            CombinedAddChoice.Up => "arriba",
            _ => "abajo",
        };
        StatusText = placeMap.Id > 0
            ? $"Mapa {placeMap.Id} pegado a la {side} del combinado"
            : $"Copia de {map.Id} pegada a la {side} · al guardar eliges nuevo Map ID";
        return true;
    }

    public void NotifyCombinedLayoutChanged()
    {
        if (!IsMapCombinedMode)
            return;

        // No salir automáticamente si queda vacío: así Deshacer puede recuperar los mapas.
        if (CombinedMaps.World is null || CombinedMaps.World.Placements.Count == 0)
        {
            RefreshCombinedMapChips();
            RaiseCombineCommands();
            NotifyMosaicUndoRedoChanged();
            StatusText = "Combinado vacío · Deshacer para recuperar, o ✕ para salir";
            CombinedMaps.RequestViewRedraw();
            return;
        }

        foreach (var p in CombinedMaps.World.Placements)
            MultiMap.EnsureEditable(p.DocumentKey);

        CombinedMaps.EnsureCombinedNeighborSlots();
        RefreshCombinedMapChips();
        RaiseCombineCommands();
        OnPropertyChanged(nameof(MapCombinedModeLabel));
        NotifyMosaicUndoRedoChanged();
        CombinedMaps.FitAllCommand.Execute(null);
    }

    private void AfterCombinedLayoutHistoryChange()
    {
        if (!IsMapCombinedMode) return;
        NotifyCombinedLayoutChanged();
    }

    /// <summary>Drag-drop payload from the left MAPAS list onto combinado "+" slots.</summary>
    public const string MapIdDragFormat = "RufusMapEditor.MapId";

    /// <summary>Combinado: click a soft "+" slot to pick a library map and glue it there.</summary>
    public async Task PromptAddMapToCombinedAtAsync(int worldX, int worldY)
    {
        if (!IsMapCombinedMode || CombinedMaps.World is null || !_library.IsLoaded)
            return;

        var ids = MapIds.Count > 0 ? MapIds.ToList() : _library.DiscoverMapIds();
        if (ids.Count == 0)
        {
            StatusText = "No hay mapas en la biblioteca";
            return;
        }

        var sizeHint = CombinedMaps.GetWorkingMapSizeLabel();
        var prompt = sizeHint is null
            ? $"Selecciona un mapa para la casilla ({worldX},{worldY}):"
            : $"Combinado en modo {sizeHint}. Solo mapas de ese tamaño.\nCasilla ({worldX},{worldY}):";

        var pick = new MapPickerWindow(
            _library,
            _mapPreviews,
            ids,
            SelectedMapId,
            title: "Añadir al combinado",
            prompt: prompt,
            allowNewMap: true)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow,
        };
        if (pick.ShowDialog() != true)
            return;

        if (pick.NewMapRequested)
        {
            await NewMapAsync(placeInCombinedAt: (worldX, worldY));
            return;
        }

        if (pick.SelectedMapId is not int mapId)
            return;

        await AddMapToCombinedAtAsync(mapId, worldX, worldY);
    }

    /// <summary>Combinado: place a known library map at a world grid cell (picker or drag-drop).</summary>
    public async Task AddMapToCombinedAtAsync(int mapId, int worldX, int worldY)
    {
        if (!IsMapCombinedMode || CombinedMaps.World is null || !_library.IsLoaded)
            return;

        IsLoading = true;
        try
        {
            // Copia fresca de biblioteca: el mismo ID puede coexistir como colocaciones independientes.
            var map = await Task.Run(() => _library.LoadMapDocument(mapId));

            if (!TryAcceptCombinedMapSize(map.Width, map.Height, mapId))
                return;

            if (!TryForkDuplicateCombinedMapId(map, mapId))
                return;

            var key = CombinedMaps.PlaceNewMapAt(map, worldX, worldY);
            if (key is null)
                return;

            EnsureMosaicBoundDocument(map, key);
            RevealCombinedAndFocusMapKey(key);
            OnPropertyChanged(nameof(MapCombinedModeLabel));
            StatusText = map.Id > 0
                ? $"Mapa {map.Id} añadido al combinado en ({worldX},{worldY})"
                : $"Copia de {mapId} añadida · al guardar eliges nuevo Map ID";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo añadir el mapa:\n{ex.Message}", "Combinado");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reject maps whose grid size differs from the combinado working size.</summary>
    private bool TryAcceptCombinedMapSize(int width, int height, int? sourceMapId = null)
    {
        if (CombinedMaps.MatchesWorkingMapSize(width, height))
            return true;

        var working = CombinedMaps.GetWorkingMapSize();
        if (working is null)
            return true;

        var workingLabel = CombinedMaps.GetWorkingMapSizeLabel() ?? $"{working.Value.Width}×{working.Value.Height}";
        var incoming = NewMapSizeWindow.DescribeSize(width, height);
        var src = sourceMapId is int id ? $"Mapa {id} ({incoming} {width}×{height})" : $"{incoming} {width}×{height}";
        MessageBox.Show(
            $"Este combinado trabaja en {workingLabel}.\n\n" +
            $"{src} no cabe en las casillas (sobresale / desalineado).\n\n" +
            "Solo puedes añadir mapas del mismo tamaño. Crea otro combinado si necesitas otro formato.",
            "Tamaño distinto",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        StatusText = $"Rechazado: tamaño distinto al del combinado ({workingLabel})";
        return false;
    }

    /// <summary>
    /// If Map ID already exists in the combinado, fork to a temp ID so official saves cannot overwrite each other.
    /// </summary>
    private bool TryForkDuplicateCombinedMapId(MapDocument map, int sourceMapId)
    {
        if (sourceMapId <= 0 || !CombinedMaps.HasPlacedMapId(sourceMapId))
            return true;

        var positions = CombinedMaps.World!.Placements
            .Where(p => CombinedMaps.World.Documents.TryGetValue(p.DocumentKey, out var e) && e.Document.Id == sourceMapId)
            .Select(p => $"({p.WorldX},{p.WorldY})")
            .ToList();

        var answer = MessageBox.Show(
            $"Map ID {sourceMapId} ya está en el combinado ({string.Join(", ", positions)}).\n\n" +
            "Si colocas otra copia y guardas ambas con el mismo ID, se pisarían en Library\\Maps.\n\n" +
            "Se colocará como mapa NUEVO (copia). Al guardar te pedirá otro Map ID.\n\n¿Colocar copia?",
            "Mapa duplicado → guardar como nuevo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            StatusText = "Añadir cancelado (Map ID duplicado)";
            return false;
        }

        map.Id = _nextTempMapId--;
        return true;
    }

    /// <summary>
    /// Before official save: if another open/combined document shares the same positive Map ID, force Save As.
    /// </summary>
    private bool EnsureNoOfficialIdCollisionBeforeSave()
    {
        if (CurrentMap is null || CurrentMap.Id <= 0)
            return true;

        var id = CurrentMap.Id;
        var siblings = new List<string>();

        foreach (var doc in _openDocuments)
        {
            if (ReferenceEquals(doc.Map, CurrentMap)) continue;
            if (doc.Map.Id == id)
                siblings.Add(doc.WindowTitle);
        }

        if (IsMapCombinedMode && CombinedMaps.World is not null)
        {
            foreach (var entry in CombinedMaps.World.Documents.Values)
            {
                if (ReferenceEquals(entry.Document, CurrentMap)) continue;
                if (entry.Document.Id != id) continue;
                var label = $"colocación {entry.Key[..Math.Min(6, entry.Key.Length)]}…";
                if (!siblings.Contains(label))
                    siblings.Add($"Mapa {id} ({label})");
            }
        }

        if (siblings.Count == 0)
            return true;

        var answer = MessageBox.Show(
            $"Hay otra copia abierta/colocada con Map ID {id}.\n\n" +
            "Si guardas aquí, sobrescribirías el mismo archivo de Library y podrías perder cambios de la otra copia.\n\n" +
            "Esta copia se guardará como mapa NUEVO (te pediremos otro Map ID).\n\n¿Continuar?",
            "Conflicto de Map ID",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return false;

        // Force the Save-As ID prompt path used by unsaved templates.
        CurrentMap.Id = _nextTempMapId--;
        if (_activeDocument is not null)
            _activeDocument.NotifyDirtyChanged();
        SelectedMapId = null;
        RefreshCombinedMapChips();
        return true;
    }

    private void RevealCombinedAndFocusMapKey(string documentKey)
    {
        WorkspaceTabIndex = 0;
        SetMapCombinedMinimized(false);
        RefreshCombinedMapChips();
        RaiseCombineCommands();
        OnPropertyChanged(nameof(MapCombinedModeLabel));
        if (!string.IsNullOrEmpty(documentKey))
            FocusOpenMapFromWorldDocumentKey(documentKey);
        CombinedMaps.FitAllCommand.Execute(null);
    }

    private void RevealCombinedAndFocusMap(int mapId)
    {
        var key = CombinedMaps.World?.Placements
            .Select(p => p.DocumentKey)
            .LastOrDefault(k =>
                CombinedMaps.World!.Documents.TryGetValue(k, out var e) && e.Document.Id == mapId);
        if (key is not null)
            RevealCombinedAndFocusMapKey(key);
        else
        {
            WorkspaceTabIndex = 0;
            SetMapCombinedMinimized(false);
            RefreshCombinedMapChips();
            RaiseCombineCommands();
            CombinedMaps.FitAllCommand.Execute(null);
        }
    }

    private async Task OpenMapFromWorldCoreAsync(MapDocument map, string documentKey)
    {
        var existing = FindOpenDocument(map.Id);
        if (existing is not null)
        {
            _worldEditingDocumentKey = documentKey;
            _openedMapFromWorld = true;
            WorkspaceTabIndex = 0;
            ActivateDocument(existing);
            return;
        }

        _worldEditingDocumentKey = documentKey;
        _openedMapFromWorld = true;
        WorkspaceTabIndex = 0;
        IsLoading = true;
        StatusText = $"Abriendo mapa {map.Id} desde Mundo...";
        HoveredCellId = null;
        FinishStroke();

        try
        {
            var result = await Task.Run(() => _library.IsLoaded ? _library.Render(map, BuildRenderOptions()) : null);
            if (result is null)
            {
                MessageBox.Show("Biblioteca no cargada.", "Mundo");
                return;
            }

            ApplyRenderResultFromWorld(map, documentKey, result);
            SelectedMapId = map.Id;
            RequestFitMap?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el mapa:\n{ex.Message}", "Mundo");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyRenderResultFromWorld(MapDocument map, string documentKey, MapRenderResult result)
    {
        var image = BitmapConversion.ToBitmapSource(result.Image);
        result.Image.Dispose();
        var hit = new IsoHitTester(map.Width, map.Height);
        var session = new MapEditSession(map, hit)
        {
            DocumentId = documentKey,
            CreatedUtc = DateTimeOffset.UtcNow,
            Source = new RufmapSourceDto
            {
                Kind = "WorldEmbedded",
                OriginalMapId = map.Id,
                LibraryPathHint = _library.RootPath,
            },
            ProjectName = World.World?.Name,
            FilePath = World.World?.Documents.TryGetValue(documentKey, out var entry) == true
                ? entry.LinkedRufmapPath
                : null,
        };
        session.CaptureLoadBaseline();

        var openDoc = new OpenMapDocument(map, session, hit, image, null)
        {
            CascadeIndex = _nextCascadeIndex++
        };
        _openDocuments.Add(openDoc);
        BindActiveDocument(openDoc, clearSelection: true);
        DocumentOpened?.Invoke(openDoc);
        RaiseCombineCommands();

        RenderTimeText = $"{result.Metrics.Render.TotalMilliseconds:F0} ms";

        var warnings = new List<string>();
        foreach (var g in result.MissingGfx)
        {
            if (g.StartsWith("Background:", StringComparison.Ordinal))
                warnings.Add($"Recurso ausente: Background GfxID {g["Background:".Length..]}");
            else
                warnings.Add($"Recurso ausente: {g}");
        }

        ResourceWarnings = warnings;
        StatusText = $"Mundo → mapa {map.Id}";
        UpdateTitle();
    }

    private void NotifyWorldMapEditedIfOpen()
    {
        if (_worldEditingDocumentKey is not null)
            World.NotifyMapEdited(_worldEditingDocumentKey);
    }

    /// <summary>Tras guardar oficial: refresca miniaturas / documentos del mismo MapId en mundos abiertos.</summary>
    private void NotifyWorldsAfterMapSaved(int mapId)
    {
        NotifyWorldMapEditedIfOpen();
        if (mapId <= 0) return;

        void Refresh(WorldViewModel w)
        {
            if (w.World is null || w.IsScratchCombined)
                return;

            var sharedLive = CurrentMap is not null &&
                             w.World.Documents.Values.Any(e => ReferenceEquals(e.Document, CurrentMap));
            if (sharedLive)
            {
                foreach (var (key, entry) in w.World.Documents)
                {
                    if (entry.Document.Id == mapId)
                        w.InvalidateThumbnail(key);
                }

                return;
            }

            try
            {
                w.ReloadPlacedMapsFromLibrary(mapId, id => _library.LoadMapDocument(id));
            }
            catch (Exception ex)
            {
                RufusLog.Error($"No se pudo refrescar mapa {mapId} en mundo: {ex.Message}");
            }
        }

        Refresh(_worldShell);
        foreach (var session in _openWorlds)
            Refresh(session.Vm);
    }

    private async void ReloadOriginal()
    {
        if (CurrentMap is null || !IsAstriaImport) return;
        if (!ConfirmDiscardIfDirty()) return;
        var id = CurrentMap.Id;
        if (_activeDocument is not null)
        {
            var doc = _activeDocument;
            _autosave.Delete(doc.Session.DocumentId);
            _openDocuments.Remove(doc);
            DocumentClosed?.Invoke(doc);
            if (_openDocuments.Count == 0)
                ClearAllOpenDocumentsState();
            else
            {
                BindActiveDocument(_openDocuments[^1], clearSelection: false);
                DocumentActivated?.Invoke(_activeDocument!);
            }
        }
        await LoadMapAsync(id);
    }

    private void Undo()
    {
        if (IsWorldTab)
        {
            if (World.UndoWorldCommand.CanExecute(null))
                World.UndoWorldCommand.Execute(null);
            return;
        }

        if (IsMapCombinedMode)
        {
            UndoCombinedChronological();
            return;
        }

        if (MosaicHost.IsMultiMapEditMode)
        {
            if (MultiMap.Undo())
            {
                NotifyMosaicUndoRedoChanged();
                SyncUiFromMultiMapSelection();
                StatusText = "Deshecho (multimap)";
            }
            return;
        }

        if (_session is null) return;
        if (_session.Undo())
        {
            AfterHistoryChange();
            CellModeOverlayRevision++;
            SyncSelectionFromSession();
            _ = RerenderAsync();
            StatusText = "Deshecho";
        }
    }

    /// <summary>Deshace el último cambio real (ítem o cuadrícula), no prioriza el layout.</summary>
    private void UndoCombinedChronological()
    {
        var layoutSeq = CombinedMaps.CanUndoWorld ? CombinedMaps.TopLayoutUndoSequence : long.MinValue;
        var multiSeq = MosaicHost.IsMultiMapEditMode && MultiMap.History.CanUndo
            ? MultiMap.History.TopUndoSequence
            : long.MinValue;
        var sessionSeq = _session?.History.CanUndo == true
            ? _session.History.TopUndoSequence
            : long.MinValue;

        var best = Math.Max(layoutSeq, Math.Max(multiSeq, sessionSeq));
        if (best == long.MinValue)
            return;

        if (best == multiSeq)
        {
            if (MultiMap.Undo())
            {
                NotifyMosaicUndoRedoChanged();
                SyncUiFromMultiMapSelection();
                StatusText = "Deshecho (pintura)";
            }
            return;
        }

        if (best == sessionSeq)
        {
            if (_session!.Undo())
            {
                AfterHistoryChange();
                CellModeOverlayRevision++;
                SyncSelectionFromSession();
                PushSessionSelectionToMultiMap();
                _ = RerenderAsync();
                StatusText = "Deshecho";
            }
            return;
        }

        if (CombinedMaps.UndoWorldCommand.CanExecute(null))
        {
            CombinedMaps.UndoWorldCommand.Execute(null);
            AfterCombinedLayoutHistoryChange();
            StatusText = "Deshecho (cuadrícula)";
        }
    }

    private void Redo()
    {
        if (IsWorldTab)
        {
            if (World.RedoWorldCommand.CanExecute(null))
                World.RedoWorldCommand.Execute(null);
            return;
        }

        if (IsMapCombinedMode)
        {
            RedoCombinedChronological();
            return;
        }

        if (MosaicHost.IsMultiMapEditMode)
        {
            if (MultiMap.Redo())
            {
                NotifyMosaicUndoRedoChanged();
                SyncUiFromMultiMapSelection();
                StatusText = "Rehecho (multimap)";
            }
            return;
        }

        if (_session is null) return;
        if (_session.Redo())
        {
            AfterHistoryChange();
            CellModeOverlayRevision++;
            SyncSelectionFromSession();
            _ = RerenderAsync();
            StatusText = "Rehecho";
        }
    }

    private void RedoCombinedChronological()
    {
        var layoutSeq = CombinedMaps.CanRedoWorld ? CombinedMaps.TopLayoutRedoSequence : long.MinValue;
        var multiSeq = MosaicHost.IsMultiMapEditMode && MultiMap.History.CanRedo
            ? MultiMap.History.TopRedoSequence
            : long.MinValue;
        var sessionSeq = _session?.History.CanRedo == true
            ? _session.History.TopRedoSequence
            : long.MinValue;

        var best = Math.Max(layoutSeq, Math.Max(multiSeq, sessionSeq));
        if (best == long.MinValue)
            return;

        if (best == multiSeq)
        {
            if (MultiMap.Redo())
            {
                NotifyMosaicUndoRedoChanged();
                SyncUiFromMultiMapSelection();
                StatusText = "Rehecho (pintura)";
            }
            return;
        }

        if (best == sessionSeq)
        {
            if (_session!.Redo())
            {
                AfterHistoryChange();
                CellModeOverlayRevision++;
                SyncSelectionFromSession();
                PushSessionSelectionToMultiMap();
                _ = RerenderAsync();
                StatusText = "Rehecho";
            }
            return;
        }

        if (CombinedMaps.RedoWorldCommand.CanExecute(null))
        {
            CombinedMaps.RedoWorldCommand.Execute(null);
            AfterCombinedLayoutHistoryChange();
            StatusText = "Rehecho (cuadrícula)";
        }
    }

    public bool IsPasteArmed => _pasteArmed;

    public void CancelPasteArmed()
    {
        if (!_pasteArmed) return;
        _pasteArmed = false;
        OnPropertyChanged(nameof(IsPasteArmed));
        PasteCommand.RaiseCanExecuteChanged();
    }

    private void CopySelection()
    {
        if (IsWorldTab)
        {
            if (World.CopyCommand.CanExecute(null))
                World.CopyCommand.Execute(null);
            return;
        }

        if (IsMapCombinedMode && MultiMap.IsActive && MultiMap.Selection.Count > 0)
        {
            CopyMultiMapSelection();
            CaptureBrushFromSelection();
            PasteCommand.RaiseCanExecuteChanged();
            DuplicateCommand.RaiseCanExecuteChanged();
            StatusText = FormatCopiedGfxStatus(MultiMap.Selection.Count);
            return;
        }

        if (_session is null || !HasSelection) return;
        _session.CopySelection();
        CaptureBrushFromSelection();
        PasteCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        StatusText = FormatCopiedGfxStatus(SelectedCellIds.Count);
    }

    private string FormatCopiedGfxStatus(int cellCount) =>
        SelectedGfxId is int gfx
            ? Tool == EditorTool.Paint
                ? $"GFX {gfx} copiado · {UiDisplayLabels.LayerTarget(PaintLayer)} · clic para colocar"
                : $"GFX {gfx} copiado · {UiDisplayLabels.LayerTarget(PaintLayer)} · Construir (B) para colocarlo"
            : cellCount > 0
                ? $"Copiadas {cellCount} celdas (sin GFX en el pincel)"
                : "Celda sin GFX — nada en el pincel";

    private void PasteSelection()
    {
        if (IsWorldTab)
        {
            if (World.PasteCommand.CanExecute(null))
                World.PasteCommand.Execute(null);
            return;
        }

        if (Tool != EditorTool.Paint)
        {
            StatusText = SelectedGfxId is int gfx
                ? $"GFX {gfx} en el pincel · {UiDisplayLabels.LayerTarget(PaintLayer)} · pasa a Construir (B) para pegarlo"
                : "Pasa a Construir (B) para pegar";
            return;
        }

        StampBrushAtHoverOrSelection();
    }

    private void StampBrushAtHoverOrSelection()
    {
        if (SelectedGfxId is not int gfxId)
        {
            if (IsMapCombinedMode && MultiMap.HasClipboard && CurrentMap is not null)
            {
                var key = FindWorldDocumentKeyForMap(CurrentMap);
                var destId = PrimarySelectedCellId ?? HoveredCellId;
                if (key is null || destId is null)
                {
                    StatusText = "Pasa el cursor por una celda destino para pegar.";
                    return;
                }

                PasteMultiMapAt(new WorldCellHit(key, destId.Value, 0, 0, 0, 0));
                return;
            }

            if (_session is null) return;
            var fallback = HoveredCellId ?? PrimarySelectedCellId;
            if (fallback is null)
            {
                StatusText = "Pasa el cursor por una celda destino para pegar.";
                return;
            }

            ApplyPasteAt(fallback.Value);
            return;
        }

        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            WorldCellRef? cell = null;
            if (MultiMap.HoveredCell is WorldCellHit hover)
                cell = new WorldCellRef(hover.DocumentKey, hover.CellId);
            else if (PrimarySelectedCellId is int id
                     && CurrentMap is not null
                     && FindWorldDocumentKeyForMap(CurrentMap) is string key)
                cell = new WorldCellRef(key, id);
            if (cell is null)
            {
                StatusText = "Pasa el cursor por una celda para pegar el GFX.";
                return;
            }

            BeginMultiMapStroke();
            HandleMultiMapCellClick(cell.Value, isDrag: false, ctrl: false);
            FinishMultiMapStroke();
            StatusText = $"Colocado GFX {gfxId} · {UiDisplayLabels.LayerTarget(PaintLayer)}";
            return;
        }

        var dest = HoveredCellId ?? PrimarySelectedCellId;
        if (dest is null)
        {
            StatusText = "Pasa el cursor por una celda para pegar el GFX.";
            return;
        }

        BeginPaintStroke();
        PaintCell(dest.Value, isDrag: false);
        FinishStroke();
        StatusText = $"Colocado GFX {gfxId} · {UiDisplayLabels.LayerTarget(PaintLayer)}";
    }

    private void ApplyPasteAt(int destCellId)
    {
        if (_session is null) return;
        var (pasted, skipped) = _session.PasteAt(destCellId);
        AfterHistoryChange();
        SyncSelectionFromSession();
        PrimarySelectedCellId = SelectedCellIds.Count > 0 ? SelectedCellIds[^1] : destCellId;
        _ = RerenderAsync();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();

        StatusText = skipped > 0
            ? $"Pegadas {pasted} celdas ({skipped} fuera del mapa omitidas)"
            : $"Pegadas {pasted} celdas";
    }

    private void DuplicateSelection()
    {
        if (_session is null || !HasSelection || HitTester is null) return;
        _session.CopySelection();

        var anchor = PrimarySelectedCellId ?? SelectedCellIds[0];
        if (!HitTester.TryGetCellCornersInHitSpace(anchor, out var c))
        {
            StatusText = "No se pudo duplicar (ancla inválida).";
            return;
        }

        var cx = (c.A.X + c.C.X) / 2.0;
        var cy = (c.B.Y + c.D.Y) / 2.0;
        var width = Math.Abs(c.B.X - c.D.X);
        if (width < 1) width = 53;
        var target = IsoSelection.ResolvePasteTarget(HitTester, cx + width, cy)
                     ?? IsoSelection.ResolvePasteTarget(HitTester, cx, cy + width * 0.5);
        if (target is null || target == anchor)
        {
            StatusText = "No hay celda destino para duplicar.";
            return;
        }

        var (pasted, skipped) = _session.PasteAt(target.Value);
        AfterHistoryChange();
        SyncSelectionFromSession();
        PrimarySelectedCellId = SelectedCellIds.Count > 0 ? SelectedCellIds[^1] : target;
        _ = RerenderAsync();

        StatusText = skipped > 0
            ? $"Duplicadas {pasted} celdas ({skipped} omitidas)"
            : $"Duplicadas {pasted} celdas";
    }

    private void CombineOpenMaps()
    {
        var maps = _openDocuments
            .Select(d => d.Map)
            .Where(m => m.Id != 0)
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .ToList();

        if (maps.Count < 2)
            maps = _openDocuments.Select(d => d.Map).ToList();

        if (maps.Count < 2)
        {
            StatusText = "Abre al menos 2 mapas en MAPA para combinarlos";
            return;
        }

        var dlg = new CombineOpenMapsWindow(maps.Count)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true) return;

        var keys = CombinedMaps.CombineFromDocuments(
            maps,
            horizontal: dlg.Horizontal,
            replaceWorld: true,
            enterMultiMapEdit: true,
            scratchCombined: true);

        if (keys.Count == 0) return;

        WorkspaceTabIndex = 0;
        IsMapCombinedMode = true;
        SetCombinedMapsMultiSelect(false);
        CombinedMaps.ShowMapBounds = true;
        CombinedMaps.MultiMap.ClearSelection();
        RefreshCombinedMapChips();
        RaiseCombineCommands();
        ScheduleCombinedViewportFit();
        StatusText = dlg.Horizontal
            ? $"Combinados {keys.Count} mapas en horizontal · marca arriba los que quieras juntos"
            : $"Combinados {keys.Count} mapas en vertical · marca arriba los que quieras juntos";
    }

    /// <summary>Encaja el mosaico cuando el viewport ya tiene tamaño (primera entrada al combinado).</summary>
    public void ScheduleCombinedViewportFit()
    {
        Application.Current?.Dispatcher.BeginInvoke(
            () =>
            {
                if (!IsMapCombinedMode || IsMapCombinedMinimized) return;
                CombinedMaps.FitAllCommand.Execute(null);
            },
            DispatcherPriority.Loaded);
    }

    private void AppendActiveMapToCombined()
    {
        if (CurrentMap is null || !IsMapCombinedMode) return;
        if (!TryAcceptCombinedMapSize(CurrentMap.Width, CurrentMap.Height, CurrentMap.Id > 0 ? CurrentMap.Id : null))
            return;

        var placeMap = MapDocumentDuplicator.DeepCopy(
            CurrentMap,
            CurrentMap.Id > 0 ? CurrentMap.Id : _nextTempMapId--);
        if (CurrentMap.Id > 0 && !TryForkDuplicateCombinedMapId(placeMap, CurrentMap.Id))
            return;

        var key = CombinedMaps.AppendDocumentAdjacent(placeMap, preferHorizontal: true);
        if (key is not null)
            EnsureMosaicBoundDocument(placeMap, key);
        WorkspaceTabIndex = 0;
        RefreshCombinedMapChips();
        RaiseCombineCommands();
        OnPropertyChanged(nameof(MapCombinedModeLabel));
        StatusText = placeMap.Id > 0
            ? $"Mapa {placeMap.Id} añadido al combinado (MAPA)"
            : $"Copia añadida al combinado · al guardar eliges nuevo Map ID";
    }

    private void ExitMapCombinedMode()
    {
        if (!IsMapCombinedMode) return;
        CombinedMaps.ResetScratchCombined();
        IsMapCombinedMode = false;
        SetCombinedMapsMultiSelect(false);
        RemoveMosaicBoundDocuments();
        WorkspaceTabIndex = 0;
        RaiseCombineCommands();
        StatusText = "Vuelta a ventanas de mapa separadas";
    }

    private void SendCombinedToWorld()
    {
        if (!IsMapCombinedMode || CombinedMaps.World is null) return;

        var maps = CombinedMaps.GetPlacedMapsInReadingOrder();
        if (maps.Count == 0) return;

        var placed = CombinedMaps.World.Placements.Count;
        var suggestW = Math.Max(10, Math.Max(CombinedMaps.World.GridWidth, placed));
        var suggestH = Math.Max(10, CombinedMaps.World.GridHeight);
        var dlg = new WorldGridSizeWindow(
            suggestWidth: suggestW,
            suggestHeight: suggestH,
            suggestOriginX: CombinedMaps.World.OriginX,
            suggestOriginY: CombinedMaps.World.OriginY)
        {
            Owner = Application.Current.MainWindow,
            Title = "Enviar a MUNDO — tamaño de cuadrícula",
        };
        if (dlg.ShowDialog() != true) return;

        var vm = CreateWorldViewModel();
        var seeded = vm.CombineFromDocuments(
            maps,
            horizontal: CombinedMaps.World.GridWidth >= CombinedMaps.World.GridHeight,
            replaceWorld: true,
            enterMultiMapEdit: false);
        if (seeded.Count == 0)
        {
            StatusText = "No se pudo enviar el combinado a MUNDO";
            return;
        }

        if (!vm.TransferCombinedToWorldGrid(
                dlg.ResultGridWidth,
                dlg.ResultGridHeight,
                dlg.ResultOriginX,
                dlg.ResultOriginY))
        {
            StatusText = "No se pudo enviar el combinado a MUNDO";
            return;
        }

        OpenWorldSessionFromVm(vm);
        StatusText =
            $"Copia en MUNDO · cuadrícula {dlg.ResultGridWidth}×{dlg.ResultGridHeight} " +
            $"(inicio {dlg.ResultOriginX},{dlg.ResultOriginY}) · Vulcania y el combinado de MAPA siguen abiertos";
    }

    private bool CanSendWorldToCombined() =>
        World.World is { Placements.Count: > 0 } && !World.IsScratchCombined;

    private void SendWorldToCombined()
    {
        var source = World;
        if (source.World is null || source.IsScratchCombined || source.World.Placements.Count == 0)
            return;

        if (IsMapCombinedMode && CombinedMaps.World is { Placements.Count: > 0 })
        {
            var replace = MessageBox.Show(
                "Ya hay un combinado en MAPA.\n¿Reemplazarlo con la disposición de este mundo?",
                "Enviar a modo mapa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (replace != MessageBoxResult.Yes)
                return;
        }

        var keys = CombinedMaps.ImportWorldLayoutAsScratch(source.World);
        if (keys.Count == 0)
        {
            StatusText = "No se pudo enviar el mundo a MAPA combinado";
            return;
        }

        WorkspaceTabIndex = 0;
        IsMapCombinedMode = true;
        SetCombinedMapsMultiSelect(false);
        CombinedMaps.ShowMapBounds = true;
        CombinedMaps.MultiMap.ClearSelection();
        RefreshCombinedMapChips();
        RaiseCombineCommands();
        FocusOpenMapFromWorldDocumentKey(keys[0]);
        ScheduleCombinedViewportFit();
        StatusText = $"Mundo → combinado · {keys.Count} mapas · misma disposición · edítalos juntos";
    }

    public void OnCombinedMapChipToggled()
    {
        if (!IsMapCombinedMode || CombinedMaps.World is null) return;

        var picked = CombinedMapChips.Where(c => c.IsSelected).Select(c => c.DocumentKey).ToList();
        CombinedMaps.SetSelectedKeys(picked);
        SetCombinedMapsMultiSelect(picked.Count > 1);
        PruneMultiMapSelectionToSelectedMaps();

        if (picked.Count == 1)
            FocusOpenMapFromWorldDocumentKey(picked[0]);
        else if (picked.Count > 1 && CurrentMap is not null)
        {
            var currentKey = FindWorldDocumentKeyForMap(CurrentMap);
            if (currentKey is null || !picked.Contains(currentKey))
                FocusOpenMapFromWorldDocumentKey(picked[0]);
        }

        RaiseCombineCommands();
        NotifyFocusGfxUi();
        CombinedMaps.RequestViewRedraw();
        StatusText = picked.Count == 0
            ? "Ningún mapa marcado · marca al menos uno arriba"
            : picked.Count == 1
                ? $"Mapa individual · Guardar seleccionado disponible"
                : $"Multi-mapa · {picked.Count} mapas · ESTE ÍTEM en todos";
    }

    private void SelectAllCombinedMapChips()
    {
        if (CombinedMapChips.Count == 0) return;
        foreach (var chip in CombinedMapChips)
            chip.SetSelectedSilent(true);
        OnCombinedMapChipToggled();
    }

    private void ClearCombinedMapChipsKeepFirst()
    {
        if (CombinedMapChips.Count == 0) return;
        for (var i = 0; i < CombinedMapChips.Count; i++)
            CombinedMapChips[i].SetSelectedSilent(i == 0);
        OnCombinedMapChipToggled();
    }

    /// <summary>Drops cell highlights that belong to maps no longer checked.</summary>
    private void PruneMultiMapSelectionToSelectedMaps()
    {
        if (!MultiMap.IsActive) return;

        var allowed = CombinedMaps.SelectedKeys;
        var kept = MultiMap.Selection.Where(c => allowed.Contains(c.DocumentKey)).ToList();
        MultiMap.SetSelection(kept);
        SyncUiFromMultiMapSelection();
    }

    public void RefreshCombinedMapChips()
    {
        CombinedMapChips.Clear();
        if (!IsMapCombinedMode || CombinedMaps.World is null) return;

        foreach (var p in CombinedMaps.World.Placements.OrderBy(p => p.WorldY).ThenBy(p => p.WorldX))
        {
            if (!CombinedMaps.World.Documents.TryGetValue(p.DocumentKey, out var entry))
                continue;
            CombinedMapChips.Add(new CombinedMapChipVm(
                this,
                p.DocumentKey,
                entry.Document.Id <= 0
                    ? $"Nuevo ({entry.Document.Width}×{entry.Document.Height})"
                    : $"Mapa {entry.Document.Id}",
                CombinedMaps.SelectedKeys.Contains(p.DocumentKey)));
        }

        SelectAllCombinedMapChipsCommand.RaiseCanExecuteChanged();
        ClearCombinedMapChipsKeepFirstCommand.RaiseCanExecuteChanged();
    }

    private void SyncCombinedMapChipsFromWorld()
    {
        if (CombinedMapChips.Count == 0)
        {
            RefreshCombinedMapChips();
            return;
        }

        foreach (var chip in CombinedMapChips)
            chip.SetSelectedSilent(CombinedMaps.SelectedKeys.Contains(chip.DocumentKey));
    }

    private void SetCombinedMapsMultiSelect(bool value)
    {
        if (_combinedMapsMultiSelect == value) return;
        _combinedMapsMultiSelect = value;
        OnPropertyChanged(nameof(IsCombinedMapsMultiSelect));
        OnPropertyChanged(nameof(MapCombinedModeLabel));
        SaveSelectedCombinedMapCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveSelectedCombinedMapAsync()
    {
        if (!IsMapCombinedMode || !CombinedMaps.HasSingleSelection || CombinedMaps.World is null)
            return;

        var key = CombinedMaps.SelectedKeys.First();
        if (!CombinedMaps.World.Documents.TryGetValue(key, out var entry))
            return;

        var open = _openDocuments.FirstOrDefault(d => ReferenceEquals(d.Map, entry.Document))
                   ?? _openDocuments.FirstOrDefault(d => d.Map.Id == entry.Document.Id);
        if (open is not null)
            ActivateDocument(open);

        var ok = await SaveOfficialMapAsync();
        if (ok)
            StatusText = $"Guardado mapa {entry.Document.Id}";
        RaiseCombineCommands();
    }

    private async Task SaveAllCombinedMapsAsync()
    {
        if (!IsMapCombinedMode) return;

        var dirtyDocs = _openDocuments.Where(d => d.IsDirty).ToList();
        if (dirtyDocs.Count == 0)
        {
            CombinedMaps.SaveModifiedMaps();
            RaiseCombineCommands();
            return;
        }

        var saved = 0;
        var previous = _activeDocument;
        foreach (var doc in dirtyDocs)
        {
            ActivateDocument(doc);
            if (await SaveOfficialMapAsync())
                saved++;
        }

        if (previous is not null && _openDocuments.Contains(previous))
            ActivateDocument(previous);

        if (CombinedMaps.ModifiedMapCount > 0)
            CombinedMaps.SaveModifiedMaps();

        StatusText = saved > 0 ? $"Guardados {saved} mapa(s)" : "Nada que guardar";
        RaiseCombineCommands();
    }

    private async Task AddCombinedMapsToPublishQueueAsync()
    {
        if (!IsMapCombinedMode || CombinedMaps.World is null)
            return;

        var ids = CombinedMaps.World.Placements
            .Select(p => CombinedMaps.World.Documents.TryGetValue(p.DocumentKey, out var e) ? e.Document.Id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            StatusText = "No hay mapas en el combinado para encolar";
            return;
        }

        foreach (var id in ids)
            await MapPublishQueue.AddMapAsync(id).ConfigureAwait(true);

        StatusText = $"Añadidos {ids.Count} mapa(s) a la cola de publicación";
        MapPublishQueue.OpenQueueCommand.Execute(null);
    }

    private void RaiseCombineCommands()
    {
        CombineOpenMapsCommand.RaiseCanExecuteChanged();
        AppendActiveMapToWorldCommand.RaiseCanExecuteChanged();
        ExitMapCombinedModeCommand.RaiseCanExecuteChanged();
        SendCombinedToWorldCommand.RaiseCanExecuteChanged();
        SendWorldToCombinedCommand.RaiseCanExecuteChanged();
        SaveSelectedCombinedMapCommand.RaiseCanExecuteChanged();
        SaveAllCombinedMapsCommand.RaiseCanExecuteChanged();
        AddCombinedMapsToPublishQueueCommand.RaiseCanExecuteChanged();
        ExportCombinedWorldCommand.RaiseCanExecuteChanged();
        MinimizeCombinedMapsCommand.RaiseCanExecuteChanged();
        RestoreCombinedMapsCommand.RaiseCanExecuteChanged();
        SelectAllCombinedMapChipsCommand.RaiseCanExecuteChanged();
        ClearCombinedMapChipsKeepFirstCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasAnyDirtyCombinedMap));
        OnPropertyChanged(nameof(IsCombinedMapsMultiSelect));
        OnPropertyChanged(nameof(MapCombinedModeLabel));
    }

    /// <summary>
    /// Start dragging GFX of the current selection. Grab cell must be inside the selection.
    /// Offsets are relative to the grabbed cell so the block sticks to the pointer.
    /// </summary>
    public bool TryBeginSelectionMove(int grabCellId)
    {
        if (_isMovingSelection) return true;
        if (_session is null || HitTester is null || CurrentMap is null || !HasSelection)
            return false;
        if (!_session.Selection.Contains(grabCellId))
            return false;

        var pieces = new List<SelectionMovePiece>();
        var (ax, ay) = GetCellCenter(grabCellId);
        foreach (var id in SelectedCellIds)
        {
            var (cx, cy) = GetCellCenter(id);
            pieces.Add(new SelectionMovePiece(
                id,
                CellSnapshot.Capture(id, CurrentMap.Cells[id]),
                cx - ax,
                cy - ay));
        }

        if (pieces.Count == 0) return false;

        _movePieces = pieces;
        _isMovingSelection = true;
        RebuildMovePreview(ax, ay);
        OnPropertyChanged(nameof(IsMovingSelection));
        OnPropertyChanged(nameof(MovePreviewItems));
        OnPropertyChanged(nameof(MovePreviewOutsideCount));
        StatusText = "Moviendo selección — suelta para colocar · rojo = fuera (se elimina)";
        return true;
    }

    public void UpdateSelectionMove(double contentX, double contentY)
    {
        if (!_isMovingSelection || _movePieces is null || HitTester is null) return;

        double ax, ay;
        if (HitTester.HitTest(contentX, contentY) is int destId)
            (ax, ay) = GetCellCenter(destId);
        else if (IsoSelection.ResolvePasteTarget(HitTester, contentX, contentY) is int nearId)
            (ax, ay) = GetCellCenter(nearId);
        else
        {
            ax = contentX;
            ay = contentY;
        }

        RebuildMovePreview(ax, ay);
        OnPropertyChanged(nameof(MovePreviewItems));
        OnPropertyChanged(nameof(MovePreviewOutsideCount));
    }

    public void CommitSelectionMove()
    {
        if (!_isMovingSelection || _movePieces is null || _session is null || CurrentMap is null || HitTester is null)
        {
            CancelSelectionMove();
            return;
        }

        var placements = new List<(int SourceId, int? DestId, CellSnapshot Snap)>(_movePieces.Count);
        for (var i = 0; i < _movePieces.Count; i++)
        {
            var piece = _movePieces[i];
            var preview = i < _movePreviewItems.Count ? _movePreviewItems[i] : default;
            placements.Add((piece.SourceCellId, preview.TargetCellId, piece.Snapshot));
        }

        var sourceSet = new HashSet<int>();
        var destGfx = new Dictionary<int, CellSnapshot>();
        var affected = new HashSet<int>();
        foreach (var p in placements)
        {
            sourceSet.Add(p.SourceId);
            affected.Add(p.SourceId);
            if (p.DestId is int d)
            {
                affected.Add(d);
                destGfx[d] = p.Snap;
            }
        }

        var outside = placements.Count(p => p.DestId is null);
        var moved = destGfx.Count;
        if (_session.Commit("Mover selección", affected, (cellId, cell) =>
            {
                if (sourceSet.Contains(cellId))
                    ClearGfxLayers(cell);

                if (destGfx.TryGetValue(cellId, out var snap))
                    ApplyGfxLayers(snap, cell);
            }))
        {
            AfterHistoryChange();
            var newSel = destGfx.Keys.OrderBy(i => i).ToList();
            _session.SetSelection(newSel);
            SyncSelectionFromSession();
            PrimarySelectedCellId = newSel.Count > 0 ? newSel[0] : null;
            _ = RerenderAsync();
            StatusText = outside > 0
                ? $"Movidas {moved} celdas · {outside} fuera del mapa eliminadas"
                : $"Movidas {moved} celdas";
        }

        ClearMoveState();
    }

    public void CancelSelectionMove()
    {
        if (!_isMovingSelection) return;
        ClearMoveState();
        StatusText = "Movimiento cancelado";
        OnPropertyChanged(nameof(SelectedCellIds));
    }

    private void ClearMoveState()
    {
        _movePieces = null;
        _movePreviewItems = Array.Empty<SelectionMovePreviewItem>();
        _isMovingSelection = false;
        OnPropertyChanged(nameof(IsMovingSelection));
        OnPropertyChanged(nameof(MovePreviewItems));
        OnPropertyChanged(nameof(MovePreviewOutsideCount));
    }

    private void RebuildMovePreview(double anchorX, double anchorY)
    {
        if (_movePieces is null || HitTester is null)
        {
            _movePreviewItems = Array.Empty<SelectionMovePreviewItem>();
            return;
        }

        var items = new SelectionMovePreviewItem[_movePieces.Count];
        for (var i = 0; i < _movePieces.Count; i++)
        {
            var piece = _movePieces[i];
            var tx = anchorX + piece.OffsetX;
            var ty = anchorY + piece.OffsetY;
            var target = IsoSelection.ResolvePasteTarget(HitTester, tx, ty);
            items[i] = new SelectionMovePreviewItem(tx, ty, target);
        }

        _movePreviewItems = items;
    }

    private (double X, double Y) GetCellCenter(int cellId)
    {
        if (HitTester is null || !HitTester.TryGetCellCornersInHitSpace(cellId, out var c))
            return (0, 0);
        return ((c.A.X + c.C.X) / 2.0, (c.B.Y + c.D.Y) / 2.0);
    }

    private static void ClearGfxLayers(CellData cell)
    {
        MapCellEditor.ClearLayer(cell, MapCellEditor.Layer.Ground);
        MapCellEditor.ClearLayer(cell, MapCellEditor.Layer.Object1);
        MapCellEditor.ClearLayer(cell, MapCellEditor.Layer.Object2);
        cell.FlipGround = false;
        cell.FlipObject1 = false;
        cell.FlipObject2 = false;
        cell.GroundRotation = 0;
        cell.Object1Rotation = 0;
    }

    private static void ApplyGfxLayers(CellSnapshot snap, CellData cell)
    {
        cell.GroundGfxId = snap.GroundGfxId;
        cell.Object1GfxId = snap.Object1GfxId;
        cell.Object2GfxId = snap.Object2GfxId;
        cell.FlipGround = snap.FlipGround;
        cell.FlipObject1 = snap.FlipObject1;
        cell.FlipObject2 = snap.FlipObject2;
        cell.GroundRotation = snap.GroundRotation;
        cell.Object1Rotation = snap.Object1Rotation;
        cell.GroundLevel = snap.GroundLevel;
        cell.GroundSlope = snap.GroundSlope;
        cell.InteractiveObject = snap.InteractiveObject;
    }

    private sealed record SelectionMovePiece(
        int SourceCellId,
        CellSnapshot Snapshot,
        double OffsetX,
        double OffsetY);

    private void ApplyBrushToSelection()
    {
        if (_session is null || !HasSelection) return;
        if ((SelectedGfxId ?? FocusGfxId) is not int gfxId) return;
        // Align brush with the GFX we are about to paint so subsequent tools stay consistent.
        SelectedGfxId = gfxId;
        if (!ValidateSelectedGfxForActiveLayer(out var error))
        {
            StatusText = error;
            return;
        }

        var layer = PaintLayer.ToEditorLayer();
        var rot = PaintLayer == PaintLayer.Object2 ? (int?)null : BrushRotation;
        var flip = BrushFlip;
        var markBlocked = PaintMarksUnwalkable;
        var count = SelectedCellIds.Count;
        if (_session.Commit("Rellenar selección", SelectedCellIds,
                (_, c) =>
                {
                    MapCellEditor.SetLayerGfx(c, layer, gfxId, flip, rot);
                    if (markBlocked)
                        MapCellEditor.SetMovement(c, MovementType.Unwalkable);
                }))
        {
            PushRecent(CategoryKey(PaintLayer.ToGfxCategory()), gfxId);
            AfterHistoryChange();
            _ = RerenderAsync();
            // MAP-PAINT.1 / MAP-AREA.1 — keep active GFX after mass fill.
            StatusText = $"Rellenadas {count} celdas · GFX {gfxId} sigue activo";
            OnPropertyChanged(nameof(FillSelectionTooltip));
        }
    }

    private void ClearSelectedLayer(PaintLayer layer)
    {
        if (_session is null || !HasSelection) return;
        var cmdName = layer switch
        {
            PaintLayer.Ground => "Vaciar Suelo (selección)",
            PaintLayer.Object1 => "Vaciar Capa 1 (selección)",
            _ => "Vaciar Capa 2 (selección)",
        };
        CommitClear(cmdName, layer);
    }

    private void ClearActiveLayer()
    {
        if (_session is null || !HasSelection) return;
        var cmdName = PaintLayer switch
        {
            PaintLayer.Ground => "Vaciar Suelo (selección)",
            PaintLayer.Object1 => "Vaciar Capa 1 (selección)",
            _ => "Vaciar Capa 2 (selección)",
        };
        CommitClear(cmdName, PaintLayer);
    }

    private void CommitClear(string cmdName, PaintLayer layer)
    {
        if (_session is null) return;
        var editorLayer = layer.ToEditorLayer();
        var count = SelectedCellIds.Count;
        if (_session.Commit(cmdName, SelectedCellIds, (_, c) => MapCellEditor.ClearLayer(c, editorLayer)))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
            StatusText = $"{cmdName}: {count} celdas";
        }
    }

    /// <summary>MAP-AREA.1 — clear selection highlight only; does not touch GFX.</summary>
    private void ClearMapSelection()
    {
        ClearSelectionUi();
        if (IsMapCombinedMode && MultiMap.IsActive)
        {
            MultiMap.ClearSelection();
            MosaicHost.RequestViewRedraw();
        }

        StatusText = "Selección: 0 celdas";
    }

    private void CommitSelection(string name, Action<int, CellData> mutate)
    {
        if (_session is null || !HasSelection) return;
        if (_session.Commit(name, SelectedCellIds, mutate))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }
    }

    private void ApplyEyedropper(int cellId)
    {
        if (CurrentMap is null) return;
        var cell = CurrentMap.Cells[cellId];
        var gfx = GetLayerGfx(cell, PaintLayer);
        if (gfx == 0)
        {
            SelectedGfxId = null;
            StatusText = "Capa vacía — pincel limpiado";
        }
        else
        {
            SelectedGfxId = gfx;
            PushRecent(CategoryKey(PaintLayer.ToGfxCategory()), gfx);
            switch (PaintLayer)
            {
                case PaintLayer.Ground:
                    BrushFlip = cell.FlipGround;
                    BrushRotation = cell.GroundRotation;
                    break;
                case PaintLayer.Object1:
                    BrushFlip = cell.FlipObject1;
                    BrushRotation = cell.Object1Rotation;
                    break;
                case PaintLayer.Object2:
                    BrushFlip = cell.FlipObject2;
                    break;
            }

            Tool = EditorTool.Paint;
            StatusText = $"Cuentagotas: {gfx}";
        }

        _session?.SetSelection(new[] { cellId });
        PrimarySelectedCellId = cellId;
        SyncSelectionFromSession();
    }

    private string PaintStrokeName => PaintLayer switch
    {
        PaintLayer.Ground => "Pintar Ground",
        PaintLayer.Object1 => "Pintar Layer 1",
        _ => "Pintar Layer 2",
    };

    private string EraseStrokeName => PaintLayer switch
    {
        PaintLayer.Ground => "Borrar Ground",
        PaintLayer.Object1 => "Borrar Layer 1",
        _ => "Borrar Layer 2",
    };

    private string CellModeStrokeName => Tool switch
    {
        EditorTool.Unwalkable => "Non marchable",
        EditorTool.LineOfSight => "Ligne de vue",
        EditorTool.FightCell1 => "Posición combate 1",
        EditorTool.FightCell2 => "Posición combate 2",
        _ => "Celda",
    };

    private string CellModeEraseStrokeName => $"{CellModeStrokeName} (quitar)";

    private async Task RerenderAsync()
    {
        if (CurrentMap is null || _library.Catalog is null)
            return;

        var gen = ++_renderGeneration;
        IsRendering = true;
        var map = CurrentMap;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => _library.Render(map, BuildRenderOptions()));
            if (gen != _renderGeneration)
            {
                result.Image.Dispose();
                return;
            }

            sw.Stop();
            var image = BitmapConversion.ToBitmapSource(result.Image);
            result.Image.Dispose();
            MapImage = image;
            RenderTimeText = $"{result.Metrics.Render.TotalMilliseconds:F0} ms";
            EditLatencyText = $"{sw.ElapsedMilliseconds} ms";
            if (!IsTransientStatus(StatusText))
                StatusText = IsDirty ? $"Mapa {map.Id} *" : $"Mapa {map.Id}";

            var warnings = new List<string>();
            foreach (var g in result.MissingGfx)
            {
                if (g.StartsWith("Background:", StringComparison.Ordinal))
                    warnings.Add($"Recurso ausente: Background GfxID {g["Background:".Length..]}");
                else
                    warnings.Add($"Recurso ausente: {g}");
            }
            ResourceWarnings = warnings;
        }
        catch (Exception ex)
        {
            StatusText = $"Error de render: {ex.Message}";
        }
        finally
        {
            if (gen == _renderGeneration)
                IsRendering = false;
        }
    }

    private void ApplyRenderResult(
        MapDocument map,
        FlasmSwfMetadataReader.SwfMapMetadata? swf,
        MapRenderResult result)
    {
        var image = BitmapConversion.ToBitmapSource(result.Image);
        result.Image.Dispose();
        var hit = new IsoHitTester(map.Width, map.Height);
        var session = new MapEditSession(map, hit)
        {
            DocumentId = Guid.NewGuid().ToString("D"),
            CreatedUtc = DateTimeOffset.UtcNow,
            Source = new RufmapSourceDto
            {
                Kind = "LegacyAstria",
                OriginalMapId = map.Id,
                LibraryPathHint = _library.RootPath,
            },
            ProjectName = null,
            FilePath = null,
        };
        session.CaptureLoadBaseline();

        var openDoc = new OpenMapDocument(map, session, hit, image, swf)
        {
            CascadeIndex = _nextCascadeIndex++
        };
        _openDocuments.Add(openDoc);
        BindActiveDocument(openDoc, clearSelection: true);
        DocumentOpened?.Invoke(openDoc);
        RaiseCombineCommands();

        RenderTimeText = $"{result.Metrics.Render.TotalMilliseconds:F0} ms";

        var warnings = new List<string>();
        foreach (var g in result.MissingGfx)
        {
            if (g.StartsWith("Background:", StringComparison.Ordinal))
                warnings.Add($"Recurso ausente: Background GfxID {g["Background:".Length..]}");
            else
                warnings.Add($"Recurso ausente: {g}");
        }
        foreach (var a in result.MissingAnchors)
            warnings.Add($"Ancla ausente: {a}");
        ResourceWarnings = warnings;
        StatusText = warnings.Count > 0 ? $"Mapa {map.Id} (avisos)" : $"Mapa {map.Id}";
        UpdateTitle();
    }

    private void TryLoadSavedLibrary()
    {
        if (_libraryLoadCompleted)
            return;
        _libraryLoadCompleted = true;
        if (TryLoadPortableSiblingLibrary())
            return;

        var path = _settings.LibraryPath;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            try
            {
                LoadLibraryCore(path, LibrarySource.UserSettings);
                return;
            }
            catch (Exception ex)
            {
                HasLibrary = false;
                LibraryStatusMessage = $"No se pudo cargar la biblioteca guardada.\n{ex.Message}";
                StatusText = "Error de biblioteca";
                return;
            }
        }

        HasLibrary = false;
        LibraryStatusMessage =
            "No se encontró la biblioteca de RUFUS Map Editor.\n" +
            "Coloque una carpeta Library junto al ejecutable o use Biblioteca... para seleccionar una.";
        StatusText = "Sin biblioteca";
    }

    private bool TryLoadPortableSiblingLibrary()
    {
        if (!PortableLibraryPaths.TryResolveSiblingLibrary(out var portablePath))
            return false;

        var validation = PortableLibraryValidator.Validate(portablePath);
        if (!validation.IsValidForEditor)
            return false;

        try
        {
            LoadLibraryCore(portablePath, LibrarySource.SiblingExecutable);
            if (!string.Equals(_settings.LibraryPath, portablePath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LibraryPath = portablePath;
                AppSettingsStore.Save(_settings);
            }
            if (validation.Warnings.Count > 0)
                LibraryStatusMessage += $"\n({validation.Warnings.Count} aviso(s) — ver estado Flasm/mapas)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SelectLibrary()
    {
        if (!ConfirmDiscardIfDirty()) return;
        var dlg = new OpenFolderDialog { Title = "Seleccionar biblioteca RUFUS Map Editor (carpeta Library)" };
        if (PortableLibraryPaths.TryResolveSiblingLibrary(out var sibling))
            dlg.InitialDirectory = sibling;
        else if (!string.IsNullOrWhiteSpace(_settings.LibraryPath) && Directory.Exists(_settings.LibraryPath))
            dlg.InitialDirectory = _settings.LibraryPath;
        if (dlg.ShowDialog() != true) return;
        try
        {
            CloseMap();
            LoadLibraryCore(dlg.FolderName, LibrarySource.ManualSelection);
            _settings.LibraryPath = dlg.FolderName;
            AppSettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cargar la biblioteca:\n{ex.Message}", "RUFUS Map Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadLibraryCore(string path, LibrarySource source = LibrarySource.ManualSelection)
    {
        StatusText = "Cargando biblioteca...";
        _thumbs.Clear();
        _overlayCache.Clear();
        _mapPreviews.Clear();
        _library.LoadLibrary(path);
        BuildCatalogIndex();
        MapIds.Clear();
        foreach (var id in _library.DiscoverMapIds())
            MapIds.Add(id);
        SyncMapListItems();

        _effectiveLibraryPath = Path.GetFullPath(path);
        _librarySource = source;
        HasLibrary = true;
        var label = source == LibrarySource.SiblingExecutable ? "Biblioteca RUFUS (portable)" : "Biblioteca RUFUS";
        LibraryStatusMessage = $"{label}\n{_effectiveLibraryPath}\n{MapIds.Count} mapas · Catálogo: {_library.Catalog?.TotalCount ?? 0} GFX.";
        OnPropertyChanged(nameof(EffectiveLibraryPath));
        OnPropertyChanged(nameof(LibrarySource));
        StatusText = $"{MapIds.Count} mapas";
        MapPublishQueue.BindLibrary(_effectiveLibraryPath);
        RebuildFolderTree();
        RefreshVisibleGfx();
        // Start with no map open. Only re-select if a document is already open (library refresh).
        SelectedMapId = _activeDocument?.MapId;
        if (!string.IsNullOrWhiteSpace(_effectiveLibraryPath))
            GeopositionsStore.EnsureRoot(_effectiveLibraryPath);
        World.NotifyLibraryRootChanged();
        SetupImagesWatcher(_effectiveLibraryPath);
    }

    private void SetupImagesWatcher(string libraryRoot)
    {
        DisposeImagesWatcher();
        try
        {
            var images = Path.Combine(libraryRoot, AstriaGfxLibraryLayout.ImagesFolderName);
            if (!Directory.Exists(images))
                return;

            _imagesReloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            _imagesReloadDebounce.Tick += (_, _) =>
            {
                _imagesReloadDebounce?.Stop();
                SoftReloadGfxCatalog("archivos Images");
            };

            _imagesWatcher = new FileSystemWatcher(images)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _imagesWatcher.Changed += OnImagesFolderChanged;
            _imagesWatcher.Created += OnImagesFolderChanged;
            _imagesWatcher.Deleted += OnImagesFolderChanged;
            _imagesWatcher.Renamed += OnImagesFolderChanged;
        }
        catch (Exception ex)
        {
            RufusLog.Error($"No se pudo vigilar Images/: {ex.Message}");
        }
    }

    private void OnImagesFolderChanged(object sender, FileSystemEventArgs e)
    {
        // Ignorar temporales/ruido de editores de imagen.
        var name = Path.GetFileName(e.Name ?? e.FullPath);
        if (string.IsNullOrEmpty(name) ||
            name.StartsWith(".", StringComparison.Ordinal) ||
            name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("~", StringComparison.Ordinal))
            return;

        var gen = Interlocked.Increment(ref _imagesReloadGeneration);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (gen != _imagesReloadGeneration) return;
            if (_imagesReloadDebounce is null) return;
            _imagesReloadDebounce.Stop();
            _imagesReloadDebounce.Start();
        });
    }

    private void SoftReloadGfxCatalog(string reason)
    {
        if (!_library.IsLoaded || string.IsNullOrWhiteSpace(_effectiveLibraryPath))
            return;

        try
        {
            StatusText = "Actualizando catálogo de imágenes…";
            _thumbs.Clear();
            _overlayCache.Clear();
            _worldThumbs.Clear();
            _library.LoadLibrary(_effectiveLibraryPath);
            BuildCatalogIndex();
            RebuildFolderTree();
            RefreshVisibleGfx(force: true);
            _ = RerenderAsync();
            World.RequestViewRedraw();
            foreach (var session in _openWorlds)
                session.Vm.RequestViewRedraw();
            if (IsMapCombinedMode)
                CombinedMaps.RequestViewRedraw();
            StatusText = $"Catálogo actualizado ({reason})";
            RufusLog.Ok($"Catálogo GFX recargado: {reason}");
        }
        catch (Exception ex)
        {
            StatusText = "Error al actualizar catálogo";
            RufusLog.Error($"SoftReloadGfxCatalog: {ex.Message}");
        }
    }

    private void DisposeImagesWatcher()
    {
        if (_imagesWatcher is not null)
        {
            _imagesWatcher.EnableRaisingEvents = false;
            _imagesWatcher.Changed -= OnImagesFolderChanged;
            _imagesWatcher.Created -= OnImagesFolderChanged;
            _imagesWatcher.Deleted -= OnImagesFolderChanged;
            _imagesWatcher.Renamed -= OnImagesFolderChanged;
            _imagesWatcher.Dispose();
            _imagesWatcher = null;
        }

        if (_imagesReloadDebounce is not null)
        {
            _imagesReloadDebounce.Stop();
            _imagesReloadDebounce = null;
        }
    }

    private void OpenMapDialog()
    {
        if (!HasLibrary || MapIds.Count == 0)
        {
            MessageBox.Show("No hay mapas descubiertos.", "RUFUS Map Editor");
            return;
        }

        var pick = new MapPickerWindow(_library, _mapPreviews, MapIds, SelectedMapId, "Abrir mapa",
            persistState: _mapListFilter) { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() == true && pick.SelectedMapId is int id)
        {
            SelectedMapId = id;
            _ = LoadMapAsync(id);
        }
    }

    private async Task NewMapAsync((int X, int Y)? placeInCombinedAt = null)
    {
        if (!HasLibrary)
        {
            MessageBox.Show("Carga primero la biblioteca (Library).", "Nuevo mapa");
            return;
        }

        int? lockW = null;
        int? lockH = null;
        string? lockReason = null;
        if (placeInCombinedAt is not null &&
            IsMapCombinedMode &&
            !IsMapCombinedMinimized &&
            CombinedMaps.GetWorkingMapSize() is { } working)
        {
            lockW = working.Width;
            lockH = working.Height;
            var label = CombinedMaps.GetWorkingMapSizeLabel() ?? $"{working.Width}×{working.Height}";
            lockReason =
                $"Estás trabajando en modo {label}. En este combinado solo puedes abrir mapas de ese tamaño. Grande y personalizado quedan bloqueados.";
        }

        var dlg = new NewMapSizeWindow(lockW, lockH, lockReason) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true)
            return;

        var tempId = _nextTempMapId--;
        try
        {
            IsLoading = true;
            StatusText = $"Abriendo plantilla {dlg.ResultWidth}×{dlg.ResultHeight}...";

            var map = BlankMapFactory.Create(tempId, dlg.ResultWidth, dlg.ResultHeight);
            var hit = new IsoHitTester(map.Width, map.Height);
            var session = new MapEditSession(map, hit)
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                Source = new RufmapSourceDto
                {
                    Kind = "NewMap",
                    OriginalMapId = null,
                    LibraryPathHint = _library.RootPath ?? _effectiveLibraryPath,
                },
                ProjectName = "mapa_nuevo",
            };

            // Desde casilla "+" del combinado: insertar en el mosaico.
            if (placeInCombinedAt is { } slot &&
                IsMapCombinedMode &&
                !IsMapCombinedMinimized &&
                CombinedMaps.World is not null)
            {
                if (!TryAcceptCombinedMapSize(map.Width, map.Height))
                    return;

                var key = CombinedMaps.PlaceNewMapAt(map, slot.X, slot.Y);
                if (key is null)
                {
                    StatusText = "No se pudo colocar el mapa nuevo en el combinado";
                    return;
                }

                EnsureMosaicBoundDocument(map, key);
                RevealCombinedAndFocusMapKey(key);
                OnPropertyChanged(nameof(MapCombinedModeLabel));
                StatusText =
                    $"Plantilla {map.Width}×{map.Height} en combinado ({slot.X},{slot.Y}) — al guardar se pedirá el Map ID";
                RufusLog.Info($"Plantilla nueva en combinado {map.Width}×{map.Height}");
                return;
            }

            // Archivo → Nuevo mapa con combinado abierto: minimizar y editar en ventana.
            if (IsMapCombinedMode && !IsMapCombinedMinimized)
                SetMapCombinedMinimized(true);

            await PresentLoadedDocumentAsync(map, session, fromAutosave: false);
            StatusText = $"Plantilla {map.Width}×{map.Height} — edita libremente; al guardar se pedirá el Map ID";
            RufusLog.Info($"Plantilla nueva {map.Width}×{map.Height} (sin Map ID aún)");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo crear el mapa:\n{ex.Message}", "Nuevo mapa",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Error al crear mapa";
            RufusLog.Error($"Error nuevo mapa: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Unsaved templates use temporary negative IDs. Before official Library save, ask for a real Map ID.
    /// </summary>
    private bool TryAssignMapIdBeforeOfficialSave()
    {
        if (_session is null || CurrentMap is null)
            return false;
        if (CurrentMap.Id > 0)
            return true;

        var reserved = new HashSet<int>(MapIds);
        foreach (var doc in _openDocuments)
        {
            if (doc.MapId > 0)
                reserved.Add(doc.MapId);
        }

        var maxId = reserved.Count > 0 ? reserved.Max() : 30000;
        var proposed = new LocalMapIdAllocator().ProposeNextId(maxId, reserved);
        var idDlg = new SaveMapIdWindow(proposed, CurrentMap.Width, CurrentMap.Height)
        {
            Owner = Application.Current.MainWindow,
        };
        if (idDlg.ShowDialog() != true)
            return false;

        var mapId = idDlg.ResultMapId;
        if (!new LocalMapIdAllocator().IsAvailable(mapId, reserved))
        {
            MessageBox.Show(
                $"El Map ID {mapId} ya existe. Elige otro.",
                "Guardar mapa",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        CurrentMap.Id = mapId;
        if (_session.Source is not null)
            _session.Source.OriginalMapId = mapId;
        _session.ProjectName = $"map_{mapId}";
        SelectedMapId = mapId;
        _activeDocument?.NotifyDirtyChanged();
        OnPropertyChanged(nameof(InfoMapId));
        return true;
    }

    private void RebuildFolderTree()
    {
        FolderTree.Clear();
        if (_library.Catalog is null) return;

        FolderTree.Add(new FolderNodeVm { Name = FavoritesFolder, IsUnifiedFavorites = true });
        FolderTree.Add(BuildCategoryRoot(GfxCategory.Ground));
        FolderTree.Add(BuildCategoryRoot(GfxCategory.Object));

        SyncSelectedCategoryFromPaintLayer();
        SelectedFolder ??= GetDefaultFolderName(SelectedCategory);
    }

    private FolderNodeVm BuildCategoryRoot(GfxCategory category)
    {
        var folders = _library.Catalog!.Enumerate(category)
            .Select(r => string.IsNullOrEmpty(r.Folder) ? "(raíz)" : r.Folder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var root = new FolderNodeVm { Name = UiDisplayLabels.CategoryRoot(category) };
        root.Children.Add(new FolderNodeVm { Name = RecentsFolder, Category = category });
        foreach (var folder in folders)
            root.Children.Add(new FolderNodeVm { Name = folder, Category = category });
        return root;
    }

    private string GetDefaultFolderName(GfxCategory category)
    {
        var rootName = UiDisplayLabels.CategoryRoot(category);
        var root = FolderTree.FirstOrDefault(node =>
            string.Equals(node.Name, rootName, StringComparison.OrdinalIgnoreCase));
        return root?.Children.Skip(1).FirstOrDefault()?.Name ?? RecentsFolder;
    }

    private void RefreshVisibleGfx(bool force = false)
    {
        if (_library.Catalog is null) return;

        var filterKey = BuildVisibleGfxFilterKey();
        if (!force && filterKey == _lastVisibleGfxFilterKey && VisibleGfxRows.Count > 0)
            return;

        _lastVisibleGfxFilterKey = filterKey;
        _catalogRefreshCount++;
        OnPropertyChanged(nameof(CatalogRefreshCount));

        VisibleGfxRows.Clear();
        var ordered = QueryVisibleResources().ToList();

        ObservableCollection<GfxItemVm>? rowItems = null;
        var col = 0;
        foreach (var res in ordered)
        {
            if (rowItems is null || col >= GfxColumns)
            {
                if (rowItems is not null)
                    VisibleGfxRows.Add(new GfxRowVm { Items = rowItems });
                rowItems = new ObservableCollection<GfxItemVm>();
                col = 0;
            }

            rowItems.Add(new GfxItemVm
            {
                Id = res.Id,
                Resource = res,
                Thumbnail = null,
                IsFavorite = IsFavorite(CategoryKey(res.Category), res.Id),
                IsSelected = SelectedGfxId == res.Id,
            });
            col++;
        }

        if (rowItems is not null && rowItems.Count > 0)
            VisibleGfxRows.Add(new GfxRowVm { Items = rowItems });
    }

    private void UpdateGfxCatalogSelectionHighlight()
    {
        foreach (var row in VisibleGfxRows)
        {
            foreach (var item in row.Items)
                item.IsSelected = SelectedGfxId == item.Id;
        }
    }

    private string BuildVisibleGfxFilterKey() =>
        $"{_showUnifiedFavorites}|{SelectedCategory}|{SelectedFolder}|{GfxSearch.Trim()}|cols:{GfxColumns}";

    private IEnumerable<GfxResource> QueryVisibleResources()
    {
        if (_library.Catalog is null)
            yield break;

        if (_showUnifiedFavorites)
        {
            foreach (var res in QueryUnifiedFavorites())
                yield return res;
            yield break;
        }

        var category = SelectedCategory;
        var catKey = CategoryKey(category);

        if (string.Equals(SelectedFolder, RecentsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var byId = GetResourceByIdIndex(category);
            foreach (var recent in _settings.Recents.Where(r =>
                         string.Equals(r.Category, catKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (byId.TryGetValue(recent.GfxId, out var res))
                    yield return res;
            }

            yield break;
        }

        IEnumerable<GfxResource> source = GetCategoryResources(category);
        if (!string.IsNullOrWhiteSpace(SelectedFolder))
        {
            var folderKey = SelectedFolder == "(raíz)" ? "" : SelectedFolder!;
            if (_folderResourceIndex is not null &&
                _folderResourceIndex.TryGetValue((category, folderKey), out var folderList))
            {
                source = folderList;
            }
            else
            {
                source = source.Where(r => string.Equals(r.Folder, folderKey, StringComparison.OrdinalIgnoreCase)
                    || (SelectedFolder == "(raíz)" && string.IsNullOrEmpty(r.Folder)));
            }
        }

        if (!string.IsNullOrWhiteSpace(GfxSearch))
        {
            var q = GfxSearch.Trim();
            source = source.Where(r => r.Id.ToString().Contains(q, StringComparison.Ordinal));
        }

        foreach (var res in source.OrderBy(r => r.Id))
            yield return res;
    }

    private IEnumerable<GfxResource> QueryUnifiedFavorites()
    {
        IEnumerable<GfxResource> source = _settings.Favorites
            .Select(f =>
            {
                var category = string.Equals(f.Category, "Ground", StringComparison.OrdinalIgnoreCase)
                    ? GfxCategory.Ground
                    : GfxCategory.Object;
                return GetResourceByIdIndex(category).TryGetValue(f.GfxId, out var res) ? res : null;
            })
            .Where(res => res is not null)
            .Cast<GfxResource>();

        if (!string.IsNullOrWhiteSpace(GfxSearch))
        {
            var q = GfxSearch.Trim();
            source = source.Where(r => r.Id.ToString().Contains(q, StringComparison.Ordinal));
        }

        return source.OrderBy(r => r.Category).ThenBy(r => r.Id);
    }

    private void BuildCatalogIndex()
    {
        _folderResourceIndex = new Dictionary<(GfxCategory, string), IReadOnlyList<GfxResource>>();
        _resourceByIdIndex = new Dictionary<(GfxCategory, int), GfxResource>();
        if (_library.Catalog is null) return;

        foreach (var category in new[] { GfxCategory.Ground, GfxCategory.Object })
        {
            var byFolder = new Dictionary<string, List<GfxResource>>(StringComparer.OrdinalIgnoreCase);
            foreach (var res in _library.Catalog.Enumerate(category))
            {
                _resourceByIdIndex[(category, res.Id)] = res;
                var folderKey = string.IsNullOrEmpty(res.Folder) ? "" : res.Folder;
                if (!byFolder.TryGetValue(folderKey, out var list))
                {
                    list = new List<GfxResource>();
                    byFolder[folderKey] = list;
                }

                list.Add(res);
            }

            foreach (var kv in byFolder)
            {
                kv.Value.Sort((a, b) => a.Id.CompareTo(b.Id));
                _folderResourceIndex[(category, kv.Key)] = kv.Value;
            }
        }
    }

    private IReadOnlyList<GfxResource> GetCategoryResources(GfxCategory category)
    {
        if (_folderResourceIndex is null)
            return _library.Catalog!.Enumerate(category).OrderBy(r => r.Id).ToList();

        return _folderResourceIndex
            .Where(kv => kv.Key.Category == category)
            .SelectMany(kv => kv.Value)
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Id)
            .ToList();
    }

    private Dictionary<int, GfxResource> GetResourceByIdIndex(GfxCategory category)
    {
        if (_resourceByIdIndex is null)
            return _library.Catalog!.Enumerate(category).ToDictionary(r => r.Id);

        return _resourceByIdIndex
            .Where(kv => kv.Key.Category == category)
            .ToDictionary(kv => kv.Key.Id, kv => kv.Value);
    }

    public void SelectInspectorLayer(InspectorLayerHighlight layer)
    {
        if (!HasSingleCellSelection || PrimarySelectedCellId is not int cellId || CurrentMap is null)
            return;

        var paintLayer = layer switch
        {
            InspectorLayerHighlight.Ground => PaintLayer.Ground,
            InspectorLayerHighlight.Object1 => PaintLayer.Object1,
            _ => PaintLayer.Object2,
        };

        var gfxId = GetLayerGfx(CurrentMap.Cells[cellId], paintLayer);
        if (gfxId <= 0)
            return;

        ApplyInspectedGfxToBrush(cellId, gfxId, paintLayer, announce: true);
    }

    private bool CanLocateLayer(PaintLayer layer)
    {
        if (!HasSingleCellSelection || PrimarySelectedCellId is not int cellId || CurrentMap is null)
            return false;
        return GetLayerGfx(CurrentMap.Cells[cellId], layer) > 0;
    }

    public void LocateLayerInCatalog(PaintLayer layer)
    {
        if (!CanLocateLayer(layer) || PrimarySelectedCellId is not int cellId || CurrentMap is null)
            return;

        var gfxId = GetLayerGfx(CurrentMap.Cells[cellId], layer);
        var category = layer.ToGfxCategory();
        if (_library.Catalog is null || !_library.Catalog.TryGet(category, gfxId, out var res) || res is null)
            return;

        PaintLayer = layer;
        SelectedCategory = category;
        var folderName = string.IsNullOrEmpty(res.Folder) ? "(raíz)" : res.Folder;
        SelectedFolder = folderName;
        GfxSearch = gfxId.ToString();
        SelectedGfxId = gfxId;
        RefreshVisibleGfx(force: true);
        ScrollCatalogToGfxId?.Invoke(gfxId);
        StatusText = $"Catálogo: {category} {gfxId}";
    }

    private void RaiseLocateCommands()
    {
        LocateGroundInCatalogCommand.RaiseCanExecuteChanged();
        LocateObject1InCatalogCommand.RaiseCanExecuteChanged();
        LocateObject2InCatalogCommand.RaiseCanExecuteChanged();
        SelectInspectorGroundCommand.RaiseCanExecuteChanged();
        SelectInspectorObject1Command.RaiseCanExecuteChanged();
        SelectInspectorObject2Command.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSingleCellSelection));
    }

    public bool TryGetCellLayerVisual(int cellId, PaintLayer layer, out LayerOverlayVisual visual)
    {
        visual = default;
        if (CurrentMap is null || HitTester is null || _library.Catalog is null)
            return false;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count)
            return false;

        var cell = CurrentMap.Cells[cellId];
        var gfxId = GetLayerGfx(cell, layer);
        if (gfxId <= 0)
            return false;

        var category = layer.ToGfxCategory();
        if (!_library.Catalog.TryGet(category, gfxId, out var resource) || resource is null)
            return false;

        var flip = layer switch
        {
            PaintLayer.Ground => cell.FlipGround,
            PaintLayer.Object1 => cell.FlipObject1,
            _ => cell.FlipObject2,
        };
        var rotation = layer switch
        {
            PaintLayer.Ground => cell.GroundRotation,
            PaintLayer.Object1 => cell.Object1Rotation,
            _ => 0,
        };
        var isObject = layer != PaintLayer.Ground;

        if (!_overlayCache.TryBuildPlacementDescriptor(
                HitTester, cellId, resource, flip, rotation, isObject, out var descriptor))
            return false;

        var image = _overlayCache.GetTransformedImage(resource, flip, rotation, isObject);
        if (image is null)
            return false;

        visual = new LayerOverlayVisual(image, descriptor.HitSpace);
        return true;
    }

    public bool TryGetBrushPreviewVisual(int cellId, out LayerOverlayVisual visual)
    {
        visual = default;
        if (!TryBuildBrushPlacement(cellId, out var descriptor, out var resource))
            return false;

        var rotation = descriptor.Rotation;
        var image = _overlayCache.GetTransformedImage(
            resource, descriptor.Flip, rotation, descriptor.IsObject);
        if (image is null)
            return false;

        visual = new LayerOverlayVisual(image, descriptor.HitSpace);
        return true;
    }

    /// <summary>Brush preview placement descriptor (same pipeline as final).</summary>
    public bool TryBuildBrushPlacement(int cellId, out GfxPlacementDescriptor descriptor) =>
        TryBuildBrushPlacement(cellId, out descriptor, out _);

    private bool TryBuildBrushPlacement(
        int cellId,
        out GfxPlacementDescriptor descriptor,
        out GfxResource resource)
    {
        descriptor = null!;
        resource = null!;
        if (SelectedGfxId is not int gfxId || HitTester is null || _library.Catalog is null)
            return false;

        var category = PaintLayer.ToGfxCategory();
        if (!_library.Catalog.TryGet(category, gfxId, out var found) || found is null)
            return false;
        resource = found;

        var rotation = PaintLayer == PaintLayer.Object2 ? 0 : BrushRotation;
        var isObject = PaintLayer != PaintLayer.Ground;
        return _overlayCache.TryBuildPlacementDescriptor(
            HitTester, cellId, resource, BrushFlip, rotation, isObject, out descriptor);
    }

    /// <summary>Committed cell-layer placement from MapDocument (post-paint).</summary>
    public bool TryBuildCommittedPlacement(
        int cellId,
        PaintLayer layer,
        out GfxPlacementDescriptor descriptor)
    {
        descriptor = null!;
        if (CurrentMap is null || HitTester is null || _library.Catalog is null)
            return false;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count)
            return false;

        var cell = CurrentMap.Cells[cellId];
        var gfxId = GetLayerGfx(cell, layer);
        if (gfxId <= 0)
            return false;

        var category = layer.ToGfxCategory();
        if (!_library.Catalog.TryGet(category, gfxId, out var resource) || resource is null)
            return false;

        var flip = layer switch
        {
            PaintLayer.Ground => cell.FlipGround,
            PaintLayer.Object1 => cell.FlipObject1,
            _ => cell.FlipObject2,
        };
        var rotation = layer switch
        {
            PaintLayer.Ground => cell.GroundRotation,
            PaintLayer.Object1 => cell.Object1Rotation,
            _ => 0,
        };
        var isObject = layer != PaintLayer.Ground;
        return _overlayCache.TryBuildPlacementDescriptor(
            HitTester, cellId, resource, flip, rotation, isObject, out descriptor);
    }

    public bool TryGetMultiMapBrushPreviewVisual(string documentKey, int cellId, out LayerOverlayVisual visual)
    {
        visual = default;
        if (SelectedGfxId is not int gfxId || _library.Catalog is null)
            return false;

        var tester = MultiMap.GetHitTester(documentKey);
        if (tester is null)
            return false;

        var category = PaintLayer.ToGfxCategory();
        if (!_library.Catalog.TryGet(category, gfxId, out var resource) || resource is null)
            return false;

        var rotation = PaintLayer == PaintLayer.Object2 ? 0 : BrushRotation;
        var isObject = PaintLayer != PaintLayer.Ground;
        if (!_overlayCache.TryBuildPlacementDescriptor(
                tester, cellId, resource, BrushFlip, rotation, isObject, out var descriptor))
            return false;

        var image = _overlayCache.GetTransformedImage(resource, BrushFlip, rotation, isObject);
        if (image is null)
            return false;

        visual = new LayerOverlayVisual(image, descriptor.HitSpace);
        return true;
    }

    public readonly record struct LayerOverlayVisual(ImageSource Image, GfxPlacementMath.PlacementRect Bounds);

    public void EnsureThumbnail(GfxItemVm item)
    {
        if (item.Thumbnail is not null) return;
        item.Thumbnail = _thumbs.GetThumbnail(item.Resource, 56);
        item.OnThumbnailChanged();
    }

    public ImageSource? GetCatalogHoverPreview(GfxItemVm item) =>
        _thumbs.GetThumbnail(item.Resource, 128);

    public MapPreviewInfo? TryGetMapHoverPreview(int mapId) =>
        _mapPreviews.TryGetCached(mapId);

    public Task<MapPreviewInfo?> GetMapHoverPreviewAsync(int mapId) =>
        HasLibrary ? _mapPreviews.GetOrRenderAsync(_library, mapId) : Task.FromResult<MapPreviewInfo?>(null);

    public async Task EnsureMapThumbnailAsync(MapPickerItemVm item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.HasThumbnail)
        {
            item.IsLoading = false;
            return;
        }

        var cached = _mapPreviews.TryGetCached(item.MapId);
        if (cached is not null)
        {
            item.Thumbnail = cached.Image;
            item.IsLoading = false;
            return;
        }

        if (!HasLibrary)
        {
            item.IsLoading = false;
            return;
        }

        await _mapThumbGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (item.HasThumbnail)
                return;

            var preview = await _mapPreviews.GetOrRenderAsync(_library, item.MapId).ConfigureAwait(true);
            item.Thumbnail = preview?.Image;
        }
        catch
        {
            // Preview opcional; el ID sigue siendo seleccionable.
        }
        finally
        {
            item.IsLoading = false;
            _mapThumbGate.Release();
        }
    }

    private void SyncMapListItems()
    {
        ApplyMapListFilter();
    }

    private void ApplyMapListFilter()
    {
        var selected = SelectedMapId;
        MapListItems.Clear();
        foreach (var id in _mapListFilter.FilterIds(MapIds))
            MapListItems.Add(new MapPickerItemVm(id));

        if (selected is int sid && MapListItems.Any(x => x.MapId == sid))
            SelectedMapId = sid;
    }

    private void SaveMapListFilterSettings()
    {
        _settings.MapListFilter ??= new MapListFilterSettings();
        _mapListFilter.SaveTo(_settings.MapListFilter);
        AppSettingsStore.Save(_settings);
    }

    private void OnMapListFilterChangedFromPicker()
    {
        SaveMapListFilterSettings();
        OnPropertyChanged(nameof(MapListSearchText));
        OnPropertyChanged(nameof(MapListRangeFromText));
        OnPropertyChanged(nameof(MapListRangeToText));
        OnPropertyChanged(nameof(MapListAscending));
        ApplyMapListFilter();
    }

    private void ClearMapListFilter()
    {
        _mapListFilter.SearchText = "";
        _mapListFilter.RangeFromText = "";
        _mapListFilter.RangeToText = "";
        _mapListFilter.Ascending = true;
        OnPropertyChanged(nameof(MapListSearchText));
        OnPropertyChanged(nameof(MapListRangeFromText));
        OnPropertyChanged(nameof(MapListRangeToText));
        OnPropertyChanged(nameof(MapListAscending));
        ApplyMapListFilter();
        SaveMapListFilterSettings();
    }

    private void RefreshMapListThumbnail(int mapId)
    {
        var item = MapListItems.FirstOrDefault(x => x.MapId == mapId);
        if (item is null)
            return;

        item.Thumbnail = null;
        item.IsLoading = true;
        _ = EnsureMapThumbnailAsync(item);
    }

    public string FormatCatalogHoverDetails(GfxItemVm item)
    {
        var dims = GfxResourceResolver.GetNativeDimensions(item.Resource);
        return dims is { Width: > 0, Height: > 0 }
            ? $"{dims.Value.Width} × {dims.Value.Height} px"
            : "";
    }

    public void ToggleFavorite(GfxItemVm item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var catKey = CategoryKey(item.Resource.Category);
        var existing = _settings.Favorites.FindIndex(f =>
            f.GfxId == item.Id && string.Equals(f.Category, catKey, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _settings.Favorites.RemoveAt(existing);
        else
            _settings.Favorites.Add(new GfxFavoriteKey { Category = catKey, GfxId = item.Id });

        item.IsFavorite = existing < 0;
        AppSettingsStore.Save(_settings);

        if (SelectedGfxId == item.Id)
            OnPropertyChanged(nameof(IsSelectedGfxFavorite));

        if (_showUnifiedFavorites)
            RefreshVisibleGfx();

        StatusText = existing >= 0 ? $"Favorito quitado: {item.Id}" : $"Favorito añadido: {item.Id}";
    }

    private GfxItemVm? FindVisibleGfxItem(int id)
    {
        foreach (var row in VisibleGfxRows)
        {
            foreach (var item in row.Items)
            {
                if (item.Id == id)
                    return item;
            }
        }

        return null;
    }

    private void PushRecent(string category, int gfxId)
    {
        _settings.Recents.RemoveAll(r =>
            r.GfxId == gfxId && string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
        _settings.Recents.Insert(0, new GfxRecentEntry { Category = category, GfxId = gfxId });
        while (_settings.Recents.Count > MaxRecents)
            _settings.Recents.RemoveAt(_settings.Recents.Count - 1);
        AppSettingsStore.Save(_settings);
        // MAP-PAINT.1 — do not RefreshVisibleGfx after every paint/selection.
        // Rebuilding the catalog mid-stroke caused scroll jumps and selection bugs.
        // Recents list refreshes when the user opens/changes the Recientes folder.
    }

    private bool IsFavorite(string category, int gfxId) =>
        _settings.Favorites.Any(f =>
            f.GfxId == gfxId && string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));

    private static string CategoryKey(GfxCategory category) =>
        category == GfxCategory.Ground ? "Ground" : "Object";

    private static int GetLayerGfx(CellData cell, PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => cell.GroundGfxId,
        PaintLayer.Object1 => cell.Object1GfxId,
        _ => cell.Object2GfxId,
    };

    private void RefreshLayerLabels()
    {
        ActiveLayerLabel = UiDisplayLabels.ActiveLayerStatus(PaintLayer);
        OnPropertyChanged(nameof(ActiveToolLabel));
        CatalogHeaderTitle = UiDisplayLabels.CatalogHeader(PaintLayer);
        CatalogHeaderDestination = UiDisplayLabels.CatalogDestination(PaintLayer);
        BrushTypeLabel = UiDisplayLabels.ResourceType(PaintLayer);
        BrushTargetLabel = UiDisplayLabels.LayerTarget(PaintLayer);
        BrushFlipLabel = BrushFlip ? "Sí" : "No";
        BrushRotationLabel = PaintLayer == PaintLayer.Object2 ? "—" : BrushRotation.ToString();
        SelectedGfxLabel = SelectedGfxId is int id ? $"GfxID: {id}" : "GfxID: —";
        BrushGfxIdLabel = SelectedGfxId?.ToString() ?? "—";

        if (SelectedGfxId is int gfx && _library.Catalog is not null
            && GfxResourceResolver.TryResolve(_library.Catalog, PaintLayer.ToGfxCategory(), gfx, out var res))
        {
            var dims = GfxResourceResolver.GetNativeDimensions(res);
            BrushDimensionsLabel = dims is { Width: > 0, Height: > 0 }
                ? $"{dims.Value.Width} × {dims.Value.Height} px"
                : "";
        }
        else
        {
            BrushDimensionsLabel = "";
        }
    }

    private void UpdateBrushPreview()
    {
        if (SelectedGfxId is not int id || _library.Catalog is null)
        {
            BrushPreview = null;
            return;
        }

        if (_library.Catalog.TryGet(PaintLayer.ToGfxCategory(), id, out var res) && res is not null)
            BrushPreview = _thumbs.GetThumbnail(res, 96);
        else
            BrushPreview = null;
    }

    private void UpdateTitle()
    {
        if (CurrentMap is null)
        {
            WindowTitle = "RUFUS Map Editor";
            OnPropertyChanged(nameof(MapWindowTitle));
            return;
        }

        var project = _session?.ProjectName
                      ?? (_session?.FilePath is string fp ? Path.GetFileName(fp) : null);
        var dirty = IsDirty ? " *" : "";
        WindowTitle = project is null
            ? $"RUFUS Map Editor — Map {CurrentMap.Id}{dirty}"
            : $"RUFUS Map Editor — {project} (Map {CurrentMap.Id}){dirty}";
        OnPropertyChanged(nameof(MapWindowTitle));
    }

    private void AfterHistoryChange()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UpdateTitle();
        _activeDocument?.NotifyDirtyChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        RefreshCellInspector();
        RefreshMapInspector();
        if (IsMapCombinedMode)
        {
            RaiseCombineCommands();
            RefreshCombinedMapVisuals();
        }

        if (_session?.IsDirty == true)
            NotifyWorldMapEditedIfOpen();
    }

    private void CopyCellMapDataCode()
    {
        if (!CanCopyCellMapData) return;
        Clipboard.SetText(CellMapDataCodeActual);
        StatusText = "Código MapData de celda copiado (10 chars)";
    }

    private void CopyFullMapData()
    {
        if (CurrentMap is null) return;
        MapCellEditor.SyncMapDataString(CurrentMap);
        Clipboard.SetText(CurrentMap.MapData);
        StatusText = $"MapData completo copiado ({CurrentMap.MapData.Length} chars)";
    }

    private void RefreshMapDataInspector(int? cellId)
    {
        if (CurrentMap is null || cellId is not int id || id < 0 || id >= CurrentMap.Cells.Count)
        {
            ShowSingleCellMapData = false;
            CellMapDataCodeActual = CellMapDataCodeSource = CellMapDataCodeSaved = "—";
            CellMapDataDiffHint = "";
            CellMapDataRange = "—";
        }
        else if (SelectedCellIds.Count != 1)
        {
            ShowSingleCellMapData = false;
            CellMapDataCodeActual = $"{SelectedCellIds.Count} celdas seleccionadas";
            CellMapDataCodeSource = CellMapDataCodeSaved = "—";
            CellMapDataDiffHint = "";
            CellMapDataRange = "—";
        }
        else
        {
            ShowSingleCellMapData = true;
            var cell = CurrentMap.Cells[id];
            var actual = MapDataCodec.EncodeCell(cell);
            CellMapDataCodeActual = actual;

            var source = _session?.GetLoadBaselineCode(id);
            CellMapDataCodeSource = source ?? "—";

            var saved = _session?.GetSavedBaselineCode(id);
            CellMapDataCodeSaved = saved ?? "—";

            var baseline = source ?? saved ?? actual;
            CellMapDataDiffHint = MapDataCodec.FormatChangedPositionsHint(baseline, actual);

            var (start, end) = MapDataCodec.GetCellBlockCharRange(id);
            CellMapDataRange = $"{start}–{end}";
        }

        InfoMapDataLength = CurrentMap?.MapData is { Length: > 0 } md
            ? md.Length.ToString()
            : CurrentMap is not null
                ? MapDataCodec.EncodeMap(CurrentMap.Cells as IReadOnlyList<CellData> ?? CurrentMap.Cells.ToList()).Length.ToString()
                : "—";

        CopyCellMapDataCodeCommand.RaiseCanExecuteChanged();
        CopyFullMapDataCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(InfoMapDataLength));
        OnPropertyChanged(nameof(CellMapDataCodeActual));
        OnPropertyChanged(nameof(CellMapDataCodeSource));
        OnPropertyChanged(nameof(CellMapDataCodeSaved));
        OnPropertyChanged(nameof(CellMapDataDiffHint));
        OnPropertyChanged(nameof(CellMapDataRange));
        OnPropertyChanged(nameof(ShowSingleCellMapData));
        OnPropertyChanged(nameof(CanCopyCellMapData));
    }

    private void RaiseSelectionCommands()
    {
        ClearGroundCommand.RaiseCanExecuteChanged();
        ClearObject1Command.RaiseCanExecuteChanged();
        ClearObject2Command.RaiseCanExecuteChanged();
        ClearActiveLayerCommand.RaiseCanExecuteChanged();
        ApplyBrushToSelectionCommand.RaiseCanExecuteChanged();
        ClearMapSelectionCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
        RaiseMassGfxCommands();
        OnPropertyChanged(nameof(FillSelectionTooltip));
        OnPropertyChanged(nameof(ClearActiveLayerInSelectionTooltip));
        OnPropertyChanged(nameof(SelectionSummaryLabel));
        OnPropertyChanged(nameof(SelectionMoveHint));
    }

    private void SyncSelectionFromSession()
    {
        if (_session is null)
        {
            SelectedCellIds = Array.Empty<int>();
            return;
        }

        SelectedCellIds = _session.Selection.OrderBy(i => i).ToList();
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionSummaryLabel));
        OnPropertyChanged(nameof(HasSingleCellSelection));
        OnPropertyChanged(nameof(FillSelectionTooltip));
        OnPropertyChanged(nameof(ClearActiveLayerInSelectionTooltip));
        if (PrimarySelectedCellId is int p && !_session.Selection.Contains(p))
            PrimarySelectedCellId = SelectedCellIds.Count > 0 ? SelectedCellIds[^1] : null;
        else if (PrimarySelectedCellId is null && SelectedCellIds.Count > 0)
            PrimarySelectedCellId = SelectedCellIds[^1];

        if (SelectedCellIds.Count != 1)
            HighlightedInspectorLayer = InspectorLayerHighlight.None;
        else if (HighlightedInspectorLayer != InspectorLayerHighlight.None)
        {
            // Drop Capas highlight when the primary cell changed (bounds stay only while inspecting that click).
            HighlightedInspectorLayer = InspectorLayerHighlight.None;
            OnPropertyChanged(nameof(IsInspectorGroundHighlighted));
            OnPropertyChanged(nameof(IsInspectorObject1Highlighted));
            OnPropertyChanged(nameof(IsInspectorObject2Highlighted));
        }

        RefreshCellInspector();
        RaiseLocateCommands();
        MapMonsters.NotifyMapOrSelectionChanged();
        RaiseFocusGfxUi();
    }

    public int SelectionCount => SelectedCellIds.Count;

    private void ClearSelectionUi()
    {
        _session?.ClearSelection();
        PrimarySelectedCellId = null;
        SelectedCellIds = Array.Empty<int>();
        HighlightedInspectorLayer = InspectorLayerHighlight.None;
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionSummaryLabel));
        OnPropertyChanged(nameof(HasSingleCellSelection));
        OnPropertyChanged(nameof(FillSelectionTooltip));
        OnPropertyChanged(nameof(ClearActiveLayerInSelectionTooltip));
        RefreshCellInspector();
        RaiseLocateCommands();
        ClearMapSelectionCommand.RaiseCanExecuteChanged();
        RaiseSelectionCommands();
    }

    private void RefreshMapInspector()
    {
        if (CurrentMap is null)
        {
            InfoMapId = InfoWidth = InfoHeight = InfoCellCount = InfoBackground = "—";
            InfoMusic = InfoAmbiance = InfoCapabilities = InfoOutdoor = "—";
            _suppressProp = true;
            EditRevision = "";
            EditWorldX = ""; EditWorldY = "";
            EditBackground = "";
            _suppressProp = false;
        }
        else
        {
            var m = CurrentMap;
            InfoMapId = m.Id.ToString();
            InfoWidth = m.Width.ToString();
            InfoHeight = m.Height.ToString();
            InfoCellCount = m.Cells.Count.ToString();
            InfoBackground = m.BackgroundId.ToString();
            if (CurrentMap is not null)
            {
                InfoMusic = CurrentMap.MusicId.ToString();
                InfoAmbiance = CurrentMap.AmbianceId.ToString();
                InfoCapabilities = CurrentMap.Capabilities.ToString();
                InfoOutdoor = CurrentMap.Outdoor is null ? "—" : (CurrentMap.Outdoor.Value ? "Sí" : "No");
            }
            else
                InfoMusic = InfoAmbiance = InfoCapabilities = InfoOutdoor = "—";

            _suppressProp = true;
            EditRevision = m.DateMap ?? "";
            EditBackground = m.BackgroundId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            EditWorldX = m.WorldCoordinatesSet ? m.WorldX.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            EditWorldY = m.WorldCoordinatesSet ? m.WorldY.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            _suppressProp = false;
        }

        if (CurrentMap is not null)
            MapCellEditor.SyncMapDataString(CurrentMap);
        InfoMapDataLength = CurrentMap?.MapData is { Length: > 0 } md
            ? md.Length.ToString()
            : "—";

        OnPropertyChanged(nameof(InfoMapId));
        OnPropertyChanged(nameof(InfoWidth));
        OnPropertyChanged(nameof(InfoHeight));
        OnPropertyChanged(nameof(InfoCellCount));
        OnPropertyChanged(nameof(InfoBackground));
        OnPropertyChanged(nameof(InfoMusic));
        OnPropertyChanged(nameof(InfoAmbiance));
        OnPropertyChanged(nameof(InfoCapabilities));
        OnPropertyChanged(nameof(InfoOutdoor));
        OnPropertyChanged(nameof(InfoMapDataLength));
    }

    private void RefreshCellInspector()
    {
        _suppressProp = true;
        try
        {
            if (CurrentMap is null || SelectedCellIds.Count == 0)
            {
                CellInfoId = CellInfoGround = CellInfoObject1 = CellInfoObject2 = "—";
                CellInfoGroundDetail = CellInfoObject1Detail = CellInfoObject2Detail = "";
                PreviewGround = PreviewObject1 = PreviewObject2 = null;
                RefreshMapDataInspector(null);
            }
            else
            {
                var cells = SelectedCellIds.Select(id => CurrentMap.Cells[id]).ToList();
                CellInfoId = SelectedCellIds.Count == 1
                    ? SelectedCellIds[0].ToString()
                    : $"{SelectedCellIds.Count} celdas";

                CellInfoGround = FmtMixed(cells.Select(c => c.GroundGfxId), id => FmtLayerGfx(GfxCategory.Ground, id));
                CellInfoObject1 = FmtMixed(cells.Select(c => c.Object1GfxId), id => FmtLayerGfx(GfxCategory.Object, id));
                CellInfoObject2 = FmtMixed(cells.Select(c => c.Object2GfxId), id => FmtLayerGfx(GfxCategory.Object, id));

                if (SelectedCellIds.Count == 1)
                {
                    var single = CurrentMap.Cells[SelectedCellIds[0]];
                    CellInfoGroundDetail = FmtLayerDetail(GfxCategory.Ground, single.GroundGfxId);
                    CellInfoObject1Detail = FmtLayerDetail(GfxCategory.Object, single.Object1GfxId);
                    CellInfoObject2Detail = FmtLayerDetail(GfxCategory.Object, single.Object2GfxId);
                }
                else
                {
                    CellInfoGroundDetail = CellInfoObject1Detail = CellInfoObject2Detail = "";
                }

                EditMovement = SameOrNull(cells.Select(c => c.Movement));
                SyncEditMovementItemFromRaw();
                EditLos = SameOrNull(cells.Select(c => c.LineOfSight));
                EditFightCell = SameOrNull(cells.Select(c => c.FightCell));
                SyncEditFightCellItemFromRaw();
                EditIo = SameOrNull(cells.Select(c => c.InteractiveObject));
                EditGroundLevel = SameOrNull(cells.Select(c => c.GroundLevel));
                EditGroundSlope = SameOrNull(cells.Select(c => c.GroundSlope));
                EditFlipG = SameOrNull(cells.Select(c => c.FlipGround));
                EditFlipO1 = SameOrNull(cells.Select(c => c.FlipObject1));
                EditFlipO2 = SameOrNull(cells.Select(c => c.FlipObject2));
                EditRotG = SameOrNull(cells.Select(c => c.GroundRotation));
                EditRotO1 = SameOrNull(cells.Select(c => c.Object1Rotation));

                var primaryId = PrimarySelectedCellId ?? SelectedCellIds[0];
                var primary = CurrentMap.Cells[primaryId];
                PreviewGround = CellInfoGround == "Mixto" ? null : LoadLayerPreview(GfxCategory.Ground, primary.GroundGfxId);
                PreviewObject1 = CellInfoObject1 == "Mixto" ? null : LoadLayerPreview(GfxCategory.Object, primary.Object1GfxId);
                PreviewObject2 = CellInfoObject2 == "Mixto" ? null : LoadLayerPreview(GfxCategory.Object, primary.Object2GfxId);

                RefreshMapDataInspector(SelectedCellIds.Count == 1 ? SelectedCellIds[0] : PrimarySelectedCellId);
            }
        }
        finally
        {
            _suppressProp = false;
        }

        OnPropertyChanged(nameof(CellInfoId));
        OnPropertyChanged(nameof(CellInfoGround));
        OnPropertyChanged(nameof(CellInfoObject1));
        OnPropertyChanged(nameof(CellInfoObject2));
        OnPropertyChanged(nameof(CellInfoGroundDetail));
        OnPropertyChanged(nameof(CellInfoObject1Detail));
        OnPropertyChanged(nameof(CellInfoObject2Detail));
        OnPropertyChanged(nameof(HasSelectedCellProp));
        OnPropertyChanged(nameof(EditBlocksVision));
    }

    private void SyncEditMovementItemFromRaw()
    {
        if (_editMovement is null)
        {
            _editMovementItem = null;
            OnPropertyChanged(nameof(EditMovementItem));
            return;
        }

        var raw = (int)_editMovement.Value;
        _editMovementItem = MovementDisplayItem.StandardOptions.FirstOrDefault(o => o.RawValue == raw)
            ?? MovementDisplayItem.ForRaw(raw);
        OnPropertyChanged(nameof(EditMovementItem));
    }

    private void SyncEditFightCellItemFromRaw()
    {
        if (_editFightCell is null)
        {
            _editFightCellItem = null;
            OnPropertyChanged(nameof(EditFightCellItem));
            return;
        }

        _editFightCellItem = FightCellDisplayItem.Options.First(o => o.Value == _editFightCell.Value);
        OnPropertyChanged(nameof(EditFightCellItem));
    }

    private string FmtLayerGfx(GfxCategory category, int id)
    {
        if (id == 0) return "—";
        if (_library.Catalog is null || !GfxResourceResolver.TryResolve(_library.Catalog, category, id, out _))
            return $"{id} (sin recurso {category})";
        return id.ToString();
    }

    private string FmtLayerDetail(GfxCategory category, int id)
    {
        if (id == 0 || _library.Catalog is null)
            return "";
        if (!GfxResourceResolver.TryResolve(_library.Catalog, category, id, out var res))
            return $"GfxID {id}: no encontrado en catálogo {category}";

        var dims = GfxResourceResolver.GetNativeDimensions(res);
        var dimText = dims is { Width: > 0, Height: > 0 } ? $"{dims.Value.Width}×{dims.Value.Height} px" : "dims ?";
        var anchorText = res.Anchor is { } a ? $"anchor {a.X},{a.Y}" : "anchor centro";
        var folder = string.IsNullOrEmpty(res.Folder) ? "" : $" · {res.Folder}";
        var overlaps = GfxResourceResolver.GetCategoriesWithId(_library.Catalog, id);
        var overlapNote = overlaps.Count > 1
            ? $" · ID en {string.Join("/", overlaps)}"
            : "";
        return $"{dimText}{folder} · {anchorText}{overlapNote}";
    }

    private ImageSource? LoadLayerPreview(GfxCategory category, int gfxId)
    {
        if (gfxId == 0 || _library.Catalog is null) return null;
        if (_library.Catalog.TryGet(category, gfxId, out var res) && res is not null)
            return _thumbs.GetThumbnail(res, 72);
        return null;
    }

    private static bool IsTransientStatus(string text) =>
        text.StartsWith("Pegadas", StringComparison.Ordinal)
        || text.StartsWith("Duplicadas", StringComparison.Ordinal)
        || text.StartsWith("Reemplazadas", StringComparison.Ordinal)
        || text.StartsWith("Copiadas", StringComparison.Ordinal)
        || text.StartsWith("Cuentagotas", StringComparison.Ordinal)
        || text.StartsWith("Capa vacía", StringComparison.Ordinal)
        || text.StartsWith("Pincel", StringComparison.Ordinal)
        || text.StartsWith("Deshecho", StringComparison.Ordinal)
        || text.StartsWith("Rehecho", StringComparison.Ordinal)
        || text.StartsWith("Favorito", StringComparison.Ordinal)
        || text.StartsWith("Ninguna celda", StringComparison.Ordinal)
        || text.StartsWith("Selecciona", StringComparison.Ordinal)
        || text.StartsWith("No hay", StringComparison.Ordinal)
        || text.StartsWith("No se pudo", StringComparison.Ordinal);

    private static string FmtMixed<T>(IEnumerable<T> values, Func<T, string> format)
    {
        using var e = values.GetEnumerator();
        if (!e.MoveNext()) return "—";
        var first = e.Current;
        while (e.MoveNext())
        {
            if (!EqualityComparer<T>.Default.Equals(first, e.Current))
                return "Mixto";
        }

        return format(first);
    }

    private static T? SameOrNull<T>(IEnumerable<T> values) where T : struct
    {
        using var e = values.GetEnumerator();
        if (!e.MoveNext()) return null;
        var first = e.Current;
        while (e.MoveNext())
        {
            if (!EqualityComparer<T>.Default.Equals(first, e.Current))
                return null;
        }

        return first;
    }

    public MapRenderOptions GetMapRenderOptions() => BuildRenderOptions();

    private MapRenderOptions BuildRenderOptions() => new()
    {
        AstriaLogoPath = null,
        CropToExportBounds = true,
        DrawBackground = ShowBackgroundLayer,
        DrawGround = ShowGroundLayer,
        DrawObjectLayer1 = ShowObject1Layer,
        DrawObjectLayer2 = ShowObject2Layer,
    };

    private void OnLayerVisibilityChanged()
    {
        PersistMapViewVisibility();
        _ = RerenderAsync();
    }

    private void PersistMapViewVisibility()
    {
        _settings.MapViewVisibility = CaptureMapViewVisibility();
        AppSettingsStore.Save(_settings);
    }

    private void ApplyMapViewVisibilityFromSettings(MapViewVisibilitySettings s)
    {
        _showBackgroundLayer = s.ShowBackground;
        _showGroundLayer = s.ShowGround;
        _showObject1Layer = s.ShowObject1;
        _showObject2Layer = s.ShowObject2;
        _showGrid = s.ShowGrid;
        _showCellIds = s.ShowCellIds;
        _showUnwalkableMarkers = s.ShowUnwalkableMarkers;
        _showLosBlockMarkers = s.ShowLosBlockMarkers;
        _showFightMarkers = s.ShowFightMarkers;
        OnPropertyChanged(nameof(ShowBackgroundLayer));
        OnPropertyChanged(nameof(ShowGroundLayer));
        OnPropertyChanged(nameof(ShowObject1Layer));
        OnPropertyChanged(nameof(ShowObject2Layer));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowCellIds));
        OnPropertyChanged(nameof(ShowCellIdsEffective));
        OnPropertyChanged(nameof(ShowUnwalkableMarkers));
        OnPropertyChanged(nameof(ShowLosBlockMarkers));
        OnPropertyChanged(nameof(ShowFightMarkers));
        BumpCellModeOverlayRevision();
    }

    private MapViewVisibilitySettings CaptureMapViewVisibility() => new()
    {
        ShowBackground = ShowBackgroundLayer,
        ShowGround = ShowGroundLayer,
        ShowObject1 = ShowObject1Layer,
        ShowObject2 = ShowObject2Layer,
        ShowGrid = ShowGrid,
        ShowCellIds = ShowCellIds,
        ShowUnwalkableMarkers = ShowUnwalkableMarkers,
        ShowLosBlockMarkers = ShowLosBlockMarkers,
        ShowFightMarkers = ShowFightMarkers,
    };

    private enum SoloLayerTarget { Background, Ground, Object1, Object2 }

    private void SoloLayer(SoloLayerTarget target)
    {
        _visibilityRestore ??= CaptureMapViewVisibility();
        ShowBackgroundLayer = target == SoloLayerTarget.Background;
        ShowGroundLayer = target == SoloLayerTarget.Ground;
        ShowObject1Layer = target == SoloLayerTarget.Object1;
        ShowObject2Layer = target == SoloLayerTarget.Object2;
        RestoreVisibilityCommand.RaiseCanExecuteChanged();
    }

    public void RestoreLayerVisibility()
    {
        if (_visibilityRestore is null) return;
        ApplyMapViewVisibilityFromSettings(_visibilityRestore);
        _visibilityRestore = null;
        RestoreVisibilityCommand.RaiseCanExecuteChanged();
        _ = RerenderAsync();
        PersistMapViewVisibility();
    }

    public void OpenAppearance()
    {
        var dlg = new AppearanceWindow(_settings) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        NotifyThemePropertiesChanged();
    }

    public void OpenDatabaseSettings()
    {
        _settings.Database ??= new DatabaseSettings();
        var dlg = new DatabaseSettingsWindow(_settings) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    public void OpenGenerateLocalLangMaps()
    {
        if (CurrentMap is null)
            return;

        int? x = CurrentMap.WorldCoordinatesSet ? CurrentMap.WorldX : null;
        int? y = CurrentMap.WorldCoordinatesSet ? CurrentMap.WorldY : null;
        // SubArea is not yet a first-class MapDocument field; user enters/confirm in dialog (FASE 11A).
        int? subArea = null;
        var dlg = new GenerateLangMapsWindow(CurrentMap.Id, x, y, subArea)
        {
            Owner = Application.Current.MainWindow,
        };
        dlg.ShowDialog();
    }

    public void OpenLangSftpSettings()
    {
        _settings.LangSftp ??= new LangSftpSettings();
        var dlg = new LangSftpSettingsWindow(_settings) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    public void OpenClipsSettings()
    {
        var dlg = new ClipsSettingsWindow(_settings) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            if (NpcGfxCatalogService.Shared.IsLoaded)
                NpcGfxCatalogService.Shared.ReloadSpriteMetadata(_settings.ClipsRootPath);
            MapMonsters.RefreshContextStatus();
        }
    }

    public void OpenLicenseStatus()
    {
        var lic = App.License;
        var dlg = new Licensing.LicenseStatusWindow(lic?.CurrentSession, lic?.StatusLabel ?? "Licencia: —", lic)
        {
            Owner = Application.Current.MainWindow,
        };
        dlg.ShowDialog();
    }

    public bool LicenseStatusVisible => App.License is not null;

    public string LicenseStatusText => App.License?.StatusLabel ?? "";

    private void OnLicenseStatusChanged()
    {
        OnPropertyChanged(nameof(LicenseStatusText));
        OnPropertyChanged(nameof(LicenseStatusVisible));
    }

    public void OpenPublishRemoteLang()
    {
        if (CurrentMap is null)
            return;

        _settings.LangSftp ??= new LangSftpSettings();
        int? x = CurrentMap.WorldCoordinatesSet ? CurrentMap.WorldX : null;
        int? y = CurrentMap.WorldCoordinatesSet ? CurrentMap.WorldY : null;
        int? subArea = null;
        var dlg = new PublishLangRemoteWindow(_settings, CurrentMap.Id, x, y, subArea)
        {
            Owner = Application.Current.MainWindow,
        };
        dlg.ShowDialog();
    }

    public async Task SyncRemoteLangAsync()
    {
        _settings.LangSftp ??= new LangSftpSettings();
        var cfg = _settings.LangSftp;
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.User))
        {
            MessageBox.Show(
                "Configure primero LANG / SFTP (Ajustes → Configuración LANG / SFTP…).",
                "Sincronizar LANG remoto",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenLangSftpSettings();
            return;
        }

        string password;
        try
        {
            password = LangSftpPasswordProtector.Unprotect(cfg.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo descifrar la contraseña SFTP guardada.\n" + ex.Message,
                "Sincronizar LANG remoto",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(
                "No hay contraseña SFTP guardada. Ábrala en Configuración LANG / SFTP.",
                "Sincronizar LANG remoto",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenLangSftpSettings();
            return;
        }

        StatusText = "Sincronizando LANG remoto (READ-ONLY)…";
        RufusLog.Info($"SFTP sincronización iniciada · {cfg.Host}:{cfg.Port} como {cfg.User}");
        LangRemoteSyncResult result;
        try
        {
            result = await Task.Run(() => LangRemoteSyncService.Sync(new LangRemoteSyncRequest
            {
                Settings = cfg,
                PlainPassword = password,
            })).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Error LANG remoto";
            RufusLog.Error("Error LANG remoto: " + ex.Message);
            MessageBox.Show(ex.Message, "Sincronizar LANG remoto", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (result.Success && result.Snapshot is not null)
        {
            cfg.LastSync = result.Snapshot;
            AppSettingsStore.Save(_settings);
            StatusText = $"LANG remoto sincronizado · maps_es {result.MapsVersion}";
            RufusLog.Ok($"LANG remoto sincronizado · maps_es {result.MapsVersion}");
        }
        else
        {
            StatusText = "LANG remoto: " + (result.StatusLabel ?? "ERROR");
            RufusLog.Error(result.Error ?? result.StatusLabel ?? "Error de sincronización LANG");
        }

        var dlg = new LangRemoteSyncWindow(result) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    public async Task PublishToDatabaseAsync()
    {
        if (CurrentMap is null)
            return;

        _settings.Database ??= new DatabaseSettings();
        var db = _settings.Database;
        if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.User))
        {
            MessageBox.Show(
                "Configure primero la conexión MySQL (Archivo → Configuración BD…).",
                "Publicar en BD",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenDatabaseSettings();
            return;
        }

        string password;
        try
        {
            password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo descifrar la contraseña guardada.\n" + DatabaseSettingsWindow.FriendlyDbError(ex),
                "Publicar en BD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var backupDir = Path.Combine(AppSettingsStore.SettingsDirectory, "db-backups");
        var label = $"{db.Database}.{db.Table}";
        IMapasRepository repo = new MysqlMapasRepository(db, password);
        var service = new MapPublishService(repo, backupDir, label);

        try
        {
            StatusText = "Publicando en BD…";
            RufusLog.Info($"Publicación BD iniciada · mapa {CurrentMap.Id} · {label}");
            // UI dialogs must run on the WPF STA dispatcher. MapPublishWorkflow also marshals
            // these callbacks via the captured SynchronizationContext after ConfigureAwait(false).
            var outcome = await MapPublishWorkflow.ExecuteAsync(
                CurrentMap,
                service,
                async ct => await SaveOfficialMapAsync().ConfigureAwait(true),
                (diff, currentFecha, newFecha) => InvokeOnUi(() =>
                {
                    var confirm = new PublishConfirmWindow(diff, currentFecha, newFecha, label)
                    {
                        Owner = Application.Current?.MainWindow,
                    };
                    return confirm.ShowDialog() == true && confirm.Confirmed;
                }),
                currentFecha => InvokeOnUi(() =>
                {
                    var input = new RevisionInputWindow(currentFecha)
                    {
                        Owner = Application.Current?.MainWindow,
                    };
                    return input.ShowDialog() == true ? input.ResultRevision : null;
                }),
                (plan, summary) => InvokeOnUi(() =>
                {
                    var offer = MessageBox.Show(
                        $"El mapa {CurrentMap.Id} no existe en la base de datos.\n\n¿Crear mapa?",
                        "Crear mapa",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (offer != MessageBoxResult.Yes)
                        return false;

                    var confirm = new CreateConfirmWindow(summary, label)
                    {
                        Owner = Application.Current?.MainWindow,
                    };
                    return confirm.ShowDialog() == true && confirm.Confirmed;
                }),
                db.NewMapDefaults).ConfigureAwait(true);

            if (outcome.NoChanges)
            {
                InvokeOnUi(() => MessageBox.Show(outcome.Error ?? "No hay cambios que publicar.", "Publicar en BD",
                    MessageBoxButton.OK, MessageBoxImage.Information));
                StatusText = "Sin cambios que publicar";
                RufusLog.Info("Publicación: sin cambios");
                return;
            }

            if (!outcome.Success)
            {
                InvokeOnUi(() => MessageBox.Show(outcome.Error ?? "Error al publicar.", "Publicar en BD",
                    MessageBoxButton.OK, MessageBoxImage.Warning));
                StatusText = "Publicación cancelada o fallida";
                RufusLog.Warn(outcome.Error ?? "Publicación cancelada o fallida");
                return;
            }

            RefreshMapInspector();
            AfterHistoryChange();
            InvokeOnUi(() => MessageBox.Show(
                $"Mapa {CurrentMap.Id} publicado correctamente.\n\n" +
                (outcome.Created
                    ? $"Operación: CREATE · Revisión inicial: {outcome.NewFecha}\n"
                    : $"Operación: UPDATE · Revisión: {outcome.CurrentFecha} → {outcome.NewFecha}\n") +
                $"BD: {label}" +
                (string.IsNullOrWhiteSpace(outcome.BackupPath) ? "" : $"\nBackup: {outcome.BackupPath}"),
                "Publicar en BD",
                MessageBoxButton.OK,
                MessageBoxImage.Information));
            StatusText = $"Publicado mapa {CurrentMap.Id} (rev {outcome.NewFecha})";
            if (outcome.Created)
                RufusLog.Ok($"Mapa nuevo {CurrentMap.Id} creado en BD · revisión {outcome.NewFecha}");
            else
                RufusLog.Ok($"Publicación completada · {outcome.CurrentFecha} → {outcome.NewFecha}");
        }
        catch (Exception ex)
        {
            InvokeOnUi(() => MessageBox.Show(DatabaseSettingsWindow.FriendlyDbError(ex), "Publicar en BD",
                MessageBoxButton.OK, MessageBoxImage.Error));
            StatusText = "Error al publicar";
            RufusLog.Error("Error al publicar: " + DatabaseSettingsWindow.FriendlyDbError(ex));
        }
    }

    /// <summary>Marshal UI-bound work onto the WPF dispatcher when needed (STA).</summary>
    private static T InvokeOnUi<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();
        return dispatcher.Invoke(action);
    }

    private static void InvokeOnUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private void SetTheme(ThemePreference theme)
    {
        if (_settings.Theme == theme) return;
        _settings.Theme = theme;
        AppSettingsStore.Save(_settings);
        ThemeService.SetPreference(theme);
        NotifyThemePropertiesChanged();
    }

    private void OnAppThemeChanged() => NotifyThemePropertiesChanged();

    private void NotifyThemePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    public async Task SyncMetadataFromDatabaseAsync()
    {
        if (CurrentMap is null)
            return;

        _settings.Database ??= new DatabaseSettings();
        var db = _settings.Database;
        if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.User))
        {
            MessageBox.Show(
                "Configure primero la conexión MySQL (Archivo → Configuración BD…).",
                "Sincronizar metadatos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenDatabaseSettings();
            return;
        }

        string password;
        try
        {
            password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo descifrar la contraseña guardada.\n" + DatabaseSettingsWindow.FriendlyDbError(ex),
                "Sincronizar metadatos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var backupDir = Path.Combine(AppSettingsStore.SettingsDirectory, "db-backups");
        var label = $"{db.Database}.{db.Table}";
        IMapasRepository repo = new MysqlMapasRepository(db, password);
        var service = new MapPublishService(repo, backupDir, label);

        try
        {
            StatusText = "Sincronizando metadatos desde BD…";
            var beforeMapData = CurrentMap.MapData;
            var beforeFight = CurrentMap.FightPlaces;
            var (ok, error, snap) = await service.SyncMetadataFromDatabaseAsync(CurrentMap).ConfigureAwait(true);
            if (!ok || snap is null)
            {
                MessageBox.Show(error ?? "No se pudo sincronizar.", "Sincronizar metadatos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText = "Sincronización fallida";
                return;
            }

            if (!string.Equals(beforeMapData, CurrentMap.MapData, StringComparison.Ordinal)
                || !string.Equals(beforeFight ?? "", CurrentMap.FightPlaces ?? "", StringComparison.Ordinal))
            {
                MessageBox.Show("ERROR INTERNO: MapData/FightPlaces fueron alterados.",
                    "Sincronizar metadatos", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Sincronización abortada";
                return;
            }

            RefreshMapInspector();
            AfterHistoryChange();
            MessageBox.Show(
                $"Metadatos del mapa {CurrentMap.Id} sincronizados desde BD (sin publicar).\n\n" +
                $"Revisión: {snap.Fecha}\nBackground: {snap.BgId}\nMúsica: {snap.MusicId}\n" +
                $"Ambiente: {snap.AmbienteId}\nOutdoor: {snap.OutDoor}\nCapabilities: {snap.Capabilities}\n" +
                $"X: {snap.X}\nY: {snap.Y}\n\nMapData: NO modificado.\nBD: {label}",
                "Sincronizar metadatos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StatusText = "Metadatos sincronizados desde BD";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sincronizar metadatos", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText = "Sincronización fallida";
        }
    }


    public void OpenBackgroundPicker()
    {
        if (CurrentMap is null || !HasLibrary) return;
        var dlg = new BackgroundPickerWindow(_library, CurrentMap.BackgroundId)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true || dlg.SelectedBackgroundId is not int newId) return;
        ApplyBackgroundId(newId);
    }

    public void ApplyBackgroundId(int backgroundId)
    {
        if (_session is null || CurrentMap is null) return;

        // Combinado: aplicar a todos los mapas marcados (bordes amarillos / chips).
        if (IsMapCombinedMode &&
            CombinedMaps.World is not null &&
            CombinedMaps.SelectedKeys.Count > 0)
        {
            ApplyBackgroundIdToCombinedSelection(backgroundId);
            return;
        }

        var before = CurrentMap.BackgroundId;
        var beforeDefined = CurrentMap.BackgroundDefined;
        if (before == backgroundId && beforeDefined) return;
        CurrentMap.BackgroundId = backgroundId;
        CurrentMap.BackgroundDefined = true;
        _session.History.PushExecuted(new MapMetadataEditCommand("Cambiar fondo", before, backgroundId, beforeDefined));
        AfterHistoryChange();
        RefreshMapInspector();
        _ = RerenderAsync();
    }

    private void ApplyBackgroundIdToCombinedSelection(int backgroundId)
    {
        if (CombinedMaps.World is null) return;

        var keys = CombinedMaps.SelectedKeys.ToList();
        var changed = 0;
        foreach (var key in keys)
        {
            if (!CombinedMaps.World.Documents.TryGetValue(key, out var entry))
                continue;

            var map = entry.Document;
            var before = map.BackgroundId;
            var beforeDefined = map.BackgroundDefined;
            if (before == backgroundId && beforeDefined)
                continue;

            map.BackgroundId = backgroundId;
            map.BackgroundDefined = true;

            var open = _openDocuments.FirstOrDefault(d => ReferenceEquals(d.Map, map));
            if (open is null && !IsMapCombinedMinimized)
                open = EnsureMosaicBoundDocument(map, key);
            open ??= _openDocuments.FirstOrDefault(d => d.Map.Id == map.Id && !IsMosaicBoundDocument(d))
                     ?? _openDocuments.FirstOrDefault(d => d.Map.Id == map.Id);

            if (open is not null)
            {
                open.Session.History.PushExecuted(
                    new MapMetadataEditCommand("Cambiar fondo", before, backgroundId, beforeDefined));
                open.NotifyDirtyChanged();
            }

            CombinedMaps.InvalidateThumbnail(key);
            CombinedMaps.NotifyMapEdited(key);
            changed++;
        }

        if (changed == 0)
        {
            StatusText = "Todos los mapas seleccionados ya tienen ese fondo";
            RefreshMapInspector();
            return;
        }

        AfterHistoryChange();
        RefreshMapInspector();
        RaiseCombineCommands();
        RefreshCombinedMapChips();
        CombinedMaps.RequestViewRedraw();
        _ = RerenderAsync();
        StatusText = backgroundId == 0
            ? $"Sin fondo aplicado a {changed} mapa(s)"
            : $"Fondo {backgroundId} aplicado a {changed} mapa(s)";
    }

    public void ResetLayout()
    {
        _settings.UiLayout = new UiLayoutSettings();
        AppSettingsStore.Save(_settings);
        Logs.IsExpanded = _settings.UiLayout.LogsExpanded;
        Logs.PanelHeight = _settings.UiLayout.LogsPanelHeight;
        OnPropertyChanged(nameof(ShowMapsPanel));
        OnPropertyChanged(nameof(ShowInspectorPanel));
        OnPropertyChanged(nameof(ShowCatalogPanel));
        OnPropertyChanged(nameof(ShowCategoriesPanel));
        OnPropertyChanged(nameof(ShowBrushPanel));
        OnPropertyChanged(nameof(ShowToolBar));
        OnPropertyChanged(nameof(ShowStatusBar));
        RequestResetPanels?.Invoke();
        RequestApplyLayout?.Invoke();
    }

    public void PersistUiLayout()
    {
        _settings.UiLayout.Clamp();
        AppSettingsStore.Save(_settings);
    }

    private void SetLayoutFlag(Action<bool> setter, bool value, string propertyName)
    {
        var current = propertyName switch
        {
            nameof(ShowMapsPanel) => _settings.UiLayout.ShowMapsPanel,
            nameof(ShowInspectorPanel) => _settings.UiLayout.ShowInspectorPanel,
            nameof(ShowCatalogPanel) => _settings.UiLayout.ShowCatalogPanel,
            nameof(ShowCategoriesPanel) => _settings.UiLayout.ShowCategoriesPanel,
            nameof(ShowBrushPanel) => _settings.UiLayout.ShowBrushPanel,
            nameof(ShowToolBar) => _settings.UiLayout.ShowToolBar,
            _ => _settings.UiLayout.ShowStatusBar,
        };
        if (current == value) return;
        setter(value);
        AppSettingsStore.Save(_settings);
        OnPropertyChanged(propertyName);
        RequestApplyLayout?.Invoke();
    }

    private bool ValidateSelectedGfxForActiveLayer(out string error)
    {
        error = "";
        if (SelectedGfxId is not int gfxId)
        {
            error = "Selecciona un GFX del catálogo.";
            return false;
        }

        if (_library.Catalog is null)
        {
            error = "No hay catálogo cargado.";
            return false;
        }

        var category = PaintLayer.ToGfxCategory();
        if (!GfxResourceResolver.TryResolve(_library.Catalog, category, gfxId, out _))
        {
            error = $"GfxID {gfxId} no existe en catálogo {UiDisplayLabels.ResourceType(PaintLayer)}.";
            SelectedGfxId = null;
            return false;
        }

        return true;
    }

    #region Multimap editing bridge

    public void BeginMultiMapStroke()
    {
        _multiMapStrokeIsErase = false;
        _multiMapEraseMatchBrush = false;
        MultiMap.BeginStroke(Tool, PaintLayer);
        MultiMap.ResetStrokePointer();
    }

    public void BeginMultiMapEraseStroke(bool matchBrushOnly = false)
    {
        _multiMapStrokeIsErase = true;
        _multiMapEraseMatchBrush = matchBrushOnly;
        MultiMap.BeginStroke(EditorTool.Erase, PaintLayer);
        MultiMap.ResetStrokePointer();
    }

    public void FinishMultiMapStroke()
    {
        MultiMap.FinishStroke();
        _multiMapStrokeIsErase = false;
        _multiMapEraseMatchBrush = false;
        foreach (var key in MultiMap.DirtyDocumentKeys)
            MarkOpenDocumentDirtyFromMultiMap(key);
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        MosaicHost.SaveModifiedMapsCommand.RaiseCanExecuteChanged();
        RaiseCombineCommands();
        SyncUiFromMultiMapSelection();
    }

    public void ContinueMultiMapStroke(double worldX, double worldY) =>
        MultiMap.ContinueStroke(
            worldX,
            worldY,
            _multiMapStrokeIsErase ? EditorTool.Erase : Tool,
            PaintLayer,
            SelectedGfxId,
            BrushFlip,
            BrushRotation,
            mosaicMode: true,
            eraseOnlySelectedGfx: _multiMapEraseMatchBrush || EraseOnlySelectedGfx,
            paintMarksUnwalkable: PaintMarksUnwalkable,
            paintSeam: PaintSeam && IsMapCombinedMode);

    /// <summary>Right-click in Construir: remove the brush GFX on the active layer (combinado).</summary>
    public bool TryEraseActiveBrushAtWorldCell(WorldCellRef cell)
    {
        if (SelectedGfxId is not int brushId)
            return false;
        var doc = MultiMap.GetDocument(cell.DocumentKey);
        if (doc is null || cell.CellId < 0 || cell.CellId >= doc.Cells.Count)
            return false;
        if (GetLayerGfx(doc.Cells[cell.CellId], PaintLayer) != brushId)
            return false;

        BeginMultiMapEraseStroke(matchBrushOnly: true);
        HandleMultiMapEraseClick(cell, matchBrushOnly: true);
        StatusText = $"Retirado GFX {brushId} — sigue activo (arrastra para borrar más)";
        return true;
    }

    public void HandleMultiMapEraseClick(WorldCellRef cell, bool matchBrushOnly)
    {
        MultiMap.HandleCellClick(
            cell,
            EditorTool.Erase,
            PaintLayer,
            SelectedGfxId,
            BrushFlip,
            BrushRotation,
            isDrag: false,
            ctrl: false,
            eraseOnlySelectedGfx: matchBrushOnly);
        SyncUiFromMultiMapSelection(cell.DocumentKey);
    }

    public void HandleMultiMapCellClick(
        WorldCellRef cell,
        bool isDrag,
        bool ctrl,
        double? mapLocalX = null,
        double? mapLocalY = null)
    {
        _inspectContentX = mapLocalX;
        _inspectContentY = mapLocalY;

        MultiMap.HandleCellClick(
            cell,
            Tool,
            PaintLayer,
            SelectedGfxId,
            BrushFlip,
            BrushRotation,
            isDrag,
            ctrl,
            gfx => ApplyMultiMapEyedropper(cell, gfx),
            eraseOnlySelectedGfx: EraseOnlySelectedGfx,
            paintMarksUnwalkable: PaintMarksUnwalkable,
            paintSeam: PaintSeam && IsMapCombinedMode);

        if (!isDrag)
            SyncUiFromMultiMapSelection(cell.DocumentKey);
    }

    /// <summary>
    /// Mirrors MultiMap cell selection into the normal MAPA inspector (Capas, propiedades, ESTE ÍTEM).
    /// </summary>
    public void SyncUiFromMultiMapSelection(string? preferDocumentKey = null)
    {
        var mmSel = MultiMap.Selection.ToList();
        if (mmSel.Count == 0)
        {
            if (_session is not null)
            {
                _session.ClearSelection();
                SyncSelectionFromSession();
            }
            return;
        }

        var docKey = preferDocumentKey
                     ?? mmSel[^1].DocumentKey;
        // Prefer the document of the majority of selected cells if mixed.
        if (preferDocumentKey is null && mmSel.Select(c => c.DocumentKey).Distinct().Count() > 1)
            docKey = mmSel.GroupBy(c => c.DocumentKey).OrderByDescending(g => g.Count()).First().Key;

        FocusOpenMapFromWorldDocumentKey(docKey);
        if (_combinedMapsMultiSelect)
            MosaicHost.EnsureKeySelected(docKey);
        else
            MosaicHost.SelectKey(docKey);
        SyncCombinedMapChipsFromWorld();
        SetCombinedMapsMultiSelect(MosaicHost.SelectedKeys.Count > 1);

        var cellIds = mmSel
            .Where(c => c.DocumentKey == docKey)
            .Select(c => c.CellId)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (_session is null || cellIds.Count == 0)
            return;

        _session.SetSelection(cellIds);
        PrimarySelectedCellId = cellIds[^1];
        SyncSelectionFromSession();
        PreferPaintLayerWithGfxOnPrimaryCell();
        RaiseFocusGfxUi();
        RaiseCombineCommands();
        RaiseSelectionCommands();
        // Do not PushSessionSelectionToMultiMap here — that would drop cells from other maps.
    }

    /// <summary>
    /// Mirrors the open-document session selection onto MultiMap overlays (MAPA combinado).
    /// </summary>
    private void PushSessionSelectionToMultiMap()
    {
        if (!IsMapCombinedMode || !MultiMap.IsActive || CurrentMap is null)
            return;

        var key = FindWorldDocumentKeyForMap(CurrentMap);
        if (key is null)
            return;

        MultiMap.SetSelection(SelectedCellIds.Select(id => new WorldCellRef(key, id)));
        MosaicHost.RequestViewRedraw();
    }

    /// <summary>Invalidates mosaic thumbnails after panel edits that go through MapEditSession.</summary>
    private void RefreshCombinedMapVisuals()
    {
        if (!IsMapCombinedMode || CurrentMap is null)
            return;

        var key = FindWorldDocumentKeyForMap(CurrentMap);
        if (key is not null)
            MosaicHost.NotifyMapEdited(key);
        else
            MosaicHost.RequestViewRedraw();

        if (_session?.IsDirty == true && key is not null)
            MarkOpenDocumentDirtyFromMultiMap(key);

        PushSessionSelectionToMultiMap();
    }

    private string? FindWorldDocumentKeyForMap(MapDocument map)
    {
        if (MosaicHost.World is null) return null;
        foreach (var (key, entry) in MosaicHost.World.Documents)
        {
            if (ReferenceEquals(entry.Document, map))
                return key;
        }

        // En combinado visible no emparejar por Id: puede ser un mapa flotante distinto con el mismo número.
        if (IsMapCombinedMode && !IsMapCombinedMinimized)
            return null;

        foreach (var (key, entry) in MosaicHost.World.Documents)
        {
            if (entry.Document.Id == map.Id)
                return key;
        }

        return null;
    }

    public void MarkOpenDocumentDirtyFromMultiMap(string documentKey)
    {
        if (MosaicHost.World is null) return;
        if (!MosaicHost.World.Documents.TryGetValue(documentKey, out var entry)) return;
        var open = _openDocuments.FirstOrDefault(d => ReferenceEquals(d.Map, entry.Document));
        if (open is null && IsMapCombinedMode && !IsMapCombinedMinimized)
            open = EnsureMosaicBoundDocument(entry.Document, documentKey);
        open ??= _openDocuments.FirstOrDefault(d => d.Map.Id == entry.Document.Id && !IsMosaicBoundDocument(d))
                 ?? _openDocuments.FirstOrDefault(d => d.Map.Id == entry.Document.Id);
        open?.Session.History.MarkDirty();
        open?.NotifyDirtyChanged();
        RaiseCombineCommands();
        UpdateTitle();
    }

    private void ApplyMultiMapEyedropper(WorldCellRef cell, int gfx)
    {
        if (gfx == 0)
        {
            SelectedGfxId = null;
            StatusText = "Capa vacía — pincel limpiado";
        }
        else
        {
            SelectedGfxId = gfx;
            PushRecent(CategoryKey(PaintLayer.ToGfxCategory()), gfx);
            var doc = MultiMap.GetDocument(cell.DocumentKey);
            if (doc is not null)
            {
                var c = doc.Cells[cell.CellId];
                switch (PaintLayer)
                {
                    case PaintLayer.Ground:
                        BrushFlip = c.FlipGround;
                        BrushRotation = c.GroundRotation;
                        break;
                    case PaintLayer.Object1:
                        BrushFlip = c.FlipObject1;
                        BrushRotation = c.Object1Rotation;
                        break;
                    case PaintLayer.Object2:
                        BrushFlip = c.FlipObject2;
                        break;
                }
            }

            Tool = EditorTool.Paint;
            StatusText = $"Cuentagotas: {gfx}";
        }
    }

    public void BeginMultiMapRectSelect(double wx, double wy) =>
        MultiMap.BeginRectSelect(wx, wy);

    public void UpdateMultiMapRectSelect(double wx, double wy) =>
        MultiMap.UpdateRectSelect(wx, wy, mosaicMode: true);

    public void EndMultiMapRectSelect(double wx, double wy)
    {
        MultiMap.EndRectSelect(wx, wy, mosaicMode: true);
        SyncUiFromMultiMapSelection();
    }

    public void UpdateMultiMapHover(double wx, double wy)
    {
        MultiMap.UpdateHover(wx, wy, mosaicMode: true);
        UpdateMultiMapStatusBar();
    }

    public void ClearMultiMapHover()
    {
        MultiMap.ClearHover();
        World.HoverText = "";
    }

    private void UpdateMultiMapStatusBar()
    {
        var hit = MultiMap.HoveredCell;
        if (hit is not WorldCellHit worldHit)
        {
            World.HoverText = "Sin mapa";
            return;
        }

        var doc = MultiMap.GetDocument(worldHit.DocumentKey);
        var layer = PaintLayer switch
        {
            PaintLayer.Ground => "Ground",
            PaintLayer.Object1 => "Capa 1",
            _ => "Capa 2",
        };
        var gfx = Tool switch
        {
            EditorTool.Paint => SelectedGfxId is int g ? $"GFX {g}" : "GFX —",
            EditorTool.Erase => "Erase",
            _ => Tool.ToString(),
        };
        World.HoverText =
            $"Map {doc?.Id} | Cell {worldHit.CellId} | World ({worldHit.WorldGridX},{worldHit.WorldGridY}) | {layer} | {gfx}";
    }

    public void CopyMultiMapSelection()
    {
        MultiMap.CopySelection();
        StatusText = $"Copiadas {MultiMap.Selection.Count} celdas (multimap)";
    }

    public void PasteMultiMapAt(WorldCellHit dest)
    {
        var (pasted, skipped) = MultiMap.PasteAt(dest, mosaicMode: true);
        StatusText = pasted > 0
            ? $"Pegadas {pasted} celdas (omitidas {skipped})"
            : "Nada que pegar";
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    public void DeleteMultiMapSelection()
    {
        MultiMap.DeleteSelection(PaintLayer);
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        StatusText = "Capa borrada en selección";
    }

    public bool ApplyMultiMapReplace(int findId, int replaceId)
    {
        var count = MultiMap.CountReplace(findId, PaintLayer);
        if (count == 0)
        {
            StatusText = "Ninguna coincidencia en selección";
            return false;
        }

        var changed = MultiMap.ApplyReplace(findId, replaceId, PaintLayer);
        StatusText = $"Reemplazadas {changed} celdas ({findId} → {replaceId})";
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        return changed > 0;
    }

    public int ApplyMultiMapReplaceInMaps(int findId, int replaceId)
    {
        var count = MultiMap.CountReplaceInEditableMaps(findId, PaintLayer);
        if (count == 0) return 0;
        var changed = MultiMap.ApplyReplaceInEditableMaps(findId, replaceId, PaintLayer);
        StatusText = $"Reemplazadas {changed} coincidencias en mapas seleccionados";
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        return changed;
    }

    public async Task<bool> SaveMultiMapModifiedAsync()
    {
        if (!MosaicHost.IsMultiMapEditMode) return false;
        MultiMap.FinishStroke();
        var n = MultiMap.SaveModifiedMaps();
        if (n > 0)
            StatusText = $"Guardados {n} mapa(s) modificados";
        return n > 0;
    }

    #endregion

    public void Dispose()
    {
        ThemeService.ThemeChanged -= OnAppThemeChanged;
        if (App.License is not null)
            App.License.StatusChanged -= OnLicenseStatusChanged;
        _mapListFilter.Changed -= OnMapListFilterChangedFromPicker;
        DisposeImagesWatcher();
        _autosaveTimer.Stop();
        _gfxSearchDebounce.Stop();
        Logs.Dispose();
        _overlayCache.Dispose();
        _mapPreviews.Clear();
        _library.Dispose();
    }
}
