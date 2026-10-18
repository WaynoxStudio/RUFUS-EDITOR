using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

public sealed class ContentDialogEditorViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace? _injectedWorkspace;
    private readonly Func<INpcPreguntasReadRepository>? _repoFactory;
    private readonly string? _dialogEsCacheDirectory;
    private readonly Func<LangSftpSettings, string, ILangSftpReadClient>? _sftpClientFactory;
    private readonly DialogEsSessionCache _dialogEsSession;
    private readonly HashSet<DialogActionDraft> _forceRawActionUi = new();
    private DialogActionDraft? _conditionEditorAction;
    private NpcsModeloDraft? _selectedNpc;
    private object? _selectedNode;
    private string _statusText = "Selecciona un NPC borrador";
    private bool _dbReady;
    private int _dbMaxQuestionId;
    private bool _isBusy;
    private bool _advancedExpanded;
    private bool _actionConditionEditorOpen;
    private bool _dialogEsLoading;
    private DialogEsSimpleUiState _simpleUi = new() { IsPending = false };
    private DispatcherTimer? _textEditFlushTimer;
    private DialogActionDraft? _gotoEditSnapshotAction;
    private int _gotoEditSnapshotAccion;
    private int? _gotoEditSnapshotTargetQuestionId;
    private string _gotoEditSnapshotArgs = "";
    private int? _cachedMissionRolePregDar;
    private int? _cachedMissionRolePregInc;
    private int? _cachedMissionRolePregComp;
    private int _selectedNodeChangeDepth;
    private bool _rebuildingDialogTree;
    private bool _repairStartQuestLinkScheduled;
    private bool _refreshMissionLinkScheduled;
    private bool _reloadingMissionChoices;
    private double _canvasViewportWidth = 900;
    private double _flowCenteringOffsetX;

    private const double FlowTopPadding = 72;
    private const double FlowRowGap = 48;
    private const double FlowResponseStackGap = 130;
    private const double FlowMainRowMinHeight = 210;
    private const double FlowMissionRoleRowGap = 48;
    private const double FlowHorizontalPadding = 24;
    private const double FlowMinQuestionResponseGap = 16;
    private const double FlowPreferredQuestionResponseGap = 48;
    private const double FlowMarkerHalfWidth = 40;

    private ContentDraftWorkspace Workspace => _injectedWorkspace ?? ContentDraftStore.Current;

    public ContentDialogEditorViewModel(
        ContentDraftWorkspace? workspace = null,
        Func<INpcPreguntasReadRepository>? repoFactory = null,
        string? dialogEsCacheDirectory = null,
        Func<LangSftpSettings, string, ILangSftpReadClient>? sftpClientFactory = null,
        DialogEsSessionCache? dialogEsSession = null)
    {
        _injectedWorkspace = workspace;
        _repoFactory = repoFactory;
        _dialogEsCacheDirectory = string.IsNullOrWhiteSpace(dialogEsCacheDirectory)
            ? DialogEsRemoteLoader.DefaultWorkDirectory
            : dialogEsCacheDirectory;
        _sftpClientFactory = sftpClientFactory;
        _dialogEsSession = dialogEsSession ?? DialogEsSessionCache.Shared;
        _dialogEsLoading = false;

        NpcChoices = new ObservableCollection<NpcsModeloDraft>();
        TreeRoots = new ObservableCollection<DialogTreeNodeVm>();
        FlatBubbles = new ObservableCollection<DialogTreeNodeVm>();
        Connectors = new ObservableCollection<DialogConnectorVm>();
        QuestionChoices = new ObservableCollection<DialogQuestionDraft>();
        MissionChoices = new ObservableCollection<MissionDraft>();

        RefreshMaxCommand = new RelayCommand(async () => await RefreshMaxAsync(), () => !IsBusy);
        ReloadNpcsCommand = new RelayCommand(ReloadNpcs);
        NewQuestionCommand = new RelayCommand(CreateQuestion, () =>
            SelectedNpc is not null && IsInteractiveMode && DbReady && !IsBusy);
        DuplicateQuestionCommand = new RelayCommand(DuplicateSelectedQuestion, CanEditQuestion);
        DeleteQuestionCommand = new RelayCommand(DeleteSelectedQuestion, CanEditQuestion);
        SetInitialCommand = new RelayCommand(SetInitialQuestion, CanEditQuestion);
        AddResponseCommand = new RelayCommand(AddResponse, CanEditQuestion);
        DuplicateResponseCommand = new RelayCommand(DuplicateSelectedResponse, CanEditResponse);
        DeleteResponseCommand = new RelayCommand(DeleteSelectedResponse, CanEditResponse);
        MoveResponseUpCommand = new RelayCommand(() => MoveResponse(-1), CanEditResponse);
        MoveResponseDownCommand = new RelayCommand(() => MoveResponse(1), CanEditResponse);
        AddGotoActionCommand = new RelayCommand(
            AddGotoWithLinkedQuestion, () => CanAddOrReplacePresetAction(DialogActionCodes.GotoQuestion));
        AddQuestActionCommand = new RelayCommand(
            () => AddPresetAction(DialogActionCodes.StartQuest),
            () => CanAddPresetAction(DialogActionCodes.StartQuest));
        AddTeleportActionCommand = new RelayCommand(
            AddTeleportWithGotoReplace,
            () => CanAddOrReplacePresetAction(DialogActionCodes.Teleport));
        AddConfirmObjectiveActionCommand = new RelayCommand(
            () => AddPresetAction(DialogActionCodes.ConfirmObjective),
            () => CanAddPresetAction(DialogActionCodes.ConfirmObjective));
        AddConfirmMissionActionCommand = new RelayCommand(
            () => AddPresetAction(DialogActionCodes.ConfirmMission),
            () => CanAddPresetAction(DialogActionCodes.ConfirmMission));
        AddAdvancedActionCommand = new RelayCommand(AddAdvancedAction, CanAddAction);
        DeleteActionCommand = new RelayCommand(DeleteSelectedAction, CanEditAction);
        CreateLinkedQuestionCommand = new RelayCommand(CreateLinkedQuestion, CanEditAction);
        ClearLinkCommand = new RelayCommand(ClearLink, CanEditAction);
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected, CanDuplicateSelected);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, CanDeleteSelected);
        BeginEditActionConditionCommand = new RelayCommand(
            () => ActionConditionEditorOpen = true, () => ShowFriendlyConditionEditor);
        DoneEditActionConditionCommand = new RelayCommand(
            () => ActionConditionEditorOpen = false, () => ShowFriendlyConditionEditor);
        ClearActionConditionCommand = new RelayCommand(ClearActionCondition, () => ShowFriendlyConditionEditor);
        SelectResponseActionCommand = new RelayCommand(SelectResponseAction, CanSelectResponseAction);

        ReloadNpcs();
    }

    public ObservableCollection<NpcsModeloDraft> NpcChoices { get; }
    public ObservableCollection<DialogTreeNodeVm> TreeRoots { get; }
    public ObservableCollection<DialogTreeNodeVm> FlatBubbles { get; }
    public ObservableCollection<DialogConnectorVm> Connectors { get; }
    public ObservableCollection<DialogQuestionDraft> QuestionChoices { get; }
    public ObservableCollection<MissionDraft> MissionChoices { get; }
    public ObservableCollection<ResponseActionTabVm> ResponseActionTabs { get; } = new();

    public NpcsModeloDraft? SelectedNpc
    {
        get => _selectedNpc;
        set
        {
            if (!SetProperty(ref _selectedNpc, value))
                return;
            SelectedNode = null;
            RebuildTree();
            RefreshQuestionChoices();
            NewQuestionCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(NpcHeader));
            OnPropertyChanged(nameof(NpcDisplayName));
            OnPropertyChanged(nameof(SelectedNpcGfxId));
            OnPropertyChanged(nameof(InitialQuestionLabel));
            OnPropertyChanged(nameof(HasIncompleteResponses));
            OnPropertyChanged(nameof(HasActionConflicts));
            OnPropertyChanged(nameof(SelectedResponseIncomplete));
            NotifyDialogModeUi();
            RefreshSimpleDialogEsUi();
            SelectInitialQuestionIfNeeded();
            // Simple pending needs dialog_es to compute D.q — retry SFTP if cache empty / last fetch failed.
            if (value?.IsPendingDialogEs == true)
                _ = EnsureDialogEsRemoteAsync();
        }
    }

    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var previousNode = _selectedNode as DialogTreeNodeVm;
            if (!SetProperty(ref _selectedNode, value))
                return;

            var isOuterSelection = ++_selectedNodeChangeDepth == 1;
            try
            {
                if (previousNode is not null)
                    previousNode.IsSelected = false;
                if (_selectedNode is DialogTreeNodeVm selectedNode)
                    selectedNode.IsSelected = true;
                OnPropertyChanged(nameof(SelectedQuestion));
                OnPropertyChanged(nameof(SelectedResponse));
                OnPropertyChanged(nameof(SelectedAction));
                OnPropertyChanged(nameof(HasQuestion));
                OnPropertyChanged(nameof(HasResponse));
                OnPropertyChanged(nameof(HasAction));
                OnPropertyChanged(nameof(IsGotoAction));
                OnPropertyChanged(nameof(IsStartQuestAction));
                OnPropertyChanged(nameof(IsTeleportAction));
                OnPropertyChanged(nameof(IsConfirmObjectiveAction));
                OnPropertyChanged(nameof(IsConfirmMissionAction));
                OnPropertyChanged(nameof(IsConfirmActionPending));
                OnPropertyChanged(nameof(IsFriendlyKnownAction));
                OnPropertyChanged(nameof(ShowFriendlyTeleportEditor));
                OnPropertyChanged(nameof(ShowFriendlyGotoEditor));
                OnPropertyChanged(nameof(ShowFriendlyQuestEditor));
                OnPropertyChanged(nameof(UseWideMissionLayout));
                OnPropertyChanged(nameof(ShowActionRawFields));
                OnPropertyChanged(nameof(IsRawAdvancedAction));
                OnPropertyChanged(nameof(ShowFriendlyConditionEditor));
                OnPropertyChanged(nameof(HasActionCondition));
                OnPropertyChanged(nameof(ActionConditionSummary));
                OnPropertyChanged(nameof(ActionConditionText));
                OnPropertyChanged(nameof(ShowConditionEmptyPrompt));
                OnPropertyChanged(nameof(ShowConditionSummaryRow));
                OnPropertyChanged(nameof(ShowConditionTextEditor));
                OnPropertyChanged(nameof(ActionTypeLabel));
                OnPropertyChanged(nameof(CanAddPlayerResponse));
                OnPropertyChanged(nameof(CanAddActionFromToolbar));
                OnPropertyChanged(nameof(LinkTargetQuestion));
                OnPropertyChanged(nameof(LinkTargetMission));
                OnPropertyChanged(nameof(LinkTargetMissionDraftIdValue));
                OnPropertyChanged(nameof(HasLinkedMission));
                OnPropertyChanged(nameof(HasBrokenMissionLink));
                OnPropertyChanged(nameof(IsStartQuestUnlinked));
                OnPropertyChanged(nameof(StartQuestLinkStatusText));
                OnPropertyChanged(nameof(ShowStartQuestLinkWarning));
                OnPropertyChanged(nameof(UseWideMissionLayout));
                OnPropertyChanged(nameof(ShowRightPropertiesPanel));
                OnPropertyChanged(nameof(ShowCreateMissionInActionPanel));
                OnPropertyChanged(nameof(NpcHasMissionDraft));
                OnPropertyChanged(nameof(TeleportCellText));
                OnPropertyChanged(nameof(SelectedResponseIncomplete));
                OnPropertyChanged(nameof(HasIncompleteResponses));
                OnPropertyChanged(nameof(HasActionConflicts));
                OnPropertyChanged(nameof(PropertiesTitle));
                if (HasAction)
                {
                    if (!ReferenceEquals(_conditionEditorAction, SelectedAction))
                    {
                        _conditionEditorAction = SelectedAction;
                        ActionConditionEditorOpen = ShowFriendlyConditionEditor;
                    }
                }
                else
                {
                    _conditionEditorAction = null;
                    ActionConditionEditorOpen = false;
                }

                CaptureGotoEditSnapshot();
                SyncInlineConditionHosts();
                RefreshResponseActionTabs();
                OnPropertyChanged(nameof(PropertiesPanelTitle));
                RaiseEditCommands();
            }
            finally
            {
                if (isOuterSelection
                    && SelectedAction?.Accion == DialogActionCodes.StartQuest
                    && !StartQuestLinkUi.HasValidLink(SelectedAction, Workspace.Missions))
                    ScheduleStartQuestLinkIfNeeded();
                _selectedNodeChangeDepth--;
            }
        }
    }

    private void ScheduleStartQuestLinkIfNeeded()
    {
        if (_repairStartQuestLinkScheduled) return;
        _repairStartQuestLinkScheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _repairStartQuestLinkScheduled = false;
            RequestStartQuestLinkIfNeeded();
        }, DispatcherPriority.Background);
    }

    private void RequestStartQuestLinkIfNeeded()
    {
        if (SelectedAction?.Accion != DialogActionCodes.StartQuest) return;
        if (StartQuestLinkUi.HasValidLink(SelectedAction, Workspace.Missions)) return;
        StartQuestActionNeedsLink?.Invoke(SelectedAction);
    }

    public DialogQuestionDraft? SelectedQuestion =>
        SelectedNode is DialogTreeNodeVm n && n.Kind == DialogTreeNodeKind.Question ? n.Question : null;

    public DialogResponseDraft? SelectedResponse =>
        SelectedNode is DialogTreeNodeVm n && n.Kind == DialogTreeNodeKind.Response ? n.Response : null;

    public DialogActionDraft? SelectedAction =>
        SelectedNode is DialogTreeNodeVm n && n.Kind == DialogTreeNodeKind.Action ? n.Action : null;

    public bool HasQuestion => SelectedQuestion is not null;
    public bool HasResponse => SelectedResponse is not null;
    public bool HasAction => SelectedAction is not null;
    public bool IsGotoAction => SelectedAction?.Accion == DialogActionCodes.GotoQuestion;
    public bool IsStartQuestAction => SelectedAction?.Accion == DialogActionCodes.StartQuest;
    public bool IsTeleportAction => SelectedAction?.Accion == DialogActionCodes.Teleport;
    public bool IsConfirmObjectiveAction => SelectedAction?.Accion == DialogActionCodes.ConfirmObjective;
    public bool IsConfirmMissionAction => SelectedAction?.Accion == DialogActionCodes.ConfirmMission;
    public bool IsFriendlyKnownAction =>
        SelectedAction is { Accion: DialogActionCodes.Teleport or DialogActionCodes.GotoQuestion
            or DialogActionCodes.StartQuest or DialogActionCodes.ConfirmObjective
            or DialogActionCodes.ConfirmMission };
    public bool IsConfirmActionPending => IsConfirmObjectiveAction || IsConfirmMissionAction;

    /// <summary>Friendly teleport Map/Cell editors (not forced advanced placeholder).</summary>
    public bool ShowFriendlyTeleportEditor =>
        IsTeleportAction && SelectedAction is not null && !_forceRawActionUi.Contains(SelectedAction);

    public bool ShowFriendlyGotoEditor =>
        IsGotoAction && SelectedAction is not null && !_forceRawActionUi.Contains(SelectedAction);

    public bool ShowFriendlyQuestEditor =>
        IsStartQuestAction && SelectedAction is not null && !_forceRawActionUi.Contains(SelectedAction);

    /// <summary>MISSIONFLOW.2B — wide layout while a linked mission exists on the active response.</summary>
    public bool UseWideMissionLayout => HasLinkedMission;

    /// <summary>Right column visible for action props and/or linked mission editor.</summary>
    public bool ShowRightPropertiesPanel => HasAction || HasLinkedMission;

    /// <summary>
    /// Show raw accion/args/condicion: Acción avanzada, 43/53, or unknown codes.
    /// Hidden for Teleport / Goto / StartQuest friendly editors.
    /// </summary>
    public bool ShowActionRawFields =>
        HasAction && SelectedAction is not null && (
            _forceRawActionUi.Contains(SelectedAction)
            || IsConfirmActionPending
            || (!IsGotoAction && !IsStartQuestAction && !IsTeleportAction));

    /// <summary>Legacy alias for raw action UI.</summary>
    public bool IsRawAdvancedAction => ShowActionRawFields;

    /// <summary>Friendly actions: condition editor without exposing accion/args.</summary>
    public bool ShowFriendlyConditionEditor => HasAction && !ShowActionRawFields;

    public bool HasActionCondition =>
        SelectedAction is not null && !string.IsNullOrWhiteSpace(SelectedAction.Condicion);

    public string ActionConditionSummary
    {
        get
        {
            if (!HasActionCondition) return "Sin condición";
            var c = SelectedAction!.Condicion.Replace("\r", " ").Replace("\n", " ").Trim();
            return c.Length <= 48 ? c : c[..48] + "…";
        }
    }

    public string ActionConditionText
    {
        get => SelectedAction?.Condicion ?? "";
        set
        {
            if (SelectedAction is null) return;
            var v = value ?? "";
            if (SelectedAction.Condicion == v) return;
            SelectedAction.Condicion = v;
            Persist();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActionCondition));
            OnPropertyChanged(nameof(ActionConditionSummary));
            OnPropertyChanged(nameof(InlineConditionExpanderHeader));
            OnPropertyChanged(nameof(ShowConditionEmptyPrompt));
            OnPropertyChanged(nameof(ShowConditionSummaryRow));
        }
    }

    public bool ActionConditionEditorOpen
    {
        get => _actionConditionEditorOpen;
        set
        {
            if (!SetProperty(ref _actionConditionEditorOpen, value)) return;
            OnPropertyChanged(nameof(ShowConditionEmptyPrompt));
            OnPropertyChanged(nameof(ShowConditionSummaryRow));
            OnPropertyChanged(nameof(ShowConditionTextEditor));
            OnPropertyChanged(nameof(InlineConditionExpanderHeader));
        }
    }

    public string InlineConditionExpanderHeader =>
        HasActionCondition ? $"Condición: {ActionConditionSummary}" : "Condición (opcional)";

    private void SyncInlineConditionHosts()
    {
        foreach (var node in FlatBubbles)
        {
            if (node.IsResponse)
                node.ShowsInlineCondition = false;
        }

        if (!ShowFriendlyConditionEditor)
            return;

        DialogTreeNodeVm? host = null;
        if (_selectedNode is DialogTreeNodeVm sel)
        {
            if (sel.IsResponse && sel.HasActions)
                host = sel;
            else if (sel.IsAction)
                host = FlatBubbles.FirstOrDefault(r => r.IsResponse && r.Children.Contains(sel));
        }

        if (host is not null)
            host.ShowsInlineCondition = true;
    }

    public bool ShowConditionEmptyPrompt =>
        ShowFriendlyConditionEditor && !ActionConditionEditorOpen && !HasActionCondition;

    public bool ShowConditionSummaryRow =>
        ShowFriendlyConditionEditor && !ActionConditionEditorOpen && HasActionCondition;

    public bool ShowConditionTextEditor =>
        ShowFriendlyConditionEditor && ActionConditionEditorOpen;

    public string ActionTypeLabel
    {
        get
        {
            if (SelectedAction is null) return "";
            if (_forceRawActionUi.Contains(SelectedAction))
                return "Acción avanzada";
            return FormatActionFriendlyName(SelectedAction.Accion);
        }
    }

    public string PropertiesTitle =>
        HasAction ? "PROPIEDADES DE LA ACCIÓN" : "Editor visual";

    public string PropertiesPanelTitle
    {
        get
        {
            if (HasAction)
            {
                if (IsStartQuestAction && HasLinkedMission)
                    return "Misión · Propiedades";
                return $"{ActionTypeLabel} · Propiedades";
            }

            if (HasLinkedMission)
                return "Misión · Propiedades";
            return "Propiedades de la acción";
        }
    }

    public bool ShowMultipleResponseActionTabs =>
        ShowRightPropertiesPanel && ResponseActionTabs.Count >= 2;

    public string NpcDisplayName
    {
        get
        {
            if (SelectedNpc is null) return "NPC";
            return string.IsNullOrWhiteSpace(SelectedNpc.Nombre) ? "(sin nombre)" : SelectedNpc.Nombre.Trim();
        }
    }

    public int SelectedNpcGfxId => SelectedNpc?.GfxId ?? 0;

    public string TeleportMapText
    {
        get
        {
            if (!ShowFriendlyTeleportEditor || SelectedAction is null) return "";
            return SplitTeleportArgs(SelectedAction.Args).map;
        }
        set
        {
            if (!ShowFriendlyTeleportEditor || SelectedAction is null) return;
            var cell = SplitTeleportArgs(SelectedAction.Args).cell;
            SelectedAction.Args = ComposeTeleportArgs(value, cell);
            NotifyTextEdited();
            OnPropertyChanged();
        }
    }

    public string TeleportCellText
    {
        get
        {
            if (!ShowFriendlyTeleportEditor || SelectedAction is null) return "";
            return SplitTeleportArgs(SelectedAction.Args).cell;
        }
        set
        {
            if (!ShowFriendlyTeleportEditor || SelectedAction is null) return;
            var map = SplitTeleportArgs(SelectedAction.Args).map;
            SelectedAction.Args = ComposeTeleportArgs(map, value);
            NotifyTextEdited();
            OnPropertyChanged();
        }
    }

    /// <summary>CONT-DIALOG.1 — selected response has 0 actions (CONT.5 would block).</summary>
    public bool SelectedResponseIncomplete =>
        IsInteractiveMode
        && SelectedResponse is not null
        && DialogDraftBatch.IsResponseIncomplete(SelectedResponse);

    /// <summary>Any response of the selected NPC lacks actions (Interactive only).</summary>
    public bool HasIncompleteResponses =>
        IsInteractiveMode
        && SelectedNpc is not null
        && Workspace.Dialogs.HasIncompleteResponsesForNpc(SelectedNpc.Id);

    /// <summary>DIALOGFLOW.3 — duplicate / incompatible actions on any response of the selected NPC.</summary>
    public bool HasActionConflicts =>
        IsInteractiveMode
        && SelectedNpc is not null
        && Workspace.Dialogs.HasActionRuleViolationsForNpc(SelectedNpc.Id);

    public bool IsSimpleMode
    {
        get => SelectedNpc?.DialogMode == NpcDialogMode.Simple;
        set
        {
            if (!value || SelectedNpc is null || SelectedNpc.DialogMode == NpcDialogMode.Simple)
                return;
            ApplyDialogMode(NpcDialogMode.Simple);
        }
    }

    public bool IsInteractiveMode
    {
        get => SelectedNpc?.DialogMode == NpcDialogMode.Interactive;
        set
        {
            if (!value || SelectedNpc is null || SelectedNpc.DialogMode == NpcDialogMode.Interactive)
                return;
            ApplyDialogMode(NpcDialogMode.Interactive);
        }
    }

    public string SimpleDialogTextLocal
    {
        get => SelectedNpc?.SimpleDialogTextLocal ?? "";
        set
        {
            if (SelectedNpc is null) return;
            var v = value ?? "";
            if (SelectedNpc.SimpleDialogTextLocal == v) return;
            SelectedNpc.SimpleDialogTextLocal = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SimpleBubbleBodyText));
            OnPropertyChanged(nameof(IsPendingDialogEs));
            OnPropertyChanged(nameof(IsSimpleDialogComplete));
        }
    }

    public string SimpleDialogIdText
    {
        get => SelectedNpc is null || SelectedNpc.Pregunta <= 0
            ? ""
            : SelectedNpc.Pregunta.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (SelectedNpc is null) return;
            var trimmed = (value ?? "").Trim();
            int id = 0;
            if (!string.IsNullOrEmpty(trimmed)
                && (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out id) || id < 0))
            {
                StatusText = "ID de diálogo inválido (usa un entero > 0 existente en dialog_es).";
                OnPropertyChanged();
                return;
            }
            if (SelectedNpc.Pregunta == id && !SelectedNpc.DialogEsIdProvisional) return;
            SelectedNpc.Pregunta = id;
            // Manual ID = reuse existing dialog_es entry (not a provisional reservation).
            SelectedNpc.DialogEsIdProvisional = false;
            Persist();
            OnPropertyChanged();
            OnPropertyChanged(nameof(InitialQuestionLabel));
            OnPropertyChanged(nameof(SimpleBubbleHeaderTitle));
            RefreshSimpleDialogEsUi();
            OnPropertyChanged(nameof(IsPendingDialogEs));
            OnPropertyChanged(nameof(IsSimpleDialogComplete));
            StatusText = id > 0
                ? $"Diálogo simple: reutiliza ID existente {id}. Tabla: npcs_modelo · Columna: pregunta"
                : (IsPendingDialogEs
                    ? _simpleUi.BannerTitle
                    : "Diálogo simple: texto nuevo (ID existente opcional)");
        }
    }

    public bool IsPendingDialogEs => SelectedNpc?.IsPendingDialogEs == true;
    public bool IsSimpleDialogComplete => SelectedNpc?.IsSimpleDialogComplete == true;
    public bool IsDialogEsPublished =>
        SelectedNpc is not null
        && SelectedNpc.DialogEsPublished
        && !SelectedNpc.IsPendingDialogEs
        && !SelectedNpc.PublishedBd;
    public string SimplePendingTitle => _simpleUi.BannerTitle;
    public string SimplePendingDetails => _simpleUi.FormatDetails();
    public string SimplePublishedDetails
    {
        get
        {
            if (SelectedNpc is null || !IsDialogEsPublished) return "";
            var id = SelectedNpc.Pregunta.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var ver = SelectedNpc.DialogEsPublishedVersion?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "—";
            return $"ID D.q final: {id}\ndialog_es activo: {ver}\nTabla: npcs_modelo\nColumna: pregunta → {id}";
        }
    }

    public string SimpleBubbleHeaderTitle
    {
        get
        {
            var name = NpcDisplayName;
            if (SelectedNpc is null) return name;
            if (SelectedNpc.Pregunta > 0)
                return $"{name} ({SelectedNpc.Pregunta.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            if (_simpleUi.ProvisionalDqId is int dq)
                return $"{name} ({dq.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            return name;
        }
    }

    public string SimpleBubbleBodyText
    {
        get
        {
            var t = SelectedNpc?.SimpleDialogTextLocal;
            return string.IsNullOrWhiteSpace(t) ? "(sin texto)" : t;
        }
    }

    public string NpcHeader
    {
        get
        {
            if (SelectedNpc is null) return "Sin NPC";
            var name = string.IsNullOrWhiteSpace(SelectedNpc.Nombre) ? "(sin nombre)" : SelectedNpc.Nombre;
            return $"NPC {SelectedNpc.Id} — {name}";
        }
    }

    public string InitialQuestionLabel
    {
        get
        {
            if (SelectedNpc is null) return "Pregunta inicial: —";
            if (SelectedNpc.DialogMode == NpcDialogMode.Simple)
            {
                if (SelectedNpc.Pregunta > 0)
                    return $"Diálogo simple · ID existente {SelectedNpc.Pregunta}";
                if (SelectedNpc.IsPendingDialogEs)
                {
                    var id = _simpleUi.ProvisionalDqId is int dq
                        ? dq.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "—";
                    return $"Diálogo simple · ⚠ Pendiente de publicación dialog_es · D.q {id}";
                }
                return "Diálogo simple · texto nuevo (ID existente opcional)";
            }
            return $"Pregunta inicial: {(SelectedNpc.Pregunta <= 0 ? "(ninguna)" : SelectedNpc.Pregunta.ToString())}";
        }
    }

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
                NewQuestionCommand.RaiseCanExecuteChanged();
        }
    }

    public int DbMaxQuestionId
    {
        get => _dbMaxQuestionId;
        private set => SetProperty(ref _dbMaxQuestionId, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseEditCommands();
        }
    }

    public bool AdvancedExpanded
    {
        get => _advancedExpanded;
        set => SetProperty(ref _advancedExpanded, value);
    }

    public int NextQuestionId => Workspace.Dialogs.NextQuestionId;

    public DialogQuestionDraft? LinkTargetQuestion
    {
        get => SelectedAction?.TargetQuestionId is int id
            ? Workspace.Dialogs.FindQuestion(id)
            : null;
        set
        {
            if (SelectedAction is null) return;
            if (value is null)
                Workspace.Dialogs.ClearGotoLink(SelectedAction);
            else
                Workspace.Dialogs.LinkGotoQuestion(SelectedAction, value.Id);
            PersistAndRefresh();
            OnPropertyChanged();
        }
    }

    public MissionDraft? LinkTargetMission
    {
        get => ContextStartQuestAction?.TargetMissionDraftId is Guid id
            ? Workspace.Missions.FindByDraftId(id)
            : null;
        set
        {
            if (ContextStartQuestAction is not { } action) return;
            if (_reloadingMissionChoices && value is null) return;
            if (value is null)
            {
                Workspace.Dialogs.ClearStartMissionLink(action);
            }
            else
            {
                Workspace.Dialogs.LinkStartMission(action, value.DraftId);
                StartQuestMissionSelected?.Invoke(action);
            }

            StartQuestLinkChanged?.Invoke();
            ScheduleRefreshAfterMissionLink();
        }
    }

    public event Action? StartQuestLinkChanged;

    public event Action<DialogActionDraft>? StartQuestMissionSelected;

    /// <summary>MISSIONS.ACTION44.LINK.1 — accion=44 needs EnsureMission + TargetMissionDraftId.</summary>
    public event Action<DialogActionDraft>? StartQuestActionNeedsLink;

    /// <summary>MISSIONS.DIALOG.ROLES.3D — inc/comp role questions removed after accion=44 delete.</summary>
    public event Action<Guid?>? StartQuestSecondaryRolesCleared;

    /// <summary>MISSIONS.PANEL.RUNTIME.DEBUG.1 — refresh inline mission panel debug block.</summary>
    public event Action? MissionPanelRuntimeDebugRefresh;

    public bool HasBrokenMissionLink =>
        SelectedAction?.Accion == DialogActionCodes.StartQuest
        && SelectedAction.TargetMissionDraftId is Guid id
        && Workspace.Missions.FindByDraftId(id) is null;

    public bool IsStartQuestUnlinked =>
        SelectedAction?.Accion == DialogActionCodes.StartQuest
        && (SelectedAction.TargetMissionDraftId is null || HasBrokenMissionLink);

    public string StartQuestLinkStatusText
    {
        get
        {
            if (!IsStartQuestAction) return "";
            if (HasLinkedMission) return "Misión vinculada";
            if (HasBrokenMissionLink) return "Acción 44 · enlace roto";
            if (_startQuestLinkAmbiguous) return "Acción 44 sin misión vinculada";
            if (IsStartQuestUnlinked) return "Acción 44 sin misión vinculada";
            return "";
        }
    }

    private bool _startQuestLinkAmbiguous;

    public bool ShowStartQuestLinkWarning =>
        IsStartQuestAction && (IsStartQuestUnlinked || _startQuestLinkAmbiguous);

    public void SetStartQuestLinkAmbiguous(bool ambiguous)
    {
        if (_startQuestLinkAmbiguous == ambiguous) return;
        _startQuestLinkAmbiguous = ambiguous;
        OnPropertyChanged(nameof(StartQuestLinkStatusText));
        OnPropertyChanged(nameof(ShowStartQuestLinkWarning));
    }

    public Guid? LinkTargetMissionDraftIdValue
    {
        get => ContextStartQuestAction?.TargetMissionDraftId;
        set
        {
            if (ContextStartQuestAction is not { } action) return;
            if (_reloadingMissionChoices && (value is null || value == Guid.Empty)) return;
            if (action.TargetMissionDraftId == value) return;
            if (value is null || value == Guid.Empty)
            {
                Workspace.Dialogs.ClearStartMissionLink(action);
            }
            else if (value is Guid draftId)
            {
                var m = Workspace.Missions.FindByDraftId(draftId);
                if (m is null) return;
                Workspace.Dialogs.LinkStartMission(action, m.DraftId);
                StartQuestMissionSelected?.Invoke(action);
            }

            OnPropertyChanged(nameof(LinkTargetMission));
            OnPropertyChanged(nameof(LinkTargetMissionDraftIdValue));
            OnPropertyChanged(nameof(HasLinkedMission));
            OnPropertyChanged(nameof(HasBrokenMissionLink));
            OnPropertyChanged(nameof(IsStartQuestUnlinked));
            OnPropertyChanged(nameof(StartQuestLinkStatusText));
            OnPropertyChanged(nameof(ShowStartQuestLinkWarning));
            OnPropertyChanged(nameof(ShowCreateMissionInActionPanel));
            OnPropertyChanged(nameof(UseWideMissionLayout));
            ReloadMissionChoices();
            StartQuestLinkChanged?.Invoke();
            ScheduleRefreshAfterMissionLink(reloadMissionChoices: true);
        }
    }

    public void RefreshAfterMissionLink(bool reloadMissionChoices = false)
    {
        if (reloadMissionChoices)
            ReloadMissionChoices();
        Persist();
        RebuildTreeKeepingSelection();
        OnPropertyChanged(nameof(LinkTargetMission));
        OnPropertyChanged(nameof(LinkTargetMissionDraftIdValue));
        OnPropertyChanged(nameof(HasLinkedMission));
        OnPropertyChanged(nameof(HasBrokenMissionLink));
        OnPropertyChanged(nameof(IsStartQuestUnlinked));
        OnPropertyChanged(nameof(StartQuestLinkStatusText));
        OnPropertyChanged(nameof(ShowStartQuestLinkWarning));
        OnPropertyChanged(nameof(ShowCreateMissionInActionPanel));
        OnPropertyChanged(nameof(UseWideMissionLayout));
        OnPropertyChanged(nameof(ShowRightPropertiesPanel));
        RefreshResponseActionTabs();
        OnPropertyChanged(nameof(PropertiesPanelTitle));
        StartQuestLinkChanged?.Invoke();
    }

    public void ScheduleRefreshAfterMissionLink(bool reloadMissionChoices = false)
    {
        if (_refreshMissionLinkScheduled) return;
        _refreshMissionLinkScheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _refreshMissionLinkScheduled = false;
            RefreshAfterMissionLink(reloadMissionChoices);
        }, DispatcherPriority.Background);
    }

    public void ReloadMissionChoices()
    {
        _reloadingMissionChoices = true;
        try
        {
            MissionChoices.Clear();
            if (SelectedNpc is null)
            {
                NotifyMissionChoiceUi();
                return;
            }

            var linkedId = ContextStartQuestAction?.TargetMissionDraftId;
            foreach (var m in StartQuestLinkUi.BuildMissionChoicesForNpc(
                         SelectedNpc.Id, linkedId, Workspace.Missions.Missions))
                MissionChoices.Add(m);
            NotifyMissionChoiceUi();
        }
        finally
        {
            _reloadingMissionChoices = false;
        }
    }

    private void NotifyMissionChoiceUi()
    {
        OnPropertyChanged(nameof(NpcHasMissionDraft));
        OnPropertyChanged(nameof(ShowCreateMissionInActionPanel));
        OnPropertyChanged(nameof(CanCreateMissionForNpc));
    }

    public bool HasLinkedMission => LinkTargetMission is not null;

    public bool NpcHasMissionDraft =>
        SelectedNpc is not null
        && Workspace.Missions.Missions.Any(m => m.StartNpcId == SelectedNpc.Id);

    /// <summary>Un NPC solo puede tener una misión; ocultar CTA si ya hay opciones.</summary>
    public bool CanCreateMissionForNpc =>
        ShowFriendlyQuestEditor && MissionChoices.Count == 0;

    public bool ShowCreateMissionInActionPanel => CanCreateMissionForNpc;

    public void ResetMissionRoleDisplayCache()
    {
        _cachedMissionRolePregDar = null;
        _cachedMissionRolePregInc = null;
        _cachedMissionRolePregComp = null;
    }

    /// <summary>Clear canvas selection when the owning NPC draft is removed.</summary>
    public void OnNpcDeleted(int npcId)
    {
        if (SelectedNpc?.Id == npcId)
        {
            SelectedNode = null;
            ResetMissionRoleDisplayCache();
        }
    }

    public void RefreshTreeDisplay() => RebuildTreeKeepingSelection();

    public void SelectQuestionOnCanvas(int questionId)
    {
        if (SelectedNpc is null) return;
        RebuildTreeKeepingSelection();
        SelectQuestion(questionId);
    }

    public bool TryLinkMissionToSelectedResponse(MissionDraft mission)
    {
        if (SelectedResponse is null || SelectedQuestion is null) return false;
        var questionId = SelectedQuestion.Id;
        var responseId = SelectedResponse.DraftId;

        var existing = SelectedResponse.Actions.FirstOrDefault(a => a.Accion == DialogActionCodes.StartQuest);
        if (existing is not null)
        {
            Workspace.Dialogs.LinkStartMission(existing, mission.DraftId);
            PersistAndRefresh();
            SelectAction(questionId, responseId, existing);
            return true;
        }

        if (!Workspace.Dialogs.TryAddAction(SelectedResponse, DialogActionCodes.StartQuest, out var added, out _))
            return false;

        Workspace.Dialogs.LinkStartMission(added!, mission.DraftId);
        PersistAndRefresh();
        SelectAction(questionId, responseId, added!);
        return true;
    }

    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand ReloadNpcsCommand { get; }
    public RelayCommand NewQuestionCommand { get; }
    public RelayCommand DuplicateQuestionCommand { get; }
    public RelayCommand DeleteQuestionCommand { get; }
    public RelayCommand SetInitialCommand { get; }
    public RelayCommand AddResponseCommand { get; }
    public RelayCommand DuplicateResponseCommand { get; }
    public RelayCommand DeleteResponseCommand { get; }
    public RelayCommand MoveResponseUpCommand { get; }
    public RelayCommand MoveResponseDownCommand { get; }
    public RelayCommand AddGotoActionCommand { get; }
    public RelayCommand AddQuestActionCommand { get; }
    public RelayCommand AddTeleportActionCommand { get; }
    public RelayCommand AddConfirmObjectiveActionCommand { get; }
    public RelayCommand AddConfirmMissionActionCommand { get; }
    public RelayCommand AddAdvancedActionCommand { get; }
    public RelayCommand DeleteActionCommand { get; }
    public RelayCommand CreateLinkedQuestionCommand { get; }
    public RelayCommand ClearLinkCommand { get; }
    public RelayCommand DuplicateSelectedCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand BeginEditActionConditionCommand { get; }
    public RelayCommand DoneEditActionConditionCommand { get; }
    public RelayCommand ClearActionConditionCommand { get; }
    public RelayCommand SelectResponseActionCommand { get; }

    public bool CanAddPlayerResponse => CanEditQuestion();
    public bool CanAddActionFromToolbar => CanAddAction();

    public async Task InitializeAsync()
    {
        ReloadNpcs();
        await RefreshMaxAsync();
        await EnsureDialogEsRemoteAsync();
    }

    public void ReloadNpcs()
    {
        NpcChoices.Clear();
        foreach (var n in Workspace.Npcs.Drafts)
            NpcChoices.Add(n);

        ReloadMissionChoices();

        if (SelectedNpc is not null)
        {
            var id = SelectedNpc.Id;
            SelectedNpc = NpcChoices.FirstOrDefault(n => n.Id == id);
        }
        else if (NpcChoices.Count > 0)
        {
            SelectedNpc = NpcChoices[0];
        }

        RebuildTreeKeepingSelection();
        StatusText = NpcChoices.Count == 0
            ? "No hay NPC borrador. Crea uno en Contenido → NPC."
            : $"NPC borrador: {NpcChoices.Count} · próximo pregunta ID = {Workspace.Dialogs.NextQuestionId}";
    }

    public void NotifyDbMaxApplied(int questionMax, int responseMax)
    {
        Workspace.Dialogs.SetDbMaxQuestionId(questionMax);
        Workspace.Dialogs.SetDbMaxResponseId(responseMax);
        DbMaxQuestionId = questionMax;
        DbReady = true;
        StatusText =
            $"MAX(npc_preguntas.id) = {questionMax} · próximo = {Workspace.Dialogs.NextQuestionId} · " +
            $"MAX(npc_respuestas.id) = {responseMax} · próximo = {Workspace.Dialogs.NextResponseId}";
        OnPropertyChanged(nameof(NextQuestionId));
        Persist();
    }

    public async Task RefreshMaxAsync()
    {
        IsBusy = true;
        try
        {
            var repo = CreateRepository();
            if (repo is null)
            {
                DbReady = false;
                StatusText = "Configura MySQL para leer MAX(npc_preguntas.id).";
                return;
            }

            var max = await repo.GetMaxIdAsync().ConfigureAwait(true);
            Workspace.Dialogs.SetDbMaxQuestionId(max);
            DbMaxQuestionId = max;

            var respRepo = CreateResponseRepository();
            if (respRepo is not null)
            {
                var maxR = await respRepo.GetMaxIdAsync().ConfigureAwait(true);
                Workspace.Dialogs.SetDbMaxResponseId(maxR);
            }

            DbReady = true;
            StatusText =
                $"MAX(npc_preguntas.id) = {max} · próximo = {Workspace.Dialogs.NextQuestionId} · " +
                $"MAX(npc_respuestas.id) = {Workspace.Dialogs.DbMaxResponseId} · próximo = {Workspace.Dialogs.NextResponseId} · solo lectura";
            OnPropertyChanged(nameof(NextQuestionId));
            if (SelectedNpc is { DialogMode: NpcDialogMode.Interactive } npc && DbReady)
                EnsureInteractiveStarter(npc);
            Persist();
            RebuildTreeKeepingSelection();
            SelectInitialQuestionIfNeeded();
        }
        catch (Exception ex)
        {
            DbReady = false;
            StatusText = "Error leyendo MAX(npc_preguntas.id): " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NotifyTextEdited()
    {
        // Keep typing responsive: refresh only the visible bubble body immediately.
        // Disk persist + status banners are debounced (full tree rebuild is not needed for TextLocal).
        if (SelectedNode is DialogTreeNodeVm node)
            node.NotifyDisplayTextChanged();
        OnPropertyChanged(nameof(SimpleBubbleBodyText));

        MissionPanelRuntimeDebugRefresh?.Invoke();
        ScheduleDebouncedTextEditFlush();
    }

    public void BeginEditConditionForResponse(DialogTreeNodeVm responseNode)
    {
        if (responseNode.Kind != DialogTreeNodeKind.Response || responseNode.Response is null)
            return;
        SelectedNode = responseNode;
        var actionNode = responseNode.Children.FirstOrDefault(c => c.IsAction);
        if (actionNode is null)
        {
            StatusText = "Añade una acción antes de configurar la condición.";
            return;
        }
        SelectedNode = actionNode;
        ActionConditionEditorOpen = true;
        StatusText = ShowActionRawFields
            ? "Edita la condición en el panel de la acción."
            : "Condición de la acción";
    }

    /// <summary>AI.5 — refresh mode radios + simple text after external draft mutation.</summary>
    public void RefreshAfterExternalDraftChange()
    {
        PersistAndRefresh();
        NotifyDialogModeUi();
        RefreshSimpleDialogEsUi();
    }

    private void ScheduleDebouncedTextEditFlush()
    {
        _textEditFlushTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _textEditFlushTimer.Tick -= OnTextEditFlushTick;
        _textEditFlushTimer.Tick += OnTextEditFlushTick;
        _textEditFlushTimer.Stop();
        _textEditFlushTimer.Start();
    }

    private void CancelDebouncedTextEditFlush()
    {
        if (_textEditFlushTimer is null)
            return;
        _textEditFlushTimer.Stop();
    }

    private void OnTextEditFlushTick(object? sender, EventArgs e)
    {
        CancelDebouncedTextEditFlush();
        FlushTextEditSideEffects();
    }

    private void FlushTextEditSideEffects()
    {
        if (TryHandleGotoActionTypeChange())
            return;

        Persist();
        OnPropertyChanged(nameof(InitialQuestionLabel));
        OnPropertyChanged(nameof(NpcHeader));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsSimpleDialogComplete));
        OnPropertyChanged(nameof(SimpleBubbleHeaderTitle));
        OnPropertyChanged(nameof(SimpleBubbleBodyText));
        OnPropertyChanged(nameof(SelectedResponseIncomplete));
        OnPropertyChanged(nameof(HasIncompleteResponses));
        OnPropertyChanged(nameof(HasActionConflicts));
        if (SelectedAction is not null)
            RebuildTreeKeepingSelection();
        RefreshSimpleDialogEsUi();
    }

    private void ApplyDialogMode(NpcDialogMode mode)
    {
        if (SelectedNpc is null) return;
        var npc = SelectedNpc;

        if (mode == NpcDialogMode.Simple)
        {
            var ownedIds = Workspace.Dialogs.QuestionsForNpc(npc.Id).Select(q => q.Id).ToHashSet();
            Workspace.Dialogs.RemoveQuestionsForNpc(npc.Id);
            foreach (var id in ownedIds)
                Workspace.Missions.ClearPreguntaReferences(id);
            // Si hubo conversación/misión y se vuelve a simple: desligar StartNpc y
            // objetivos «hablar/volver» a este NPC (si no, en juego sigue cumpliendo la misión).
            Workspace.Missions.ClearNpcReferences(npc.Id);
            if (ownedIds.Contains(npc.Pregunta))
            {
                npc.Pregunta = 0;
                npc.DialogEsIdProvisional = false;
            }
            npc.DialogMode = NpcDialogMode.Simple;
            SelectedNode = null;
            _forceRawActionUi.Clear();
            StatusText = "Modo diálogo simple: texto nuevo (ID existente opcional). No crea npc_preguntas ni npc_respuestas.";
        }
        else
        {
            if (Workspace.Dialogs.FindQuestion(npc.Pregunta) is null)
                npc.Pregunta = 0;
            npc.DialogMode = NpcDialogMode.Interactive;
            if (DbReady)
                EnsureInteractiveStarter(npc);
            StatusText = "Modo conversación interactiva (CONT.3).";
        }

        PersistAndRefresh();
        NotifyDialogModeUi();
        RefreshSimpleDialogEsUi();
        if (mode == NpcDialogMode.Interactive)
            SelectInitialQuestionIfNeeded();
    }

    /// <summary>
    /// Garantiza pregunta inicial + al menos una respuesta del jugador al entrar en modo interactivo.
    /// </summary>
    private DialogQuestionDraft? EnsureInteractiveStarter(NpcsModeloDraft npc)
    {
        DialogQuestionDraft q;
        if (npc.Pregunta > 0 && Workspace.Dialogs.FindQuestion(npc.Pregunta) is { } existing)
        {
            q = existing;
        }
        else
        {
            q = Workspace.Dialogs.CreateQuestion(npc.Id);
            Workspace.Dialogs.SetInitialQuestion(npc, q.Id);
        }

        if (q.Responses.Count == 0)
            Workspace.Dialogs.AddResponse(q);

        return q;
    }

    private void NotifyDialogModeUi()
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsInteractiveMode));
        OnPropertyChanged(nameof(SimpleDialogTextLocal));
        OnPropertyChanged(nameof(SimpleDialogIdText));
        OnPropertyChanged(nameof(SimpleBubbleHeaderTitle));
        OnPropertyChanged(nameof(SimpleBubbleBodyText));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsSimpleDialogComplete));
        OnPropertyChanged(nameof(IsDialogEsPublished));
        OnPropertyChanged(nameof(SimplePendingTitle));
        OnPropertyChanged(nameof(SimplePendingDetails));
        OnPropertyChanged(nameof(SimplePublishedDetails));
        OnPropertyChanged(nameof(HasIncompleteResponses));
        OnPropertyChanged(nameof(HasActionConflicts));
        OnPropertyChanged(nameof(SelectedResponseIncomplete));
        NewQuestionCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSimpleDialogEsUi()
    {
        if (SelectedNpc is null)
        {
            _simpleUi = new DialogEsSimpleUiState { IsPending = false };
        }
        else
        {
            var last = _dialogEsSession.Last;
            if (_dialogEsLoading && last is null)
            {
                _simpleUi = new DialogEsSimpleUiState
                {
                    IsPending = SelectedNpc.IsPendingDialogEs,
                    Loading = SelectedNpc.IsPendingDialogEs,
                };
            }
            else if (last?.Success == true && last.Snapshot is not null)
            {
                _simpleUi = DialogEsSimpleUiResolver.ForNpc(
                    Workspace, SelectedNpc, last.Snapshot, last.StatusLabel);
            }
            else
            {
                var err = last?.Error;
                if (string.IsNullOrWhiteSpace(err) && _dialogEsCacheDirectory is not null)
                {
                    var snap = DialogEsSimpleUiResolver.TryLoadSnapshot(_dialogEsCacheDirectory, out var cacheStatus);
                    if (snap is not null)
                    {
                        _simpleUi = DialogEsSimpleUiResolver.ForNpc(Workspace, SelectedNpc, snap, cacheStatus);
                        goto Notify;
                    }
                    err = cacheStatus;
                }

                err ??= "SFTP no disponible.";
                _simpleUi = DialogEsSimpleUiResolver.ForNpc(Workspace, SelectedNpc, null, err);
            }
        }

        Notify:
        // Persist provisional D.q into pregunta so conversation / BD metadata show the real ID
        // while still blocking BD until Publicar cliente (DialogEsIdProvisional).
        if (SelectedNpc is not null
            && SelectedNpc.IsPendingDialogEs
            && _simpleUi.ProvisionalDqId is int provisional
            && provisional > 0
            && (SelectedNpc.Pregunta != provisional || !SelectedNpc.DialogEsIdProvisional))
        {
            SelectedNpc.Pregunta = provisional;
            SelectedNpc.DialogEsIdProvisional = true;
            Persist();
            OnPropertyChanged(nameof(SimpleDialogIdText));
            OnPropertyChanged(nameof(IsSimpleDialogComplete));
        }

        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(SimplePendingTitle));
        OnPropertyChanged(nameof(SimplePendingDetails));
        OnPropertyChanged(nameof(IsDialogEsPublished));
        OnPropertyChanged(nameof(SimplePublishedDetails));
        OnPropertyChanged(nameof(SimpleBubbleHeaderTitle));
        OnPropertyChanged(nameof(InitialQuestionLabel));
        if (_simpleUi.IsPending)
            StatusText = _simpleUi.CannotCalculate
                ? DialogEsRemoteLoadResult.CannotCalculateMessage
                : _simpleUi.BannerTitle;
        else if (IsDialogEsPublished)
            StatusText = "✓ dialog_es publicado · ⚠ Pendiente de publicación BD";
    }

    private async Task EnsureDialogEsRemoteAsync(bool forceRemote = false)
    {
        if (!forceRemote && _dialogEsSession.Last?.Success == true)
        {
            RefreshSimpleDialogEsUi();
            return;
        }

        // Previous failure must not stick forever — retry when opening/refreshing.
        if (!forceRemote && _dialogEsSession.Last is { Success: false })
            forceRemote = true;

        _dialogEsLoading = true;
        RefreshSimpleDialogEsUi();
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new DialogEsRemoteLoadRequest
            {
                Settings = settings,
                PlainPassword = password,
                ClientFactory = _sftpClientFactory,
            };
            await Task.Run(() => _dialogEsSession.GetOrFetch(request, forceRemote)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = DialogEsRemoteLoadResult.CannotCalculateMessage + "\n" + ex.Message;
        }
        finally
        {
            _dialogEsLoading = false;
            RefreshSimpleDialogEsUi();
        }
    }

    private void CreateQuestion()
    {
        if (SelectedNpc is null || !IsInteractiveMode || !DbReady) return;
        var q = Workspace.Dialogs.CreateQuestion(SelectedNpc.Id);
        if (SelectedNpc.Pregunta <= 0)
            Workspace.Dialogs.SetInitialQuestion(SelectedNpc, q.Id);
        Workspace.Dialogs.AddResponse(q);
        PersistAndRefresh();
        SelectQuestion(q.Id);
        StatusText = $"Pregunta provisional {q.Id} creada";
    }

    private void DuplicateSelectedQuestion()
    {
        if (SelectedQuestion is null) return;
        var copy = Workspace.Dialogs.DuplicateQuestion(SelectedQuestion);
        PersistAndRefresh();
        SelectQuestion(copy.Id);
        StatusText = $"Pregunta duplicada → {copy.Id}";
    }

    private void DeleteQuestionImmediate(int id)
    {
        if (SelectedNpc is null) return;
        Workspace.Dialogs.TryDeleteQuestion(id, unlinkAndDelete: true, out _);
        if (SelectedNpc.Pregunta == id)
            SelectedNpc.Pregunta = 0;
        Workspace.Missions.ClearPreguntaReferences(id);
        PersistAndRefresh();
        SelectedNode = null;
        SelectInitialQuestionIfNeeded();
        StatusText = $"Pregunta {id} eliminada";
    }

    private bool QuestionHasDeleteableContent(DialogQuestionDraft q) =>
        !string.IsNullOrWhiteSpace(q.TextLocal)
        || !string.IsNullOrWhiteSpace(q.Params)
        || !string.IsNullOrWhiteSpace(q.Alternos)
        || q.Responses.Any(ResponseHasDeleteableContent)
        || Workspace.Missions.MissionsReferencingPregunta(q.Id).Count > 0;

    private static bool ResponseHasDeleteableContent(DialogResponseDraft r) =>
        !string.IsNullOrWhiteSpace(r.TextLocal)
        || r.Actions.Count > 0
        || r.Actions.Any(a => !string.IsNullOrWhiteSpace(a.Condicion));

    private void DeleteSelectedQuestion()
    {
        if (SelectedQuestion is null || SelectedNpc is null) return;
        var id = SelectedQuestion.Id;

        var missionRefs = Workspace.Missions.MissionsReferencingPregunta(id);
        if (missionRefs.Count > 0)
        {
            var warn =
                $"La pregunta {id} está enlazada en {missionRefs.Count} misión(es).\n\n" +
                "OK = limpiar preg* de esas misiones y continuar.\nCancelar = no borrar.";
            if (MessageBox.Show(warn, "Pregunta en misión", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;
        }

        var result = Workspace.Dialogs.TryDeleteQuestion(id, unlinkAndDelete: true, out _);
        if (result != QuestionDeleteResult.Deleted)
            return;

        if (SelectedNpc.Pregunta == id)
            SelectedNpc.Pregunta = 0;

        Workspace.Missions.ClearPreguntaReferences(id);

        PersistAndRefresh();
        StatusText = $"Pregunta {id} eliminada";
    }

    private void SetInitialQuestion()
    {
        if (SelectedQuestion is null || SelectedNpc is null) return;
        Workspace.Dialogs.SetInitialQuestion(SelectedNpc, SelectedQuestion.Id);
        PersistAndRefresh();
        StatusText = $"Pregunta inicial del NPC {SelectedNpc.Id} → {SelectedQuestion.Id}";
        OnPropertyChanged(nameof(InitialQuestionLabel));
    }

    private void AddResponse()
    {
        if (SelectedQuestion is null) return;
        var qid = SelectedQuestion.Id;
        var r = Workspace.Dialogs.AddResponse(SelectedQuestion);
        PersistAndRefresh();
        SelectResponse(qid, r.DraftId);
        StatusText = "Respuesta añadida (DraftId interno, sin ID numérico de publicación)";
    }

    private void DuplicateSelectedResponse()
    {
        if (SelectedResponse is null || SelectedNode is not DialogTreeNodeVm node || node.Question is null)
            return;
        var copy = Workspace.Dialogs.DuplicateResponse(node.Question, SelectedResponse);
        PersistAndRefresh();
        SelectResponse(node.Question.Id, copy.DraftId);
    }

    private void DeleteSelectedResponse()
    {
        if (SelectedResponse is null || SelectedNode is not DialogTreeNodeVm node || node.Question is null)
            return;
        Workspace.Dialogs.RemoveResponse(node.Question, SelectedResponse);
        PersistAndRefresh();
    }

    private void MoveResponse(int delta)
    {
        if (SelectedResponse is null || SelectedNode is not DialogTreeNodeVm node || node.Question is null)
            return;
        var qid = node.Question.Id;
        var rid = SelectedResponse.DraftId;
        if (Workspace.Dialogs.MoveResponse(node.Question, SelectedResponse, delta))
        {
            PersistAndRefresh();
            SelectResponse(qid, rid);
        }
    }

    private DialogResponseDraft? ActiveResponseForActions =>
        SelectedResponse
        ?? (SelectedNode is DialogTreeNodeVm { Kind: DialogTreeNodeKind.Action } n ? n.Response : null);

    private void RefreshResponseActionTabs()
    {
        ResponseActionTabs.Clear();
        var response = ActiveResponseForActions;
        if (response is null || !ShowRightPropertiesPanel)
        {
            OnPropertyChanged(nameof(ShowMultipleResponseActionTabs));
            return;
        }

        foreach (var action in response.Actions)
        {
            ResponseActionTabs.Add(new ResponseActionTabVm(
                action,
                GetActionTabLabel(action),
                ReferenceEquals(SelectedAction, action)));
        }

        OnPropertyChanged(nameof(ShowMultipleResponseActionTabs));
    }

    private string GetActionTabLabel(DialogActionDraft action)
    {
        if (_forceRawActionUi.Contains(action))
            return "Acción avanzada";
        return FormatActionFriendlyName(action.Accion);
    }

    private bool CanSelectResponseAction(object? parameter) =>
        parameter is DialogActionDraft;

    private void SelectResponseAction(object? parameter)
    {
        if (parameter is not DialogActionDraft action)
            return;
        var response = ActiveResponseForActions;
        if (response is null || !response.Actions.Contains(action))
            return;
        var parentQ = FindParentQuestion(response);
        if (parentQ is null)
            return;
        SelectAction(parentQ.Id, response.DraftId, action);
    }

    /// <summary>StartQuest on the active response (selected or sibling when editing another action).</summary>
    public DialogActionDraft? ContextStartQuestAction
    {
        get
        {
            if (SelectedAction?.Accion == DialogActionCodes.StartQuest)
                return SelectedAction;
            return ActiveResponseForActions?
                .Actions.FirstOrDefault(a => a.Accion == DialogActionCodes.StartQuest);
        }
    }

    private void AddTeleportWithGotoReplace()
    {
        var response = ActiveResponseForActions;
        if (response is null) return;
        var gotoAction = response.Actions.FirstOrDefault(a => a.Accion == DialogActionCodes.GotoQuestion);
        if (gotoAction is not null
            && !DialogResponseActionRules.CanAddAction(response, DialogActionCodes.Teleport))
        {
            PromptReplaceGotoAction(gotoAction, response, DialogActionCodes.Teleport, inPlace: false);
            return;
        }

        AddPresetAction(DialogActionCodes.Teleport);
    }

    private void AddPresetAction(int accion)
    {
        var response = ActiveResponseForActions;
        if (response is null) return;
        if (!Workspace.Dialogs.TryAddAction(response, accion, out var a, out var error))
        {
            StatusText = error ?? "No se puede añadir la acción.";
            return;
        }

        var parentQ = FindParentQuestion(response);
        var responseId = response.DraftId;
        PersistAndRefresh();
        if (parentQ is not null)
            SelectAction(parentQ.Id, responseId, a!);
        StatusText = $"{FormatActionFriendlyName(accion)} añadida";
        if (accion is DialogActionCodes.ConfirmObjective or DialogActionCodes.ConfirmMission
            || accion is not (DialogActionCodes.Teleport or DialogActionCodes.GotoQuestion
                or DialogActionCodes.StartQuest))
            AdvancedExpanded = true;
    }

    private void AddGotoWithLinkedQuestion()
    {
        var response = ActiveResponseForActions;
        if (response is null || SelectedNpc is null) return;

        var teleport = response.Actions.FirstOrDefault(a => a.Accion == DialogActionCodes.Teleport);
        if (teleport is not null
            && !DialogResponseActionRules.CanAddAction(response, DialogActionCodes.GotoQuestion))
        {
            _forceRawActionUi.Remove(teleport);
            Workspace.Dialogs.RemoveAction(response, teleport);
        }

        if (!Workspace.Dialogs.TryAddAction(response, DialogActionCodes.GotoQuestion, out var a, out var error))
        {
            StatusText = error ?? "No se puede añadir Ir a otra pregunta.";
            return;
        }

        var newQ = Workspace.Dialogs.CreateQuestionLinkedFrom(a!, SelectedNpc.Id);
        PersistAndRefresh();
        SelectQuestion(newQ.Id);
        StatusText = $"Siguiente paso: pregunta #{newQ.Id} creada y enlazada";
    }

    private void CaptureGotoEditSnapshot()
    {
        if (SelectedAction?.Accion == DialogActionCodes.GotoQuestion)
        {
            _gotoEditSnapshotAction = SelectedAction;
            _gotoEditSnapshotAccion = SelectedAction.Accion;
            _gotoEditSnapshotTargetQuestionId = SelectedAction.TargetQuestionId;
            _gotoEditSnapshotArgs = SelectedAction.Args;
            return;
        }

        _gotoEditSnapshotAction = null;
    }

    private void RevertGotoEditSnapshot()
    {
        if (_gotoEditSnapshotAction is null)
            return;

        _gotoEditSnapshotAction.Accion = _gotoEditSnapshotAccion;
        _gotoEditSnapshotAction.TargetQuestionId = _gotoEditSnapshotTargetQuestionId;
        _gotoEditSnapshotAction.Args = _gotoEditSnapshotArgs;
        OnPropertyChanged(nameof(ActionTypeLabel));
        OnPropertyChanged(nameof(IsGotoAction));
        OnPropertyChanged(nameof(IsTeleportAction));
        OnPropertyChanged(nameof(ShowActionRawFields));
        OnPropertyChanged(nameof(ShowFriendlyTeleportEditor));
        OnPropertyChanged(nameof(ShowFriendlyGotoEditor));
    }

    private bool TryHandleGotoActionTypeChange()
    {
        if (_gotoEditSnapshotAction is null || SelectedAction != _gotoEditSnapshotAction)
            return false;
        if (_gotoEditSnapshotAccion != DialogActionCodes.GotoQuestion)
            return false;
        if (SelectedAction.Accion == DialogActionCodes.GotoQuestion)
            return false;

        var newAccion = SelectedAction.Accion;
        var response = SelectedNode is DialogTreeNodeVm { Response: { } fromNode }
            ? fromNode
            : FindResponseForAction(SelectedAction);
        if (response is null)
            return false;

        RevertGotoEditSnapshot();
        PromptReplaceGotoAction(_gotoEditSnapshotAction, response, newAccion, inPlace: true);
        return true;
    }

    private void PromptReplaceGotoAction(
        DialogActionDraft gotoAction,
        DialogResponseDraft response,
        int newAccion,
        bool inPlace)
    {
        var targetId = Workspace.Dialogs.ResolveGotoTargetQuestionId(gotoAction);
        var exclusive = Workspace.Dialogs.IsExclusiveGotoReference(gotoAction);
        var missionRefs = targetId is int qid
            ? Workspace.Missions.MissionsReferencingPregunta(qid).Count
            : 0;
        var canDeleteQuestion = exclusive && missionRefs == 0 && targetId is not null;

        string message;
        if (targetId is int linkedId)
        {
            message = canDeleteQuestion
                ? $"Esta acción está enlazada a la pregunta #{linkedId}.\n\n" +
                  "Al cambiar la acción, esta pregunta dejará de formar parte de este flujo.\n\n" +
                  "Si continúas, la pregunta enlazada y su contenido se eliminarán."
                : $"La pregunta #{linkedId} también está siendo utilizada por otro enlace.\n" +
                  "Se quitará únicamente este enlace.";
        }
        else
        {
            message = "Al cambiar esta acción se quitará el enlace a la siguiente pregunta.";
        }

        var result = MessageBox.Show(
            message,
            canDeleteQuestion ? "Cambiar y eliminar pregunta" : "Cambiar acción enlazada",
            MessageBoxButton.OKCancel,
            canDeleteQuestion ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (result != MessageBoxResult.OK)
        {
            if (inPlace)
                RevertGotoEditSnapshot();
            return;
        }

        ExecuteReplaceGotoAction(response, gotoAction, newAccion, inPlace, canDeleteQuestion);
    }

    private void ExecuteReplaceGotoAction(
        DialogResponseDraft response,
        DialogActionDraft gotoAction,
        int newAccion,
        bool inPlace,
        bool deleteLinkedQuestion)
    {
        var parentQ = FindParentQuestion(response);
        var responseId = response.DraftId;
        _forceRawActionUi.Remove(gotoAction);

        DialogActionDraft resultAction;
        if (deleteLinkedQuestion
            && Workspace.Dialogs.ResolveGotoTargetQuestionId(gotoAction) is int questionId)
        {
            Workspace.Dialogs.RemoveAction(response, gotoAction);
            Workspace.Dialogs.TryDeleteQuestion(questionId, unlinkAndDelete: true, out _);
            if (SelectedNpc?.Pregunta == questionId)
                SelectedNpc.Pregunta = 0;
            Workspace.Missions.ClearPreguntaReferences(questionId);

            if (!Workspace.Dialogs.TryAddAction(response, newAccion, out var added, out var error))
            {
                StatusText = error ?? "No se pudo aplicar la nueva acción.";
                PersistAndRefresh();
                return;
            }

            resultAction = added!;
        }
        else if (inPlace)
        {
            Workspace.Dialogs.ConvertActionAwayFromGoto(gotoAction, newAccion);
            resultAction = gotoAction;
            if (newAccion is DialogActionCodes.ConfirmObjective or DialogActionCodes.ConfirmMission
                || !DialogResponseActionRules.IsFriendlyCatalogAction(newAccion))
                _forceRawActionUi.Add(gotoAction);
        }
        else
        {
            Workspace.Dialogs.RemoveAction(response, gotoAction);
            if (!Workspace.Dialogs.TryAddAction(response, newAccion, out var added, out var error))
            {
                StatusText = error ?? "No se pudo aplicar la nueva acción.";
                PersistAndRefresh();
                return;
            }

            resultAction = added!;
        }

        CaptureGotoEditSnapshot();
        PersistAndRefresh();
        if (parentQ is not null)
            SelectAction(parentQ.Id, responseId, resultAction);
        StatusText = $"{FormatActionFriendlyName(newAccion)} aplicada";
        if (newAccion is DialogActionCodes.ConfirmObjective or DialogActionCodes.ConfirmMission
            || newAccion is not (DialogActionCodes.Teleport or DialogActionCodes.GotoQuestion
                or DialogActionCodes.StartQuest))
            AdvancedExpanded = true;
    }

    private DialogResponseDraft? FindResponseForAction(DialogActionDraft action)
    {
        foreach (var q in Workspace.Dialogs.Questions)
        {
            foreach (var r in q.Responses)
            {
                if (r.Actions.Any(a => ReferenceEquals(a, action)))
                    return r;
            }
        }

        return null;
    }

    private void ClearActionCondition()
    {
        if (SelectedAction is null) return;
        SelectedAction.Condicion = "";
        ActionConditionEditorOpen = false;
        Persist();
        OnPropertyChanged(nameof(ActionConditionText));
        OnPropertyChanged(nameof(HasActionCondition));
        OnPropertyChanged(nameof(ActionConditionSummary));
        OnPropertyChanged(nameof(InlineConditionExpanderHeader));
        OnPropertyChanged(nameof(ShowConditionEmptyPrompt));
        OnPropertyChanged(nameof(ShowConditionSummaryRow));
        OnPropertyChanged(nameof(ShowConditionTextEditor));
        SyncInlineConditionHosts();
    }

    private void AddAdvancedAction()
    {
        var response = ActiveResponseForActions;
        if (response is null) return;
        var parentQ = FindParentQuestion(response);
        var responseId = response.DraftId;
        // Placeholder Accion=0; se marca como UI avanzada (RAW) hasta que el usuario complete.
        var a = Workspace.Dialogs.AddActionAllowingRuleViolations(response, DialogActionCodes.Teleport);
        a.Args = "";
        a.Condicion = "";
        _forceRawActionUi.Add(a);
        PersistAndRefresh();
        if (parentQ is not null)
            SelectAction(parentQ.Id, responseId, a);
        StatusText = "Acción avanzada — completa accion / args / condicion";
    }

    private void DuplicateSelected()
    {
        if (HasQuestion)
            DuplicateSelectedQuestion();
        else if (HasResponse)
            DuplicateSelectedResponse();
    }

    private void DeleteSelected()
    {
        if (HasQuestion)
            DeleteSelectedQuestion();
        else if (HasResponse)
            DeleteSelectedResponse();
        else if (HasAction)
            DeleteSelectedAction();
    }

    public void DeleteActionNode(DialogTreeNodeVm node)
    {
        if (node.Kind != DialogTreeNodeKind.Action || node.Action is null || node.Response is null)
            return;
        SelectedNode = node;
        DeleteSelectedAction();
    }

    public void DeleteQuestionNode(DialogTreeNodeVm node)
    {
        if (node.Kind != DialogTreeNodeKind.Question || node.Question is null || SelectedNpc is null)
            return;
        if (node.IsInitial)
            return;
        SelectedNode = node;
        if (QuestionHasDeleteableContent(node.Question))
            DeleteSelectedQuestion();
        else
            DeleteQuestionImmediate(node.Question.Id);
    }

    public void DeleteResponseNode(DialogTreeNodeVm node)
    {
        if (node.Kind != DialogTreeNodeKind.Response || node.Response is null || node.Question is null)
            return;
        if (node.IsMandatoryStarter)
            return;
        SelectedNode = node;
        var response = node.Response;
        var question = node.Question;
        if (ResponseHasDeleteableContent(response))
        {
            if (MessageBox.Show(
                    "¿Eliminar esta respuesta y sus acciones enlazadas?",
                    "Eliminar respuesta",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
        }

        foreach (var a in response.Actions.ToList())
            TryCleanupSecondaryMissionRoles(a);

        Workspace.Dialogs.RemoveResponse(question, response);
        PersistAndRefresh();
        SelectQuestion(question.Id);
        StatusText = "Respuesta eliminada";
    }

    private MissionDialogRolesCleanupResult TryCleanupSecondaryMissionRoles(DialogActionDraft action)
    {
        if (action.Accion != DialogActionCodes.StartQuest || SelectedNpc is null)
            return new MissionDialogRolesCleanupResult();

        var result = MissionDialogRolesUi.RemoveSecondaryRolesOnStartQuestRemoved(
            action, SelectedNpc, Workspace);
        if (!result.Changed)
            return result;

        ResetMissionRoleDisplayCache();
        StartQuestSecondaryRolesCleared?.Invoke(result.MissionDraftId);
        return result;
    }

    private void DeleteSelectedAction()
    {
        if (SelectedAction is null || SelectedNode is not DialogTreeNodeVm node || node.Response is null)
            return;
        var action = SelectedAction;
        var questionId = node.Question?.Id;
        var responseId = node.Response.DraftId;
        TryCleanupSecondaryMissionRoles(action);
        _forceRawActionUi.Remove(action);
        Workspace.Dialogs.RemoveAction(node.Response, action);
        PersistAndRefresh();
        if (questionId is int qid)
            SelectResponse(qid, responseId);
        else
            SelectedNode = null;
    }

    private void CreateLinkedQuestion()
    {
        if (SelectedAction is null || SelectedNpc is null) return;
        var q = Workspace.Dialogs.CreateQuestionLinkedFrom(SelectedAction, SelectedNpc.Id);
        PersistAndRefresh();
        SelectQuestion(q.Id);
        StatusText = $"Nueva pregunta {q.Id} enlazada desde acción";
    }

    private void ClearLink()
    {
        if (SelectedAction is null) return;
        Workspace.Dialogs.ClearGotoLink(SelectedAction);
        PersistAndRefresh();
        OnPropertyChanged(nameof(LinkTargetQuestion));
    }

    /// <summary>Mission-flow UI: refresh canvas tags from MissionDraft roles (no duplicate role list).</summary>
    public void SetMissionQuestionRoles(int? pregDarId, int? pregIncId, int? pregCompId)
    {
        if (pregDarId == _cachedMissionRolePregDar
            && pregIncId == _cachedMissionRolePregInc
            && pregCompId == _cachedMissionRolePregComp)
            return;
        _cachedMissionRolePregDar = pregDarId;
        _cachedMissionRolePregInc = pregIncId;
        _cachedMissionRolePregComp = pregCompId;
        RebuildTreeKeepingSelection();
    }

    private string? ResolveMissionRoleTag(int questionId)
    {
        var npcId = SelectedNpc?.Id;
        var action = SelectedAction?.Accion == DialogActionCodes.StartQuest ? SelectedAction : null;
        var m = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, Workspace.Missions, npcId);
        return m is null ? null : MissionDialogRolesUi.ResolveRoleTag(m, questionId);
    }

    private void RebuildTreeKeepingSelection()
    {
        if (_rebuildingDialogTree) return;
        _rebuildingDialogTree = true;
        try
        {
            var kind = (SelectedNode as DialogTreeNodeVm)?.Kind;
            var qid = (SelectedNode as DialogTreeNodeVm)?.Question?.Id;
            var rid = (SelectedNode as DialogTreeNodeVm)?.Response?.DraftId;
            var actionSnapshot = (SelectedNode as DialogTreeNodeVm)?.Action;
            RebuildTree();
            if (kind == DialogTreeNodeKind.Question && qid is int qi)
                SelectQuestion(qi);
            else if (kind == DialogTreeNodeKind.Response && qid is int qr && rid is Guid g)
                SelectResponse(qr, g);
            else if (kind == DialogTreeNodeKind.Action && qid is int qa && rid is Guid ga && actionSnapshot is not null)
                SelectAction(qa, ga, actionSnapshot);
            else
                SelectInitialQuestionIfNeeded();
        }
        finally
        {
            _rebuildingDialogTree = false;
        }
    }

    private DialogTreeNodeVm BuildQuestionNode(DialogQuestionDraft q, bool isInitial, string npcName)
    {
        var node = new DialogTreeNodeVm(
            DialogTreeNodeKind.Question, q, null, null, isInitial, npcName,
            missionRoleTag: ResolveMissionRoleTag(q.Id));
        for (var i = 0; i < q.Responses.Count; i++)
        {
            var r = q.Responses[i];
            var rNode = new DialogTreeNodeVm(
                DialogTreeNodeKind.Response, q, r, null, false, npcName,
                isMandatoryStarter: isInitial && i == 0);
            foreach (var a in r.Actions)
            {
                string? linkedMissionName = null;
                if (a.Accion == DialogActionCodes.StartQuest && a.TargetMissionDraftId is Guid mid)
                    linkedMissionName = Workspace.Missions.FindByDraftId(mid)?.Nombre;
                rNode.Children.Add(new DialogTreeNodeVm(
                    DialogTreeNodeKind.Action, q, r, a, false, npcName, linkedMissionName: linkedMissionName));
            }
            node.Children.Add(rNode);
        }
        return node;
    }

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (SelectedNpc is null || SelectedNpc.DialogMode != NpcDialogMode.Interactive) return;

        var npcName = NpcDisplayName;
        var questions = Workspace.Dialogs.QuestionsForNpc(SelectedNpc.Id);
        var initialId = SelectedNpc.Pregunta;
        var nodes = questions
            .OrderByDescending(q => q.Id == initialId)
            .ThenBy(q => q.Id)
            .Select(q => BuildQuestionNode(q, isInitial: q.Id == initialId, npcName))
            .ToList();
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].HasNextQuestion = i < nodes.Count - 1;
            TreeRoots.Add(nodes[i]);
        }

        RebuildFlatCanvas();
    }

    private void RebuildFlatCanvas()
    {
        FlatBubbles.Clear();
        foreach (var root in TreeRoots)
        {
            FlatBubbles.Add(root);
            foreach (var child in root.Children)
            {
                if (child.IsResponse)
                    FlatBubbles.Add(child);
            }
        }

        ApplyBubbleLayoutFromStoreOrDefault();
        RefreshConnectors();
        OnPropertyChanged(nameof(CanvasExtentWidth));
        OnPropertyChanged(nameof(CanvasExtentHeight));
        OnPropertyChanged(nameof(FinMarkerTop));
        OnPropertyChanged(nameof(FlowMarkerLeft));
        SyncInlineConditionHosts();
    }

    private void ApplyBubbleLayoutFromStoreOrDefault()
    {
        if (SelectedNpc is null) return;
        _flowCenteringOffsetX = 0;
        BuildAndApplyDefaultFlowLayout();
        if (Workspace.DialogBubbleLayouts.TryGetValue(SelectedNpc.Id.ToString(), out var stored))
        {
            foreach (var node in FlatBubbles)
                TryApplyStoredBaseLayout(node, stored);
        }

        ApplyFlowViewportLayout();
    }

    private void BuildAndApplyDefaultFlowLayout()
    {
        var rows = BuildDefaultFlowRows();
        if (rows.Count == 0) return;

        var maxRowWidth = rows.Max(r => r.Width);
        var y = FlowTopPadding;
        foreach (var row in rows)
        {
            var rowStartX = (maxRowWidth - row.Width) / 2;
            foreach (var (node, localX, localY) in row.Nodes)
            {
                node.LayoutX = rowStartX + localX;
                node.LayoutY = y + localY;
            }

            y += row.Height + (row.IsMissionRole ? FlowMissionRoleRowGap : FlowRowGap);
        }
    }

    private List<FlowRowLayout> BuildDefaultFlowRows()
    {
        var rows = new List<FlowRowLayout>();
        foreach (var root in TreeRoots)
        {
            if (root.IsCompactMissionRoleBubble)
            {
                rows.Add(new FlowRowLayout(
                    [(root, 0, 0)],
                    EffectiveWidth(root),
                    EffectiveHeight(root),
                    IsMissionRole: true));
                continue;
            }

            var responses = root.Children.Where(c => c.IsResponse).ToList();
            if (responses.Count == 0)
            {
                rows.Add(new FlowRowLayout(
                    [(root, 0, 0)],
                    EffectiveWidth(root),
                    EffectiveHeight(root),
                    IsMissionRole: false));
                continue;
            }

            var questionWidth = EffectiveWidth(root);
            var gap = ComputeQuestionResponseGap(root, responses);
            var rowNodes = new List<(DialogTreeNodeVm, double, double)> { (root, 0, 0) };
            var rowHeight = FlowMainRowMinHeight;
            for (var j = 0; j < responses.Count; j++)
            {
                var localY = j * FlowResponseStackGap;
                rowNodes.Add((responses[j], questionWidth + gap, localY));
                rowHeight = Math.Max(rowHeight, localY + EffectiveHeight(responses[j]));
            }

            var maxResponseWidth = responses.Max(EffectiveWidth);
            rows.Add(new FlowRowLayout(
                rowNodes,
                questionWidth + gap + maxResponseWidth,
                rowHeight,
                IsMissionRole: false));
        }

        return rows;
    }

    private double ComputeQuestionResponseGap(
        DialogTreeNodeVm question,
        IReadOnlyList<DialogTreeNodeVm> responses)
    {
        var questionWidth = EffectiveWidth(question);
        var responseWidth = responses.Count == 0 ? 0 : responses.Max(EffectiveWidth);
        var pairWidth = questionWidth + responseWidth;
        var available = Math.Max(320, _canvasViewportWidth - FlowHorizontalPadding * 2);
        if (pairWidth + FlowPreferredQuestionResponseGap <= available)
            return FlowPreferredQuestionResponseGap;
        if (pairWidth + FlowMinQuestionResponseGap <= available)
            return Math.Max(FlowMinQuestionResponseGap, available - pairWidth);
        return FlowMinQuestionResponseGap;
    }

    private void ApplyFlowViewportLayout()
    {
        if (FlatBubbles.Count == 0)
        {
            OnPropertyChanged(nameof(FlowMarkerLeft));
            return;
        }

        foreach (var node in FlatBubbles)
            node.LayoutX -= _flowCenteringOffsetX;

        var bounds = ComputeContentBounds();
        var contentWidth = bounds.MaxX - bounds.MinX;
        var viewport = Math.Max(320, _canvasViewportWidth);
        double offset;
        if (contentWidth + FlowHorizontalPadding * 2 <= viewport)
            offset = (viewport - contentWidth) / 2 - bounds.MinX;
        else
            offset = FlowHorizontalPadding - bounds.MinX;

        foreach (var node in FlatBubbles)
            node.LayoutX += offset;

        _flowCenteringOffsetX = offset;
        OnPropertyChanged(nameof(FlowMarkerLeft));
    }

    private static bool TryApplyStoredBaseLayout(
        DialogTreeNodeVm node,
        Dictionary<string, double[]>? stored)
    {
        if (stored is null
            || string.IsNullOrEmpty(node.LayoutKey)
            || !stored.TryGetValue(node.LayoutKey, out var pos)
            || pos.Length < 2)
            return false;

        if (IsLegacyAutoLayoutX(pos[0]))
            return false;

        node.LayoutX = pos[0];
        node.LayoutY = pos[1];
        return true;
    }

    private static bool IsLegacyAutoLayoutX(double x) =>
        Math.Abs(x - 24) < 1 || Math.Abs(x - 500) < 1;

    private static double EffectiveWidth(DialogTreeNodeVm node) =>
        Math.Max(node.LayoutWidth, node.IsQuestion
            ? (node.IsCompactMissionRoleBubble ? 220 : 340)
            : 300);

    private static double EffectiveHeight(DialogTreeNodeVm node) =>
        Math.Max(node.LayoutHeight, node.IsQuestion
            ? (node.IsCompactMissionRoleBubble ? 90 : 120)
            : 90);

    private (double MinX, double MaxX, double MinY, double MaxY) ComputeContentBounds()
    {
        if (FlatBubbles.Count == 0)
            return (0, 900, 0, 400);

        var minX = FlatBubbles.Min(n => n.LayoutX);
        var maxX = FlatBubbles.Max(n => n.LayoutX + EffectiveWidth(n));
        var minY = FlatBubbles.Min(n => n.LayoutY);
        var maxY = FlatBubbles.Max(n => n.LayoutY + EffectiveHeight(n));
        return (minX, maxX, minY, maxY);
    }

    public double CanvasViewportWidth
    {
        get => _canvasViewportWidth;
        set
        {
            if (value <= 0 || Math.Abs(_canvasViewportWidth - value) < 0.5)
                return;
            _canvasViewportWidth = value;
            ApplyBubbleLayoutFromStoreOrDefault();
            RefreshConnectors();
            OnPropertyChanged(nameof(CanvasExtentWidth));
            OnPropertyChanged(nameof(FlowMarkerLeft));
        }
    }

    public double FlowMarkerLeft
    {
        get
        {
            if (FlatBubbles.Count == 0)
                return Math.Max(0, (_canvasViewportWidth / 2) - FlowMarkerHalfWidth);

            var bounds = ComputeContentBounds();
            return bounds.MinX + ((bounds.MaxX - bounds.MinX) / 2) - FlowMarkerHalfWidth;
        }
    }

    private sealed record FlowRowLayout(
        List<(DialogTreeNodeVm Node, double LocalX, double LocalY)> Nodes,
        double Width,
        double Height,
        bool IsMissionRole);

    public void SetBubbleLayout(DialogTreeNodeVm node, double x, double y, bool persist = true)
    {
        if (SelectedNpc is null || !node.IsQuestion && !node.IsResponse) return;
        node.LayoutX = x;
        node.LayoutY = y;

        if (persist)
        {
            var npcKey = SelectedNpc.Id.ToString();
            if (!Workspace.DialogBubbleLayouts.TryGetValue(npcKey, out var map))
            {
                map = new Dictionary<string, double[]>();
                Workspace.DialogBubbleLayouts[npcKey] = map;
            }

            map[node.LayoutKey] = new[] { x - _flowCenteringOffsetX, y };
        }

        RefreshConnectors();
        OnPropertyChanged(nameof(CanvasExtentWidth));
        OnPropertyChanged(nameof(CanvasExtentHeight));
        OnPropertyChanged(nameof(FinMarkerTop));
        OnPropertyChanged(nameof(FlowMarkerLeft));
        if (persist)
            Persist();
    }

    public void NotifyBubbleMeasured(DialogTreeNodeVm node, double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        if (Math.Abs(node.LayoutWidth - width) < 0.5 && Math.Abs(node.LayoutHeight - height) < 0.5)
            return;
        node.LayoutWidth = width;
        node.LayoutHeight = height;
        ApplyBubbleLayoutFromStoreOrDefault();
        RefreshConnectors();
        OnPropertyChanged(nameof(CanvasExtentWidth));
        OnPropertyChanged(nameof(CanvasExtentHeight));
        OnPropertyChanged(nameof(FinMarkerTop));
        OnPropertyChanged(nameof(FlowMarkerLeft));
    }

    public void RefreshConnectors()
    {
        Connectors.Clear();
        if (FlatBubbles.Count == 0) return;

        foreach (var q in FlatBubbles.Where(n => n.IsQuestion))
        {
            foreach (var r in q.Children.Where(c => c.IsResponse))
                AddConnector(DialogConnectorKind.QuestionToResponse, RightMid(q), LeftMid(r));
        }

        for (var i = 0; i < TreeRoots.Count - 1; i++)
        {
            var current = TreeRoots[i];
            var next = TreeRoots[i + 1];
            var anchor = current.Children.LastOrDefault(c => c.IsResponse) ?? current;
            AddConnector(DialogConnectorKind.Flow, BottomMid(anchor), TopMid(next));
        }

        foreach (var r in FlatBubbles.Where(n => n.IsResponse))
        {
            var gotoAction = r.Children.FirstOrDefault(c =>
                c.Action?.Accion == DialogActionCodes.GotoQuestion
                && c.Action.TargetQuestionId is not null);
            if (gotoAction?.Action?.TargetQuestionId is not int targetId) continue;
            var target = FlatBubbles.FirstOrDefault(n => n.IsQuestion && n.Question?.Id == targetId);
            if (target is null) continue;
            AddConnector(DialogConnectorKind.Goto, BottomMid(r), TopMid(target));
        }
    }

    private void AddConnector(DialogConnectorKind kind, Point from, Point to)
    {
        Connectors.Add(new DialogConnectorVm
        {
            Kind = kind,
            PathData = BuildConnectorPath(from, to),
        });
    }

    private static Point RightMid(DialogTreeNodeVm node) =>
        new(node.LayoutX + EffectiveWidth(node),
            node.LayoutY + EffectiveHeight(node) / 2);

    private static Point LeftMid(DialogTreeNodeVm node) =>
        new(node.LayoutX, node.LayoutY + EffectiveHeight(node) / 2);

    private static Point BottomMid(DialogTreeNodeVm node) =>
        new(node.LayoutX + EffectiveWidth(node) / 2,
            node.LayoutY + EffectiveHeight(node));

    private static Point TopMid(DialogTreeNodeVm node) =>
        new(node.LayoutX + EffectiveWidth(node) / 2,
            node.LayoutY);

    private static string BuildConnectorPath(Point from, Point to)
    {
        if (Math.Abs(from.Y - to.Y) < 8 && to.X > from.X)
            return FormattableString.Invariant($"M {from.X:F1},{from.Y:F1} L {to.X:F1},{to.Y:F1}");

        var midY = (from.Y + to.Y) / 2;
        return FormattableString.Invariant(
            $"M {from.X:F1},{from.Y:F1} L {from.X:F1},{midY:F1} L {to.X:F1},{midY:F1} L {to.X:F1},{to.Y:F1}");
    }

    public double CanvasExtentWidth
    {
        get
        {
            if (FlatBubbles.Count == 0)
                return Math.Max(900, _canvasViewportWidth);

            var bounds = ComputeContentBounds();
            var contentWidth = bounds.MaxX - bounds.MinX + FlowHorizontalPadding * 2;
            return Math.Max(_canvasViewportWidth, contentWidth);
        }
    }

    public double CanvasExtentHeight
    {
        get
        {
            if (FlatBubbles.Count == 0) return 400;
            return FlatBubbles.Max(n => n.LayoutY + EffectiveHeight(n)) + 120;
        }
    }

    public double FinMarkerTop => Math.Max(CanvasExtentHeight - 56, 280);

    /// <summary>Live-update NPC name in conversation bubbles when identity name changes.</summary>
    public void NotifyNpcNameChanged()
    {
        var name = NpcDisplayName;
        foreach (var root in TreeRoots)
            RefreshTreeNpcDisplayNames(root, name);

        OnPropertyChanged(nameof(NpcDisplayName));
        OnPropertyChanged(nameof(NpcHeader));
        OnPropertyChanged(nameof(SimpleBubbleHeaderTitle));
    }

    private static void RefreshTreeNpcDisplayNames(DialogTreeNodeVm node, string name)
    {
        node.SetNpcDisplayName(name);
        foreach (var child in node.Children)
            RefreshTreeNpcDisplayNames(child, name);
    }

    private void SelectInitialQuestionIfNeeded()
    {
        if (!IsInteractiveMode || SelectedNpc is null) return;
        if (TreeRoots.Count == 0) return;
        if (SelectedNode is DialogTreeNodeVm) return;
        var initialId = SelectedNpc.Pregunta;
        var node = TreeRoots.FirstOrDefault(n => n.Question?.Id == initialId) ?? TreeRoots[0];
        SelectedNode = node;
    }

    public static string FormatActionFriendlyName(int accion) => accion switch
    {
        DialogActionCodes.Teleport => "Teletransporte",
        DialogActionCodes.GotoQuestion => "Ir a otra pregunta",
        DialogActionCodes.ConfirmObjective => "Confirmar objetivo",
        DialogActionCodes.StartQuest => "Iniciar misión",
        DialogActionCodes.ConfirmMission => "Confirmar misión",
        _ => $"Acción avanzada (ID {accion})",
    };

    private static (string map, string cell) SplitTeleportArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return ("", "");
        var parts = args.Split(',', 2, StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], ""),
            _ => (parts[0], parts[1]),
        };
    }

    private static string ComposeTeleportArgs(string? map, string? cell)
    {
        var m = (map ?? "").Trim();
        var c = (cell ?? "").Trim();
        if (m.Length == 0 && c.Length == 0) return "";
        return $"{m},{c}";
    }

    private void RefreshQuestionChoices()
    {
        QuestionChoices.Clear();
        if (SelectedNpc is null) return;
        foreach (var q in Workspace.Dialogs.QuestionsForNpc(SelectedNpc.Id).OrderBy(q => q.Id))
            QuestionChoices.Add(q);
    }

    private void SelectQuestion(int id)
    {
        foreach (var root in TreeRoots)
        {
            if (root.Question?.Id == id)
            {
                SelectedNode = root;
                return;
            }
        }
    }

    private void SelectResponse(int questionId, Guid draftId)
    {
        foreach (var root in TreeRoots)
        {
            if (root.Question?.Id != questionId) continue;
            foreach (var child in root.Children)
            {
                if (child.Response?.DraftId == draftId)
                {
                    SelectedNode = child;
                    return;
                }
            }
        }
    }

    private void SelectAction(int questionId, Guid responseId, DialogActionDraft action)
    {
        foreach (var root in TreeRoots)
        {
            if (root.Question?.Id != questionId) continue;
            foreach (var child in root.Children)
            {
                if (child.Response?.DraftId != responseId) continue;
                foreach (var a in child.Children)
                {
                    if (ReferenceEquals(a.Action, action))
                    {
                        SelectedNode = a;
                        return;
                    }
                }
                // Fallback after rebuild: match by values
                foreach (var a in child.Children)
                {
                    if (a.Action is null) continue;
                    if (a.Action.Accion == action.Accion &&
                        a.Action.Args == action.Args &&
                        a.Action.TargetQuestionId == action.TargetQuestionId &&
                        a.Action.TargetMissionDraftId == action.TargetMissionDraftId)
                    {
                        SelectedNode = a;
                        return;
                    }
                }
            }
        }
    }

    private DialogQuestionDraft? FindParentQuestion(DialogResponseDraft response)
    {
        foreach (var q in Workspace.Dialogs.Questions)
            if (q.Responses.Any(r => r.DraftId == response.DraftId))
                return q;
        return null;
    }

    private void PersistAndRefresh()
    {
        CancelDebouncedTextEditFlush();
        Persist();
        RebuildTreeKeepingSelection();
        RefreshQuestionChoices();
        OnPropertyChanged(nameof(InitialQuestionLabel));
        OnPropertyChanged(nameof(NextQuestionId));
        OnPropertyChanged(nameof(LinkTargetQuestion));
        OnPropertyChanged(nameof(LinkTargetMission));
        OnPropertyChanged(nameof(LinkTargetMissionDraftIdValue));
        OnPropertyChanged(nameof(HasLinkedMission));
        OnPropertyChanged(nameof(HasBrokenMissionLink));
        OnPropertyChanged(nameof(IsStartQuestUnlinked));
        OnPropertyChanged(nameof(StartQuestLinkStatusText));
        OnPropertyChanged(nameof(ShowStartQuestLinkWarning));
        OnPropertyChanged(nameof(SelectedResponseIncomplete));
        OnPropertyChanged(nameof(HasIncompleteResponses));
        OnPropertyChanged(nameof(HasActionConflicts));
        RefreshResponseActionTabs();
        OnPropertyChanged(nameof(PropertiesPanelTitle));
        RaiseEditCommands();
        MissionPanelRuntimeDebugRefresh?.Invoke();
    }

    private void Persist()
    {
        if (_injectedWorkspace is null)
            ContentDraftStore.Save();
    }

    private bool CanEditQuestion() => IsInteractiveMode && SelectedQuestion is not null && !IsBusy;
    private bool CanEditResponse() => IsInteractiveMode && SelectedResponse is not null && !IsBusy;
    private bool CanEditAction() => IsInteractiveMode && SelectedAction is not null && !IsBusy;
    private bool CanAddAction() =>
        IsInteractiveMode && !IsBusy && ActiveResponseForActions is not null;

    private bool CanAddOrReplacePresetAction(int accion)
    {
        if (!IsInteractiveMode || IsBusy || ActiveResponseForActions is not { } response)
            return false;
        if (DialogResponseActionRules.CanAddAction(response, accion))
            return true;
        if (accion == DialogActionCodes.Teleport
            && response.Actions.Any(a => a.Accion == DialogActionCodes.GotoQuestion))
            return true;
        if (accion == DialogActionCodes.GotoQuestion
            && response.Actions.Any(a => a.Accion == DialogActionCodes.Teleport))
            return true;
        return false;
    }

    private bool CanAddPresetAction(int accion) =>
        IsInteractiveMode && !IsBusy
        && ActiveResponseForActions is { } response
        && DialogResponseActionRules.CanAddAction(response, accion);
    private bool CanDuplicateSelected() => CanEditQuestion() || CanEditResponse();
    private bool CanDeleteSelected() => CanEditQuestion() || CanEditResponse() || CanEditAction();
    private bool CanLinkGoto() => IsGotoAction && !IsBusy;

    private void RaiseEditCommands()
    {
        RefreshMaxCommand.RaiseCanExecuteChanged();
        NewQuestionCommand.RaiseCanExecuteChanged();
        DuplicateQuestionCommand.RaiseCanExecuteChanged();
        DeleteQuestionCommand.RaiseCanExecuteChanged();
        SetInitialCommand.RaiseCanExecuteChanged();
        AddResponseCommand.RaiseCanExecuteChanged();
        DuplicateResponseCommand.RaiseCanExecuteChanged();
        DeleteResponseCommand.RaiseCanExecuteChanged();
        MoveResponseUpCommand.RaiseCanExecuteChanged();
        MoveResponseDownCommand.RaiseCanExecuteChanged();
        AddGotoActionCommand.RaiseCanExecuteChanged();
        AddQuestActionCommand.RaiseCanExecuteChanged();
        AddTeleportActionCommand.RaiseCanExecuteChanged();
        AddConfirmObjectiveActionCommand.RaiseCanExecuteChanged();
        AddConfirmMissionActionCommand.RaiseCanExecuteChanged();
        AddAdvancedActionCommand.RaiseCanExecuteChanged();
        DeleteActionCommand.RaiseCanExecuteChanged();
        CreateLinkedQuestionCommand.RaiseCanExecuteChanged();
        ClearLinkCommand.RaiseCanExecuteChanged();
        DuplicateSelectedCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        BeginEditActionConditionCommand.RaiseCanExecuteChanged();
        DoneEditActionConditionCommand.RaiseCanExecuteChanged();
        ClearActionConditionCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanAddPlayerResponse));
        OnPropertyChanged(nameof(CanAddActionFromToolbar));
    }

    private INpcPreguntasReadRepository? CreateRepository()
    {
        if (_repoFactory is not null) return _repoFactory();
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlNpcPreguntasReadRepository(settings, password);
    }

    private INpcRespuestasReadRepository? CreateResponseRepository()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlNpcRespuestasReadRepository(settings, password);
    }
}

public enum DialogTreeNodeKind { Question, Response, Action }

public sealed class DialogTreeNodeVm : ViewModelBase
{
    private bool _isSelected;
    private string _npcDisplayName;

    public DialogTreeNodeVm(
        DialogTreeNodeKind kind,
        DialogQuestionDraft? question,
        DialogResponseDraft? response,
        DialogActionDraft? action,
        bool isInitial,
        string npcDisplayName,
        bool isMandatoryStarter = false,
        string? missionRoleTag = null,
        string? linkedMissionName = null)
    {
        Kind = kind;
        Question = question;
        Response = response;
        Action = action;
        IsInitial = isInitial;
        IsMandatoryStarter = isMandatoryStarter;
        _npcDisplayName = npcDisplayName;
        MissionRoleTag = missionRoleTag;
        LinkedMissionName = linkedMissionName;
        Children = new ObservableCollection<DialogTreeNodeVm>();
    }

    public DialogTreeNodeKind Kind { get; }
    public DialogQuestionDraft? Question { get; }
    public DialogResponseDraft? Response { get; }
    public DialogActionDraft? Action { get; }
    public bool IsInitial { get; }
    /// <summary>First response on the initial question — mandatory in conversation mode.</summary>
    public bool IsMandatoryStarter { get; }
    public string NpcDisplayName => _npcDisplayName;
    public string? MissionRoleTag { get; }
    public bool HasMissionRoleTag => !string.IsNullOrWhiteSpace(MissionRoleTag);
    public bool IsCompactMissionRoleBubble =>
        MissionRoleTag == MissionDialogRolesUi.TagIncomplete
        || MissionRoleTag == MissionDialogRolesUi.TagComplete;
    public bool ShowKindTagBadge => !IsCompactMissionRoleBubble;
    public double QuestionBubbleMinWidth => IsCompactMissionRoleBubble ? 220 : 300;
    public double QuestionBubbleMaxWidth => IsCompactMissionRoleBubble ? 300 : 400;
    public Thickness QuestionBubblePadding =>
        IsCompactMissionRoleBubble ? new Thickness(10, 8, 10, 8) : new Thickness(14, 12, 14, 12);
    public double QuestionAvatarSize => IsCompactMissionRoleBubble ? 42 : 58;
    public double QuestionAvatarScale => IsCompactMissionRoleBubble ? 0.72 : 1.0;
    public double QuestionHeaderFontSize => IsCompactMissionRoleBubble ? 12 : 14;
    public string? LinkedMissionName { get; }
    public ObservableCollection<DialogTreeNodeVm> Children { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsQuestion => Kind == DialogTreeNodeKind.Question;
    public bool IsResponse => Kind == DialogTreeNodeKind.Response;
    public bool IsAction => Kind == DialogTreeNodeKind.Action;
    public bool ShowDeleteButton => Kind switch
    {
        DialogTreeNodeKind.Question => !IsInitial,
        DialogTreeNodeKind.Response => !IsMandatoryStarter,
        _ => true,
    };
    public bool HasResponses => IsQuestion && Children.Count > 0;
    public bool HasActions => IsResponse && Children.Count > 0;
    public bool HasNextQuestion { get; set; }

    private bool _showsInlineCondition;
    public bool ShowsInlineCondition
    {
        get => _showsInlineCondition;
        set => SetProperty(ref _showsInlineCondition, value);
    }

    public string LayoutKey => Kind switch
    {
        DialogTreeNodeKind.Question when Question is not null => $"q:{Question.Id}",
        DialogTreeNodeKind.Response when Response is not null && Question is not null =>
            $"r:{Question.Id}:{Response.DraftId:N}",
        DialogTreeNodeKind.Response when Response is not null =>
            $"r:{Response.DraftId:N}",
        _ => "",
    };

    private double _layoutX;
    private double _layoutY;
    private double _layoutWidth;
    private double _layoutHeight;

    public double LayoutX
    {
        get => _layoutX;
        set => SetProperty(ref _layoutX, value);
    }

    public double LayoutY
    {
        get => _layoutY;
        set => SetProperty(ref _layoutY, value);
    }

    public double LayoutWidth
    {
        get => _layoutWidth;
        set => SetProperty(ref _layoutWidth, value);
    }

    public double LayoutHeight
    {
        get => _layoutHeight;
        set => SetProperty(ref _layoutHeight, value);
    }

    public bool ShowAddGotoActionMenu =>
        IsResponse && Response is not null && (
            DialogResponseActionRules.CanAddAction(Response, DialogActionCodes.GotoQuestion)
            || Response.Actions.Any(a => a.Accion == DialogActionCodes.Teleport));

    public bool ShowAddTeleportActionMenu =>
        IsResponse && Response is not null && (
            DialogResponseActionRules.CanAddAction(Response, DialogActionCodes.Teleport)
            || Response.Actions.Any(a => a.Accion == DialogActionCodes.GotoQuestion));

    public bool ShowAddQuestActionMenu =>
        IsResponse && Response is not null
        && DialogResponseActionRules.CanAddAction(Response, DialogActionCodes.StartQuest);

    public bool ShowAddConfirmObjectiveActionMenu =>
        IsResponse && Response is not null
        && DialogResponseActionRules.CanAddAction(Response, DialogActionCodes.ConfirmObjective);

    public bool ShowAddConfirmMissionActionMenu =>
        IsResponse && Response is not null
        && DialogResponseActionRules.CanAddAction(Response, DialogActionCodes.ConfirmMission);

    public string? ActionConflictWarning =>
        IsResponse && Response is not null
            ? DialogResponseActionRules.ValidateResponse(Response).FirstOrDefault()
            : null;

    public bool HasActionConflict => !string.IsNullOrWhiteSpace(ActionConflictWarning);

    public string KindTag => Kind switch
    {
        DialogTreeNodeKind.Question when IsInitial => "Pregunta inicial",
        DialogTreeNodeKind.Question => "Pregunta",
        DialogTreeNodeKind.Response => "Respuesta",
        _ => "Acción",
    };

    public string Label
    {
        get
        {
            if (IsQuestion && Question is not null)
                return $"{KindTag} #{Question.Id} — {Truncate(Question.TextLocal)}";
            if (IsResponse && Response is not null)
            {
                var incomplete = DialogDraftBatch.IsResponseIncomplete(Response);
                var prefix = incomplete ? "⚠ " : "";
                return $"{prefix}{KindTag} — {Truncate(Response.TextLocal)}";
            }
            if (IsAction && Action is not null)
                return ActionLine;
            return KindTag;
        }
    }

    public string HeaderTitle
    {
        get
        {
            if (IsQuestion && Question is not null)
            {
                if (IsCompactMissionRoleBubble)
                    return $"#{Question.Id}";
                return $"{NpcDisplayName} ({Question.Id})";
            }
            if (IsResponse)
                return "Jugador";
            return KindTag;
        }
    }

    public string BodyText
    {
        get
        {
            if (IsQuestion)
                return string.IsNullOrWhiteSpace(Question?.TextLocal) ? "(sin texto)" : Question!.TextLocal;
            if (IsResponse)
                return string.IsNullOrWhiteSpace(Response?.TextLocal) ? "(sin texto)" : Response!.TextLocal;
            return ActionLine;
        }
    }

    public string ActionLine
    {
        get
        {
            if (Action is null) return "";
            var head = ContentDialogEditorViewModel.FormatActionFriendlyName(Action.Accion);
            if (Action.Accion == DialogActionCodes.GotoQuestion && Action.TargetQuestionId is int t)
                return $"⚡ Acción: {head} → #{t}";
            if (Action.Accion == DialogActionCodes.StartQuest)
            {
                if (!string.IsNullOrWhiteSpace(LinkedMissionName))
                    return $"⚡ Iniciar misión · {LinkedMissionName}";
                return $"⚡ Acción: {head}";
            }
            if (!string.IsNullOrWhiteSpace(Action.Args)
                && Action.Accion is DialogActionCodes.Teleport)
                return $"⚡ Acción: {head} ({Action.Args})";
            return $"⚡ Acción: {head}";
        }
    }

    public bool ResponseIncomplete =>
        IsResponse && Response is not null && DialogDraftBatch.IsResponseIncomplete(Response);

    /// <summary>Refresh bubble preview after TextLocal edits without rebuilding the tree.</summary>
    public void NotifyDisplayTextChanged()
    {
        OnPropertyChanged(nameof(BodyText));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ActionLine));
        OnPropertyChanged(nameof(ResponseIncomplete));
    }

    public void SetNpcDisplayName(string name)
    {
        if (string.Equals(_npcDisplayName, name, StringComparison.Ordinal))
            return;
        _npcDisplayName = name;
        OnPropertyChanged(nameof(NpcDisplayName));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(sin texto)";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= 40 ? s : s[..40] + "…";
    }
}

public sealed class ResponseActionTabVm
{
    public ResponseActionTabVm(DialogActionDraft action, string label, bool isSelected)
    {
        Action = action;
        Label = label;
        IsSelected = isSelected;
    }

    public DialogActionDraft Action { get; }
    public string Label { get; }
    public bool IsSelected { get; }
}

public enum DialogConnectorKind { QuestionToResponse, Flow, Goto }

public sealed class DialogConnectorVm
{
    public DialogConnectorKind Kind { get; init; }
    public required string PathData { get; init; }
}
