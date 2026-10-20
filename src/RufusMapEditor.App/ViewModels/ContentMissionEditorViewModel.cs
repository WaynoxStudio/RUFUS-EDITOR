using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.ViewModels;

public sealed partial class ContentMissionEditorViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace? _injected;
    private readonly Func<IMisionEtapasReadRepository>? _stageRepoFactory;
    private readonly Func<IMisionObjetivosReadRepository>? _objRepoFactory;
    private MissionListItemVm? _selected;
    private bool _localDraftDirty;
    private MissionStageDraft? _selectedStage;
    private MissionObjectiveDraft? _selectedObjective;
    private string _statusText = "Borradores locales de misiones";
    private bool _dbReady;
    private bool _isBusy;
    private bool _advancedRewards;
    private int _dbMaxStage;
    private int _dbMaxObjective;
    private string _deliverNpcId = "";
    private string _deliverItemId = "";
    private string _deliverQty = "1";
    private ContentDbCatalogSnapshot? _dbCatalog;
    private string _dbCatalogStatus = "";

    private ContentDraftWorkspace Workspace => _injected ?? ContentDraftStore.Current;

    public ContentMissionEditorViewModel(
        ContentDraftWorkspace? workspace = null,
        Func<IMisionEtapasReadRepository>? stageRepoFactory = null,
        Func<IMisionObjetivosReadRepository>? objRepoFactory = null)
    {
        _injected = workspace;
        _stageRepoFactory = stageRepoFactory;
        _objRepoFactory = objRepoFactory;

        Items = new ObservableCollection<MissionListItemVm>();
        FlowStages = new ObservableCollection<MissionFlowNodeVm>();
        NpcChoices = new ObservableCollection<NpcsModeloDraft>();
        QuestionChoices = new ObservableCollection<MissionQuestionChoiceItem>();
        DbMissionCatalog = new ObservableCollection<DbMissionCatalogItemVm>();
        RewardItems = new ObservableCollection<MissionRewardItem>();

        RefreshMaxCommand = new RelayCommand(async () => await RefreshMaxAsync(), () => !IsBusy);
        ReloadCommand = new RelayCommand(Reload);
        NewMissionCommand = new RelayCommand(CreateMission, () => !IsBusy);
        DuplicateMissionCommand = new RelayCommand(DuplicateMission, () => Selected is not null && !IsBusy);
        SaveDraftCommand = new RelayCommand(SaveDraft, () => Selected is not null && !IsBusy);
        DeleteMissionCommand = new RelayCommand(DeleteMission, () => Selected is not null && !IsBusy);
        AddStageCommand = new RelayCommand(AddStage, () => Selected is not null && !IsBusy);
        DuplicateStageCommand = new RelayCommand(DuplicateStage, () => SelectedStage is not null && !IsBusy);
        DeleteStageCommand = new RelayCommand(DeleteStage, () => SelectedStage is not null && !IsBusy);
        MoveStageUpCommand = new RelayCommand(() => MoveStage(-1), () => SelectedStage is not null && !IsBusy);
        MoveStageDownCommand = new RelayCommand(() => MoveStage(1), () => SelectedStage is not null && !IsBusy);
        AddDeliverObjectiveCommand = new RelayCommand(AddDeliverObjective, () => SelectedStage is not null && !IsBusy);
        AddAdvancedObjectiveCommand = new RelayCommand(AddAdvancedObjective, () => SelectedStage is not null && !IsBusy);
        AddObjectiveCommand = new RelayCommand(AddObjectiveOfSelectedType, () => SelectedStage is not null && !IsBusy);
        BeginAddObjectiveCommand = new RelayCommand(BeginAddObjective, () => SelectedStage is not null && !IsBusy);
        DeleteObjectiveCommand = new RelayCommand(
            DeleteObjective,
            () => SelectedObjective is not null && !SelectedObjective.IsAutoReturnToStartNpc && !IsBusy);
        ApplyObjectiveFieldsCommand = new RelayCommand(ApplyObjectiveEditorFields, () => SelectedObjective is not null && !IsBusy);
        AddRewardItemCommand = new RelayCommand(AddRewardItem, () => SelectedStage is not null && !IsBusy);
        AddManualRewardItemCommand = new RelayCommand(AddManualRewardItem, () => SelectedStage is not null && !IsBusy);
        RemoveRewardItemCommand = new RelayCommand(RemoveRewardItem, () => SelectedRewardRow is not null && !IsBusy);
        PickDeliverItemCommand = new RelayCommand(async () => await PickDeliverItemAsync(), () => !IsBusy);
        PickRewardItemCommand = new RelayCommand(async () => await PickRewardItemAsync(), () => SelectedStage is not null && !IsBusy);
        PickObjectiveNpcCommand = new RelayCommand(async () => await PickObjectiveNpcAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveAreaCommand = new RelayCommand(async () => await PickObjectiveAreaAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveMapCommand = new RelayCommand(PickObjectiveMap, () => SelectedObjective is not null && !IsBusy);
        EnsureUxCommands();
        EnsureDialogRoleCommands();

        foreach (var t in MissionObjectiveTypes.UiNormalTypes)
            ObjectiveTypeChoices.Add(new ObjectiveTypeChoice(t, MissionObjectiveUiSync.UiTypeLabel(t)));
        SelectedObjectiveType = ObjectiveTypeChoices.FirstOrDefault(c => c.Tipo == MissionObjectiveTypes.TalkToNpc)
                                ?? ObjectiveTypeChoices.FirstOrDefault();

        Reload();
    }

    public RelayCommand AddObjectiveCommand { get; }
    public RelayCommand BeginAddObjectiveCommand { get; }
    public RelayCommand ApplyObjectiveFieldsCommand { get; }
    public RelayCommand PickObjectiveNpcCommand { get; }
    public RelayCommand PickObjectiveAreaCommand { get; }
    public RelayCommand PickObjectiveMapCommand { get; }
    public ObservableCollection<ObjectiveTypeChoice> ObjectiveTypeChoices { get; } = new();

    public ObservableCollection<MissionListItemVm> Items { get; }
    public event Action? DraftEdited;

    private bool _embeddedCompactMode;
    public bool EmbeddedCompactMode
    {
        get => _embeddedCompactMode;
        set => SetProperty(ref _embeddedCompactMode, value);
    }

    private bool _actionInlineMode;
    private bool _refreshingQuestionChoices;
    public bool ActionInlineMode
    {
        get => _actionInlineMode;
        set
        {
            if (!SetProperty(ref _actionInlineMode, value))
                return;
            RefreshDebugMissionState();
        }
    }

    public ObservableCollection<MissionFlowNodeVm> FlowStages { get; }
    public ObservableCollection<NpcsModeloDraft> NpcChoices { get; }
    public ObservableCollection<MissionQuestionChoiceItem> QuestionChoices { get; }
    public ObservableCollection<DbMissionCatalogItemVm> DbMissionCatalog { get; }
    public ObservableCollection<MissionRewardItem> RewardItems { get; }

    public MissionListItemVm? Selected
    {
        get => _selected;
        set
        {
            var currentId = _selected?.Model.DraftId;
            var nextId = value?.Model.DraftId;
            if (MissionDraftNavigationGuard.RequiresUnsavedPrompt(currentId, _localDraftDirty, nextId))
            {
                var r = MessageBox.Show(
                    "Hay cambios sin guardar en el borrador actual.\n\n¿Guardar antes de cambiar?",
                    "Cambios sin guardar",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel)
                    return;
                if (r == MessageBoxResult.Yes)
                    SaveDraft();
                else
                    _localDraftDirty = false;
            }

            if (!SetProperty(ref _selected, value))
            {
                if (value is not null)
                    RefreshDialogRoleBindings();
                return;
            }
            SelectedStage = value?.Model.Stages.FirstOrDefault();
            RefreshDialogRoleBindings();
            RebuildFlow();
            SyncStageRows();
            SyncRewardItems();
            NotifyDialogRoleUi();
            OnPropertyChanged(nameof(EditorEnabled));
            OnPropertyChanged(nameof(QuestIdLabel));
            OnPropertyChanged(nameof(PregDarPreview));
            OnPropertyChanged(nameof(PregIncPreview));
            OnPropertyChanged(nameof(PregCompPreview));
            OnPropertyChanged(nameof(RewardsRawPreview));
            OnPropertyChanged(nameof(TechnicalPreview));
            RaiseMissionCommands();
        }
    }

    /// <summary>True when local mission editor has edits since last explicit SaveDraft.</summary>
    public bool HasUnsavedLocalDraft => _localDraftDirty && Selected is not null;

    /// <summary>
    /// Confirm leave (close ADMIN / abandon workspace). Returns false if user cancels.
    /// Does not clear dirty on Cancel; Yes saves; No discards local dirty flag only.
    /// </summary>
    public bool ConfirmLeaveIfDirty()
    {
        if (!HasUnsavedLocalDraft)
            return true;
        var r = MessageBox.Show(
            "Hay cambios sin guardar en el borrador actual.\n\n¿Guardar antes de salir?",
            "Cambios sin guardar",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel)
            return false;
        if (r == MessageBoxResult.Yes)
            SaveDraft();
        else
            _localDraftDirty = false;
        return true;
    }

    /// <summary>CONTENT.LIBRARY.ORGANIZE.DELETE.2 — descarta dirty sin modal genérico de navegación.</summary>
    public void DiscardUnsavedLocalDraftFlag() => _localDraftDirty = false;

    public MissionStageDraft? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (!SetProperty(ref _selectedStage, value))
                return;
            if (Selected is { Model: var mission })
                SyncAutoReturnsFor(mission);
            SelectedObjective = value?.Objectives.FirstOrDefault(o => !o.IsAutoReturnToStartNpc);
            SyncRewardItems();
            SyncStageRows();
            SyncObjectiveRows();
            RebuildFlow();
            OnPropertyChanged(nameof(HasStage));
            OnPropertyChanged(nameof(RewardsRawPreview));
            OnPropertyChanged(nameof(SelectedStageProvisionalLabel));
            OnPropertyChanged(nameof(SelectedStageHeaderTitle));
            OnPropertyChanged(nameof(StageNameValidation));
            OnPropertyChanged(nameof(HasStageNameValidation));
            OnPropertyChanged(nameof(StageTechnicalPreview));
            OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
            OnPropertyChanged(nameof(ShowAutoReturnBanner));
            OnPropertyChanged(nameof(AutoReturnBannerText));
            OnPropertyChanged(nameof(SelectedStageRow));
            OnPropertyChanged(nameof(StageIndexLabel));
            OnPropertyChanged(nameof(StageSummaryLine));
            OnPropertyChanged(nameof(Step1Status));
            OnPropertyChanged(nameof(Step2Status));
            OnPropertyChanged(nameof(Step3Status));
            OnPropertyChanged(nameof(Step4Status));
            OnPropertyChanged(nameof(Step5Status));
            OnPropertyChanged(nameof(CanNavigatePrevStage));
            OnPropertyChanged(nameof(CanNavigateNextStage));
            PrevStageCommand?.RaiseCanExecuteChanged();
            NextStageCommand?.RaiseCanExecuteChanged();
            RaiseStageCommands();
        }
    }

    public MissionObjectiveDraft? SelectedObjective
    {
        get => _selectedObjective;
        set
        {
            if (SetProperty(ref _selectedObjective, value))
            {
                LoadObjectiveEditorFromSelected();
                OnPropertyChanged(nameof(HasObjective));
                OnPropertyChanged(nameof(ObjectiveTypeLabel));
                OnPropertyChanged(nameof(ShowCoordsCheckbox));
                OnPropertyChanged(nameof(ShowCoordsPanel));
                OnPropertyChanged(nameof(ShowNpcFields));
                OnPropertyChanged(nameof(ShowBoundStartNpcInfo));
                OnPropertyChanged(nameof(BoundStartNpcDisplay));
                OnPropertyChanged(nameof(BoundStartNpcMissing));
                OnPropertyChanged(nameof(ShowItemFields));
                OnPropertyChanged(nameof(ShowQtyField));
                OnPropertyChanged(nameof(ShowMobFields));
                OnPropertyChanged(nameof(ShowMapFields));
                OnPropertyChanged(nameof(HideInlineObjectiveEditor));
                OnPropertyChanged(nameof(ShowAreaFields));
                OnPropertyChanged(nameof(ShowManualFields));
                OnPropertyChanged(nameof(ShowUnsupportedManual));
                OnPropertyChanged(nameof(UnsupportedManualBanner));
                OnPropertyChanged(nameof(ShowLevelFields));
                OnPropertyChanged(nameof(ShowSpellFields));
                OnPropertyChanged(nameof(ShowJobFields));
                OnPropertyChanged(nameof(ShowItemSemanticsNote));
                OnPropertyChanged(nameof(ItemSemanticsNote));
                OnPropertyChanged(nameof(ObjectiveArgsPreview));
                OnPropertyChanged(nameof(TechnicalPreview));
                OnPropertyChanged(nameof(SelectedObjectiveRow));
                OnPropertyChanged(nameof(HasObjectiveValidation));
                DeleteObjectiveCommand.RaiseCanExecuteChanged();
                ApplyObjectiveFieldsCommand.RaiseCanExecuteChanged();
                PickObjectiveNpcCommand.RaiseCanExecuteChanged();
                PickObjectiveAreaCommand.RaiseCanExecuteChanged();
                PickObjectiveMapCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public MissionObjectiveRowVm? SelectedObjectiveRow
    {
        get => ObjectiveRows.FirstOrDefault(r => ReferenceEquals(r.Objective, SelectedObjective));
        set
        {
            if (value is not null)
                SelectedObjective = value.Objective;
        }
    }

    public MissionStageRowVm? SelectedStageRow
    {
        get => StageRows.FirstOrDefault(r => ReferenceEquals(r.Stage, SelectedStage));
        set
        {
            if (value is not null)
                SelectedStage = value.Stage;
        }
    }

    public MissionRewardItem? SelectedRewardItem { get; set; }

    private ObjectiveTypeChoice? _selectedObjectiveType;
    public ObjectiveTypeChoice? SelectedObjectiveType
    {
        get => _selectedObjectiveType;
        set => SetProperty(ref _selectedObjectiveType, value);
    }

    private bool _showObjectiveTypePicker;
    public bool ShowObjectiveTypePicker
    {
        get => _showObjectiveTypePicker;
        set => SetProperty(ref _showObjectiveTypePicker, value);
    }

    private string _objManualText = "";
    private string _objNpcId = "";
    private string _objItemId = "";
    private string _objQty = "1";
    private string _objMobId = "";
    private string _objMapId = "";
    private string _objAreaId = "";
    private string _objLevel = "1";
    private string _objSpellCount = "1";
    private string _objJobCount = "1";
    private string _objJobLevel = "1";
    private bool _objRestrictCoords;
    private string _objX = "";
    private string _objY = "";
    private bool _showTechnical;

    public string ObjManualText
    {
        get => _objManualText;
        set { if (SetProperty(ref _objManualText, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjNpcId
    {
        get => _objNpcId;
        set { if (SetProperty(ref _objNpcId, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjItemId
    {
        get => _objItemId;
        set { if (SetProperty(ref _objItemId, value)) OnObjItemIdChanged(); }
    }
    public string ObjQty
    {
        get => _objQty;
        set { if (SetProperty(ref _objQty, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjMobId
    {
        get => _objMobId;
        set { if (SetProperty(ref _objMobId, value)) OnObjMobIdChanged(); }
    }
    public string ObjMapId
    {
        get => _objMapId;
        set { if (SetProperty(ref _objMapId, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjAreaId
    {
        get => _objAreaId;
        set { if (SetProperty(ref _objAreaId, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjLevel
    {
        get => _objLevel;
        set { if (SetProperty(ref _objLevel, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjSpellCount
    {
        get => _objSpellCount;
        set { if (SetProperty(ref _objSpellCount, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjJobCount
    {
        get => _objJobCount;
        set { if (SetProperty(ref _objJobCount, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjJobLevel
    {
        get => _objJobLevel;
        set { if (SetProperty(ref _objJobLevel, value)) OnObjectiveFieldChanged(); }
    }
    public bool ObjRestrictCoords
    {
        get => _objRestrictCoords;
        set
        {
            if (SetProperty(ref _objRestrictCoords, value))
            {
                OnPropertyChanged(nameof(ShowCoordsPanel));
                OnObjectiveFieldChanged();
            }
        }
    }
    public string ObjX
    {
        get => _objX;
        set { if (SetProperty(ref _objX, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjY
    {
        get => _objY;
        set { if (SetProperty(ref _objY, value)) OnObjectiveFieldChanged(); }
    }
    public bool ShowTechnical { get => _showTechnical; set => SetProperty(ref _showTechnical, value); }

    public string ObjectiveTypeLabel =>
        SelectedObjective is null ? "" : MissionObjectiveUiSync.UiTypeLabel(SelectedObjective.Tipo);

    public string SelectedStageHeaderTitle =>
        SelectedStage is null ? "ETAPA SELECCIONADA" : $"ETAPA SELECCIONADA · #{SelectedStage.Id}";

    public string SelectedStageProvisionalLabel =>
        SelectedStage is null ? "" : $"ID provisional #{SelectedStage.Id}";

    // ── STEP-BY-STEP UI PROPERTIES ──

    public string StageIndexLabel
    {
        get
        {
            if (Selected is null || SelectedStage is null) return "";
            var idx = Selected.Model.Stages.IndexOf(SelectedStage);
            return idx >= 0 ? $"Etapa {idx + 1} de {Selected.Model.Stages.Count}" : "";
        }
    }

    public string StageSummaryLine
    {
        get
        {
            if (SelectedStage is null) return "";
            var name = string.IsNullOrWhiteSpace(SelectedStage.Nombre) ? "Sin nombre" : SelectedStage.Nombre;
            var objs = SelectedStage.Objectives.Count;
            var hasRewards = (SelectedStage.Rewards.Exp > 0 || SelectedStage.Rewards.Kamas > 0 || SelectedStage.Rewards.Objetos.Count > 0);
            return $"{name} · {objs} objetivo(s) · Recompensas: {(hasRewards ? "Sí" : "No")}";
        }
    }

    // Step statuses follow CompactLayoutOnly order:
    // 1 info · 2 objetivos · 3 recompensas básicas · 4 recompensas avanzadas · 5 técnico
    public string Step1Status => SelectedStage is null ? ""
        : (!string.IsNullOrWhiteSpace(SelectedStage.Nombre) ? "✓ Completo" : "⏳ Pendiente");

    public string Step2Status => SelectedStage is null ? ""
        : (SelectedStage.Objectives.Any(o => !o.IsAutoReturnToStartNpc) ? "✓ Completo" : "⏳ Pendiente");

    public bool ShowAutoReturnBanner =>
        Selected is not null
        && SelectedStage is not null
        && !Selected.Model.PublishedBd
        && (MissionAutoReturnSync.HasDiscoverMap(SelectedStage)
            || MissionAutoReturnSync.FindAutoReturn(SelectedStage) is not null
            || MissionAutoReturnSync.NeedsCoordsForAutoReturn(
                Selected.Model, SelectedStage, ResolveNpcWorldCoords));

    public string AutoReturnBannerText
    {
        get
        {
            if (Selected is null || SelectedStage is null) return "";
            if (!MissionAutoReturnSync.HasDiscoverMap(SelectedStage)
                && MissionAutoReturnSync.FindAutoReturn(SelectedStage) is null
                && !MissionAutoReturnSync.NeedsCoordsForAutoReturn(
                    Selected.Model, SelectedStage, ResolveNpcWorldCoords))
                return "";

            if (MissionAutoReturnSync.NeedsStartNpcForAutoReturn(Selected.Model, SelectedStage))
                return "↳ Final automático: DATO PENDIENTE DE CONFIRMAR · NPC inicial";

            if (MissionAutoReturnSync.NeedsCoordsForAutoReturn(
                    Selected.Model, SelectedStage, ResolveNpcWorldCoords))
                return "↳ Final automático: " + MissionAutoReturnSync.PendingCoordsMessage;

            var label = MissionAutoReturnSync.FormatAutoReturnLabel(
                Selected.Model.StartNpcId, ResolveNpcName);
            return "↳ Final automático: " + label;
        }
    }

    /// <summary>AUDIT_FIX.4 / REPAIR.6 — mapId → mapas.X/Y (wired by ContentWorkspaceViewModel).</summary>
    public Func<int, (int X, int Y)?>? MapWorldCoordsLookup { get; set; }

    /// <summary>npcId → npcs_ubicacion.mapa (BD, optional).</summary>
    public Func<int, int?>? BdNpcMapIdLookup { get; set; }

    private (int X, int Y)? ResolveNpcWorldCoords(int npcId) =>
        MissionAutoReturnSync.TryResolveNpcWorldCoords(
            npcId, Workspace.Npcs.Drafts, MapWorldCoordsLookup, BdNpcMapIdLookup);

    private bool SyncAutoReturnsFor(MissionDraft mission)
    {
        var auto = Workspace.Missions.SyncAutoReturns(mission, ResolveNpcWorldCoords);
        var showDeliver = Workspace.Missions.SyncShowDeliverToStartNpc(mission);
        return auto || showDeliver;
    }

    /// <summary>Called when StartNpc MapId changes — resync X/Y on unpublished auto-return.</summary>
    public void SyncAutoReturnsForNpcLocationChange(MissionDraft mission)
    {
        if (SyncAutoReturnsFor(mission))
        {
            Persist();
            SyncObjectiveRows();
            OnPropertyChanged(nameof(ShowAutoReturnBanner));
            OnPropertyChanged(nameof(AutoReturnBannerText));
            Selected?.Refresh();
        }
    }

    public string Step3Status
    {
        get
        {
            if (SelectedStage is null) return "";
            var r = SelectedStage.Rewards;
            return (r.Exp > 0 || r.Kamas > 0 || r.Objetos.Count > 0) ? "✓ Completo" : "⏳ Pendiente";
        }
    }

    public string Step4Status
    {
        get
        {
            if (SelectedStage is null) return "";
            var r = SelectedStage.Rewards;
            var has = !string.IsNullOrWhiteSpace(r.Emotes) || !string.IsNullOrWhiteSpace(r.Hechizos)
                   || !string.IsNullOrWhiteSpace(r.Oficios) || !string.IsNullOrWhiteSpace(r.Acciones);
            return has ? "✓ Configurado" : "— Opcional";
        }
    }

    public string Step5Status => "— Opcional";

    public bool CanNavigatePrevStage =>
        Selected is not null && SelectedStage is not null
        && Selected.Model.Stages.IndexOf(SelectedStage) > 0;

    public bool CanNavigateNextStage =>
        Selected is not null && SelectedStage is not null
        && Selected.Model.Stages.IndexOf(SelectedStage) < Selected.Model.Stages.Count - 1;

    public RelayCommand PrevStageCommand { get; private set; } = null!;
    public RelayCommand NextStageCommand { get; private set; } = null!;

    public string StageNameValidation =>
        SelectedStage is null ? "" : MissionObjectiveUiSync.ValidateStageName(SelectedStage.Nombre);

    public bool HasStageNameValidation => !string.IsNullOrWhiteSpace(StageNameValidation);

    public bool ShowCoordsCheckbox =>
        SelectedObjective is not null && MissionObjectiveTypes.SupportsCoordinates(SelectedObjective.Tipo);

    public bool ShowCoordsPanel => ShowCoordsCheckbox && ObjRestrictCoords;

    public bool ShowNpcFields =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.TalkToNpc
            or MissionObjectiveTypes.ReturnToNpc;

    public bool ShowBoundStartNpcInfo =>
        SelectedObjective is not null
        && MissionShowDeliverNpcBinding.IsBoundTipo(SelectedObjective.Tipo);

    public string BoundStartNpcDisplay
    {
        get
        {
            if (!ShowBoundStartNpcInfo) return "";
            if (Selected?.Model.StartNpcId is not int sid || sid <= 0)
                return MissionShowDeliverNpcBinding.MissingStartNpcMessage;
            var name = ResolveNpcName(sid);
            return string.IsNullOrWhiteSpace(name) || name.StartsWith("NPC #", StringComparison.Ordinal)
                ? $"NPC #{sid}"
                : name;
        }
    }

    public bool BoundStartNpcMissing =>
        ShowBoundStartNpcInfo
        && (Selected?.Model.StartNpcId is not int sid || sid <= 0);

    public bool ShowItemFields =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc
            or MissionObjectiveTypes.UseItem;

    public bool ShowQtyField =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc;

    /// <summary>DefeatMobs fields live inline on the objective row.</summary>
    public bool ShowMobFields => false;
    public bool ShowMapFields => SelectedObjective?.Tipo == MissionObjectiveTypes.DiscoverMap;
    /// <summary>Bottom typed editor hidden when fields live inline on the objective row.</summary>
    public bool HideInlineObjectiveEditor =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.DiscoverMap
            or MissionObjectiveTypes.DefeatMobs;
    public bool ShowAreaFields => SelectedObjective?.Tipo == MissionObjectiveTypes.DiscoverArea;
    public bool ShowManualFields => false;

    public bool ShowUnsupportedManual =>
        SelectedObjective?.Tipo == MissionObjectiveTypes.Manual;

    public string UnsupportedManualBanner => MissionObjectiveUiSync.UnsupportedManualUiLabel;
    public bool ShowLevelFields => SelectedObjective?.Tipo == MissionObjectiveTypes.ReachLevel;
    public bool ShowSpellFields => SelectedObjective?.Tipo == MissionObjectiveTypes.HaveSpells;
    public bool ShowJobFields => SelectedObjective?.Tipo == MissionObjectiveTypes.JobLevel;

    public bool ShowItemSemanticsNote =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc;

    public string ItemSemanticsNote => SelectedObjective?.Tipo switch
    {
        MissionObjectiveTypes.ShowItemToNpc =>
            "Enseñar objeto: el personaje muestra el ítem; no se elimina del inventario.",
        MissionObjectiveTypes.DeliverItemsToNpc =>
            "Entregar objeto: los objetos se entregan/eliminan según la lógica del servidor.",
        _ => "",
    };

    public string ObjectiveArgsPreview => SelectedObjective?.Args ?? "";
    public string QuestsEsStatusLine => Selected?.Model switch
    {
        null => "Cliente quests_es: —",
        { QuestsEsPublished: true } m =>
            string.Create(CultureInfo.InvariantCulture,
                $"Cliente quests_es: ✓ Publicado (v{m.QuestsEsPublishedVersion})"),
        { PublishedBd: true, PublishedQuestId: not null } =>
            "Cliente quests_es: ⚠ Pendiente de publicación cliente",
        _ => "Cliente quests_es: ○ Requiere publicación BD primero",
    };
    public string TechnicalPreview
    {
        get
        {
            if (Selected is null) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"misión DraftId={Selected.Model.DraftId:N}");
            sb.AppendLine($"nombre={Selected.Model.Nombre}");
            if (SelectedStage is not null)
            {
                sb.AppendLine($"etapa id={SelectedStage.Id} · {SelectedStage.Nombre}");
                sb.AppendLine($"recompensas: {SelectedStage.Rewards.ToRaw()}");
                sb.AppendLine($"objetivos: {string.Join(",", SelectedStage.Objectives.Select(o => o.Id))}");
            }
            if (SelectedObjective is not null)
            {
                sb.AppendLine($"objetivo id={SelectedObjective.Id} tipo={SelectedObjective.Tipo}");
                sb.AppendLine($"args: {SelectedObjective.Args}");
                sb.AppendLine($"detalle: {SelectedObjective.Detalle}");
            }
            sb.AppendLine(QuestsEsStatusLine);
            return sb.ToString().TrimEnd();
        }
    }

    public bool EditorEnabled => ActionInlineMode
        ? ResolveDialogRoleMission() is not null
        : Selected is not null;
    public bool HasStage => SelectedStage is not null;
    public bool HasObjective => SelectedObjective is not null;

    public string QuestIdLabel => "Quest ID: se asignará al publicar";

    public string PregDarPreview => BuildRolePreview(ResolveDialogRoleMission()?.PregDarPreguntaId);
    public string PregIncPreview => BuildRolePreview(ResolveDialogRoleMission()?.PregIncompletaPreguntaId);
    public string PregCompPreview => BuildRolePreview(ResolveDialogRoleMission()?.PregCompletadaPreguntaId);
    public string RewardsRawPreview => SelectedStage?.Rewards.ToRaw() ?? "";

    public string StageTechnicalPreview =>
        SelectedStage is null ? "" : MissionStagesUi.BuildStageTechnicalPreview(SelectedStage);

    public bool ShowStageNoObjectivesHint =>
        SelectedStage is not null
        && !SelectedStage.Objectives.Any(o => !o.IsAutoReturnToStartNpc);

    public bool ShowStageNoRewardItemsHint =>
        SelectedStage is not null && RewardItemRows.Count == 0;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool DbReady
    {
        get => _dbReady;
        private set
        {
            if (SetProperty(ref _dbReady, value))
                RaiseStageCommands();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseMissionCommands();
        }
    }

    public bool AdvancedRewards
    {
        get => _advancedRewards;
        set => SetProperty(ref _advancedRewards, value);
    }

    public int DbMaxStage
    {
        get => _dbMaxStage;
        private set => SetProperty(ref _dbMaxStage, value);
    }

    public int DbMaxObjective
    {
        get => _dbMaxObjective;
        private set => SetProperty(ref _dbMaxObjective, value);
    }

    public string DeliverNpcId
    {
        get => _deliverNpcId;
        set => SetProperty(ref _deliverNpcId, value);
    }

    public string DeliverItemId
    {
        get => _deliverItemId;
        set => SetProperty(ref _deliverItemId, value);
    }

    public string DeliverQty
    {
        get => _deliverQty;
        set => SetProperty(ref _deliverQty, value);
    }

    /// <summary>Internal StartNpcId mirror; not exposed in UI (MISSIONS.DIALOG.ROLES.2).</summary>
    public int? StartNpcIdValue => ResolveDialogRoleMission()?.StartNpcId;

    public int? PregDarQuestionIdValue => ResolveDialogRoleMission()?.PregDarPreguntaId;

    public int? PregIncQuestionIdValue => ResolveDialogRoleMission()?.PregIncompletaPreguntaId;

    public int? PregCompQuestionIdValue => ResolveDialogRoleMission()?.PregCompletadaPreguntaId;

    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand NewMissionCommand { get; }
    public RelayCommand DuplicateMissionCommand { get; }
    public RelayCommand SaveDraftCommand { get; }
    public RelayCommand DeleteMissionCommand { get; }
    public RelayCommand AddStageCommand { get; }
    public RelayCommand DuplicateStageCommand { get; }
    public RelayCommand DeleteStageCommand { get; }
    public RelayCommand MoveStageUpCommand { get; }
    public RelayCommand MoveStageDownCommand { get; }
    public RelayCommand AddDeliverObjectiveCommand { get; }
    public RelayCommand AddAdvancedObjectiveCommand { get; }
    public RelayCommand DeleteObjectiveCommand { get; }
    public RelayCommand AddRewardItemCommand { get; }
    public RelayCommand AddManualRewardItemCommand { get; }
    public RelayCommand RemoveRewardItemCommand { get; }
    public RelayCommand PickDeliverItemCommand { get; }
    public RelayCommand PickRewardItemCommand { get; }

    public string ItemsCatalogStatus => VisualLibraryService.Shared.StatusItems;

    public string DbCatalogStatus
    {
        get => _dbCatalogStatus;
        private set => SetProperty(ref _dbCatalogStatus, value);
    }

    public bool HasDbMissionCatalog => DbMissionCatalog.Count > 0;

    public ContentDbCatalogSnapshot? DbCatalog => _dbCatalog;

    /// <summary>MISSIONS.ACTION44.SEED.1 — sync UI after external seed on linked draft.</summary>
    public void SyncFromMissionDraft(Guid draftId)
    {
        if (Items.All(i => i.Model.DraftId != draftId))
            Reload();
        Selected = Items.FirstOrDefault(i => i.Model.DraftId == draftId);
        RefreshDialogRoleBindings();
        OnPropertyChanged(nameof(PregDarPreview));
        OnPropertyChanged(nameof(PregIncPreview));
        OnPropertyChanged(nameof(PregCompPreview));
    }

    public void LogDialogRoleDiagnostics(string context)
    {
        var mission = ResolveDialogRoleMission();
        var draftId = mission?.DraftId.ToString("N")[..8] ?? "—";
        var owner = OwnerNpcId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var start = mission?.StartNpcId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var pregDar = mission?.PregDarPreguntaId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var pregInc = mission?.PregIncompletaPreguntaId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var pregComp = mission?.PregCompletadaPreguntaId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        var linkedDraft = CurrentLinkedMission?.DraftId.ToString("N")[..8] ?? "—";
        var selectedDraft = Selected?.Model.DraftId.ToString("N")[..8] ?? "—";
        var bdNpc = _dbCatalog?.Npcs.Count ?? 0;
        var bdQ = _dbCatalog?.Questions.Count ?? 0;
        var bdM = _dbCatalog?.Missions.Count ?? 0;
        System.Diagnostics.Debug.WriteLine(
            $"[MissionDialogRoles/{context}] draft={draftId} linked={linkedDraft} selected={selectedDraft} " +
            $"owner={owner} start={start} pregDar={pregDar} pregInc={pregInc} pregComp={pregComp} " +
            $"npcChoices={NpcChoices.Count} qChoices={QuestionChoices.Count} " +
            $"bdNpc={bdNpc} bdQ={bdQ} bdM={bdM} catalogLoaded={_dbCatalog?.LoadedFromDatabase == true}");
    }

    /// <summary>MISSIONS.DB.READ.1 — apply RO BD snapshot; never mutates local drafts.</summary>
    public void ApplyDbCatalog(ContentDbCatalogSnapshot? catalog, string? errorMessage)
    {
        _dbCatalog = catalog?.LoadedFromDatabase == true ? catalog : null;
        DbMissionCatalog.Clear();
        if (_dbCatalog is not null)
        {
            foreach (var m in _dbCatalog.Missions.OrderBy(x => x.Id))
                DbMissionCatalog.Add(new DbMissionCatalogItemVm(m));
        }

        DbCatalogStatus = errorMessage ?? (_dbCatalog is null
            ? ""
            : $"Catálogo BD: {_dbCatalog.Npcs.Count} NPC · {_dbCatalog.Questions.Count} preg · {_dbCatalog.Missions.Count} misiones");
        RefreshDialogRoleBindings();
        OnPropertyChanged(nameof(HasDbMissionCatalog));
    }

    public async Task RefreshDbCatalogAsync(IContentDbCatalogReadRepository? repository = null)
    {
        if (repository is null)
        {
            ApplyDbCatalog(null, null);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await ContentDbCatalogReader.ReadAsync(repository).ConfigureAwait(true);
            if (!result.Success)
            {
                ApplyDbCatalog(null, "No se pudieron actualizar los catálogos de BD.");
                return;
            }

            ApplyDbCatalog(result.Snapshot, null);
        }
        catch
        {
            ApplyDbCatalog(null, "No se pudieron actualizar los catálogos de BD.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InitializeAsync()
    {
        Reload();
        await RefreshMaxAsync();
        await RefreshDbCatalogAsync(CreateDbCatalogRepo());
    }

    private static IContentDbCatalogReadRepository? CreateDbCatalogRepo()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlContentDbCatalogReadRepository(settings, password);
    }

    public void Reload()
    {
        var keepId = Selected?.Model.DraftId;
        var keepDirty = _localDraftDirty;
        Items.Clear();
        foreach (var m in Workspace.Missions.Missions)
            Items.Add(new MissionListItemVm(m, ResolveNpcName));

        if (keepId is Guid id)
        {
            // Rebind wrapper for same draft — must not trip unsaved prompt or clear dirty.
            var next = Items.FirstOrDefault(i => i.Model.DraftId == id);
            Selected = next;
            _localDraftDirty = keepDirty && next is not null;
        }

        RefreshDialogRoleBindings();
        StatusText = Items.Count == 0
            ? "Sin misiones. Crea una con + Nueva misión."
            : $"Misiones: {Items.Count} · próxima etapa = {Workspace.Missions.NextStageId} · próximo objetivo = {Workspace.Missions.NextObjectiveId}";
    }

    public void NotifyDbMaxApplied(int questMax, int stageMax, int objectiveMax)
    {
        Workspace.Missions.SetDbMaxQuestId(questMax);
        Workspace.Missions.SetDbMaxStageId(stageMax);
        Workspace.Missions.SetDbMaxObjectiveId(objectiveMax);
        DbMaxStage = stageMax;
        DbMaxObjective = objectiveMax;
        DbReady = true;
        StatusText =
            $"MAX(misiones.id) = {questMax} · próximo = {Workspace.Missions.NextQuestId} · " +
            $"etapas={stageMax}→{Workspace.Missions.NextStageId} · objetivos={objectiveMax}→{Workspace.Missions.NextObjectiveId}";
        // Persist MAX into the store without marking the open mission as "unsaved".
        Persist(markPendingBd: false, markLocalDirty: false);
    }

    public async Task RefreshMaxAsync()
    {
        IsBusy = true;
        try
        {
            var stageRepo = CreateStageRepo();
            var objRepo = CreateObjRepo();
            var questRepo = CreateQuestRepo();
            if (stageRepo is null || objRepo is null || questRepo is null)
            {
                DbReady = false;
                StatusText = "Configura MySQL para leer MAX(misiones / mision_etapas / mision_objetivos).";
                return;
            }

            var maxQ = await questRepo.GetMaxIdAsync().ConfigureAwait(true);
            var maxS = await stageRepo.GetMaxIdAsync().ConfigureAwait(true);
            var maxO = await objRepo.GetMaxIdAsync().ConfigureAwait(true);
            Workspace.Missions.SetDbMaxQuestId(maxQ);
            Workspace.Missions.SetDbMaxStageId(maxS);
            Workspace.Missions.SetDbMaxObjectiveId(maxO);
            DbMaxStage = maxS;
            DbMaxObjective = maxO;
            DbReady = true;
            StatusText =
                $"MAX misiones={maxQ}→{Workspace.Missions.NextQuestId} · etapas={maxS}→{Workspace.Missions.NextStageId} · objetivos={maxO}→{Workspace.Missions.NextObjectiveId} · solo lectura";
            Persist();
        }
        catch (Exception ex)
        {
            DbReady = false;
            StatusText = "Error leyendo MAX misiones: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NotifyEdited()
    {
        Selected?.Refresh();
        RebuildFlow();
        SyncStageRows();
        SyncObjectiveRows();
        OnPropertyChanged(nameof(RewardsRawPreview));
        OnPropertyChanged(nameof(PregDarPreview));
        OnPropertyChanged(nameof(PregIncPreview));
        OnPropertyChanged(nameof(PregCompPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        OnPropertyChanged(nameof(ObjectiveArgsPreview));
        OnPropertyChanged(nameof(StageNameValidation));
        OnPropertyChanged(nameof(HasStageNameValidation));
        OnPropertyChanged(nameof(StageTechnicalPreview));
        OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
        OnPropertyChanged(nameof(StageIndexLabel));
        OnPropertyChanged(nameof(StageSummaryLine));
        OnPropertyChanged(nameof(Step1Status));
        OnPropertyChanged(nameof(Step2Status));
        OnPropertyChanged(nameof(Step3Status));
        OnPropertyChanged(nameof(Step4Status));
        Persist();
        DraftEdited?.Invoke();
    }

    public void ApplyRewardsFromUi()
    {
        if (SelectedStage is null) return;
        SelectedStage.Rewards.Objetos = RewardItemRows
            .Select(r => r.ToModel())
            .Where(r => r.ItemId > 0 && r.Cantidad > 0)
            .ToList();
        OnPropertyChanged(nameof(RewardsRawPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        Persist();
    }

    private void CreateMission()
    {
        var m = Workspace.Missions.CreateMission();
        m.Nombre = MissionDialogRolesUi.DefaultMissionName;
        var vm = new MissionListItemVm(m, ResolveNpcName);
        Items.Add(vm);
        Selected = vm;
        Persist();
        StatusText = "Misión creada (DraftId interno · Quest ID al publicar)";
    }

    private void DuplicateMission()
    {
        if (Selected is null || !DbReady) return;
        var copy = Workspace.Missions.DuplicateMission(Selected.Model);
        var vm = new MissionListItemVm(copy, ResolveNpcName);
        Items.Add(vm);
        Selected = vm;
        Persist();
        StatusText = "Borrador duplicado (nueva identidad · sin IDs publicados)";
    }

    private void SaveDraft()
    {
        if (Selected is null) return;
        Persist();
        _localDraftDirty = false;
        Selected.Refresh();
        StatusText = "Borrador guardado localmente";
        DraftEdited?.Invoke();
    }

    private void DeleteMission()
    {
        if (Selected is null) return;
        var id = Selected.Model.DraftId;
        var result = Workspace.Missions.TryDeleteMission(
            id,
            unlinkAndDelete: false,
            Workspace.Dialogs.FindResponseRefsToMission,
            out var blocked);

        if (result == MissionDeleteResult.HasReferences)
        {
            var msg =
                $"La misión está enlazada desde {blocked!.Value.ResponseDraftIds.Count} respuesta(s) (accion=44).\n\n" +
                "OK = quitar enlaces y eliminar misión.\nCancelar = no borrar.";
            if (MessageBox.Show(msg, "Misión referenciada", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;
            Workspace.Dialogs.UnlinkAllMissionReferences(id);
            Workspace.Missions.TryDeleteMission(id, unlinkAndDelete: true, null, out _);
        }
        else if (result != MissionDeleteResult.Deleted)
        {
            return;
        }

        Items.Remove(Selected);
        Selected = Items.LastOrDefault();
        Persist();
        StatusText = "Misión eliminada";
    }

    private void AddStage()
    {
        if (Selected is null) return;
        var s = Workspace.Missions.AddStage(Selected.Model);
        SelectedStage = s;
        Selected.Refresh();
        RebuildFlow();
        Persist();
        StatusText = $"Etapa provisional {s.Id} añadida";
    }

    private void DuplicateStage()
    {
        if (Selected is null || SelectedStage is null) return;
        var s = Workspace.Missions.DuplicateStage(Selected.Model, SelectedStage);
        SelectedStage = s;
        Selected.Refresh();
        RebuildFlow();
        Persist();
    }

    private void DeleteStage()
    {
        if (Selected is null || SelectedStage is null) return;
        if (!Workspace.Missions.RemoveStage(Selected.Model, SelectedStage))
        {
            StatusText = Selected.Model.IsPublishedStage(SelectedStage.Id)
                ? "No se puede eliminar una etapa ya publicada (DELETE no confirmado)"
                : "No se pudo eliminar la etapa";
            return;
        }
        SelectedStage = Selected.Model.Stages.FirstOrDefault();
        Selected.Refresh();
        RebuildFlow();
        Persist();
    }

    private void MoveStage(int delta)
    {
        if (Selected is null || SelectedStage is null) return;
        var stage = SelectedStage;
        if (Workspace.Missions.MoveStage(Selected.Model, stage, delta))
        {
            SelectedStage = stage;
            Selected.Refresh();
            RebuildFlow();
            Persist();
        }
    }

    private void AddDeliverObjective()
    {
        if (SelectedStage is null) return;
        if (!int.TryParse(DeliverNpcId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var npc)
            || !int.TryParse(DeliverItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var item)
            || !int.TryParse(DeliverQty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
        {
            StatusText = "Entregar objetos: indica NPC ID, Item ID y cantidad numéricos.";
            return;
        }
        var o = Workspace.Missions.AddDeliverItemsObjective(SelectedStage, npc, item, qty);
        SelectedObjective = o;
        Selected?.Refresh();
        Persist();
        StatusText = $"Objetivo {o.Id} tipo=3 {o.Args}";
    }

    private void AddAdvancedObjective()
    {
        if (SelectedStage is null) return;
        var o = Workspace.Missions.AddObjective(SelectedStage, tipo: 0);
        SelectedObjective = o;
        Selected?.Refresh();
        Persist();
        StatusText = $"Objetivo avanzado {o.Id} (edita tipo/args raw)";
    }

    private void DeleteObjective()
    {
        if (SelectedStage is null || SelectedObjective is null) return;
        TryDeleteObjective(SelectedObjective);
    }

    private bool _suppressObjectiveRowMapCallbacks;

    internal void TryDeleteObjective(MissionObjectiveDraft objective)
    {
        if (Selected is null) return;
        if (objective.IsAutoReturnToStartNpc)
            return;

        // Kill Map ID LostFocus write-back before mutating collections.
        foreach (var row in ObjectiveRows)
            row.DetachMapEdits();

        var stage = SelectedStage;
        if (stage is null || stage.Objectives.All(o => o.Id != objective.Id))
            stage = Selected.Model.Stages.FirstOrDefault(s => s.Objectives.Any(o => o.Id == objective.Id));
        if (stage is null)
        {
            StatusText = "No se pudo localizar el objetivo a eliminar.";
            SyncObjectiveRows();
            return;
        }

        _suppressObjectiveRowMapCallbacks = true;
        try
        {
            if (!Workspace.Missions.RemoveObjective(Selected.Model, stage, objective))
            {
                StatusText = "No se pudo eliminar el objetivo";
                SyncObjectiveRows();
                return;
            }

            if (!ReferenceEquals(SelectedStage, stage))
                SelectedStage = stage;

            SyncAutoReturnsFor(Selected.Model);
            SelectedObjective = stage.Objectives.FirstOrDefault(o => !o.IsAutoReturnToStartNpc);
            SyncObjectiveRows();
            Selected?.Refresh();
            OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
            OnPropertyChanged(nameof(ShowAutoReturnBanner));
            OnPropertyChanged(nameof(AutoReturnBannerText));
            OnPropertyChanged(nameof(StageTechnicalPreview));
            OnPropertyChanged(nameof(Step2Status));
            Persist();
            DraftEdited?.Invoke();
            StatusText = Selected.Model.PendingDeletedObjectiveIds.Contains(objective.Id)
                ? "Objetivo eliminado del borrador (DELETE en próxima publicación BD)"
                : "Objetivo eliminado del borrador";
        }
        finally
        {
            _suppressObjectiveRowMapCallbacks = false;
        }
    }

    internal void EditObjective(MissionObjectiveDraft objective)
    {
        SelectedObjective = objective;
    }

    internal void DuplicateObjective(MissionObjectiveDraft objective)
    {
        if (SelectedStage is null) return;
        var copy = Workspace.Missions.DuplicateObjective(SelectedStage, objective);
        SelectedObjective = copy;
        SyncObjectiveRows();
        LoadObjectiveEditorFromSelected();
        Selected?.Refresh();
        OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
        OnPropertyChanged(nameof(StageTechnicalPreview));
        Persist();
        StatusText = $"Objetivo duplicado → #{copy.Id}";
    }

    private void AddRewardItem()
    {
        // Unified with picker: open catalog instead of empty id,cantidad row.
        _ = PickRewardItemAsync();
    }

    /// <summary>MISSIONS.REWARDS.MANUAL_ITEM_ROWS.2 — [+] adds an empty editable reward row.</summary>
    private void AddManualRewardItem()
    {
        if (SelectedStage is null)
            return;

        RewardItemRows.Add(MissionRewardItemRowVm.CreateEmpty());
        NotifyRewardItemsUi();
        StatusText = "Nueva fila de objeto";
    }

    private async Task PickDeliverItemAsync()
    {
        StatusText = "Cargando catálogo de objetos…";
        await VisualLibraryBootstrap.EnsureItemsAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.ItemsLoaded)
        {
            MessageBox.Show("No se pudo cargar el catálogo de objetos.", "Buscar objeto");
            return;
        }

        var dlg = new ItemPickerWindow(VisualLibraryService.Shared, DeliverItemId) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;

        DeliverItemId = dlg.SelectedEntry.ItemId.ToString(CultureInfo.InvariantCulture);
        DeliverQty = dlg.SelectedQuantity.ToString(CultureInfo.InvariantCulture);
        StatusText = $"{dlg.SelectedEntry.Nombre} · #{dlg.SelectedEntry.ItemId}";
    }

    private async Task PickRewardItemAsync()
    {
        if (SelectedStage is null) return;
        StatusText = "Cargando catálogo de objetos…";
        await VisualLibraryBootstrap.EnsureItemsAsync().ConfigureAwait(true);
        if (!VisualLibraryService.Shared.ItemsLoaded)
        {
            MessageBox.Show("No se pudo cargar el catálogo de objetos.", "Añadir objeto");
            StatusText = "No se pudo cargar el catálogo de objetos.";
            return;
        }

        var dlg = new ItemPickerWindow(VisualLibraryService.Shared, null, 1) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;

        var row = MissionRewardItemRowVm.FromModel(new MissionRewardItem
        {
            ItemId = dlg.SelectedEntry.ItemId,
            Cantidad = dlg.SelectedQuantity,
        });
        RewardItemRows.Add(row);
        ApplyRewardsFromUi();
        NotifyEdited();
        NotifyRewardItemsUi();
        StatusText = $"{dlg.SelectedEntry.Nombre} · #{dlg.SelectedEntry.ItemId} ×{dlg.SelectedQuantity}";
    }

    private void RemoveRewardItem()
    {
        if (SelectedRewardRow is null) return;
        RemoveRewardRow(SelectedRewardRow);
    }

    internal void RemoveRewardRow(MissionRewardItemRowVm row)
    {
        if (!RewardItemRows.Contains(row)) return;
        RewardItemRows.Remove(row);
        if (ReferenceEquals(_selectedRewardRow, row))
        {
            _selectedRewardRow = null;
            OnPropertyChanged(nameof(SelectedRewardRow));
        }

        if (SelectedStage is not null && RewardItemRows.Count == 0)
            RewardItemRows.Add(MissionRewardItemRowVm.CreateEmpty());

        ApplyRewardsFromUi();
        NotifyEdited();
        NotifyRewardItemsUi();
    }

    private void NotifyRewardItemsUi() => OnPropertyChanged(nameof(ShowStageNoRewardItemsHint));

    private void RebuildFlow()
    {
        FlowStages.Clear();
        var nodes = new List<MissionFlowNodeVm> { new("INICIO", isEndpoint: true) };
        if (Selected is null)
        {
            nodes.Add(new MissionFlowNodeVm("FINAL", isEndpoint: true));
        }
        else
        {
            var i = 1;
            foreach (var s in Selected.Model.Stages)
            {
                var name = string.IsNullOrWhiteSpace(s.Nombre) ? "Sin nombre" : s.Nombre;
                nodes.Add(new MissionFlowNodeVm($"Etapa {i++} · {name}", s, isEndpoint: false,
                    isSelected: ReferenceEquals(s, SelectedStage)));
            }
            nodes.Add(new MissionFlowNodeVm("FINAL", isEndpoint: true));
        }

        for (var idx = 0; idx < nodes.Count; idx++)
        {
            var n = nodes[idx];
            FlowStages.Add(new MissionFlowNodeVm(
                n.Label, n.Stage, n.IsEndpoint, n.IsSelected, showTrailingArrow: idx < nodes.Count - 1));
        }
    }

    private void SyncRewardItems()
    {
        SyncRewardItemRows();
    }

    private void RefreshQuestionChoices()
    {
        _refreshingQuestionChoices = true;
        try
        {
            QuestionChoices.Clear();
            if (ResolveDialogRoleMission() is not { } mission)
                return;

            var ownerNpcId = ResolveEffectiveOwnerNpcId();
            foreach (var item in MissionDbCatalogUi.BuildQuestionChoiceItems(
                         Workspace.Dialogs,
                         _dbCatalog,
                         ownerNpcId,
                         mission.PregDarPreguntaId,
                         mission.PregIncompletaPreguntaId,
                         mission.PregCompletadaPreguntaId))
                QuestionChoices.Add(item);
        }
        finally
        {
            _refreshingQuestionChoices = false;
        }
    }

    private DialogQuestionDraft? ResolveQuestionChoice(int? questionId)
    {
        if (questionId is not int id) return null;
        return MissionDbCatalogUi.ResolveQuestionDraft(id, Workspace.Dialogs, _dbCatalog)
            ?? FindQ(id);
    }

    private DialogQuestionDraft? FindQ(int? id) =>
        id is int i ? Workspace.Dialogs.FindQuestion(i) : null;

    private string ResolveNpcName(int? npcId) =>
        MissionDbCatalogUi.ResolveNpcShortName(npcId, Workspace, _dbCatalog);

    private void NotifyMissionMeta()
    {
        if (ResolveDialogRoleMission() is { } mission)
            RefreshMissionListItem(mission);
        OnPropertyChanged(nameof(PregDarPreview));
        OnPropertyChanged(nameof(PregIncPreview));
        OnPropertyChanged(nameof(PregCompPreview));
        NotifyDialogRoleSelectionProperties();
        Persist();
        DraftEdited?.Invoke();
    }

    private void Persist(bool markPendingBd = true, bool markLocalDirty = false)
    {
        // Edits autosave to ContentDraftStore immediately. Do not treat that as
        // "unsaved" — otherwise switching NPC (which rebinds Missions.Selected)
        // shows «Cambios sin guardar» even though the draft is already on disk.
        // Explicit SaveDraft still clears the flag; markLocalDirty is for rare
        // in-memory-only paths that have not written yet.
        if (markPendingBd)
            Selected?.Model.MarkPendingBdEdit();
        if (markLocalDirty)
            _localDraftDirty = true;
        else
            _localDraftDirty = false;
        if (_injected is null)
            ContentDraftStore.Save();
        Selected?.Refresh();
    }

    private void RaiseMissionCommands()
    {
        RefreshMaxCommand.RaiseCanExecuteChanged();
        NewMissionCommand.RaiseCanExecuteChanged();
        DuplicateMissionCommand.RaiseCanExecuteChanged();
        SaveDraftCommand.RaiseCanExecuteChanged();
        DeleteMissionCommand.RaiseCanExecuteChanged();
        RaiseStageCommands();
    }

    private void RaiseStageCommands()
    {
        AddStageCommand.RaiseCanExecuteChanged();
        DuplicateStageCommand.RaiseCanExecuteChanged();
        DeleteStageCommand.RaiseCanExecuteChanged();
        MoveStageUpCommand.RaiseCanExecuteChanged();
        MoveStageDownCommand.RaiseCanExecuteChanged();
        AddDeliverObjectiveCommand.RaiseCanExecuteChanged();
        AddAdvancedObjectiveCommand.RaiseCanExecuteChanged();
        AddObjectiveCommand.RaiseCanExecuteChanged();
        BeginAddObjectiveCommand.RaiseCanExecuteChanged();
        DeleteObjectiveCommand.RaiseCanExecuteChanged();
        ApplyObjectiveFieldsCommand.RaiseCanExecuteChanged();
        AddRewardItemCommand.RaiseCanExecuteChanged();
        AddManualRewardItemCommand.RaiseCanExecuteChanged();
        RemoveRewardItemCommand.RaiseCanExecuteChanged();
        PickDeliverItemCommand.RaiseCanExecuteChanged();
        PickRewardItemCommand.RaiseCanExecuteChanged();
        PickObjectiveNpcCommand.RaiseCanExecuteChanged();
        PickObjectiveAreaCommand.RaiseCanExecuteChanged();
        PickObjectiveMapCommand.RaiseCanExecuteChanged();
    }

    private IMisionEtapasReadRepository? CreateStageRepo()
    {
        if (_stageRepoFactory is not null) return _stageRepoFactory();
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlMisionEtapasReadRepository(settings, password);
    }

    private IMisionObjetivosReadRepository? CreateObjRepo()
    {
        if (_objRepoFactory is not null) return _objRepoFactory();
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlMisionObjetivosReadRepository(settings, password);
    }

    private IMisionesReadRepository? CreateQuestRepo()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlMisionesReadRepository(settings, password);
    }
}

public sealed class MissionListItemVm : ViewModelBase
{
    private readonly Func<int?, string> _npcName;

    public MissionListItemVm(MissionDraft model, Func<int?, string> npcName)
    {
        Model = model;
        _npcName = npcName;
    }

    public MissionDraft Model { get; }
    public string Status => Model.Status;
    public string Nombre
    {
        get => Model.Nombre;
        set { if (Model.Nombre != value) { Model.Nombre = value ?? ""; OnPropertyChanged(); } }
    }

    public bool PuedeRepetirse
    {
        get => Model.PuedeRepetirse;
        set { if (Model.PuedeRepetirse != value) { Model.PuedeRepetirse = value; OnPropertyChanged(); } }
    }

    public string NpcLabel => _npcName(Model.StartNpcId);
    public int StageCount => Model.Stages.Count;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(NpcLabel));
        OnPropertyChanged(nameof(StageCount));
    }
}

public sealed class MissionFlowNodeVm
{
    public MissionFlowNodeVm(
        string label,
        MissionStageDraft? stage = null,
        bool isEndpoint = false,
        bool isSelected = false,
        bool showTrailingArrow = false)
    {
        Label = label;
        Stage = stage;
        IsEndpoint = isEndpoint;
        IsSelected = isSelected;
        IsClickable = stage is not null;
        ShowTrailingArrow = showTrailingArrow;
    }

    public string Label { get; }
    public MissionStageDraft? Stage { get; }
    public bool IsEndpoint { get; }
    public bool IsSelected { get; }
    public bool IsClickable { get; }
    public bool ShowTrailingArrow { get; }
}

public sealed record ObjectiveTypeChoice(int Tipo, string Nombre)
{
    public override string ToString() => Nombre;
}
