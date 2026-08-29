using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.ViewModels;

public sealed partial class ContentMissionEditorViewModel
{
    private bool _loadingObjectiveFields;
    private string _objNpcLabel = "";
    private string _objItemLabel = "";
    private string _objMobLabel = "";
    private string _objAreaLabel = "";
    private string _objMapLabel = "";
    private string _objectiveValidation = "";
    private ImageSource? _objMobPreview;
    private ImageSource? _objItemPreview;

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
    }

    private void AddObjectiveOfSelectedType()
    {
        if (SelectedStage is null || !DbReady) return;
        var tipo = SelectedObjectiveType?.Tipo ?? MissionObjectiveTypes.Manual;
        if (tipo == MissionObjectiveTypes.DeliverSouls || !MissionObjectiveTypes.IsUiNormal(tipo))
        {
            ObjectiveValidation = "Entregar almas aún no está disponible (dato pendiente).";
            return;
        }

        var o = Workspace.Missions.AddObjective(SelectedStage, tipo);
        o.EsAlHablar = "0";
        o.EsOculto = 0;
        SelectedObjective = o;
        SyncObjectiveRows();
        AutoApplyObjectiveFields();
        Selected?.Refresh();
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
                    ObjNpcId = nums.Length >= 1 ? nums[0].ToString(CultureInfo.InvariantCulture) : "";
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
            ObjectiveValidation = "";
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

        if (int.TryParse(ObjItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) && itemId > 0)
        {
            var it = VisualLibraryService.Shared.Items.FirstOrDefault(i => i.ItemId == itemId);
            ObjItemLabel = it is null
                ? $"Objeto #{itemId}"
                : $"{it.Nombre}  #{itemId}";
            ObjItemPreview = TryLoadIcon(it?.IconFullPath);
        }
        else
        {
            ObjItemLabel = "";
            ObjItemPreview = null;
        }

        if (int.TryParse(ObjMobId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobId) && mobId > 0)
        {
            var m = VisualLibraryService.Shared.Monsters.FirstOrDefault(x => x.Id == mobId);
            ObjMobLabel = m is null
                ? $"Mob #{mobId}"
                : $"{m.Nombre}  #{mobId}";
            ObjMobPreview = TryLoadIcon(m?.ArtworkFullPath ?? m?.SpriteFullPath);
        }
        else
        {
            ObjMobLabel = "";
            ObjMobPreview = null;
        }

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
        var fields = CaptureUiFields();
        fields.Tipo = SelectedObjective.Tipo;
        var err = MissionObjectiveUiSync.TryApply(SelectedObjective, fields);
        ObjectiveValidation = err ?? "";
        OnPropertyChanged(nameof(HasObjectiveValidation));
        OnPropertyChanged(nameof(ObjectiveArgsPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        SyncObjectiveRows();
        Selected?.Refresh();
        if (err is null)
            Persist();
    }

    private MissionObjectiveUiFields CaptureUiFields() => new()
    {
        Tipo = SelectedObjective?.Tipo ?? 0,
        ManualText = ObjManualText,
        NpcId = ObjNpcId,
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

    // Kept for any remaining callers; normal UX uses AutoApplyObjectiveFields.
    private void ApplyObjectiveEditorFields() => AutoApplyObjectiveFields();

    private void OnObjectiveFieldChanged()
    {
        if (_loadingObjectiveFields) return;
        AutoApplyObjectiveFields();
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

    private async Task PickObjectiveItemAsync()
    {
        StatusText = "Cargando catálogo de objetos…";
        await VisualLibraryBootstrap.EnsureItemsAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.ItemsLoaded)
        {
            MessageBox.Show("No se pudo cargar el catálogo de objetos.", "Buscar objeto");
            StatusText = "No se pudo cargar el catálogo de objetos.";
            return;
        }

        var qty = int.TryParse(ObjQty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) && q > 0 ? q : 1;
        var dlg = new ItemPickerWindow(VisualLibraryService.Shared, ObjItemId, qty) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;
        _loadingObjectiveFields = true;
        ObjItemId = dlg.SelectedEntry.ItemId.ToString(CultureInfo.InvariantCulture);
        if (ShowQtyField)
            ObjQty = dlg.SelectedQuantity.ToString(CultureInfo.InvariantCulture);
        ObjItemLabel = $"{dlg.SelectedEntry.Nombre}  #{dlg.SelectedEntry.ItemId}";
        ObjItemPreview = TryLoadIcon(dlg.SelectedEntry.IconFullPath);
        _loadingObjectiveFields = false;
        AutoApplyObjectiveFields();
        StatusText = ObjItemLabel;
    }

    private async Task PickObjectiveMobAsync()
    {
        StatusText = "Cargando catálogo de monstruos…";
        await VisualLibraryBootstrap.EnsureMonstersAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.MonstersLoaded)
        {
            MessageBox.Show("No se pudo cargar el catálogo de monstruos.", "Buscar monstruo");
            return;
        }

        var dlg = new MonsterPickerWindow(VisualLibraryService.Shared, ObjMobId) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;
        _loadingObjectiveFields = true;
        ObjMobId = dlg.SelectedEntry.Id.ToString(CultureInfo.InvariantCulture);
        ObjMobLabel = $"{dlg.SelectedEntry.Nombre}  #{dlg.SelectedEntry.Id}";
        ObjMobPreview = TryLoadIcon(dlg.SelectedEntry.ArtworkFullPath ?? dlg.SelectedEntry.SpriteFullPath);
        _loadingObjectiveFields = false;
        AutoApplyObjectiveFields();
        StatusText = ObjMobLabel;
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
        foreach (var o in SelectedStage.Objectives)
            ObjectiveRows.Add(new MissionObjectiveRowVm(i++, o, ResolveNpcName));
    }

    private void SyncRewardItemRows()
    {
        RewardItemRows.Clear();
        if (SelectedStage is null) return;
        foreach (var o in SelectedStage.Rewards.Objetos)
            RewardItemRows.Add(MissionRewardItemRowVm.FromModel(o));
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

public sealed class MissionObjectiveRowVm
{
    public MissionObjectiveRowVm(int ordinal, MissionObjectiveDraft objective, Func<int?, string> npcName)
    {
        Ordinal = ordinal;
        Objective = objective;
        var (core, _, _) = MissionObjectiveArgsCodec.StripCoords(objective.Args);
        var nums = MissionObjectiveArgsCodec.ParseBracketInts(core);
        TypeLabel = MissionObjectiveUiSync.UiTypeLabel(objective.Tipo);
        Summary = BuildSummary(objective.Tipo, core, nums, npcName);
        IdHint = $"#{objective.Id} provisional";
    }

    public int Ordinal { get; }
    public MissionObjectiveDraft Objective { get; }
    public string TypeLabel { get; }
    public string Summary { get; }
    public string IdHint { get; }
    public string Title => $"{Ordinal}. {TypeLabel}";

    private static string BuildSummary(int tipo, string core, int[] nums, Func<int?, string> npcName)
    {
        return tipo switch
        {
            MissionObjectiveTypes.TalkToNpc or MissionObjectiveTypes.ReturnToNpc =>
                nums.Length >= 1 ? $"NPC: {npcName(nums[0])}" : "NPC: pendiente",
            MissionObjectiveTypes.ShowItemToNpc or MissionObjectiveTypes.DeliverItemsToNpc =>
                nums.Length >= 3
                    ? $"{npcName(nums[0])} · objeto #{nums[1]} ×{nums[2]}"
                    : "NPC / objeto: pendiente",
            MissionObjectiveTypes.DiscoverMap =>
                int.TryParse(core.Trim(), out var m) ? $"Map ID {m}" : "Mapa: pendiente",
            MissionObjectiveTypes.DiscoverArea =>
                int.TryParse(core.Trim(), out var a) ? $"Área #{a}" : "Zona: pendiente",
            MissionObjectiveTypes.DefeatMobs =>
                nums.Length >= 2 ? $"Mob #{nums[0]} ×{nums[1]}" : "Monstruo: pendiente",
            MissionObjectiveTypes.UseItem =>
                nums.Length >= 1 ? $"Objeto #{nums[0]}" : "Objeto: pendiente",
            MissionObjectiveTypes.ReachLevel =>
                nums.Length >= 1 ? $"Nivel {nums[0]}" : "Nivel: pendiente",
            MissionObjectiveTypes.HaveSpells =>
                nums.Length >= 1 ? $"{nums[0]} hechizo(s)" : "Hechizos: pendiente",
            MissionObjectiveTypes.JobLevel =>
                nums.Length >= 2 ? $"{nums[0]} oficio(s) · nivel {nums[1]}" : "Oficios: pendiente",
            MissionObjectiveTypes.Manual =>
                string.IsNullOrWhiteSpace(core) ? "Descripción pendiente" : "Manual",
            _ => string.IsNullOrWhiteSpace(core) ? "—" : core,
        };
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
                RefreshCatalog();
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

    public static MissionRewardItemRowVm FromModel(MissionRewardItem m)
    {
        var vm = new MissionRewardItemRowVm { _itemId = m.ItemId, _cantidad = m.Cantidad };
        vm.RefreshCatalog();
        return vm;
    }

    public MissionRewardItem ToModel() => new() { ItemId = ItemId, Cantidad = Cantidad };

    private void RefreshCatalog()
    {
        var it = VisualLibraryService.Shared.Items.FirstOrDefault(i => i.ItemId == ItemId);
        _nombre = it?.Nombre ?? (ItemId > 0 ? $"Objeto #{ItemId}" : "Sin objeto");
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
    }
}
