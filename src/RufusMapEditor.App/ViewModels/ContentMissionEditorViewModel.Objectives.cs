using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.ViewModels;

public sealed partial class ContentMissionEditorViewModel
{
    private const int ObjItemNameDebounceMs = 350;

    private bool _loadingObjectiveFields;
    private string _objNpcLabel = "";
    private string _objItemLabel = "";
    private string _objMobLabel = "";
    private string _objAreaLabel = "";
    private string _objMapLabel = "";
    private string _objectiveValidation = "";
    private ImageSource? _objMobPreview;
    private ImageSource? _objItemPreview;
    private string _objItemNameDisplay = "";
    private bool _objItemIdInvalid;
    private int _objItemNameDebounceToken;
    private DispatcherTimer? _objItemNameDebounce;
    private string _objMobNameDisplay = "";
    private bool _objMobIdInvalid;
    private int _objMobNameDebounceToken;
    private DispatcherTimer? _objMobNameDebounce;

    public string ObjNpcLabel { get => _objNpcLabel; private set => SetProperty(ref _objNpcLabel, value); }
    public string ObjItemLabel { get => _objItemLabel; private set => SetProperty(ref _objItemLabel, value); }
    public string ObjMobLabel { get => _objMobLabel; private set => SetProperty(ref _objMobLabel, value); }
    public string ObjAreaLabel { get => _objAreaLabel; private set => SetProperty(ref _objAreaLabel, value); }
    public string ObjMapLabel { get => _objMapLabel; private set => SetProperty(ref _objMapLabel, value); }
    public string ObjectiveValidation
    {
        get => _objectiveValidation;
        private set => SetProperty(ref _objectiveValidation, value);
    }
    public bool HasObjectiveValidation => !string.IsNullOrWhiteSpace(ObjectiveValidation);
    public ImageSource? ObjMobPreview { get => _objMobPreview; private set => SetProperty(ref _objMobPreview, value); }
    public ImageSource? ObjItemPreview { get => _objItemPreview; private set => SetProperty(ref _objItemPreview, value); }

    public string ObjItemNameDisplay
    {
        get => _objItemNameDisplay;
        private set => SetProperty(ref _objItemNameDisplay, value);
    }

    public bool IsObjItemIdInvalid
    {
        get => _objItemIdInvalid;
        private set
        {
            if (!SetProperty(ref _objItemIdInvalid, value)) return;
            OnPropertyChanged(nameof(ObjItemNameForeground));
        }
    }

    public Brush ObjItemNameForeground
    {
        get
        {
            if (IsObjItemIdInvalid)
                return Brushes.IndianRed;
            if (Application.Current?.TryFindResource("TextSecondary") is Brush brush)
                return brush;
            return Brushes.Gray;
        }
    }

    public string ObjMobNameDisplay
    {
        get => _objMobNameDisplay;
        private set => SetProperty(ref _objMobNameDisplay, value);
    }

    public bool IsObjMobIdInvalid
    {
        get => _objMobIdInvalid;
        private set
        {
            if (!SetProperty(ref _objMobIdInvalid, value)) return;
            OnPropertyChanged(nameof(ObjMobNameForeground));
        }
    }

    public Brush ObjMobNameForeground
    {
        get
        {
            if (IsObjMobIdInvalid)
                return Brushes.IndianRed;
            if (Application.Current?.TryFindResource("TextSecondary") is Brush brush)
                return brush;
            return Brushes.Gray;
        }
    }

    public ObservableCollection<MissionStageRowVm> StageRows { get; } = new();
    public ObservableCollection<MissionObjectiveRowVm> ObjectiveRows { get; } = new();
    public ObservableCollection<MissionRewardItemRowVm> RewardItemRows { get; } = new();

    public MissionRewardItemRowVm? SelectedRewardRow
    {
        get => RewardItemRows.FirstOrDefault(r => ReferenceEquals(r, _selectedRewardRow));
        set
        {
            _selectedRewardRow = value;
            OnPropertyChanged();
            RemoveRewardItemCommand.RaiseCanExecuteChanged();
        }
    }
    private MissionRewardItemRowVm? _selectedRewardRow;

    public RelayCommand SelectFlowStageCommand { get; private set; } = null!;

    private void EnsureUxCommands()
    {
        SelectFlowStageCommand = new RelayCommand(p =>
        {
            if (p is MissionStageDraft stage)
                SelectedStage = stage;
            else if (p is MissionFlowNodeVm node && node.Stage is not null)
                SelectedStage = node.Stage;
        });

        PrevStageCommand = new RelayCommand(() =>
        {
            if (Selected is null || SelectedStage is null) return;
            var idx = Selected.Model.Stages.IndexOf(SelectedStage);
            if (idx > 0) SelectedStage = Selected.Model.Stages[idx - 1];
        }, () => CanNavigatePrevStage);

        NextStageCommand = new RelayCommand(() =>
        {
            if (Selected is null || SelectedStage is null) return;
            var idx = Selected.Model.Stages.IndexOf(SelectedStage);
            if (idx < Selected.Model.Stages.Count - 1) SelectedStage = Selected.Model.Stages[idx + 1];
        }, () => CanNavigateNextStage);
    }

    private void BeginAddObjective()
    {
        if (SelectedStage is null) return;
        if (ShowObjectiveTypePicker)
        {
            ShowObjectiveTypePicker = false;
            return;
        }

        // Clear so ComboBox requires an explicit pick (even same type as last time).
        SelectedObjectiveType = null;
        ShowObjectiveTypePicker = true;
    }

    internal void CompleteAddObjectiveFromPicker(ObjectiveTypeChoice choice)
    {
        if (SelectedStage is null) return;
        SelectedObjectiveType = choice;
        AddObjectiveOfSelectedType();
        ShowObjectiveTypePicker = false;
    }

    private void AddObjectiveOfSelectedType()
    {
        if (SelectedStage is null || Selected is null) return;
        var tipo = SelectedObjectiveType?.Tipo ?? MissionObjectiveTypes.TalkToNpc;
        if (tipo == MissionObjectiveTypes.Manual
            || tipo == MissionObjectiveTypes.DeliverSouls
            || !MissionObjectiveTypes.IsUiNormal(tipo))
        {
            ObjectiveValidation = tipo == MissionObjectiveTypes.Manual
                ? MissionObjectiveUiSync.UnsupportedManualMessage
                : "Entregar almas aún no está disponible (dato pendiente).";
            return;
        }

        var o = Workspace.Missions.AddObjective(SelectedStage, tipo);
        o.EsAlHablar = "0";
        o.EsOculto = 0;
        // Do not AutoApply here: the shared editor fields may still hold the previous
        // objective's Map ID / NPC / etc. (WPF LostFocus). New drafts start empty.
        SyncAutoReturnsFor(Selected.Model);
        SelectedObjective = o.IsAutoReturnToStartNpc
            ? SelectedStage.Objectives.FirstOrDefault(x => !x.IsAutoReturnToStartNpc)
            : o;
        SyncObjectiveRows();
        ShowObjectiveTypePicker = false;
        Selected?.Refresh();
        OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
        OnPropertyChanged(nameof(StageTechnicalPreview));
        OnPropertyChanged(nameof(Step2Status));
        Persist();
        StatusText = MissionObjectiveUiSync.UiTypeLabel(tipo);
    }

    private void LoadObjectiveEditorFromSelected()
    {
        _loadingObjectiveFields = true;
        try
        {
            var o = SelectedObjective;
            if (o is null)
            {
                ObjManualText = "";
                ObjNpcId = "";
                ObjItemId = "";
                ObjQty = "1";
                ObjMobId = "";
                ObjMapId = "";
                ObjAreaId = "";
                ObjLevel = "1";
                ObjSpellCount = "1";
                ObjJobCount = "1";
                ObjJobLevel = "1";
                ObjRestrictCoords = false;
                ObjX = "";
                ObjY = "";
                ClearPickerLabels();
                ObjectiveValidation = "";
                return;
            }

            var (core, x, y) = MissionObjectiveArgsCodec.StripCoords(o.Args);
            ObjRestrictCoords = x is not null && y is not null;
            ObjX = x?.ToString(CultureInfo.InvariantCulture) ?? "";
            ObjY = y?.ToString(CultureInfo.InvariantCulture) ?? "";

            var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
            ObjManualText = "";
            ObjNpcId = "";
            ObjItemId = "";
            ObjQty = "1";
            ObjMobId = "";
            ObjMapId = "";
            ObjAreaId = "";
            ObjLevel = "1";
            ObjSpellCount = "1";
            ObjJobCount = "1";
            ObjJobLevel = "1";

            switch (o.Tipo)
            {
                case MissionObjectiveTypes.Manual:
                    ObjManualText = string.IsNullOrWhiteSpace(o.Detalle) ? "" : o.Detalle;
                    break;
                case MissionObjectiveTypes.TalkToNpc:
                case MissionObjectiveTypes.ReturnToNpc:
                    ObjNpcId = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "";
                    break;
                case MissionObjectiveTypes.ShowItemToNpc:
                case MissionObjectiveTypes.DeliverItemsToNpc:
                    ObjNpcId = Selected?.Model.StartNpcId is int startNpc && startNpc > 0
                        ? startNpc.ToString(CultureInfo.InvariantCulture)
                        : "";
                    ObjItemId = nums.Length >= 2 ? nums[1].ToString(CultureInfo.InvariantCulture) : "";
                    ObjQty = nums.Length >= 3 ? nums[2].ToString(CultureInfo.InvariantCulture) : "1";
                    break;
                case MissionObjectiveTypes.DiscoverMap:
                    ObjMapId = int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId)
                        ? mapId.ToString(CultureInfo.InvariantCulture)
                        : "";
                    break;
                case MissionObjectiveTypes.DiscoverArea:
                    ObjAreaId = int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var areaId)
                        ? areaId.ToString(CultureInfo.InvariantCulture)
                        : "";
                    break;
                case MissionObjectiveTypes.DefeatMobs:
                    ObjMobId = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "";
                    ObjQty = nums.Length >= 2 ? nums[1].ToString(CultureInfo.InvariantCulture) : "1";
                    break;
                case MissionObjectiveTypes.UseItem:
                    ObjItemId = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "";
                    break;
                case MissionObjectiveTypes.ReachLevel:
                    ObjLevel = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "1";
                    break;
                case MissionObjectiveTypes.HaveSpells:
                    ObjSpellCount = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "1";
                    break;
                case MissionObjectiveTypes.JobLevel:
                    ObjJobCount = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "1";
                    ObjJobLevel = nums.Length >= 2 ? nums[1].ToString(CultureInfo.InvariantCulture) : "1";
                    break;
            }

            RefreshPickerLabelsFromIds();
            ObjectiveValidation = o.Tipo == MissionObjectiveTypes.Manual
                ? MissionObjectiveUiSync.UnsupportedManualMessage
                : "";
            _ = ResolveObjItemNameAsync(immediate: true);
            _ = ResolveObjMobNameAsync(immediate: true);
        }
        finally
        {
            _loadingObjectiveFields = false;
        }
    }

    private void ClearPickerLabels()
    {
        ObjNpcLabel = "";
        ObjItemLabel = "";
        ObjMobLabel = "";
        ObjAreaLabel = "";
        ObjMapLabel = "";
        ObjMobPreview = null;
        ObjItemPreview = null;
        ObjItemNameDisplay = "";
        IsObjItemIdInvalid = false;
        ObjMobNameDisplay = "";
        IsObjMobIdInvalid = false;
    }

    private void RefreshPickerLabelsFromIds()
    {
        if (int.TryParse(ObjNpcId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var npcId) && npcId != 0)
        {
            var n = Workspace.Npcs.FindById(npcId);
            ObjNpcLabel = n is null
                ? $"NPC #{npcId}"
                : $"{(string.IsNullOrWhiteSpace(n.Nombre) ? "NPC" : n.Nombre)}  #{npcId}";
        }
        else ObjNpcLabel = "";

        // Item name for tipo 2/3/8 comes from objetos_modelo (see ResolveObjItemNameAsync).
        ObjItemLabel = "";
        ObjItemPreview = null;

        // Mob name for tipo 6 comes from mobs_modelo (see ResolveObjMobNameAsync).
        ObjMobLabel = "";
        ObjMobPreview = null;

        if (int.TryParse(ObjAreaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var areaId) && areaId > 0)
            ObjAreaLabel = $"Área #{areaId}";
        else
            ObjAreaLabel = "";

        if (int.TryParse(ObjMapId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId) && mapId > 0)
            ObjMapLabel = $"Map ID {mapId}";
        else
            ObjMapLabel = "";
    }

    private static ImageSource? TryLoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ADMIN.UI.4B.1 — fields change → rebuild args automatically (no Apply button).</summary>
    private void AutoApplyObjectiveFields()
    {
        if (_loadingObjectiveFields || SelectedObjective is null) return;
        if (SelectedObjective.Tipo == MissionObjectiveTypes.Manual)
        {
            ObjectiveValidation = MissionObjectiveUiSync.UnsupportedManualMessage;
            OnPropertyChanged(nameof(HasObjectiveValidation));
            return;
        }
        var fields = CaptureUiFields();
        fields.Tipo = SelectedObjective.Tipo;
        var err = MissionObjectiveUiSync.TryApply(SelectedObjective, fields);
        ObjectiveValidation = err ?? "";
        OnPropertyChanged(nameof(HasObjectiveValidation));
        OnPropertyChanged(nameof(ObjectiveArgsPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        if (Selected is { Model.PublishedBd: false })
            SyncAutoReturnsFor(Selected.Model);
        SyncObjectiveRows();
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
        OnPropertyChanged(nameof(Step2Status));
        Selected?.Refresh();
        if (err is null)
            Persist();
    }

    private MissionObjectiveUiFields CaptureUiFields()
    {
        var tipo = SelectedObjective?.Tipo ?? 0;
        var npcId = ObjNpcId;
        if (MissionShowDeliverNpcBinding.IsBoundTipo(tipo))
        {
            npcId = Selected?.Model.StartNpcId is int sid && sid > 0
                ? sid.ToString(CultureInfo.InvariantCulture)
                : "";
        }

        return new MissionObjectiveUiFields
        {
            Tipo = tipo,
            ManualText = ObjManualText,
            NpcId = npcId,
            ItemId = ObjItemId,
            Qty = ObjQty,
            MobId = ObjMobId,
            MapId = ObjMapId,
            AreaId = ObjAreaId,
            Level = ObjLevel,
            SpellCount = ObjSpellCount,
            JobCount = ObjJobCount,
            JobLevel = ObjJobLevel,
            RestrictCoords = ObjRestrictCoords,
            X = ObjX,
            Y = ObjY,
        };
    }

    // Kept for any remaining callers; normal UX uses AutoApplyObjectiveFields.
    private void ApplyObjectiveEditorFields() => AutoApplyObjectiveFields();

    private void OnObjectiveFieldChanged()
    {
        if (_loadingObjectiveFields) return;
        AutoApplyObjectiveFields();
    }

    internal void OnObjItemIdChanged()
    {
        if (_loadingObjectiveFields) return;
        AutoApplyObjectiveFields();
        ScheduleResolveObjItemName();
    }

    internal void OnObjMobIdChanged()
    {
        if (_loadingObjectiveFields) return;
        AutoApplyObjectiveFields();
        ScheduleResolveObjMobName();
    }

    private void ScheduleResolveObjItemName()
    {
        _objItemNameDebounce ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(ObjItemNameDebounceMs),
        };
        _objItemNameDebounce.Tick -= ObjItemNameDebounce_Tick;
        _objItemNameDebounce.Tick += ObjItemNameDebounce_Tick;
        _objItemNameDebounce.Stop();
        _objItemNameDebounce.Start();
    }

    private void ScheduleResolveObjMobName()
    {
        _objMobNameDebounce ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(ObjItemNameDebounceMs),
        };
        _objMobNameDebounce.Tick -= ObjMobNameDebounce_Tick;
        _objMobNameDebounce.Tick += ObjMobNameDebounce_Tick;
        _objMobNameDebounce.Stop();
        _objMobNameDebounce.Start();
    }

    private void ObjItemNameDebounce_Tick(object? sender, EventArgs e)
    {
        _objItemNameDebounce?.Stop();
        _ = ResolveObjItemNameAsync(immediate: false);
    }

    private void ObjMobNameDebounce_Tick(object? sender, EventArgs e)
    {
        _objMobNameDebounce?.Stop();
        _ = ResolveObjMobNameAsync(immediate: false);
    }

    private async Task ResolveObjItemNameAsync(bool immediate)
    {
        var token = Interlocked.Increment(ref _objItemNameDebounceToken);
        if (!ShowItemFields)
        {
            ObjItemNameDisplay = "";
            IsObjItemIdInvalid = false;
            return;
        }

        if (!int.TryParse(ObjItemId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
            || itemId <= 0)
        {
            ObjItemNameDisplay = string.IsNullOrWhiteSpace(ObjItemId) ? "" : ObjetosModeloNameCache.NotFoundMessage;
            IsObjItemIdInvalid = !string.IsNullOrWhiteSpace(ObjItemId);
            return;
        }

        EnsureObjetosModeloCache();
        var cache = ObjetosModeloNameCache.Shared;
        if (cache is null)
        {
            ObjItemNameDisplay = $"Nombre: (sin BD · Item ID {itemId})";
            IsObjItemIdInvalid = false;
            return;
        }

        if (!immediate && cache.TryGetCached(itemId, out var cachedHit))
        {
            ApplyResolvedItemName(itemId, cachedHit);
            return;
        }

        ObjItemNameDisplay = "Nombre: …";
        IsObjItemIdInvalid = false;
        string? nombre;
        try
        {
            nombre = await cache.ResolveAsync(itemId).ConfigureAwait(true);
        }
        catch
        {
            if (token != Volatile.Read(ref _objItemNameDebounceToken)) return;
            ObjItemNameDisplay = ObjetosModeloNameCache.NotFoundMessage;
            IsObjItemIdInvalid = true;
            return;
        }

        if (token != Volatile.Read(ref _objItemNameDebounceToken)) return;
        ApplyResolvedItemName(itemId, nombre);
    }

    private void ApplyResolvedItemName(int itemId, string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            ObjItemNameDisplay = ObjetosModeloNameCache.NotFoundMessage;
            IsObjItemIdInvalid = true;
            return;
        }

        ObjItemNameDisplay = "Nombre: " + nombre;
        IsObjItemIdInvalid = false;
        _ = itemId;
    }

    private async Task ResolveObjMobNameAsync(bool immediate)
    {
        var token = Interlocked.Increment(ref _objMobNameDebounceToken);
        if (!ShowMobFields)
        {
            ObjMobNameDisplay = "";
            IsObjMobIdInvalid = false;
            return;
        }

        if (!int.TryParse(ObjMobId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobId)
            || mobId <= 0)
        {
            ObjMobNameDisplay = string.IsNullOrWhiteSpace(ObjMobId) ? "" : MobsModeloNameCache.NotFoundMessage;
            IsObjMobIdInvalid = !string.IsNullOrWhiteSpace(ObjMobId);
            return;
        }

        EnsureMobsModeloCache();
        var cache = MobsModeloNameCache.Shared;
        if (cache is null)
        {
            ObjMobNameDisplay = $"Nombre: (sin BD · Monster ID {mobId})";
            IsObjMobIdInvalid = false;
            return;
        }

        if (!immediate && cache.TryGetCached(mobId, out var cachedHit))
        {
            ApplyResolvedMobName(mobId, cachedHit);
            return;
        }

        ObjMobNameDisplay = "Nombre: …";
        IsObjMobIdInvalid = false;
        string? nombre;
        try
        {
            nombre = await cache.ResolveAsync(mobId).ConfigureAwait(true);
        }
        catch
        {
            if (token != Volatile.Read(ref _objMobNameDebounceToken)) return;
            ObjMobNameDisplay = MobsModeloNameCache.NotFoundMessage;
            IsObjMobIdInvalid = true;
            return;
        }

        if (token != Volatile.Read(ref _objMobNameDebounceToken)) return;
        ApplyResolvedMobName(mobId, nombre);
    }

    private void ApplyResolvedMobName(int mobId, string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            ObjMobNameDisplay = MobsModeloNameCache.NotFoundMessage;
            IsObjMobIdInvalid = true;
            return;
        }

        ObjMobNameDisplay = "Nombre: " + nombre;
        IsObjMobIdInvalid = false;
        _ = mobId;
    }

    private static void EnsureObjetosModeloCache()
    {
        if (ObjetosModeloNameCache.Shared is not null) return;
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        ObjetosModeloNameCache.Shared = ObjetosModeloNameCache.FromRepository(
            new MysqlObjetosModeloNameReadRepository(settings, password));
    }

    private static void EnsureMobsModeloCache()
    {
        if (MobsModeloNameCache.Shared is not null) return;
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        MobsModeloNameCache.Shared = MobsModeloNameCache.FromRepository(
            new MysqlMobsModeloNameReadRepository(settings, password));
    }

    private Task PickObjectiveNpcAsync()
    {
        var repo = CreateNpcCatalogRepo();
        var dlg = new NpcPickerWindow(repo, ObjNpcId) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return Task.CompletedTask;
        _loadingObjectiveFields = true;
        ObjNpcId = dlg.SelectedEntry.Id.ToString(CultureInfo.InvariantCulture);
        ObjNpcLabel = $"{dlg.SelectedEntry.Nombre}  #{dlg.SelectedEntry.Id}";
        _loadingObjectiveFields = false;
        AutoApplyObjectiveFields();
        StatusText = ObjNpcLabel + " · preview visual pendiente";
        return Task.CompletedTask;
    }

    private Task PickObjectiveAreaAsync()
    {
        var repo = CreateAreasRepo();
        if (repo is null)
        {
            MessageBox.Show(
                "Configura MySQL para buscar zonas (áreas).\nTambién puedes escribir el Area ID si lo conoces.",
                "Buscar zona");
            return Task.CompletedTask;
        }

        var dlg = new AreaPickerWindow(repo, ObjAreaId) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return Task.CompletedTask;
        _loadingObjectiveFields = true;
        ObjAreaId = dlg.SelectedEntry.Id.ToString(CultureInfo.InvariantCulture);
        ObjAreaLabel = $"{dlg.SelectedEntry.Nombre}  #{dlg.SelectedEntry.Id}";
        _loadingObjectiveFields = false;
        AutoApplyObjectiveFields();
        StatusText = ObjAreaLabel;
        return Task.CompletedTask;
    }

    private void PickObjectiveMap()
    {
        var proposed = int.TryParse(ObjMapId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cur) && cur > 0
            ? cur
            : 1;
        var dlg = new MapIdInputWindow(
            "Introduce un Map ID real (no inventar).",
            proposed)
        {
            Owner = ActiveOwner(),
            Title = "Mapa · descubrir mapa",
        };
        if (dlg.ShowDialog() != true || dlg.ResultMapId is not int mapId)
            return;
        _loadingObjectiveFields = true;
        ObjMapId = mapId.ToString(CultureInfo.InvariantCulture);
        ObjMapLabel = $"Map ID {mapId}";
        _loadingObjectiveFields = false;
        AutoApplyObjectiveFields();
        StatusText = ObjMapLabel;
    }

    private void SyncStageRows()
    {
        StageRows.Clear();
        if (Selected?.Model.Stages is null) return;
        var i = 1;
        foreach (var s in Selected.Model.Stages)
            StageRows.Add(new MissionStageRowVm(i++, s));
    }

    private void SyncObjectiveRows()
    {
        ObjectiveRows.Clear();
        if (SelectedStage is null) return;
        var i = 1;
        foreach (var o in SelectedStage.Objectives.Where(x => !x.IsAutoReturnToStartNpc))
            ObjectiveRows.Add(new MissionObjectiveRowVm(i++, o, ResolveNpcName, OnObjectiveRowMapChanged));
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
    }

    private void OnObjectiveRowMapChanged(MissionObjectiveDraft objective)
    {
        if (_suppressObjectiveRowMapCallbacks) return;
        if (SelectedStage is null || SelectedStage.Objectives.All(o => o.Id != objective.Id))
            return;
        if (Selected is { Model.PublishedBd: false })
            SyncAutoReturnsFor(Selected.Model);
        if (ReferenceEquals(SelectedObjective, objective))
            LoadObjectiveEditorFromSelected();
        OnPropertyChanged(nameof(StageTechnicalPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        OnPropertyChanged(nameof(Step2Status));
        OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
        Selected?.Refresh();
        Persist();
        DraftEdited?.Invoke();
    }

    /// <summary>Map picker scoped to a specific objective row (not the shared editor selection alone).</summary>
    internal void PickObjectiveMapForRow(MissionObjectiveDraft objective)
    {
        if (objective.Tipo != MissionObjectiveTypes.DiscoverMap) return;
        SelectedObjective = objective;
        var (core, _, _) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        var proposed = int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cur) && cur > 0
            ? cur
            : 1;
        var dlg = new MapIdInputWindow(
            "Introduce un Map ID real (no inventar).",
            proposed)
        {
            Owner = ActiveOwner(),
            Title = "Mapa · descubrir mapa",
        };
        if (dlg.ShowDialog() != true || dlg.ResultMapId is not int mapId)
            return;

        objective.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(mapId);
        objective.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(
            MissionObjectiveTypes.DiscoverMap, objective.Args, "");
        if (Selected is { Model.PublishedBd: false })
            SyncAutoReturnsFor(Selected.Model);
        SyncObjectiveRows();
        LoadObjectiveEditorFromSelected();
        OnPropertyChanged(nameof(StageTechnicalPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        OnPropertyChanged(nameof(Step2Status));
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
        Selected?.Refresh();
        Persist();
        DraftEdited?.Invoke();
        StatusText = $"Map ID {mapId}";
    }

    private void SyncRewardItemRows()
    {
        RewardItemRows.Clear();
        if (SelectedStage is null) return;
        foreach (var o in SelectedStage.Rewards.Objetos)
            RewardItemRows.Add(MissionRewardItemRowVm.FromModel(o));
        if (RewardItemRows.Count == 0)
            RewardItemRows.Add(MissionRewardItemRowVm.CreateEmpty());
        _selectedRewardRow = null;
        OnPropertyChanged(nameof(SelectedRewardRow));
        NotifyRewardItemsUi();
    }

    private INpcsModeloCatalogReadRepository CreateNpcCatalogRepo()
    {
        var local = Workspace.Npcs.Drafts
            .Select(n => new NpcCatalogEntry { Id = n.Id, Nombre = n.Nombre ?? "" })
            .ToList();
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return new FixedNpcsModeloCatalogReadRepository(local);

        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlNpcsModeloCatalogReadRepository(settings, password);
    }

    private IAreasReadRepository? CreateAreasRepo()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlAreasReadRepository(settings, password);
    }

    private static Window? ActiveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}

public sealed class MissionStageRowVm
{
    public MissionStageRowVm(int ordinal, MissionStageDraft stage)
    {
        Ordinal = ordinal;
        Stage = stage;
    }

    public int Ordinal { get; }
    public MissionStageDraft Stage { get; }
    public string Title =>
        string.IsNullOrWhiteSpace(Stage.Nombre)
            ? $"Etapa {Ordinal} · Sin nombre"
            : $"Etapa {Ordinal} · {Stage.Nombre}";
    public string IdHint => $"#{Stage.Id} provisional";
}

public sealed class MissionDefeatMobPairRowVm : ViewModelBase
{
    private readonly MissionObjectiveRowVm _owner;
    private string _mobIdText;
    private string _qtyText;
    private bool _enabled = true;

    public MissionDefeatMobPairRowVm(MissionObjectiveRowVm owner, string mobIdText, string qtyText)
    {
        _owner = owner;
        _mobIdText = mobIdText;
        _qtyText = qtyText;
        RemoveCommand = new RelayCommand(Remove);
    }

    public RelayCommand RemoveCommand { get; }

    public void Detach() => _enabled = false;

    public string MobIdText
    {
        get => _mobIdText;
        set
        {
            if (!_enabled) return;
            var next = value ?? "";
            if (_mobIdText == next) return;
            _mobIdText = next;
            OnPropertyChanged();
            _owner.CommitMobPairs();
        }
    }

    public string QtyText
    {
        get => _qtyText;
        set
        {
            if (!_enabled) return;
            // Do not Trim here — Trim on every keystroke fights the caret and clears input.
            var next = value ?? "";
            if (_qtyText == next) return;
            _qtyText = next;
            OnPropertyChanged();
            _owner.CommitMobPairs();
        }
    }

    private void Remove()
    {
        if (!_enabled) return;
        _owner.RemoveMobPair(this);
    }
}

public sealed class MissionObjectiveRowVm : ViewModelBase
{
    private readonly Func<int?, string> _npcName;
    private Action<MissionObjectiveDraft>? _onInlineChanged;
    private string _mapIdText;
    private bool _inlineEditsEnabled = true;

    public MissionObjectiveRowVm(
        int ordinal,
        MissionObjectiveDraft objective,
        Func<int?, string> npcName,
        Action<MissionObjectiveDraft>? onInlineChanged = null)
    {
        Ordinal = ordinal;
        Objective = objective;
        _npcName = npcName;
        _onInlineChanged = onInlineChanged;
        _mapIdText = ReadMapId(objective);
        TypeLabel = MissionObjectiveUiSync.UiTypeLabel(objective.Tipo);
        IdHint = $"#{objective.Id} provisional";
        MobPairs = new ObservableCollection<MissionDefeatMobPairRowVm>();
        AddMobPairCommand = new RelayCommand(AddMobPair);
        LoadMobPairsFromArgs();
    }

    public int Ordinal { get; }
    public MissionObjectiveDraft Objective { get; }
    public string TypeLabel { get; }
    public string IdHint { get; }
    public string Title => $"{Ordinal}. {TypeLabel}";
    public bool IsDiscoverMap => Objective.Tipo == MissionObjectiveTypes.DiscoverMap;
    public bool IsDefeatMobs => Objective.Tipo == MissionObjectiveTypes.DefeatMobs;
    public string Summary => BuildSummary(Objective.Tipo, Objective, _npcName);
    public string CardSubtitle => Summary;
    public ObservableCollection<MissionDefeatMobPairRowVm> MobPairs { get; }
    public RelayCommand AddMobPairCommand { get; }

    /// <summary>Stops inline Map/Mob edits from writing back after the row is deleted.</summary>
    public void DetachMapEdits()
    {
        _inlineEditsEnabled = false;
        _onInlineChanged = null;
        foreach (var p in MobPairs)
            p.Detach();
    }

    public string MapIdText
    {
        get => _mapIdText;
        set
        {
            if (!_inlineEditsEnabled || !IsDiscoverMap) return;
            var next = (value ?? "").Trim();
            if (_mapIdText == next) return;
            if (string.IsNullOrEmpty(next))
            {
                Objective.Args = "";
                Objective.Detalle = "";
                _mapIdText = "";
            }
            else if (int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId) && mapId > 0)
            {
                Objective.Args = MissionObjectiveArgsCodec.BuildDiscoverMap(mapId);
                Objective.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(
                    MissionObjectiveTypes.DiscoverMap, Objective.Args, "");
                _mapIdText = mapId.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // Invalid input: keep typing buffer, do not write Args yet.
                _mapIdText = next;
                OnPropertyChanged();
                return;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CardSubtitle));
            _onInlineChanged?.Invoke(Objective);
        }
    }

    public void AddMobPair()
    {
        if (!_inlineEditsEnabled || !IsDefeatMobs) return;
        MobPairs.Add(new MissionDefeatMobPairRowVm(this, "", "1"));
    }

    public void RemoveMobPair(MissionDefeatMobPairRowVm pair)
    {
        if (!_inlineEditsEnabled || !IsDefeatMobs) return;
        pair.Detach();
        MobPairs.Remove(pair);
        if (MobPairs.Count == 0)
            MobPairs.Add(new MissionDefeatMobPairRowVm(this, "", "1"));
        CommitMobPairs();
    }

    public void CommitMobPairs()
    {
        if (!_inlineEditsEnabled || !IsDefeatMobs) return;
        var pairs = new List<(int MobId, int Qty)>();
        foreach (var row in MobPairs)
        {
            if (!int.TryParse(row.MobIdText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobId)
                || mobId <= 0)
                continue;
            if (!int.TryParse(row.QtyText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)
                || qty <= 0)
                continue;
            pairs.Add((mobId, qty));
        }

        var (_, x, y) = MissionObjectiveArgsCodec.StripCoords(Objective.Args);
        Objective.Args = MissionObjectiveArgsCodec.BuildDefeatMobs(pairs, x, y);
        Objective.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(
            MissionObjectiveTypes.DefeatMobs, Objective.Args, "");
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CardSubtitle));
        _onInlineChanged?.Invoke(Objective);
    }

    private void LoadMobPairsFromArgs()
    {
        MobPairs.Clear();
        if (!IsDefeatMobs)
            return;

        var pairs = MissionObjectiveArgsCodec.ParseDefeatMobPairs(Objective.Args);
        if (pairs.Count == 0)
        {
            MobPairs.Add(new MissionDefeatMobPairRowVm(this, "", "1"));
            return;
        }

        foreach (var (mobId, qty) in pairs)
        {
            MobPairs.Add(new MissionDefeatMobPairRowVm(
                this,
                mobId.ToString(CultureInfo.InvariantCulture),
                qty.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static string ReadMapId(MissionObjectiveDraft objective)
    {
        if (objective.Tipo != MissionObjectiveTypes.DiscoverMap) return "";
        var (core, _, _) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        return int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0
            ? m.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    private static string BuildSummary(int tipo, MissionObjectiveDraft objective, Func<int?, string> npcName)
    {
        var (core, _, _) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        return tipo switch
        {
            MissionObjectiveTypes.TalkToNpc or MissionObjectiveTypes.ReturnToNpc =>
                nums.Length >= 1 ? npcName(nums[0]) : "NPC pendiente",
            MissionObjectiveTypes.ShowItemToNpc or MissionObjectiveTypes.DeliverItemsToNpc =>
                nums.Length >= 3
                    ? $"{npcName(nums[0])} · objeto #{nums[1]} ×{nums[2]}"
                    : "NPC / objeto pendiente",
            MissionObjectiveTypes.DiscoverMap =>
                int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0
                    ? $"Map ID {m}"
                    : "Map ID pendiente",
            MissionObjectiveTypes.DiscoverArea =>
                int.TryParse(core.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                    ? $"Área #{a}"
                    : "Zona pendiente",
            MissionObjectiveTypes.DefeatMobs =>
                FormatDefeatMobSummary(MissionObjectiveArgsCodec.ParseDefeatMobPairs(objective.Args)),
            MissionObjectiveTypes.UseItem =>
                nums.Length >= 1 ? $"Objeto #{nums[0]}" : "Objeto pendiente",
            MissionObjectiveTypes.ReachLevel =>
                nums.Length >= 1 ? $"Nivel {nums[0]}" : "Nivel pendiente",
            MissionObjectiveTypes.HaveSpells =>
                nums.Length >= 1 ? $"{nums[0]} hechizo(s)" : "Hechizos pendiente",
            MissionObjectiveTypes.JobLevel =>
                nums.Length >= 2 ? $"{nums[0]} oficio(s) · nivel {nums[1]}" : "Oficios pendiente",
            MissionObjectiveTypes.Manual =>
                MissionObjectiveUiSync.UnsupportedManualUiLabel,
            _ => string.IsNullOrWhiteSpace(core) ? "—" : core,
        };
    }

    private static string FormatDefeatMobSummary(IReadOnlyList<(int MobId, int Qty)> pairs)
    {
        if (pairs.Count == 0)
            return "Monstruo pendiente";

        static string One(int mobId, int qty)
        {
            string? nombre = null;
            if (MobsModeloNameCache.Shared?.TryGetCached(mobId, out var cached) == true)
                nombre = cached;
            else if (MobsModeloNameCache.Shared is not null)
                nombre = MobsModeloNameCache.Shared.ResolveSync(mobId);

            if (string.IsNullOrWhiteSpace(nombre))
                return string.Create(CultureInfo.InvariantCulture, $"ID {mobId} · x{qty}");
            return string.Create(CultureInfo.InvariantCulture, $"{nombre} · ID {mobId} · x{qty}");
        }

        if (pairs.Count == 1)
            return One(pairs[0].MobId, pairs[0].Qty);
        return string.Join(" · ", pairs.Select(p => One(p.MobId, p.Qty)));
    }
}

public sealed class MissionRewardItemRowVm : ViewModelBase
{
    private int _itemId;
    private int _cantidad;
    private string _nombre = "";
    private string _meta = "";
    private ImageSource? _icon;

    public int ItemId
    {
        get => _itemId;
        set
        {
            if (SetProperty(ref _itemId, value))
            {
                OnPropertyChanged(nameof(ItemIdText));
                RefreshCatalog();
            }
        }
    }

    /// <summary>MISSIONS.REWARDS.MANUAL_ITEM_ROWS.2 — empty string when unset (not "0").</summary>
    public string ItemIdText
    {
        get => _itemId > 0 ? _itemId.ToString(CultureInfo.InvariantCulture) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_itemId == 0)
                    return;
                _itemId = 0;
                OnPropertyChanged(nameof(ItemId));
                OnPropertyChanged(nameof(ItemIdText));
                RefreshCatalog();
                return;
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                || id < 0)
                return;

            ItemId = id;
        }
    }

    public int Cantidad
    {
        get => _cantidad;
        set => SetProperty(ref _cantidad, Math.Max(1, value));
    }

    public string Nombre => _nombre;
    public string Meta => _meta;
    public ImageSource? Icon => _icon;
    public string IdLine => $"#{ItemId}";

    public string DetailLine =>
        string.IsNullOrWhiteSpace(Meta) ? IdLine : $"{IdLine} · {Meta}";

    public static MissionRewardItemRowVm CreateEmpty() =>
        FromModel(new MissionRewardItem { ItemId = 0, Cantidad = 1 });

    public static MissionRewardItemRowVm FromModel(MissionRewardItem m)
    {
        var vm = new MissionRewardItemRowVm { _itemId = m.ItemId, _cantidad = Math.Max(1, m.Cantidad) };
        vm.RefreshCatalog();
        return vm;
    }

    public MissionRewardItem ToModel() => new() { ItemId = ItemId, Cantidad = Cantidad };

    private void RefreshCatalog()
    {
        var it = VisualLibraryService.Shared.Items.FirstOrDefault(i => i.ItemId == ItemId);
        _nombre = it?.Nombre ?? (ItemId > 0 ? $"Item #{ItemId}" : "Sin objeto");
        _meta = it is null ? "" : $"{it.Category} · niv. {it.Level}";
        _icon = null;
        var iconPath = it?.IconFullPath;
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _icon = bmp;
            }
            catch { /* preview optional */ }
        }
        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IdLine));
        OnPropertyChanged(nameof(DetailLine));
    }
}
