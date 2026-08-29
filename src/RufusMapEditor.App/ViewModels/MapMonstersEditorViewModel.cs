using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// LIB.4.5 — Mapas panel «MONSTRUOS DEL MAPA»: población natural (<c>mapas.mobs</c>).
/// No Cell ID · no mobs_fix · no escritura BD en esta fase.
/// Fixed-spawn code lives in <see cref="MapFixedMobsEditorViewModel"/> (isolated / inactive).
/// </summary>
public sealed class MapMonstersEditorViewModel : ViewModelBase
{
    private readonly Func<int?> _getMapId;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private MonsterSearchResultVm? _selectedSearchResult;
    private MapNaturalMobItemVm? _selectedMapMob;
    private string _searchQuery = "";
    private string _catalogStatus = "Catálogo monstruos: —";
    private string _dbStatus = "BD mapas: —";
    private string _clipsWarning = "";
    private string _contextStatus = "Mapa: —";
    private string _loadStatus = "";
    private string _rawMobs = "";
    private string _maxGrupoMobs = "—";
    private string _maxMobsPorGrupo = "—";
    private string _minMobsPorGrupo = "—";
    private string _minNivelGrupoMob = "—";
    private string _maxNivelGrupoMob = "—";
    private bool _isBusy;
    private int? _loadedMapId;
    private bool _panelExpanded = true;
    private bool _hasUnrecognizedTokens;

    public MapMonstersEditorViewModel(Func<int?>? getMapId = null, Func<int?>? getCellCount = null, Action? onFixedMobsChanged = null)
    {
        // getCellCount / onFixedMobsChanged kept for MainViewModel ctor compatibility; unused (no mobs_fix UI).
        _ = getCellCount;
        _ = onFixedMobsChanged;
        _getMapId = getMapId ?? (() => null);

        MapMobs = new ObservableCollection<MapNaturalMobItemVm>();
        SearchResults = new ObservableCollection<MonsterSearchResultVm>();
        FixedMobCellIds = new HashSet<int>();

        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RefreshSearchResults();
        };

        EnsureCatalogCommand = new RelayCommand(async () => await EnsureCatalogAsync(refreshDb: true), () => !IsBusy);
        AddSelectedCommand = new RelayCommand(AddSelectedToMap,
            () => !IsBusy && SelectedSearchResult is not null);
        OpenPickerCommand = new RelayCommand(async () => await SearchAndAddViaDialogAsync(), () => !IsBusy);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedMapMob is not null && !IsBusy);
        ReloadMapMobsCommand = new RelayCommand(async () => await LoadNaturalMobsForCurrentMapAsync(), () => !IsBusy);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => MoveSelected(1), () => CanMove(1));
    }

    /// <summary>Raised when UI should expand/focus the panel.</summary>
    public event Action? RequestFocusPanel;

    public void FocusPanel()
    {
        PanelExpanded = true;
        RequestFocusPanel?.Invoke();
    }

    public ObservableCollection<MapNaturalMobItemVm> MapMobs { get; }
    public ObservableCollection<MonsterSearchResultVm> SearchResults { get; }

    /// <summary>Always empty in LIB.4.5 — mobs_fix markers disabled in normal flow.</summary>
    public HashSet<int> FixedMobCellIds { get; private set; }

    /// <summary>Unused in natural flow (compat for MapViewport overlay).</summary>
    public int? MobTargetCellId => null;

    public string PublishPendingBanner { get; } =
        "⚠ Publicación de monstruos naturales pendiente de validación";

    public string PublishPendingDetail { get; } =
        "Puedes buscar, añadir y quitar monstruos en la configuración local. " +
        "No se escribe mapas.mobs ni mobs_fix en esta fase.";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? ""))
                return;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }
    }

    public MonsterSearchResultVm? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (SetProperty(ref _selectedSearchResult, value))
                AddSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    public MapNaturalMobItemVm? SelectedMapMob
    {
        get => _selectedMapMob;
        set
        {
            if (SetProperty(ref _selectedMapMob, value))
            {
                RemoveSelectedCommand.RaiseCanExecuteChanged();
                MoveUpCommand.RaiseCanExecuteChanged();
                MoveDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool PanelExpanded
    {
        get => _panelExpanded;
        set => SetProperty(ref _panelExpanded, value);
    }

    public string CatalogStatus
    {
        get => _catalogStatus;
        private set => SetProperty(ref _catalogStatus, value);
    }

    public string DbStatus
    {
        get => _dbStatus;
        private set => SetProperty(ref _dbStatus, value);
    }

    public string ClipsWarning
    {
        get => _clipsWarning;
        private set => SetProperty(ref _clipsWarning, value);
    }

    public string ContextStatus
    {
        get => _contextStatus;
        private set => SetProperty(ref _contextStatus, value);
    }

    public string LoadStatus
    {
        get => _loadStatus;
        private set => SetProperty(ref _loadStatus, value);
    }

    public string RawMobs
    {
        get => _rawMobs;
        private set => SetProperty(ref _rawMobs, value);
    }

    public string MaxGrupoMobs
    {
        get => _maxGrupoMobs;
        private set => SetProperty(ref _maxGrupoMobs, value);
    }

    public string MaxMobsPorGrupo
    {
        get => _maxMobsPorGrupo;
        private set => SetProperty(ref _maxMobsPorGrupo, value);
    }

    public string MinMobsPorGrupo
    {
        get => _minMobsPorGrupo;
        private set => SetProperty(ref _minMobsPorGrupo, value);
    }

    public string MinNivelGrupoMob
    {
        get => _minNivelGrupoMob;
        private set => SetProperty(ref _minNivelGrupoMob, value);
    }

    public string MaxNivelGrupoMob
    {
        get => _maxNivelGrupoMob;
        private set => SetProperty(ref _maxNivelGrupoMob, value);
    }

    public bool HasUnrecognizedTokens
    {
        get => _hasUnrecognizedTokens;
        private set => SetProperty(ref _hasUnrecognizedTokens, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                EnsureCatalogCommand.RaiseCanExecuteChanged();
                AddSelectedCommand.RaiseCanExecuteChanged();
                OpenPickerCommand.RaiseCanExecuteChanged();
                RemoveSelectedCommand.RaiseCanExecuteChanged();
                ReloadMapMobsCommand.RaiseCanExecuteChanged();
                MoveUpCommand.RaiseCanExecuteChanged();
                MoveDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MapMobsHeader => $"MONSTRUOS DISPONIBLES EN ESTE MAPA ({MapMobs.Count})";

    public string SearchResultsHeader
    {
        get
        {
            if (!VisualLibraryService.Shared.MonstersLoaded)
                return "Catálogo: no cargado";
            var total = VisualLibraryService.Shared.Monsters.Count;
            return $"Mostrando {SearchResults.Count} de {total}";
        }
    }

    public string LocalPreviewLine =>
        "Vista local (no publicada): " +
        (MapMobs.Count == 0
            ? "(vacío)"
            : MapasMobsNaturalParser.BuildSimple(MapMobs.Select(m => m.MobId)));

    public RelayCommand EnsureCatalogCommand { get; }
    public RelayCommand AddSelectedCommand { get; }
    public RelayCommand OpenPickerCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ReloadMapMobsCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    /// <summary>No-op: Cell ID selection removed from natural-spawn flow (LIB.4.5).</summary>
    public void SetMobTargetCell(int? cellId) => _ = cellId;

    public void NotifyMapOrSelectionChanged()
    {
        RefreshContextStatus();
        var mapId = _getMapId();
        if (mapId != _loadedMapId)
            _ = LoadNaturalMobsForCurrentMapAsync();
    }

    public void RefreshContextStatus()
    {
        var mapId = _getMapId();
        ContextStatus = mapId is int m
            ? $"Mapa: {m} · Monstruos naturales (mapas.mobs)"
            : "Mapa: — · Abre un mapa para ver / editar la población natural";
        RefreshClipsWarning();
        OnPropertyChanged(nameof(LocalPreviewLine));
        OnPropertyChanged(nameof(MapMobsHeader));
    }

    public async Task EnsureCatalogAsync(bool refreshDb = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await VisualLibraryBootstrap.EnsureMonstersAsync().ConfigureAwait(true);
            CatalogStatus = VisualLibraryService.Shared.MonstersLoaded
                ? "Catálogo monstruos: ✓ Cargado"
                : "Catálogo monstruos: ERROR · " + VisualLibraryService.Shared.StatusMonsters;
            RefreshClipsWarning();
            RefreshSearchResults();
            if (refreshDb)
                await RefreshDbStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CatalogStatus = "Catálogo monstruos: ERROR · " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshDbStatusAsync()
    {
        if (!TryCreateMapasRepository(out var repo, out var err))
        {
            DbStatus = "BD mapas: ERROR · " + (err ?? "sin config");
            return;
        }

        try
        {
            await repo!.TestConnectionAsync().ConfigureAwait(true);
            DbStatus = "BD mapas: ✓ Disponible (solo lectura)";
        }
        catch (Exception ex)
        {
            DbStatus = "BD mapas: ERROR · " + ex.Message;
        }
    }

    public async Task LoadNaturalMobsForCurrentMapAsync()
    {
        var mapId = _getMapId();
        if (mapId is null)
        {
            ClearMapState();
            _loadedMapId = null;
            LoadStatus = "Sin mapa abierto.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!TryCreateMapasRepository(out var repo, out var err))
            {
                LoadStatus = err ?? "BD no configurada.";
                DbStatus = "BD mapas: ERROR · " + (err ?? "sin config");
                ClearMapState();
                _loadedMapId = mapId;
                return;
            }

            await repo!.TestConnectionAsync().ConfigureAwait(true);
            DbStatus = "BD mapas: ✓ Disponible (solo lectura)";

            var row = await repo.TryGetAsync(mapId.Value).ConfigureAwait(true);
            _loadedMapId = mapId;

            if (row is null)
            {
                ClearMapState();
                LoadStatus = $"Mapa {mapId} no encontrado en BD (solo lectura). Lista local vacía.";
                return;
            }

            ApplyRowReadOnly(row);
        }
        catch (Exception ex)
        {
            LoadStatus = "Error leyendo mapas.mobs: " + ex.Message;
            DbStatus = "BD mapas: ERROR · " + ex.Message;
            ClearMapState();
        }
        finally
        {
            IsBusy = false;
            RefreshContextStatus();
        }
    }

    /// <summary>Compat: expander still may call this name — redirects to natural load.</summary>
    public Task LoadFixedMobsForCurrentMapAsync() => LoadNaturalMobsForCurrentMapAsync();

    private void ApplyRowReadOnly(MapasRow row)
    {
        RawMobs = row.Mobs ?? "";
        MaxGrupoMobs = FormatInt(row.MaxGrupoMobs);
        MaxMobsPorGrupo = FormatInt(row.MaxMobsPorGrupo);
        MinMobsPorGrupo = FormatInt(row.MinMobsPorGrupo);
        MinNivelGrupoMob = FormatInt(row.MinNivelGrupoMob);
        MaxNivelGrupoMob = FormatInt(row.MaxNivelGrupoMob);

        MapMobs.Clear();
        SelectedMapMob = null;
        HasUnrecognizedTokens = false;

        var tokens = MapasMobsNaturalParser.Parse(row.Mobs);
        if (tokens.Count == 0)
        {
            LoadStatus = string.IsNullOrWhiteSpace(row.Mobs)
                ? $"Mapa {row.Id}: mapas.mobs vacío."
                : $"Mapa {row.Id}: mapas.mobs no parseable · raw conservado.";
            if (!string.IsNullOrWhiteSpace(row.Mobs))
                HasUnrecognizedTokens = true;
            OnPropertyChanged(nameof(MapMobsHeader));
            OnPropertyChanged(nameof(LocalPreviewLine));
            return;
        }

        var seen = new HashSet<int>();
        foreach (var t in tokens)
        {
            if (t.MobId <= 0)
            {
                HasUnrecognizedTokens = true;
                continue;
            }

            // Server skips duplicates when loading; keep first for display.
            if (!seen.Add(t.MobId))
                continue;

            var entry = VisualLibraryService.Shared.GetMonster(t.MobId);
            MapMobs.Add(MapNaturalMobItemVm.FromCatalogOrRaw(t.MobId, entry, t));
        }

        LoadStatus = HasUnrecognizedTokens
            ? $"Mapa {row.Id}: {MapMobs.Count} monstruo(s) reconocidos · hay tokens no estándar (ver raw)."
            : $"Mapa {row.Id}: {MapMobs.Count} monstruo(s) desde mapas.mobs (solo lectura).";
        OnPropertyChanged(nameof(MapMobsHeader));
        OnPropertyChanged(nameof(LocalPreviewLine));
    }

    private void ClearMapState()
    {
        MapMobs.Clear();
        SelectedMapMob = null;
        RawMobs = "";
        MaxGrupoMobs = "—";
        MaxMobsPorGrupo = "—";
        MinMobsPorGrupo = "—";
        MinNivelGrupoMob = "—";
        MaxNivelGrupoMob = "—";
        HasUnrecognizedTokens = false;
        OnPropertyChanged(nameof(MapMobsHeader));
        OnPropertyChanged(nameof(LocalPreviewLine));
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        if (!VisualLibraryService.Shared.MonstersLoaded)
        {
            OnPropertyChanged(nameof(SearchResultsHeader));
            return;
        }

        foreach (var m in VisualLibraryService.Shared.SearchMonsters(_searchQuery))
            SearchResults.Add(new MonsterSearchResultVm(m));
        OnPropertyChanged(nameof(SearchResultsHeader));
    }

    private void AddSelectedToMap()
    {
        if (SelectedSearchResult is null) return;
        AddMob(SelectedSearchResult.Entry);
    }

    private async Task SearchAndAddViaDialogAsync()
    {
        await EnsureCatalogAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.MonstersLoaded)
        {
            MessageBox.Show(
                "No se pudo cargar el catálogo de monstruos.\nComprueba BD (mobs_modelo) y LANG/SFTP.",
                "Monstruos del mapa");
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dlg = new MonsterPickerWindow(VisualLibraryService.Shared, _searchQuery) { Owner = owner };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;

        AddMob(dlg.SelectedEntry);
    }

    public void AddMob(MonsterCatalogEntry entry)
    {
        if (MapMobs.Any(m => m.MobId == entry.Id))
        {
            LoadStatus = $"Mob ID {entry.Id} ya está en la lista local.";
            return;
        }

        MapMobs.Add(MapNaturalMobItemVm.FromCatalog(entry));
        OnPropertyChanged(nameof(MapMobsHeader));
        OnPropertyChanged(nameof(LocalPreviewLine));
        LoadStatus = "Añadido en configuración local (no publicado).";
    }

    private void RemoveSelected()
    {
        if (SelectedMapMob is null) return;
        MapMobs.Remove(SelectedMapMob);
        SelectedMapMob = null;
        OnPropertyChanged(nameof(MapMobsHeader));
        OnPropertyChanged(nameof(LocalPreviewLine));
        LoadStatus = "Quitado de la configuración local (no publicado).";
    }

    private bool CanMove(int delta)
    {
        if (SelectedMapMob is null || IsBusy) return false;
        var i = MapMobs.IndexOf(SelectedMapMob);
        var j = i + delta;
        return i >= 0 && j >= 0 && j < MapMobs.Count;
    }

    private void MoveSelected(int delta)
    {
        if (SelectedMapMob is null) return;
        var i = MapMobs.IndexOf(SelectedMapMob);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= MapMobs.Count) return;
        MapMobs.Move(i, j);
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(LocalPreviewLine));
    }

    private void RefreshClipsWarning()
    {
        ArtworkPreviewService.Shared.RefreshClipsStatus();
        ClipsWarning = ArtworkPreviewService.Shared.ClipsStatus;
    }

    private static string FormatInt(int? v) =>
        v is int i ? i.ToString(CultureInfo.InvariantCulture) : "—";

    private static bool TryCreateMapasRepository(out IMapasRepository? repo, out string? error)
    {
        repo = null;
        error = null;
        var settings = AppSettingsStore.Load();
        settings.Database ??= new DatabaseSettings();
        var db = settings.Database;
        if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.User))
        {
            error = "Configura primero la conexión MySQL (Archivo → Configuración BD…).";
            return false;
        }

        string password;
        try
        {
            password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            error = "No se pudo descifrar la contraseña: " + ex.Message;
            return false;
        }

        repo = new MysqlMapasRepository(db, password);
        return true;
    }
}

public sealed class MapNaturalMobItemVm
{
    private MapNaturalMobItemVm(int mobId, string nombre, int gfxId, string levelsLine, string? note)
    {
        MobId = mobId;
        Nombre = nombre;
        GfxId = gfxId;
        LevelsLine = levelsLine;
        Note = note ?? "";
        MobIdLine = $"Mob ID: {mobId}";
        GfxLine = $"GFX: {gfxId}";
        Title = $"{nombre}";
    }

    public int MobId { get; }
    public string Nombre { get; }
    public int GfxId { get; }
    public string Title { get; }
    public string MobIdLine { get; }
    public string GfxLine { get; }
    public string LevelsLine { get; }
    public string Note { get; }

    public static MapNaturalMobItemVm FromCatalog(MonsterCatalogEntry entry) =>
        new(entry.Id, entry.Nombre, entry.GfxId, $"Niveles: {entry.LevelsDisplay}", null);

    public static MapNaturalMobItemVm FromCatalogOrRaw(
        int mobId,
        MonsterCatalogEntry? entry,
        MapasMobsNaturalParser.Token token)
    {
        if (entry is not null)
        {
            var note = token.HasExtendedFields
                ? $"Token BD: {token.Raw}"
                : null;
            return new MapNaturalMobItemVm(entry.Id, entry.Nombre, entry.GfxId,
                $"Niveles: {entry.LevelsDisplay}", note);
        }

        return new MapNaturalMobItemVm(
            mobId,
            $"Mob {mobId} (no en catálogo)",
            0,
            "Niveles: —",
            $"Token BD: {token.Raw}");
    }
}

public sealed class MonsterSearchResultVm
{
    public MonsterSearchResultVm(MonsterCatalogEntry entry)
    {
        Entry = entry;
        Nombre = entry.Nombre;
        MobIdLine = $"Mob ID: {entry.Id}";
        GfxLine = $"GFX: {entry.GfxId}";
        LevelsLine = $"Niveles: {entry.LevelsDisplay}";
        FileLine = entry.ArtworkExists
            ? $"SWF: {entry.ArtworkRelativePath}"
            : $"SWF: {entry.ArtworkRelativePath} (ausente)";
        GfxId = entry.GfxId;
    }

    public MonsterCatalogEntry Entry { get; }
    public int GfxId { get; }
    public string Nombre { get; }
    public string MobIdLine { get; }
    public string GfxLine { get; }
    public string LevelsLine { get; }
    public string FileLine { get; }
}
