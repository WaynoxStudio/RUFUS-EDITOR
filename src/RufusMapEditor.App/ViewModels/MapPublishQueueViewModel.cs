using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.MapPublishQueue;

namespace RufusMapEditor.App.ViewModels;

public sealed class MapPublishQueueRow : ViewModelBase
{
    public required MapPublishQueueItem Item { get; init; }
    public int MapId => Item.MapId;

    private string _statusLabel = "";
    public string StatusLabel
    {
        get => _statusLabel;
        set => SetProperty(ref _statusLabel, value);
    }

    private string _dbKindLabel = "—";
    public string DbKindLabel
    {
        get => _dbKindLabel;
        set => SetProperty(ref _dbKindLabel, value);
    }

    private MapPublishQueueItemStatus _status;
    public MapPublishQueueItemStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string SaLine => Item.SubAreaDefined ? $"sa={Item.SubArea}" : "sa pendiente";

    public string EpLine => Item.Ep == MapPublishQueueItem.DefaultEp
        ? $"EP: {Item.Ep} (predeterminado)"
        : $"EP: {Item.Ep}";

    public string DisplayLine => $"{MapId}  ·  {DbKindLabel}  ·  {StatusLabel}";
}

/// <summary>MAP-BATCH.1 — queue UI + batch publish orchestration.</summary>
public sealed class MapPublishQueueViewModel : ViewModelBase
{
    private readonly MapPublishQueueStore _store = new();
    private readonly Func<string?> _getLibraryRoot;
    private readonly Func<int, bool> _isMapDirty;
    private readonly Func<int, Task> _openMapAsync;
    private readonly Func<MapDocument?> _getCurrentMap;
    private readonly Func<Task<bool>> _saveCurrentAsync;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<int, MapDocument> _loadMapDocument;
    private readonly Action<string>? _reportStatus;

    public MapPublishQueueViewModel(
        Func<string?> getLibraryRoot,
        Func<int, bool> isMapDirty,
        Func<int, Task> openMapAsync,
        Func<MapDocument?> getCurrentMap,
        Func<Task<bool>> saveCurrentAsync,
        Func<AppSettings> getSettings,
        Func<int, MapDocument> loadMapDocument,
        Action<string>? reportStatus = null)
    {
        _getLibraryRoot = getLibraryRoot;
        _isMapDirty = isMapDirty;
        _openMapAsync = openMapAsync;
        _getCurrentMap = getCurrentMap;
        _saveCurrentAsync = saveCurrentAsync;
        _getSettings = getSettings;
        _loadMapDocument = loadMapDocument;
        _reportStatus = reportStatus;

        Rows = new ObservableCollection<MapPublishQueueRow>();
        AddCurrentToQueueCommand = new RelayCommand(() => _ = AddCurrentAsync(), () => _getCurrentMap() is not null);
        OpenQueueCommand = new RelayCommand(OpenQueueWindow);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedRow is not null);
        ClearQueueCommand = new RelayCommand(ClearQueue, () => Count > 0);
        OpenSelectedCommand = new RelayCommand(() => _ = OpenSelectedAsync(), () => SelectedRow is not null);
        CompletarDatosCommand = new RelayCommand(CompletarDatosSelected, () => SelectedRow is not null);
        PublishAllCommand = new RelayCommand(() => _ = PublishAllAsync(), () => Count > 0);
        PublishSelectedCommand = new RelayCommand(() => _ = PublishSelectedAsync(), () => SelectedRow is not null);
        RefreshCommand = new RelayCommand(Refresh);
    }

    public ObservableCollection<MapPublishQueueRow> Rows { get; }

    private MapPublishQueueRow? _selectedRow;
    public MapPublishQueueRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                RemoveSelectedCommand.RaiseCanExecuteChanged();
                OpenSelectedCommand.RaiseCanExecuteChanged();
                CompletarDatosCommand.RaiseCanExecuteChanged();
                PublishSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private int _count;
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(FloatingLabel));
                OnPropertyChanged(nameof(FloatingTooltip));
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(HasWarnings));
                ClearQueueCommand.RaiseCanExecuteChanged();
                PublishAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasItems => Count > 0;

    private bool _hasWarnings;
    public bool HasWarnings
    {
        get => _hasWarnings;
        private set
        {
            if (SetProperty(ref _hasWarnings, value))
                OnPropertyChanged(nameof(FloatingTooltip));
        }
    }

    public string FloatingLabel => Count == 0 ? "🗂 0" : $"🗂 {Count}";

    public string FloatingTooltip =>
        Count == 0
            ? "Cola de publicación (vacía) — abrir bandeja"
            : HasWarnings
                ? $"Cola de publicación · {Count} mapa(s) · hay pendientes"
                : $"Cola de publicación · {Count} mapa(s)";

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>Raised when queue membership/status changes (header + buttons sync).</summary>
    public event Action? QueueChanged;

    public RelayCommand AddCurrentToQueueCommand { get; }
    public RelayCommand OpenQueueCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand OpenSelectedCommand { get; }
    public RelayCommand CompletarDatosCommand { get; }
    public RelayCommand PublishAllCommand { get; }
    public RelayCommand PublishSelectedCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public string GetHeaderGlyph(int mapId)
    {
        if (!IsInQueue(mapId))
            return "+";
        if (!string.IsNullOrWhiteSpace(_store.LibraryRoot) &&
            _store.TryGet(mapId, out var item) &&
            item is not null)
        {
            var status = MapPublishQueueStore.EvaluateStatus(item, _store.LibraryRoot!, _isMapDirty(mapId));
            if (status == MapPublishQueueItemStatus.UnsavedChanges ||
                status == MapPublishQueueItemStatus.ModifiedAfterQueued)
                return "!";
        }

        return "✓";
    }

    public string GetHeaderTooltip(int mapId)
    {
        if (!_store.TryGet(mapId, out var item) || item is null || string.IsNullOrWhiteSpace(_store.LibraryRoot))
            return "Añadir mapa a la cola de publicación";
        var status = MapPublishQueueStore.EvaluateStatus(item, _store.LibraryRoot!, _isMapDirty(mapId));
        var label = "En cola · " + MapPublishQueueStore.StatusLabel(status, item);
        if (status == MapPublishQueueItemStatus.UnsavedChanges)
            return label + "\nClic: guardar y actualizar la cola";
        if (status == MapPublishQueueItemStatus.ModifiedAfterQueued)
            return label + "\nClic: actualizar huella con el guardado actual";
        return label;
    }

    public bool IsInQueue(int mapId) => _store.TryGet(mapId, out _);

    public void BindLibrary(string? libraryRoot)
    {
        _store.ConfigureLibraryRoot(libraryRoot);
        Refresh();
    }

    public void Refresh()
    {
        var root = _getLibraryRoot();
        if (!string.IsNullOrWhiteSpace(root) &&
            !string.Equals(_store.LibraryRoot, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            _store.ConfigureLibraryRoot(root);

        Rows.Clear();
        if (string.IsNullOrWhiteSpace(_store.LibraryRoot))
        {
            Count = 0;
            HasWarnings = false;
            return;
        }

        var warnings = false;
        foreach (var item in _store.Items)
        {
            var dirty = _isMapDirty(item.MapId);
            var status = MapPublishQueueStore.EvaluateStatus(item, _store.LibraryRoot!, dirty);
            if (status is not MapPublishQueueItemStatus.Ready)
                warnings = true;

            Rows.Add(new MapPublishQueueRow
            {
                Item = item,
                Status = status,
                StatusLabel = MapPublishQueueStore.StatusLabel(status, item),
                DbKindLabel = "—",
            });
        }

        Count = Rows.Count;
        HasWarnings = warnings;
        AddCurrentToQueueCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(FloatingTooltip));
        QueueChanged?.Invoke();
    }

    /// <summary>MAP-BATCH.1.2 — add from map chrome [+] (or refresh fingerprint if already queued).</summary>
    public async Task AddMapAsync(int mapId)
    {
        var root = _getLibraryRoot();
        if (mapId <= 0 || string.IsNullOrWhiteSpace(root))
        {
            MessageBox.Show("Abre un mapa y configura la Library.", "Cola de publicación");
            return;
        }

        var current = _getCurrentMap();
        if (current?.Id != mapId)
            await _openMapAsync(mapId).ConfigureAwait(true);

        // Already queued → refresh snapshot to latest save (no publish).
        if (_store.TryGet(mapId, out var existing) && existing is not null)
        {
            if (_isMapDirty(mapId))
            {
                if (!await _saveCurrentAsync().ConfigureAwait(true))
                {
                    MessageBox.Show(
                        "No se pudo guardar. La cola no se actualizó.",
                        "Cola de publicación",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Refresh();
                    return;
                }
            }

            SyncFingerprintAfterSave(mapId);
            var msg = $"✓ Map {mapId} actualizado en la cola";
            StatusText = msg;
            _reportStatus?.Invoke(msg);
            return;
        }

        await AddCurrentAsync().ConfigureAwait(true);
    }

    /// <summary>After Official Save: if queued, refresh fingerprint so ! → ✓.</summary>
    public void SyncFingerprintAfterSave(int mapId)
    {
        var root = _getLibraryRoot();
        if (string.IsNullOrWhiteSpace(root) || !_store.TryGet(mapId, out var existing) || existing is null)
        {
            Refresh();
            return;
        }

        if (!MapPublishQueueStore.HasOfficialSave(root, mapId))
        {
            Refresh();
            return;
        }

        var sha = MapPublishQueueStore.TryComputeRufmapSha256(root, mapId);
        if (sha is null)
        {
            Refresh();
            return;
        }

        var path = MapPublishQueueStore.GetOfficialRufmapPath(root, mapId);
        existing.RufmapSha256 = sha;
        existing.RufmapUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
        _store.ConfigureLibraryRoot(root);
        _store.Upsert(existing);
        Refresh();
    }

    public async Task AddCurrentAsync()
    {
        var map = _getCurrentMap();
        var root = _getLibraryRoot();
        if (map is null || string.IsNullOrWhiteSpace(root))
        {
            MessageBox.Show("Abre un mapa y configura la Library.", "Cola de publicación");
            return;
        }

        if (_isMapDirty(map.Id))
        {
            var save = MessageBox.Show(
                "Hay cambios sin guardar.\n\n¿Guardar localmente y añadir a la cola?",
                "Añadir a publicación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (save != MessageBoxResult.Yes)
                return;
            if (!await _saveCurrentAsync().ConfigureAwait(true))
            {
                MessageBox.Show("No se pudo guardar. No se añadió a la cola.", "Añadir a publicación");
                return;
            }
        }

        if (!MapPublishQueueStore.HasOfficialSave(root, map.Id))
        {
            MessageBox.Show(
                "El mapa debe estar guardado localmente (Library/Maps) antes de añadirlo.",
                "Añadir a publicación");
            return;
        }

        var sha = MapPublishQueueStore.TryComputeRufmapSha256(root, map.Id);
        if (sha is null)
        {
            MessageBox.Show("No se pudo leer el .rufmap oficial.", "Cola de publicación");
            return;
        }

        // Preserve sa/ep if re-adding / updating existing queue entry.
        var had = _store.TryGet(map.Id, out var existing) && existing is not null;

        var rufmapPath = MapPublishQueueStore.GetOfficialRufmapPath(root, map.Id);
        var ticks = File.GetLastWriteTimeUtc(rufmapPath).Ticks;

        var item = new MapPublishQueueItem
        {
            MapId = map.Id,
            RufmapSha256 = sha,
            DateMapSnapshot = map.DateMap ?? "",
            RufmapUtcTicks = ticks,
            QueuedUtc = DateTimeOffset.UtcNow,
            SubAreaDefined = had && existing!.SubAreaDefined,
            SubArea = had ? existing!.SubArea : 0,
            Ep = had ? existing!.Ep : MapPublishQueueItem.DefaultEp,
            WorldX = map.WorldCoordinatesSet ? map.WorldX : 0,
            WorldY = map.WorldCoordinatesSet ? map.WorldY : 0,
            WorldCoordinatesSet = map.WorldCoordinatesSet,
        };

        _store.ConfigureLibraryRoot(root);
        var added = _store.Upsert(item);
        Refresh();
        var msg = added
            ? $"✓ Map {map.Id} añadido a publicación"
            : $"✓ Map {map.Id} actualizado en la cola";
        StatusText = msg;
        _reportStatus?.Invoke(msg);
    }

    private void CompletarDatosSelected()
    {
        if (SelectedRow is null) return;
        CompletarDatos(SelectedRow.Item);
    }

    public void CompletarDatos(MapPublishQueueItem item)
    {
        var root = _getLibraryRoot();
        if (string.IsNullOrWhiteSpace(root)) return;

        MapDocument? doc = null;
        try { doc = _loadMapDocument(item.MapId); } catch { /* use queue snapshot */ }

        var dlg = new MapPublishQueueEditWindow(
            item.MapId,
            doc?.WorldCoordinatesSet == true ? doc.WorldX : (item.WorldCoordinatesSet ? item.WorldX : null),
            doc?.WorldCoordinatesSet == true ? doc.WorldY : (item.WorldCoordinatesSet ? item.WorldY : null),
            item.SubAreaDefined ? item.SubArea : null,
            item.Ep > 0 ? item.Ep : MapPublishQueueItem.DefaultEp)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (dlg.ShowDialog() != true)
            return;

        item.SubAreaDefined = true;
        item.SubArea = dlg.SubArea;
        item.Ep = dlg.Ep;
        item.WorldX = dlg.WorldX;
        item.WorldY = dlg.WorldY;
        item.WorldCoordinatesSet = true;

        // Also push X/Y into open document if loaded? Spec says edit queue data for publish.
        // Persist coordinates into map document when open so save keeps them.
        var open = _getCurrentMap();
        if (open is not null && open.Id == item.MapId)
        {
            open.WorldX = dlg.WorldX;
            open.WorldY = dlg.WorldY;
            open.WorldCoordinatesSet = true;
        }

        _store.ConfigureLibraryRoot(root);
        _store.Upsert(item);
        Refresh();
        StatusText = $"✓ Datos de Map {item.MapId} actualizados";
    }

    private void OpenQueueWindow()
    {
        Refresh();
        var win = new MapPublishQueueWindow(this) { Owner = Application.Current?.MainWindow };
        win.ShowDialog();
        Refresh();
    }

    private void RemoveSelected()
    {
        if (SelectedRow is null) return;
        _store.Remove(SelectedRow.MapId);
        Refresh();
    }

    private void ClearQueue()
    {
        if (Count == 0) return;
        var confirm = MessageBox.Show(
            $"¿Vaciar la cola ({Count} mapas)?",
            "Cola de publicación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _store.Clear();
        Refresh();
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedRow is null) return;
        await _openMapAsync(SelectedRow.MapId).ConfigureAwait(true);
    }

    public async Task PublishAllAsync() => await PublishBatchAsync(null).ConfigureAwait(true);

    public async Task PublishSelectedAsync()
    {
        if (SelectedRow is null)
        {
            MessageBox.Show("Selecciona un mapa en la cola.", "PUBLICAR SELECCIONADOS");
            return;
        }

        await PublishBatchAsync(new[] { SelectedRow.MapId }).ConfigureAwait(true);
    }

    /// <param name="onlyMapIds">null = toda la cola; otherwise subset.</param>
    private async Task PublishBatchAsync(IReadOnlyCollection<int>? onlyMapIds)
    {
        Refresh();
        var root = _getLibraryRoot();
        if (string.IsNullOrWhiteSpace(root) || Count == 0)
            return;

        var targetRows = onlyMapIds is null
            ? Rows.ToList()
            : Rows.Where(r => onlyMapIds.Contains(r.MapId)).ToList();
        if (targetRows.Count == 0)
            return;

        var blockers = new List<string>();
        foreach (var row in targetRows)
        {
            var b = MapPublishQueueStore.GetPublishBlockers(
                row.Item, root, _isMapDirty(row.MapId));
            foreach (var line in b)
                blockers.Add($"⚠ Map {row.MapId} — {line}");
        }

        if (blockers.Count > 0)
        {
            MessageBox.Show(
                "No se puede publicar todavía:\n\n" + string.Join("\n", blockers) +
                "\n\nCompleta los datos desde la cola o guarda los mapas afectados.",
                "PUBLICAR LOTE",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var settings = _getSettings();
        settings.Database ??= new DatabaseSettings();
        settings.LangSftp ??= new LangSftpSettings();
        var db = settings.Database;
        var lang = settings.LangSftp;

        if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.User))
        {
            MessageBox.Show("Configura MySQL (Archivo → Configuración BD…).", "PUBLICAR LOTE");
            return;
        }

        if (string.IsNullOrWhiteSpace(lang.Host) || string.IsNullOrWhiteSpace(lang.User))
        {
            MessageBox.Show("Configura LANG / SFTP (Ajustes).", "PUBLICAR LOTE");
            return;
        }

        string dbPassword;
        string sftpPassword;
        try
        {
            dbPassword = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
            sftpPassword = LangSftpPasswordProtector.Unprotect(lang.PasswordProtectedBase64);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo descifrar contraseñas:\n" + ex.Message, "PUBLICAR LOTE");
            return;
        }

        // Load latest local documents
        var pairs = new List<(MapDocument Doc, MapPublishQueueItem Queue)>();
        foreach (var row in targetRows)
        {
            try
            {
                var doc = _loadMapDocument(row.MapId);
                // Prefer live X/Y from document for LANG
                if (doc.WorldCoordinatesSet)
                {
                    row.Item.WorldX = doc.WorldX;
                    row.Item.WorldY = doc.WorldY;
                    row.Item.WorldCoordinatesSet = true;
                }

                if (!doc.WorldCoordinatesSet && !row.Item.WorldCoordinatesSet)
                {
                    MessageBox.Show(
                        $"⚠ Map {row.MapId} — falta X/Y",
                        "PUBLICAR LOTE");
                    return;
                }

                if (!doc.WorldCoordinatesSet && row.Item.WorldCoordinatesSet)
                {
                    doc.WorldX = row.Item.WorldX;
                    doc.WorldY = row.Item.WorldY;
                    doc.WorldCoordinatesSet = true;
                }

                pairs.Add((doc, row.Item));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo cargar mapa {row.MapId}:\n{ex.Message}", "PUBLICAR LOTE");
                return;
            }
        }

        var backupDir = Path.Combine(AppSettingsStore.SettingsDirectory, "db-backups");
        var label = $"{db.Database}.{db.Table}";
        IMapasRepository repo = new MysqlMapasRepository(db, dbPassword);
        var publishService = new MapPublishService(repo, backupDir, label);

        StatusText = "Prevalidando lote…";
        MapBatchPrepareResult prep;
        try
        {
            prep = await MapBatchPublishPlanner.PrepareDatabaseAsync(
                pairs, publishService, db.NewMapDefaults).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PUBLICAR LOTE");
            return;
        }

        if (!prep.Success)
        {
            MessageBox.Show(prep.Error ?? "Prevalidación BD falló.", "PUBLICAR LOTE",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Refresh DbKind labels
        foreach (var plan in prep.Items)
        {
            var row = Rows.FirstOrDefault(r => r.MapId == plan.MapId);
            if (row is null) continue;
            row.DbKindLabel = plan.IsInsert ? "NUEVO → INSERT" : plan.DbNoOp ? "EXISTENTE (sin cambios BD)" : "EXISTENTE → UPDATE";
        }

        var sync = lang.LastSync;
        var mapsVersion = sync?.MapsVersion;
        var preview = BuildPreview(prep.Items, mapsVersion);
        var confirm = new MapBatchPublishConfirmWindow(preview, prep.Items.Count)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (confirm.ShowDialog() != true || !confirm.Confirmed)
            return;

        // BD sequential
        StatusText = "Publicando BD…";
        RufusLog.Info($"MAP-BATCH BD · {prep.Items.Count} mapas");
        var dbResult = await MapBatchPublishPlanner.ExecuteDatabaseSequentialAsync(prep.Items, publishService)
            .ConfigureAwait(true);

        var perMap = dbResult.PerMap.Select(m => new MapBatchMapResult
        {
            MapId = m.MapId,
            DbOk = m.DbOk,
            Error = m.Error,
        }).ToList();

        if (!dbResult.Success)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PUBLICACIÓN PARCIAL (BD)");
            sb.AppendLine();
            foreach (var m in perMap)
            {
                sb.AppendLine($"{m.MapId}   {(m.DbOk ? "✓ BD" : "✕ BD")}   — Cliente pendiente");
                if (!string.IsNullOrWhiteSpace(m.Error))
                    sb.AppendLine("  " + m.Error);
            }

            MessageBox.Show(sb.ToString(), "PUBLICACIÓN PARCIAL", MessageBoxButton.OK, MessageBoxImage.Warning);
            // Keep all in queue (none fully complete)
            Refresh();
            return;
        }

        // LANG batch — one N+1
        StatusText = "Publicando maps_es (lote)…";
        var langEntries = pairs.Select(p => new LangMapsBatchEntry
        {
            MapId = p.Doc.Id,
            X = p.Doc.WorldX,
            Y = p.Doc.WorldY,
            SubArea = p.Queue.SubArea,
            Ep = p.Queue.Ep,
        }).ToList();

        LangRemotePublishResult langResult;
        try
        {
            langResult = await Task.Run(() => LangRemotePublishService.PublishBatch(new LangRemoteBatchPublishRequest
            {
                Settings = lang,
                PlainPassword = sftpPassword,
                Entries = langEntries,
            })).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "BD publicada, pero LANG falló:\n" + ex.Message +
                "\n\nLos mapas permanecen en la cola.",
                "PUBLICACIÓN PARCIAL",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Refresh();
            return;
        }

        if (!langResult.Success)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PUBLICACIÓN PARCIAL");
            sb.AppendLine();
            foreach (var m in perMap)
                sb.AppendLine($"{m.MapId}   ✓ BD   ✕ Cliente");
            sb.AppendLine();
            sb.AppendLine(langResult.Error ?? "Error LANG");
            MessageBox.Show(sb.ToString(), "PUBLICACIÓN PARCIAL", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
            return;
        }

        // Update LastSync snapshot
        if (langResult.TargetVersion is int tv)
        {
            lang.LastSync = new LangRemoteSyncSnapshot
            {
                MapsVersion = tv,
                SwfFileName = langResult.TargetSwfFileName ?? VersionsEsParser.BuildSwfFileName(tv),
                SwfSha256 = langResult.RemoteSwfSha256 ?? langResult.LocalSwfSha256 ?? "",
                VersionsEsSha256 = "",
                VersionsEsRelevantLine = $"maps,es,{tv}",
                SyncedUtc = DateTimeOffset.UtcNow,
                LocalCachePath = langResult.LocalGeneratedSwfPath,
            };
            AppSettingsStore.Save(settings);
        }

        foreach (var m in perMap)
            m.ClientOk = true;

        var doneIds = perMap.Where(m => m.Complete).Select(m => m.MapId).ToList();
        _store.RemoveMany(doneIds);
        Refresh();

        var summary = new StringBuilder();
        summary.AppendLine("PUBLICACIÓN COMPLETADA");
        summary.AppendLine();
        summary.AppendLine($"{doneIds.Count} / {perMap.Count} mapas publicados");
        summary.AppendLine();
        foreach (var m in perMap)
            summary.AppendLine($"{m.MapId}   ✓ BD   ✓ Cliente");
        summary.AppendLine();
        summary.AppendLine($"BD: ✓ {doneIds.Count}/{perMap.Count}");
        summary.AppendLine($"maps_es: ✓ {langResult.SourceVersion} → {langResult.TargetVersion}");
        summary.AppendLine("versions_es: ✓ actualizado");

        StatusText = $"Lote OK · {doneIds.Count} mapas";
        RufusLog.Ok($"MAP-BATCH completado · {doneIds.Count} mapas · maps_es {langResult.SourceVersion}→{langResult.TargetVersion}");
        MessageBox.Show(summary.ToString(), "PUBLICACIÓN COMPLETADA", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string BuildPreview(IReadOnlyList<MapBatchDbPlanItem> items, int? mapsVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PUBLICAR LOTE DE MAPAS");
        sb.AppendLine();
        sb.AppendLine($"Mapas: {items.Count}");
        sb.AppendLine();
        sb.AppendLine("--------------------------------");
        sb.AppendLine("BD");
        sb.AppendLine("--------------------------------");
        sb.AppendLine();
        foreach (var i in items)
        {
            var op = i.IsInsert ? "INSERT" : i.DbNoOp ? "UPDATE (sin cambios)" : "UPDATE";
            sb.AppendLine($"{i.MapId} → {op}");
        }

        sb.AppendLine();
        sb.AppendLine("--------------------------------");
        sb.AppendLine("MAPS_ES");
        sb.AppendLine("--------------------------------");
        sb.AppendLine();
        if (mapsVersion is int n)
        {
            sb.AppendLine($"Versión activa: {n}");
            sb.AppendLine($"Versión nueva:  {n + 1}");
        }
        else
        {
            sb.AppendLine("Versión activa: (sincronizar LANG antes; se leerá en remoto)");
            sb.AppendLine("Versión nueva:  N+1");
        }

        sb.AppendLine($"Entradas MA.m nuevas/actualizadas: {items.Count}");
        sb.AppendLine();
        sb.AppendLine("--------------------------------");
        sb.AppendLine("SFTP");
        sb.AppendLine("--------------------------------");
        sb.AppendLine();
        if (mapsVersion is int n2)
        {
            sb.AppendLine($"{VersionsEsParser.BuildSwfFileName(n2)}");
            sb.AppendLine("→");
            sb.AppendLine($"{VersionsEsParser.BuildSwfFileName(n2 + 1)}");
            sb.AppendLine();
            sb.AppendLine($"versions_es: maps,es,{n2} → {n2 + 1}");
        }
        else
        {
            sb.AppendLine("maps_es_N.swf → maps_es_N+1.swf");
            sb.AppendLine("versions_es: maps,es,N → N+1");
        }

        sb.AppendLine();
        sb.AppendLine("Una sola generación maps_es N+1 para todo el lote.");
        return sb.ToString();
    }
}
