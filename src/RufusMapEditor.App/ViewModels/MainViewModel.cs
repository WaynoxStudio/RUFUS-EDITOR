using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private double? _lastStrokeContentX;
    private double? _lastStrokeContentY;
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

    private bool _libraryLoadCompleted;
    private readonly bool _deferLibraryLoad;

    public MainViewModel(bool deferLibraryLoad = false)
    {
        _deferLibraryLoad = deferLibraryLoad;
        _settings = AppSettingsStore.Load();
        MapIds = new ObservableCollection<int>();
        MapListItems = new ObservableCollection<MapPickerItemVm>();
        FolderTree = new ObservableCollection<FolderNodeVm>();
        VisibleGfxRows = new ObservableCollection<GfxRowVm>();
        MovementDisplayOptions = new ObservableCollection<MovementDisplayItem>(MovementDisplayItem.StandardOptions);
        FightCellDisplayOptions = new ObservableCollection<FightCellDisplayItem>(FightCellDisplayItem.Options);
        RotationOptions = new ObservableCollection<int>(new[] { 0, 1, 2, 3 });

        SelectLibraryCommand = new RelayCommand(SelectLibrary);
        OpenMapDialogCommand = new RelayCommand(OpenMapDialog, () => HasLibrary);
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

        UndoCommand = new RelayCommand(Undo, () =>
            (_session?.History.CanUndo == true) ||
            (World.IsMultiMapEditMode && MultiMap.History.CanUndo));
        RedoCommand = new RelayCommand(Redo, () =>
            (_session?.History.CanRedo == true) ||
            (World.IsMultiMapEditMode && MultiMap.History.CanRedo));
        CopyCommand = new RelayCommand(CopySelection, () => HasSelection);
        PasteCommand = new RelayCommand(PasteSelection, () => _session?.Clipboard is not null && CurrentMap is not null);
        DuplicateCommand = new RelayCommand(DuplicateSelection, () => HasSelection);

        SetToolSelectCommand = new RelayCommand(() => Tool = EditorTool.Select);
        SetToolRectSelectCommand = new RelayCommand(() => Tool = EditorTool.RectSelect);
        SetToolPaintCommand = new RelayCommand(() => Tool = EditorTool.Paint);
        SetToolEraseCommand = new RelayCommand(() => Tool = EditorTool.Erase);
        SetToolEyedropperCommand = new RelayCommand(() => Tool = EditorTool.Eyedropper);
        SetToolUnwalkableCommand = new RelayCommand(() => Tool = EditorTool.Unwalkable);
        SetToolLineOfSightCommand = new RelayCommand(() => Tool = EditorTool.LineOfSight);
        SetToolFightCell1Command = new RelayCommand(() => Tool = EditorTool.FightCell1);
        SetToolFightCell2Command = new RelayCommand(() => Tool = EditorTool.FightCell2);
        CycleBrushRotationCommand = new RelayCommand(CycleBrushRotation, () => PaintLayer != PaintLayer.Object2);

        SetLayerGroundCommand = new RelayCommand(() => PaintLayer = PaintLayer.Ground);
        SetLayerObject1Command = new RelayCommand(() => PaintLayer = PaintLayer.Object1);
        SetLayerObject2Command = new RelayCommand(() => PaintLayer = PaintLayer.Object2);

        ClearGroundCommand = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Ground), () => HasSelection);
        ClearObject1Command = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Object1), () => HasSelection);
        ClearObject2Command = new RelayCommand(() => ClearSelectedLayer(PaintLayer.Object2), () => HasSelection);
        ClearActiveLayerCommand = new RelayCommand(ClearActiveLayer, () => HasSelection);
        ApplyBrushToSelectionCommand = new RelayCommand(ApplyBrushToSelection, () => HasSelection && SelectedGfxId is not null);
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
            if (World.IsMultiMapEditMode)
                await SaveMultiMapModifiedAsync();
            else
                await SaveAsync();
        }, () => CurrentMap is not null || (World.IsMultiMapEditMode && MultiMap.ModifiedMapCount > 0));
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
            World.TryAutosave();
        };
        _autosaveTimer.Start();

        RecentProjects = new ObservableCollection<string>(_settings.RecentProjects);

        MultiMap = new MultiMapEditService(_library, _worldThumbs);

        World = new WorldViewModel(
            _library,
            _worldThumbs,
            _mapPreviews,
            MultiMap,
            OpenMapFromWorldAsync,
            () => MapIds.ToList(),
            s => StatusText = s);
        World.SetEditorHost(this);

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

    public WorldViewModel World { get; }

    public MultiMapEditService MultiMap { get; }

    public LogConsoleViewModel Logs { get; }

    public int WorkspaceTabIndex
    {
        get => _workspaceTabIndex;
        set
        {
            if (!SetProperty(ref _workspaceTabIndex, value)) return;
            OnPropertyChanged(nameof(IsWorldTab));
            if (value == 1 && _worldEditingDocumentKey is not null)
                World.NotifyMapEdited(_worldEditingDocumentKey);
        }
    }

    public bool IsWorldTab => WorkspaceTabIndex == 1;

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
    public RelayCommand SetToolSelectCommand { get; }
    public RelayCommand SetToolRectSelectCommand { get; }
    public RelayCommand SetToolPaintCommand { get; }
    public RelayCommand SetToolEraseCommand { get; }
    public RelayCommand SetToolEyedropperCommand { get; }
    public RelayCommand SetToolUnwalkableCommand { get; }
    public RelayCommand SetToolLineOfSightCommand { get; }
    public RelayCommand SetToolFightCell1Command { get; }
    public RelayCommand SetToolFightCell2Command { get; }
    public RelayCommand CycleBrushRotationCommand { get; }
    public RelayCommand SetLayerGroundCommand { get; }
    public RelayCommand SetLayerObject1Command { get; }
    public RelayCommand SetLayerObject2Command { get; }
    public RelayCommand ClearGroundCommand { get; }
    public RelayCommand ClearObject1Command { get; }
    public RelayCommand ClearObject2Command { get; }
    public RelayCommand ClearActiveLayerCommand { get; }
    public RelayCommand ApplyBrushToSelectionCommand { get; }
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
        _ => "—",
    };

    public string SelectedGfxLabel
    {
        get => _selectedGfxLabel;
        private set => SetProperty(ref _selectedGfxLabel, value);
    }

    public string UndoLabel =>
        _session?.History.UndoName is string name ? $"Deshacer {name}" : "Deshacer";

    public string RedoLabel =>
        _session?.History.RedoName is string name ? $"Rehacer {name}" : "Rehacer";

    public bool CanUndo =>
        _session?.History.CanUndo == true ||
        (World.IsMultiMapEditMode && MultiMap.History.CanUndo);

    public bool CanRedo =>
        _session?.History.CanRedo == true ||
        (World.IsMultiMapEditMode && MultiMap.History.CanRedo);

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
        private set => SetProperty(ref _highlightedInspectorLayer, value);
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
    public bool IsDirty => _session?.IsDirty == true || World.IsDirty;

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
        SelectedGfxId is null
            ? "Selecciona primero un GFX del catálogo."
            : HasSelection
                ? $"Rellenar {SelectionCount} celdas con GFX {SelectedGfxId} en {UiDisplayLabels.LayerTarget(PaintLayer)}"
                : "Selecciona un área primero.";

    public string ClearActiveLayerInSelectionTooltip =>
        HasSelection
            ? $"Vaciar {UiDisplayLabels.LayerTarget(PaintLayer)} en {SelectionCount} celdas"
            : "Selecciona un área primero.";

    public bool IsRectSelecting => _rectSelecting;

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
                OnPropertyChanged(nameof(IsCellModeTool));
                StatusText = value switch
                {
                    EditorTool.Paint => "Herramienta: Construcción (GFX)",
                    EditorTool.Erase => "Herramienta: Borrar GFX",
                    EditorTool.Eyedropper => "Herramienta: Cuentagotas",
                    EditorTool.RectSelect => "Herramienta: Selección rectangular",
                    EditorTool.Unwalkable => "Herramienta: No transitable",
                    EditorTool.LineOfSight => "Herramienta: Bloquear visión",
                    EditorTool.FightCell1 => "Herramienta: Combate — Equipo 1",
                    EditorTool.FightCell2 => "Herramienta: Combate — Equipo 2",
                    EditorTool.MobCell => "Herramienta: Grupos fijos (inactiva · LIB.4 aislado)",
                    _ => "Herramienta: Seleccionar",
                };
                OnPropertyChanged(nameof(ActiveToolLabel));
            }
        }
    }

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
        set { if (value) PaintLayer = PaintLayer.Object1; }
    }

    public bool IsLayerObject2
    {
        get => PaintLayer == PaintLayer.Object2;
        set { if (value) PaintLayer = PaintLayer.Object2; }
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
                OnPropertyChanged(nameof(IsSelectedGfxFavorite));
                OnPropertyChanged(nameof(FillSelectionTooltip));
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

        var keepGfx = SelectedGfxId;
        BeginEraseStroke();
        EraseCell(cellId, isDrag: false);
        SelectedGfxId = keepGfx;
        Tool = EditorTool.Paint;
        StatusText = $"Retirado GFX {brushId} — sigue activo para pintar";
        return true;
    }

    /// <summary>Starts a paint stroke (MouseDown). No-op for other tools.</summary>
    public void BeginPaintStroke()
    {
        _strokeIsErase = false;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(PaintStrokeName);
    }

    /// <summary>Starts an erase stroke (MouseDown, typically right button).</summary>
    public void BeginEraseStroke()
    {
        _strokeIsErase = true;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(Tool.IsCellModeTool() ? CellModeEraseStrokeName : EraseStrokeName);
    }

    /// <summary>Starts a cell-mode paint stroke (MouseDown).</summary>
    public void BeginCellModeStroke()
    {
        _strokeIsErase = false;
        _lastPaintedCell = -1;
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
        _session?.BeginStroke(CellModeStrokeName);
    }

    /// <summary>Starts a cell-mode erase stroke (MouseDown, right button).</summary>
    public void BeginCellModeEraseStroke()
    {
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
            _strokeIsErase = false;
            _session.BeginStroke(PaintStrokeName);
        }
        else if (Tool == EditorTool.Erase)
        {
            _strokeIsErase = true;
            _session.BeginStroke(EraseStrokeName);
        }
        else if (Tool.IsCellModeTool())
        {
            _strokeIsErase = false;
            _session.BeginStroke(CellModeStrokeName);
        }
    }

    /// <summary>Ends the current stroke as one undo command (MouseUp).</summary>
    public void FinishStroke()
    {
        _lastStrokeContentX = null;
        _lastStrokeContentY = null;
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
        _session.StrokeMutate(cellId, c => MapCellEditor.SetLayerGfx(c, layer, gfxId, flip, rot));
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
                    if (erase)
                    {
                        if (cell.FightCell == 1)
                            MapCellEditor.SetFightCell(cell, 0);
                    }
                    else
                        MapCellEditor.SetFightCell(cell, 1);
                    break;
                case EditorTool.FightCell2:
                    if (erase)
                    {
                        if (cell.FightCell == 2)
                            MapCellEditor.SetFightCell(cell, 0);
                    }
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

    public void HandleCellClick(int cellId, bool isDrag, bool ctrl = false)
    {
        if (CurrentMap is null || _session is null) return;
        if (cellId < 0 || cellId >= CurrentMap.Cells.Count) return;

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
        if (SelectedGfxId is null && Tool is not (EditorTool.Paint or EditorTool.Erase))
            return;

        var layer = PaintLayer switch
        {
            PaintLayer.Ground => "Ground",
            PaintLayer.Object1 => "Capa 1",
            _ => "Capa 2",
        };
        var cell = HoveredCellId?.ToString() ?? "—";
        var gfx = SelectedGfxId is int g ? $"GFX {g}" : "GFX —";
        StatusText = Tool == EditorTool.Erase
            ? $"Map {CurrentMap.Id} | Cell {cell} | {layer} | Borrar"
            : $"Map {CurrentMap.Id} | Cell {cell} | {layer} | {gfx} · Clic: colocar (sigue activo)";
    }

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

        if (World.IsDirty && !World.ConfirmDiscard())
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
            NotifyWorldMapEditedIfOpen();

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
        ResourceWarnings = warnings;
        RenderTimeText = result is null ? "—" : $"{renderMs:F0} ms";
        SelectedMapId = map.Id;
        RequestFitMap?.Invoke();
    }


    /// <summary>
    /// Counts how many selected cells on the active layer would change from findId to replaceId.
    /// Does not mutate.
    /// </summary>
    public int ReplaceGfx(int findId, int replaceId)
    {
        if (CurrentMap is null || SelectedCellIds.Count == 0)
            return 0;

        var count = 0;
        foreach (var id in SelectedCellIds)
        {
            var cell = CurrentMap.Cells[id];
            var current = GetLayerGfx(cell, PaintLayer);
            if (current == findId && findId != replaceId)
                count++;
        }

        return count;
    }

    /// <summary>Applies GFX replace on the selection as one undo command.</summary>
    public int ApplyReplace(int findId, int replaceId)
    {
        if (_session is null || CurrentMap is null || SelectedCellIds.Count == 0)
            return 0;

        var layer = PaintLayer.ToEditorLayer();
        var changed = 0;
        if (_session.Commit("Reemplazar GFX", SelectedCellIds, (_, c) =>
            {
                if (GetLayerGfx(c, PaintLayer) == findId)
                {
                    MapCellEditor.SetLayerGfx(c, layer, replaceId);
                    changed++;
                }
            }))
        {
            AfterHistoryChange();
            _ = RerenderAsync();
        }

        StatusText = changed > 0
            ? $"Reemplazadas {changed} celdas ({findId} → {replaceId})"
            : "Ninguna celda coincidía";
        return changed;
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
        _openDocuments.FirstOrDefault(d => d.MapId == mapId);

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

    public void CloseDocument(OpenMapDocument doc)
    {
        if (doc is null || !_openDocuments.Contains(doc)) return;

        if (ReferenceEquals(_activeDocument, doc))
            FinishStroke();

        _autosave.Delete(doc.Session.DocumentId);

        var wasActive = ReferenceEquals(_activeDocument, doc);
        _openDocuments.Remove(doc);
        DocumentClosed?.Invoke(doc);

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
        if (World.IsMultiMapEditMode)
        {
            if (MultiMap.Undo())
            {
                UndoCommand.RaiseCanExecuteChanged();
                RedoCommand.RaiseCanExecuteChanged();
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

    private void Redo()
    {
        if (World.IsMultiMapEditMode)
        {
            if (MultiMap.Redo())
            {
                UndoCommand.RaiseCanExecuteChanged();
                RedoCommand.RaiseCanExecuteChanged();
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

    private void CopySelection()
    {
        if (_session is null || !HasSelection) return;
        _session.CopySelection();
        PasteCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        StatusText = $"Copiadas {SelectedCellIds.Count} celdas";
    }

    private void PasteSelection()
    {
        if (_session is null) return;
        var dest = PrimarySelectedCellId ?? HoveredCellId;
        if (dest is null)
        {
            StatusText = "Selecciona una celda destino para pegar.";
            return;
        }

        var (pasted, skipped) = _session.PasteAt(dest.Value);
        AfterHistoryChange();
        SyncSelectionFromSession();
        PrimarySelectedCellId = SelectedCellIds.Count > 0 ? SelectedCellIds[^1] : dest;
        _ = RerenderAsync();

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

    private void ApplyBrushToSelection()
    {
        if (_session is null || SelectedGfxId is not int gfxId || !HasSelection) return;
        if (!ValidateSelectedGfxForActiveLayer(out var error))
        {
            StatusText = error;
            return;
        }

        var layer = PaintLayer.ToEditorLayer();
        var rot = PaintLayer == PaintLayer.Object2 ? (int?)null : BrushRotation;
        var flip = BrushFlip;
        var count = SelectedCellIds.Count;
        if (_session.Commit("Rellenar selección", SelectedCellIds,
                (_, c) => MapCellEditor.SetLayerGfx(c, layer, gfxId, flip, rot)))
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
    }

    private void OpenMapDialog()
    {
        if (!HasLibrary || MapIds.Count == 0)
        {
            MessageBox.Show("No hay mapas descubiertos.", "RUFUS Map Editor");
            return;
        }

        var pick = new MapPickerWindow(_library, _mapPreviews, MapIds, SelectedMapId, "Abrir mapa") { Owner = Application.Current.MainWindow };
        if (pick.ShowDialog() == true && pick.SelectedMapId is int id)
        {
            SelectedMapId = id;
            _ = LoadMapAsync(id);
        }
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

        if (GetLayerGfx(CurrentMap.Cells[cellId], paintLayer) == 0)
            return;

        HighlightedInspectorLayer = layer;
        OnPropertyChanged(nameof(IsInspectorGroundHighlighted));
        OnPropertyChanged(nameof(IsInspectorObject1Highlighted));
        OnPropertyChanged(nameof(IsInspectorObject2Highlighted));
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
        MapListItems.Clear();
        foreach (var id in MapIds)
            MapListItems.Add(new MapPickerItemVm(id));
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
        OnPropertyChanged(nameof(FillSelectionTooltip));
        OnPropertyChanged(nameof(ClearActiveLayerInSelectionTooltip));
        OnPropertyChanged(nameof(SelectionSummaryLabel));
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

        RefreshCellInspector();
        RaiseLocateCommands();
        MapMonsters.NotifyMapOrSelectionChanged();
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
        MultiMap.BeginStroke(Tool, PaintLayer);
        MultiMap.ResetStrokePointer();
    }

    public void FinishMultiMapStroke()
    {
        MultiMap.FinishStroke();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        World.SaveModifiedMapsCommand.RaiseCanExecuteChanged();
    }

    public void ContinueMultiMapStroke(double worldX, double worldY) =>
        MultiMap.ContinueStroke(
            worldX,
            worldY,
            Tool,
            PaintLayer,
            SelectedGfxId,
            BrushFlip,
            BrushRotation,
            mosaicMode: true);

    public void HandleMultiMapCellClick(WorldCellRef cell, bool isDrag, bool ctrl) =>
        MultiMap.HandleCellClick(
            cell,
            Tool,
            PaintLayer,
            SelectedGfxId,
            BrushFlip,
            BrushRotation,
            isDrag,
            ctrl,
            gfx => ApplyMultiMapEyedropper(cell, gfx));

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

    public void EndMultiMapRectSelect(double wx, double wy) =>
        MultiMap.EndRectSelect(wx, wy, mosaicMode: true);

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
        if (!World.IsMultiMapEditMode) return false;
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
        _autosaveTimer.Stop();
        _gfxSearchDebounce.Stop();
        Logs.Dispose();
        _overlayCache.Dispose();
        _mapPreviews.Clear();
        _library.Dispose();
    }
}
