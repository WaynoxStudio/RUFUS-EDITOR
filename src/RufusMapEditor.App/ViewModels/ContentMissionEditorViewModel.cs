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
        QuestionChoices = new ObservableCollection<DialogQuestionDraft>();
        RewardItems = new ObservableCollection<MissionRewardItem>();

        RefreshMaxCommand = new RelayCommand(async () => await RefreshMaxAsync(), () => !IsBusy);
        ReloadCommand = new RelayCommand(Reload);
        NewMissionCommand = new RelayCommand(CreateMission, () => !IsBusy);
        DuplicateMissionCommand = new RelayCommand(DuplicateMission, () => Selected is not null && !IsBusy);
        DeleteMissionCommand = new RelayCommand(DeleteMission, () => Selected is not null && !IsBusy);
        AddStageCommand = new RelayCommand(AddStage, () => Selected is not null && DbReady && !IsBusy);
        DuplicateStageCommand = new RelayCommand(DuplicateStage, () => SelectedStage is not null && DbReady && !IsBusy);
        DeleteStageCommand = new RelayCommand(DeleteStage, () => SelectedStage is not null && !IsBusy);
        MoveStageUpCommand = new RelayCommand(() => MoveStage(-1), () => SelectedStage is not null && !IsBusy);
        MoveStageDownCommand = new RelayCommand(() => MoveStage(1), () => SelectedStage is not null && !IsBusy);
        AddDeliverObjectiveCommand = new RelayCommand(AddDeliverObjective, () => SelectedStage is not null && DbReady && !IsBusy);
        AddAdvancedObjectiveCommand = new RelayCommand(AddAdvancedObjective, () => SelectedStage is not null && DbReady && !IsBusy);
        AddObjectiveCommand = new RelayCommand(AddObjectiveOfSelectedType, () => SelectedStage is not null && DbReady && !IsBusy);
        DeleteObjectiveCommand = new RelayCommand(DeleteObjective, () => SelectedObjective is not null && !IsBusy);
        ApplyObjectiveFieldsCommand = new RelayCommand(ApplyObjectiveEditorFields, () => SelectedObjective is not null && !IsBusy);
        AddRewardItemCommand = new RelayCommand(AddRewardItem, () => SelectedStage is not null && !IsBusy);
        RemoveRewardItemCommand = new RelayCommand(RemoveRewardItem, () => SelectedRewardRow is not null && !IsBusy);
        PickDeliverItemCommand = new RelayCommand(async () => await PickDeliverItemAsync(), () => !IsBusy);
        PickRewardItemCommand = new RelayCommand(async () => await PickRewardItemAsync(), () => SelectedStage is not null && !IsBusy);
        PickObjectiveNpcCommand = new RelayCommand(async () => await PickObjectiveNpcAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveItemCommand = new RelayCommand(async () => await PickObjectiveItemAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveMobCommand = new RelayCommand(async () => await PickObjectiveMobAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveAreaCommand = new RelayCommand(async () => await PickObjectiveAreaAsync(), () => SelectedObjective is not null && !IsBusy);
        PickObjectiveMapCommand = new RelayCommand(PickObjectiveMap, () => SelectedObjective is not null && !IsBusy);
        EnsureUxCommands();

        foreach (var t in MissionObjectiveTypes.UiNormalTypes)
            ObjectiveTypeChoices.Add(new ObjectiveTypeChoice(t, MissionObjectiveUiSync.UiTypeLabel(t)));
        SelectedObjectiveType = ObjectiveTypeChoices.FirstOrDefault(c => c.Tipo == MissionObjectiveTypes.TalkToNpc)
                                ?? ObjectiveTypeChoices.FirstOrDefault();

        Reload();
    }

    public RelayCommand AddObjectiveCommand { get; }
    public RelayCommand ApplyObjectiveFieldsCommand { get; }
    public RelayCommand PickObjectiveNpcCommand { get; }
    public RelayCommand PickObjectiveItemCommand { get; }
    public RelayCommand PickObjectiveMobCommand { get; }
    public RelayCommand PickObjectiveAreaCommand { get; }
    public RelayCommand PickObjectiveMapCommand { get; }
    public ObservableCollection<ObjectiveTypeChoice> ObjectiveTypeChoices { get; } = new();

    public ObservableCollection<MissionListItemVm> Items { get; }
    public ObservableCollection<MissionFlowNodeVm> FlowStages { get; }
    public ObservableCollection<NpcsModeloDraft> NpcChoices { get; }
    public ObservableCollection<DialogQuestionDraft> QuestionChoices { get; }
    public ObservableCollection<MissionRewardItem> RewardItems { get; }

    public MissionListItemVm? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            SelectedStage = value?.Model.Stages.FirstOrDefault();
            RefreshQuestionChoices();
            RebuildFlow();
            SyncStageRows();
            SyncRewardItems();
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

    public MissionStageDraft? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (!SetProperty(ref _selectedStage, value))
                return;
            SelectedObjective = value?.Objectives.FirstOrDefault();
            SyncRewardItems();
            SyncStageRows();
            SyncObjectiveRows();
            RebuildFlow();
            OnPropertyChanged(nameof(HasStage));
            OnPropertyChanged(nameof(RewardsRawPreview));
            OnPropertyChanged(nameof(SelectedStageProvisionalLabel));
            OnPropertyChanged(nameof(StageNameValidation));
            OnPropertyChanged(nameof(HasStageNameValidation));
            OnPropertyChanged(nameof(SelectedStageRow));
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
                OnPropertyChanged(nameof(ShowItemFields));
                OnPropertyChanged(nameof(ShowQtyField));
                OnPropertyChanged(nameof(ShowMobFields));
                OnPropertyChanged(nameof(ShowMapFields));
                OnPropertyChanged(nameof(ShowAreaFields));
                OnPropertyChanged(nameof(ShowManualFields));
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
                PickObjectiveItemCommand.RaiseCanExecuteChanged();
                PickObjectiveMobCommand.RaiseCanExecuteChanged();
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
        set { if (SetProperty(ref _objItemId, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjQty
    {
        get => _objQty;
        set { if (SetProperty(ref _objQty, value)) OnObjectiveFieldChanged(); }
    }
    public string ObjMobId
    {
        get => _objMobId;
        set { if (SetProperty(ref _objMobId, value)) OnObjectiveFieldChanged(); }
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

    public string SelectedStageProvisionalLabel =>
        SelectedStage is null ? "" : $"ID provisional #{SelectedStage.Id}";

    public string StageNameValidation =>
        SelectedStage is null ? "" : MissionObjectiveUiSync.ValidateStageName(SelectedStage.Nombre);

    public bool HasStageNameValidation => !string.IsNullOrWhiteSpace(StageNameValidation);

    public bool ShowCoordsCheckbox =>
        SelectedObjective is not null && MissionObjectiveTypes.SupportsCoordinates(SelectedObjective.Tipo);

    public bool ShowCoordsPanel => ShowCoordsCheckbox && ObjRestrictCoords;

    public bool ShowNpcFields =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.TalkToNpc
            or MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc
            or MissionObjectiveTypes.ReturnToNpc;

    public bool ShowItemFields =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc
            or MissionObjectiveTypes.UseItem;

    public bool ShowQtyField =>
        SelectedObjective?.Tipo is MissionObjectiveTypes.ShowItemToNpc
            or MissionObjectiveTypes.DeliverItemsToNpc
            or MissionObjectiveTypes.DefeatMobs;

    public bool ShowMobFields => SelectedObjective?.Tipo == MissionObjectiveTypes.DefeatMobs;
    public bool ShowMapFields => SelectedObjective?.Tipo == MissionObjectiveTypes.DiscoverMap;
    public bool ShowAreaFields => SelectedObjective?.Tipo == MissionObjectiveTypes.DiscoverArea;
    public bool ShowManualFields => SelectedObjective?.Tipo == MissionObjectiveTypes.Manual;
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
    public string QuestsEsStatusLine => "Cliente quests_es: ⚠ Pendiente de publicación / soporte en fase posterior";
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

    public bool EditorEnabled => Selected is not null;
    public bool HasStage => SelectedStage is not null;
    public bool HasObjective => SelectedObjective is not null;

    public string QuestIdLabel => "Quest ID: se asignará al publicar";

    public string PregDarPreview => Selected?.Model.BuildPregDar() ?? "";
    public string PregIncPreview => Selected?.Model.BuildPregIncompleta() ?? "";
    public string PregCompPreview => Selected?.Model.BuildPregCompletada() ?? "";
    public string RewardsRawPreview => SelectedStage?.Rewards.ToRaw() ?? "";

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

    public NpcsModeloDraft? SelectedStartNpc
    {
        get => Selected?.Model.StartNpcId is int id
            ? NpcChoices.FirstOrDefault(n => n.Id == id)
            : null;
        set
        {
            if (Selected is null) return;
            Selected.Model.StartNpcId = value?.Id;
            RefreshQuestionChoices();
            NotifyMissionMeta();
        }
    }

    public DialogQuestionDraft? SelectedPregDar
    {
        get => FindQ(Selected?.Model.PregDarPreguntaId);
        set
        {
            if (Selected is null) return;
            Selected.Model.PregDarPreguntaId = value?.Id;
            NotifyMissionMeta();
        }
    }

    public DialogQuestionDraft? SelectedPregInc
    {
        get => FindQ(Selected?.Model.PregIncompletaPreguntaId);
        set
        {
            if (Selected is null) return;
            Selected.Model.PregIncompletaPreguntaId = value?.Id;
            NotifyMissionMeta();
        }
    }

    public DialogQuestionDraft? SelectedPregComp
    {
        get => FindQ(Selected?.Model.PregCompletadaPreguntaId);
        set
        {
            if (Selected is null) return;
            Selected.Model.PregCompletadaPreguntaId = value?.Id;
            NotifyMissionMeta();
        }
    }

    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand NewMissionCommand { get; }
    public RelayCommand DuplicateMissionCommand { get; }
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
    public RelayCommand RemoveRewardItemCommand { get; }
    public RelayCommand PickDeliverItemCommand { get; }
    public RelayCommand PickRewardItemCommand { get; }

    public string ItemsCatalogStatus => VisualLibraryService.Shared.StatusItems;

    public async Task InitializeAsync()
    {
        Reload();
        await RefreshMaxAsync();
    }

    public void Reload()
    {
        NpcChoices.Clear();
        foreach (var n in Workspace.Npcs.Drafts)
            NpcChoices.Add(n);

        Items.Clear();
        foreach (var m in Workspace.Missions.Missions)
            Items.Add(new MissionListItemVm(m, ResolveNpcName));

        if (Selected is not null)
        {
            var id = Selected.Model.DraftId;
            Selected = Items.FirstOrDefault(i => i.Model.DraftId == id);
        }

        StatusText = Items.Count == 0
            ? "Sin misiones. Crea una con + Nueva misión."
            : $"Misiones: {Items.Count} · próxima etapa = {Workspace.Missions.NextStageId} · próximo objetivo = {Workspace.Missions.NextObjectiveId}";
    }

    public async Task RefreshMaxAsync()
    {
        IsBusy = true;
        try
        {
            var stageRepo = CreateStageRepo();
            var objRepo = CreateObjRepo();
            if (stageRepo is null || objRepo is null)
            {
                DbReady = false;
                StatusText = "Configura MySQL para leer MAX(mision_etapas/objetivos).";
                return;
            }

            var maxS = await stageRepo.GetMaxIdAsync().ConfigureAwait(true);
            var maxO = await objRepo.GetMaxIdAsync().ConfigureAwait(true);
            Workspace.Missions.SetDbMaxStageId(maxS);
            Workspace.Missions.SetDbMaxObjectiveId(maxO);
            DbMaxStage = maxS;
            DbMaxObjective = maxO;
            DbReady = true;
            StatusText = $"MAX etapas={maxS} → próximo {Workspace.Missions.NextStageId} · MAX objetivos={maxO} → próximo {Workspace.Missions.NextObjectiveId} · solo lectura";
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
        Persist();
    }

    public void ApplyRewardsFromUi()
    {
        if (SelectedStage is null) return;
        SelectedStage.Rewards.Objetos = RewardItemRows
            .Select(r => r.ToModel())
            .Where(r => r.ItemId > 0)
            .ToList();
        OnPropertyChanged(nameof(RewardsRawPreview));
        OnPropertyChanged(nameof(TechnicalPreview));
        Persist();
    }

    private void CreateMission()
    {
        var m = Workspace.Missions.CreateMission();
        m.Nombre = "Nueva misión";
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
        StatusText = "Misión duplicada con nuevas etapas/objetivos provisionales";
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
        if (Selected is null || !DbReady) return;
        var s = Workspace.Missions.AddStage(Selected.Model);
        SelectedStage = s;
        Selected.Refresh();
        RebuildFlow();
        Persist();
        StatusText = $"Etapa provisional {s.Id} añadida";
    }

    private void DuplicateStage()
    {
        if (Selected is null || SelectedStage is null || !DbReady) return;
        var s = Workspace.Missions.DuplicateStage(Selected.Model, SelectedStage);
        SelectedStage = s;
        Selected.Refresh();
        RebuildFlow();
        Persist();
    }

    private void DeleteStage()
    {
        if (Selected is null || SelectedStage is null) return;
        Workspace.Missions.RemoveStage(Selected.Model, SelectedStage);
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
        if (SelectedStage is null || !DbReady) return;
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
        if (SelectedStage is null || !DbReady) return;
        var o = Workspace.Missions.AddObjective(SelectedStage, tipo: 0);
        SelectedObjective = o;
        Selected?.Refresh();
        Persist();
        StatusText = $"Objetivo avanzado {o.Id} (edita tipo/args raw)";
    }

    private void DeleteObjective()
    {
        if (SelectedStage is null || SelectedObjective is null) return;
        Workspace.Missions.RemoveObjective(SelectedStage, SelectedObjective);
        SelectedObjective = SelectedStage.Objectives.FirstOrDefault();
        Selected?.Refresh();
        Persist();
    }

    private void AddRewardItem()
    {
        // Unified with picker: open catalog instead of empty id,cantidad row.
        _ = PickRewardItemAsync();
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

        var initial = SelectedRewardRow?.ItemId > 0
            ? SelectedRewardRow.ItemId.ToString(CultureInfo.InvariantCulture)
            : null;
        var qty = SelectedRewardRow?.Cantidad > 0 ? SelectedRewardRow.Cantidad : 1;
        var dlg = new ItemPickerWindow(VisualLibraryService.Shared, initial, qty) { Owner = ActiveOwner() };
        if (dlg.ShowDialog() != true || dlg.SelectedEntry is null)
            return;

        var row = MissionRewardItemRowVm.FromModel(new MissionRewardItem
        {
            ItemId = dlg.SelectedEntry.ItemId,
            Cantidad = dlg.SelectedQuantity,
        });
        if (SelectedRewardRow is null)
        {
            RewardItemRows.Add(row);
            SelectedRewardRow = row;
        }
        else
        {
            var idx = RewardItemRows.IndexOf(SelectedRewardRow);
            if (idx >= 0)
            {
                RewardItemRows[idx] = row;
                SelectedRewardRow = row;
            }
            else
            {
                RewardItemRows.Add(row);
                SelectedRewardRow = row;
            }
        }

        ApplyRewardsFromUi();
        NotifyEdited();
        StatusText = $"{dlg.SelectedEntry.Nombre} · #{dlg.SelectedEntry.ItemId} ×{dlg.SelectedQuantity}";
    }

    private void RemoveRewardItem()
    {
        if (SelectedRewardRow is null) return;
        RewardItemRows.Remove(SelectedRewardRow);
        SelectedRewardRow = null;
        ApplyRewardsFromUi();
    }

    private void RebuildFlow()
    {
        FlowStages.Clear();
        FlowStages.Add(new MissionFlowNodeVm("Inicio", isEndpoint: true));
        if (Selected is null)
        {
            FlowStages.Add(new MissionFlowNodeVm("Final", isEndpoint: true));
            return;
        }
        var i = 1;
        foreach (var s in Selected.Model.Stages)
        {
            var name = string.IsNullOrWhiteSpace(s.Nombre) ? "Sin nombre" : s.Nombre;
            FlowStages.Add(new MissionFlowNodeVm($"Etapa {i++}", s, isEndpoint: false, isSelected: ReferenceEquals(s, SelectedStage)));
            // Label already short; detail in stage list
            _ = name;
        }
        FlowStages.Add(new MissionFlowNodeVm("Final", isEndpoint: true));
    }

    private void SyncRewardItems()
    {
        SyncRewardItemRows();
        SelectedRewardRow = RewardItemRows.FirstOrDefault();
    }

    private void RefreshQuestionChoices()
    {
        QuestionChoices.Clear();
        var npcId = Selected?.Model.StartNpcId;
        IEnumerable<DialogQuestionDraft> qs = Workspace.Dialogs.Questions;
        if (npcId is int id)
            qs = Workspace.Dialogs.QuestionsForNpc(id);
        foreach (var q in qs.OrderBy(q => q.Id))
            QuestionChoices.Add(q);
        OnPropertyChanged(nameof(SelectedPregDar));
        OnPropertyChanged(nameof(SelectedPregInc));
        OnPropertyChanged(nameof(SelectedPregComp));
        OnPropertyChanged(nameof(SelectedStartNpc));
    }

    private DialogQuestionDraft? FindQ(int? id) =>
        id is int i ? Workspace.Dialogs.FindQuestion(i) : null;

    private string ResolveNpcName(int? npcId)
    {
        if (npcId is null) return "(sin NPC)";
        var n = Workspace.Npcs.FindById(npcId.Value);
        if (n is null) return $"#{npcId}";
        return string.IsNullOrWhiteSpace(n.Nombre) ? $"#{npcId}" : n.Nombre;
    }

    private void NotifyMissionMeta()
    {
        Selected?.Refresh();
        OnPropertyChanged(nameof(PregDarPreview));
        OnPropertyChanged(nameof(PregIncPreview));
        OnPropertyChanged(nameof(PregCompPreview));
        OnPropertyChanged(nameof(SelectedPregDar));
        OnPropertyChanged(nameof(SelectedPregInc));
        OnPropertyChanged(nameof(SelectedPregComp));
        Persist();
    }

    private void Persist()
    {
        if (_injected is null)
            ContentDraftStore.Save();
    }

    private void RaiseMissionCommands()
    {
        RefreshMaxCommand.RaiseCanExecuteChanged();
        NewMissionCommand.RaiseCanExecuteChanged();
        DuplicateMissionCommand.RaiseCanExecuteChanged();
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
        DeleteObjectiveCommand.RaiseCanExecuteChanged();
        ApplyObjectiveFieldsCommand.RaiseCanExecuteChanged();
        AddRewardItemCommand.RaiseCanExecuteChanged();
        RemoveRewardItemCommand.RaiseCanExecuteChanged();
        PickDeliverItemCommand.RaiseCanExecuteChanged();
        PickRewardItemCommand.RaiseCanExecuteChanged();
        PickObjectiveNpcCommand.RaiseCanExecuteChanged();
        PickObjectiveItemCommand.RaiseCanExecuteChanged();
        PickObjectiveMobCommand.RaiseCanExecuteChanged();
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
        OnPropertyChanged(nameof(NpcLabel));
        OnPropertyChanged(nameof(StageCount));
    }
}

public sealed class MissionFlowNodeVm
{
    public MissionFlowNodeVm(string label, MissionStageDraft? stage = null, bool isEndpoint = false, bool isSelected = false)
    {
        Label = label;
        Stage = stage;
        IsEndpoint = isEndpoint;
        IsSelected = isSelected;
        IsClickable = stage is not null;
    }

    public string Label { get; }
    public MissionStageDraft? Stage { get; }
    public bool IsEndpoint { get; }
    public bool IsSelected { get; }
    public bool IsClickable { get; }
}

public sealed record ObjectiveTypeChoice(int Tipo, string Nombre)
{
    public override string ToString() => Nombre;
}
