using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// LIB.4 / LIB.4.5 — Isolated fixed-spawn (<c>mobs_fix</c>) editor. Not wired to the main Mapas UI.
/// Future advanced feature: «Grupos fijos».
/// </summary>
public sealed class MapFixedMobsEditorViewModel : ViewModelBase
{
    public const int MaxSlots = MapMonsterGroupLimits.MaxSlots;

    private readonly Func<int?> _getMapId;
    private readonly Func<int?> _getCellCount;
    private readonly Action? _onFixedMobsChanged;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private MapMonsterSlotVm? _selectedSlot;
    private MobsFixExistingGroupVm? _selectedExisting;
    private MonsterSearchResultVm? _selectedSearchResult;
    private string _searchQuery = "";
    private string _catalogStatus = "Catálogo monstruos: —";
    private string _dbStatus = "BD mobs_fix: —";
    private string _clipsWarning = "";
    private string _contextStatus = "Celda seleccionada: (elige herramienta MOBS y pulsa una celda)";
    private string _loadStatus = "";
    private string _publishStatus = "";
    private int _spawnTipo = -1;
    private string _condiciones = "";
    private string _descripcion = "";
    private string _segundosRespawn = "0";
    private bool _isBusy;
    private int? _loadedMapId;
    private int? _mobTargetCellId;
    private bool _suppressCellSync;
    private bool _panelExpanded = true;

    public MapFixedMobsEditorViewModel(
        Func<int?>? getMapId = null,
        Func<int?>? getCellCount = null,
        Action? onFixedMobsChanged = null)
    {
        _getMapId = getMapId ?? (() => null);
        _getCellCount = getCellCount ?? (() => null);
        _onFixedMobsChanged = onFixedMobsChanged;

        Slots = new ObservableCollection<MapMonsterSlotVm>();
        SearchResults = new ObservableCollection<MonsterSearchResultVm>();
        ExistingGroups = new ObservableCollection<MobsFixExistingGroupVm>();
        FixedMobCellIds = new HashSet<int>();

        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RefreshSearchResults();
        };

        EnsureCatalogCommand = new RelayCommand(async () => await EnsureCatalogAsync(refreshDb: true), () => !IsBusy);
        AddSelectedToGroupCommand = new RelayCommand(AddSelectedToGroup,
            () => !IsBusy && SelectedSearchResult is not null && Slots.Count < MaxSlots);
        OpenPickerCommand = new RelayCommand(async () => await SearchAndAddViaDialogAsync(),
            () => !IsBusy && Slots.Count < MaxSlots);
        RemoveSlotCommand = new RelayCommand(RemoveSelected, () => SelectedSlot is not null && !IsBusy);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => MoveSelected(1), () => CanMove(1));
        ReloadFixedMobsCommand = new RelayCommand(async () => await LoadFixedMobsForCurrentMapAsync(), () => !IsBusy);
        SaveToSqlCommand = new RelayCommand(async () => await SaveToSqlAsync(), () => !IsBusy);
        ClearMobCellCommand = new RelayCommand(() => SetMobTargetCell(null), () => MobTargetCellId is not null);
        ApplyImmediatelyCommand = new RelayCommand(() => { }, () => false);
    }

    /// <summary>Raised when UI should expand/focus the MONSTRUOS panel (e.g. click on M mark).</summary>
    public event Action? RequestFocusPanel;

    public void FocusPanel()
    {
        PanelExpanded = true;
        RequestFocusPanel?.Invoke();
    }

    public ObservableCollection<MapMonsterSlotVm> Slots { get; }
    public ObservableCollection<MonsterSearchResultVm> SearchResults { get; }
    public ObservableCollection<MobsFixExistingGroupVm> ExistingGroups { get; }
    public HashSet<int> FixedMobCellIds { get; private set; }

    public string SpawnPublishBanner { get; } =
        "Persistencia: REPLACE → mobs_fix. Sin spawn runtime hasta recarga del servidor.";

    public string ApplyImmediatelyHint { get; } =
        "[ Aplicar inmediatamente ] — pendiente (canal seguro hacia game server).";

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
                AddSelectedToGroupCommand.RaiseCanExecuteChanged();
        }
    }

    public int? MobTargetCellId
    {
        get => _mobTargetCellId;
        private set
        {
            if (!SetProperty(ref _mobTargetCellId, value))
                return;
            RefreshContextStatus();
            ClearMobCellCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(MobCellLabel));
            _onFixedMobsChanged?.Invoke();
        }
    }

    public string MobCellLabel =>
        MobTargetCellId is int c ? $"Celda seleccionada: {c}" : "Celda seleccionada: (ninguna)";

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

    public string PublishStatus
    {
        get => _publishStatus;
        private set => SetProperty(ref _publishStatus, value);
    }

    public MapMonsterSlotVm? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (SetProperty(ref _selectedSlot, value))
            {
                RemoveSlotCommand.RaiseCanExecuteChanged();
                MoveUpCommand.RaiseCanExecuteChanged();
                MoveDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public MobsFixExistingGroupVm? SelectedExisting
    {
        get => _selectedExisting;
        set
        {
            if (!SetProperty(ref _selectedExisting, value) || value is null || _suppressCellSync)
                return;
            if (_mobTargetCellId != value.Celda)
            {
                _mobTargetCellId = value.Celda;
                OnPropertyChanged(nameof(MobTargetCellId));
                OnPropertyChanged(nameof(MobCellLabel));
                RefreshContextStatus();
                ClearMobCellCommand.RaiseCanExecuteChanged();
                _onFixedMobsChanged?.Invoke();
            }

            LoadGroupIntoEditor(value.Row);
            FocusPanel();
        }
    }

    public bool IsTipoFijo
    {
        get => _spawnTipo == -1;
        set { if (value) SetTipo(-1); }
    }

    public bool IsTipoNormal
    {
        get => _spawnTipo == 0;
        set { if (value) SetTipo(0); }
    }

    public bool IsTipoUnaPelea
    {
        get => _spawnTipo == 1;
        set { if (value) SetTipo(1); }
    }

    public bool IsTipoHastaMorir
    {
        get => _spawnTipo == 2;
        set { if (value) SetTipo(2); }
    }

    public string Condiciones
    {
        get => _condiciones;
        set => SetProperty(ref _condiciones, value);
    }

    public string Descripcion
    {
        get => _descripcion;
        set => SetProperty(ref _descripcion, value);
    }

    public string SegundosRespawn
    {
        get => _segundosRespawn;
        set => SetProperty(ref _segundosRespawn, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                EnsureCatalogCommand.RaiseCanExecuteChanged();
                AddSelectedToGroupCommand.RaiseCanExecuteChanged();
                OpenPickerCommand.RaiseCanExecuteChanged();
                RemoveSlotCommand.RaiseCanExecuteChanged();
                ReloadFixedMobsCommand.RaiseCanExecuteChanged();
                SaveToSqlCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SlotsHeader => $"Grupo ({Slots.Count}/{MaxSlots})";
    public string ExistingHeader => $"Grupos persistentes en mapa ({ExistingGroups.Count})";
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

    public RelayCommand EnsureCatalogCommand { get; }
    public RelayCommand AddSelectedToGroupCommand { get; }
    public RelayCommand OpenPickerCommand { get; }
    public RelayCommand RemoveSlotCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand ReloadFixedMobsCommand { get; }
    public RelayCommand SaveToSqlCommand { get; }
    public RelayCommand ClearMobCellCommand { get; }
    public RelayCommand ApplyImmediatelyCommand { get; }

    public void SetMobTargetCell(int? cellId)
    {
        MobTargetCellId = cellId;
        if (cellId is int c)
            TryLoadExistingAtCell(c, focusPanel: true);
    }

    public void NotifyMapOrSelectionChanged()
    {
        RefreshContextStatus();
        var mapId = _getMapId();
        if (mapId != _loadedMapId)
            _ = LoadFixedMobsForCurrentMapAsync();
    }

    public void RefreshContextStatus()
    {
        var mapId = _getMapId();
        ContextStatus = mapId is int m
            ? MobTargetCellId is int c
                ? $"Mapa: {m} · Celda seleccionada: {c}"
                : $"Mapa: {m} · Celda seleccionada: (herramienta MOBS → clic en celda)"
            : "Mapa: — · Celda seleccionada: —";
        RefreshClipsWarning();
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
        if (!TryCreateRepository(out var repo, out var err))
        {
            DbStatus = "BD mobs_fix: ERROR · " + (err ?? "sin config");
            return;
        }

        try
        {
            await repo!.PingAsync().ConfigureAwait(true);
            await repo.ValidateSchemaAsync().ConfigureAwait(true);
            DbStatus = "BD mobs_fix: ✓ Disponible";
        }
        catch (Exception ex)
        {
            DbStatus = "BD mobs_fix: ERROR · " + ex.Message;
        }
    }

    public async Task LoadFixedMobsForCurrentMapAsync()
    {
        var mapId = _getMapId();
        if (mapId is null)
        {
            ClearExisting();
            _loadedMapId = null;
            LoadStatus = "Sin mapa abierto.";
            _onFixedMobsChanged?.Invoke();
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!TryCreateRepository(out var repo, out var err))
            {
                LoadStatus = err ?? "BD no configurada.";
                DbStatus = "BD mobs_fix: ERROR · " + (err ?? "sin config");
                ClearExisting();
                _loadedMapId = mapId;
                _onFixedMobsChanged?.Invoke();
                return;
            }

            await repo!.PingAsync().ConfigureAwait(true);
            DbStatus = "BD mobs_fix: ✓ Disponible";

            var rows = await repo.GetByMapaAsync(mapId.Value).ConfigureAwait(true);
            ExistingGroups.Clear();
            FixedMobCellIds = new HashSet<int>();
            foreach (var row in rows)
            {
                ExistingGroups.Add(new MobsFixExistingGroupVm(row));
                FixedMobCellIds.Add(row.Celda);
            }

            _loadedMapId = mapId;
            LoadStatus = rows.Count == 0
                ? $"Sin grupos fijos en mapa {mapId}."
                : $"Cargados {rows.Count} grupo(s) desde mobs_fix.";
            OnPropertyChanged(nameof(ExistingHeader));
            OnPropertyChanged(nameof(FixedMobCellIds));
            if (MobTargetCellId is int cell)
                TryLoadExistingAtCell(cell, focusPanel: false);
            _onFixedMobsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LoadStatus = "Error cargando mobs_fix: " + ex.Message;
            DbStatus = "BD mobs_fix: ERROR · " + ex.Message;
            ClearExisting();
            _onFixedMobsChanged?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
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

    private void AddSelectedToGroup()
    {
        if (SelectedSearchResult is null) return;
        AddSlot(SelectedSearchResult.Entry);
    }

    private async Task SearchAndAddViaDialogAsync()
    {
        if (Slots.Count >= MaxSlots)
        {
            MessageBox.Show("Máximo 8 monstruos en el grupo.", "MONSTRUOS");
            return;
        }

        await EnsureCatalogAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.MonstersLoaded)
        {
            MessageBox.Show(
                "No se pudo cargar el catálogo de monstruos.\nComprueba BD (mobs_modelo) y LANG/SFTP.",
                "MONSTRUOS");
            return;
        }

        var owner = Application.Current?.MainWindow;
        var dlg = new MonsterPickerWindow(VisualLibraryService.Shared, _searchQuery) { Owner = owner };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;

        AddSlot(dlg.SelectedEntry);
    }

    public void AddSlot(MonsterCatalogEntry entry)
    {
        if (Slots.Count >= MaxSlots)
            return;

        var min = entry.Levels.Count > 0 ? entry.Levels[0] : 0;
        var max = entry.Levels.Count > 0 ? entry.Levels[^1] : 0;
        Slots.Add(new MapMonsterSlotVm(Slots.Count + 1, entry.Id, entry.Nombre, entry.GfxId, min, max));
        RenumberSlots();
        OnPropertyChanged(nameof(SlotsHeader));
        AddSelectedToGroupCommand.RaiseCanExecuteChanged();
        OpenPickerCommand.RaiseCanExecuteChanged();
    }

    private void RemoveSelected()
    {
        if (SelectedSlot is null) return;
        Slots.Remove(SelectedSlot);
        SelectedSlot = null;
        RenumberSlots();
        OnPropertyChanged(nameof(SlotsHeader));
        AddSelectedToGroupCommand.RaiseCanExecuteChanged();
        OpenPickerCommand.RaiseCanExecuteChanged();
    }

    private void RenumberSlots()
    {
        for (var i = 0; i < Slots.Count; i++)
            Slots[i].SlotNumber = i + 1;
    }

    private bool CanMove(int delta)
    {
        if (SelectedSlot is null || IsBusy) return false;
        var i = Slots.IndexOf(SelectedSlot);
        var j = i + delta;
        return i >= 0 && j >= 0 && j < Slots.Count;
    }

    private void MoveSelected(int delta)
    {
        if (SelectedSlot is null) return;
        var i = Slots.IndexOf(SelectedSlot);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Slots.Count) return;
        Slots.Move(i, j);
        RenumberSlots();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveToSqlAsync()
    {
        if (IsBusy) return;
        RefreshContextStatus();

        await EnsureCatalogAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.MonstersLoaded)
        {
            MessageBox.Show("Catálogo de monstruos no disponible.", "Guardar mobs en SQL");
            return;
        }

        if (!TryCreateRepository(out var repo, out var cfgErr))
        {
            MessageBox.Show(cfgErr ?? "BD no configurada.", "Guardar mobs en SQL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mapId = _getMapId();
        var cellId = MobTargetCellId;
        var cellCount = _getCellCount();

        MobsFixRow? existing = null;
        try
        {
            IsBusy = true;
            await repo!.PingAsync().ConfigureAwait(true);
            await repo.ValidateSchemaAsync().ConfigureAwait(true);
            if (mapId is int m && cellId is int c)
                existing = await repo.GetByMapaCeldaAsync(m, c).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Validación BD abortada:\n" + ex.Message, "Guardar mobs en SQL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var drafts = SyncSlotsToDrafts();
        var validation = MobsFixValidator.Validate(
            mapId,
            cellId,
            cellCount,
            drafts,
            _spawnTipo,
            Condiciones,
            SegundosRespawn,
            Descripcion,
            id => VisualLibraryService.Shared.GetMonster(id) is not null,
            existing);

        if (!validation.Ok || validation.Request is null)
        {
            MessageBox.Show(validation.Error ?? "Validación fallida.", "Guardar mobs en SQL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PublishStatus = "ABORTADO: " + (validation.Error ?? "validación");
            return;
        }

        var preview = MobsFixPublishService.BuildPreviewText(validation.Request);
        var owner = Application.Current?.MainWindow;
        var confirm = new MobsFixConfirmWindow(preview) { Owner = owner };
        if (confirm.ShowDialog() != true || !confirm.Confirmed)
        {
            PublishStatus = "Cancelado.";
            return;
        }

        IsBusy = true;
        PublishStatus = "Guardando…";
        try
        {
            var service = new MobsFixPublishService(repo!);
            var result = await service.PublishAsync(validation.Request).ConfigureAwait(true);
            if (!result.Ok)
            {
                PublishStatus = "ERROR: " + result.Error;
                MessageBox.Show(result.Error ?? "Error al guardar.", "Guardar mobs en SQL",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            PublishStatus = "✓ Mobs guardados en SQL";
            MessageBox.Show(
                "✓ Mobs guardados en SQL\n\n" +
                $"mapa={result.VerifiedRow!.Mapa} celda={result.VerifiedRow.Celda}\n" +
                $"mobs={result.VerifiedRow.Mobs}\n" +
                $"tipo={result.VerifiedRow.Tipo}\n" +
                $"Sala={result.VerifiedRow.Sala} movible={result.VerifiedRow.Movible} " +
                $"oleadas={result.VerifiedRow.Oleadas} id=NULL\n\n" +
                "No aparecerá en runtime hasta que el servidor recargue los mobs fijos.",
                "Guardar mobs en SQL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadFixedMobsForCurrentMapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PublishStatus = "ERROR: " + ex.Message;
            MessageBox.Show(ex.Message, "Guardar mobs en SQL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<MobsFixSlotDraft> SyncSlotsToDrafts() =>
        Slots.Select(s => new MobsFixSlotDraft(s.MobId, s.MinLvl, s.MaxLvl)).ToList();

    private void TryLoadExistingAtCell(int cellId, bool focusPanel)
    {
        var match = ExistingGroups.FirstOrDefault(g => g.Celda == cellId);
        if (match is null) return;
        _suppressCellSync = true;
        try
        {
            SelectedExisting = match;
            LoadGroupIntoEditor(match.Row);
        }
        finally
        {
            _suppressCellSync = false;
        }

        if (focusPanel)
            FocusPanel();
    }

    private void LoadGroupIntoEditor(MobsFixRow row)
    {
        Slots.Clear();
        SelectedSlot = null;

        if (row.HasLegacyOrUnrecognizedMobsFormat
            || !MobsFixGroupString.TryParseStrict(row.Mobs, out var slots))
        {
            Condiciones = row.Condicion ?? "";
            Descripcion = row.Descripcion ?? "";
            SegundosRespawn = row.SegundosRespawn.ToString(CultureInfo.InvariantCulture);
            SetTipo(row.Tipo);
            OnPropertyChanged(nameof(SlotsHeader));
            AddSelectedToGroupCommand.RaiseCanExecuteChanged();
            OpenPickerCommand.RaiseCanExecuteChanged();
            PublishStatus =
                "⚠ Formato legacy/no reconocido — valor original conservado:\n" + row.Mobs;
            return;
        }

        foreach (var s in slots)
        {
            var entry = VisualLibraryService.Shared.GetMonster(s.MobId);
            var nombre = entry?.Nombre ?? $"Mob {s.MobId}";
            var gfx = entry?.GfxId ?? 0;
            Slots.Add(new MapMonsterSlotVm(Slots.Count + 1, s.MobId, nombre, gfx, s.MinLvl, s.MaxLvl));
        }

        RenumberSlots();
        Condiciones = row.Condicion ?? "";
        Descripcion = row.Descripcion ?? "";
        SegundosRespawn = row.SegundosRespawn.ToString(CultureInfo.InvariantCulture);
        SetTipo(row.Tipo);
        OnPropertyChanged(nameof(SlotsHeader));
        AddSelectedToGroupCommand.RaiseCanExecuteChanged();
        OpenPickerCommand.RaiseCanExecuteChanged();
        PublishStatus = $"Grupo cargado · celda {row.Celda}";
    }

    private void SetTipo(int tipo)
    {
        _spawnTipo = MobsFixTipoValues.IsAllowed(tipo) ? tipo : -1;
        OnPropertyChanged(nameof(IsTipoFijo));
        OnPropertyChanged(nameof(IsTipoNormal));
        OnPropertyChanged(nameof(IsTipoUnaPelea));
        OnPropertyChanged(nameof(IsTipoHastaMorir));
    }

    private void ClearExisting()
    {
        ExistingGroups.Clear();
        FixedMobCellIds = new HashSet<int>();
        OnPropertyChanged(nameof(ExistingHeader));
        OnPropertyChanged(nameof(FixedMobCellIds));
    }

    private void RefreshClipsWarning()
    {
        ArtworkPreviewService.Shared.RefreshClipsStatus();
        ClipsWarning = ArtworkPreviewService.Shared.ClipsStatus;
    }

    private static bool TryCreateRepository(out IMobsFixRepository? repo, out string? error)
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

        repo = new MysqlMobsFixRepository(db, password);
        return true;
    }
}

public sealed class MapMonsterSlotVm : ViewModelBase
{
    private string _minLvl;
    private string _maxLvl;
    private int _slotNumber;

    public MapMonsterSlotVm(int slotNumber, int mobId, string nombre, int gfxId, int minLvl, int maxLvl)
    {
        _slotNumber = slotNumber;
        MobId = mobId;
        Nombre = nombre;
        GfxId = gfxId;
        _minLvl = minLvl.ToString(CultureInfo.InvariantCulture);
        _maxLvl = maxLvl.ToString(CultureInfo.InvariantCulture);
    }

    public int SlotNumber
    {
        get => _slotNumber;
        set
        {
            if (SetProperty(ref _slotNumber, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public int MobId { get; }
    public string Nombre { get; }
    public int GfxId { get; }

    public string Title => $"[{SlotNumber}] {Nombre} · Mob ID {MobId} · GFX {GfxId}";

    public string MinLvl
    {
        get => _minLvl;
        set => SetProperty(ref _minLvl, value);
    }

    public string MaxLvl
    {
        get => _maxLvl;
        set => SetProperty(ref _maxLvl, value);
    }
}

public sealed class MobsFixExistingGroupVm
{
    public MobsFixExistingGroupVm(MobsFixRow row)
    {
        Row = row;
        Celda = row.Celda;
        var warn = row.HasLegacyOrUnrecognizedMobsFormat ? " ⚠ legacy" : "";
        Display = $"Celda {row.Celda} · tipo {row.Tipo} · {Truncate(row.Mobs, 48)}{warn}";
    }

    public MobsFixRow Row { get; }
    public int Celda { get; }
    public string Display { get; }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
