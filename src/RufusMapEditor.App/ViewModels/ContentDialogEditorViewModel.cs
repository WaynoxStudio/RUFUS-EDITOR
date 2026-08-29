using System.Collections.ObjectModel;
using System.Windows;
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
    private NpcsModeloDraft? _selectedNpc;
    private object? _selectedNode;
    private string _statusText = "Selecciona un NPC borrador";
    private bool _dbReady;
    private int _dbMaxQuestionId;
    private bool _isBusy;
    private bool _advancedExpanded;
    private bool _dialogEsLoading;
    private DialogEsSimpleUiState _simpleUi = new() { IsPending = false };

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
        _dialogEsCacheDirectory = dialogEsCacheDirectory;
        _sftpClientFactory = sftpClientFactory;
        _dialogEsSession = dialogEsSession ?? DialogEsSessionCache.Shared;
        _dialogEsLoading = true;

        NpcChoices = new ObservableCollection<NpcsModeloDraft>();
        TreeRoots = new ObservableCollection<DialogTreeNodeVm>();
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
        AddGotoActionCommand = new RelayCommand(() => AddPresetAction(DialogActionCodes.GotoQuestion), CanEditResponse);
        AddQuestActionCommand = new RelayCommand(() => AddPresetAction(DialogActionCodes.StartQuest), CanEditResponse);
        AddTeleportActionCommand = new RelayCommand(() => AddPresetAction(DialogActionCodes.Teleport), CanEditResponse);
        AddAdvancedActionCommand = new RelayCommand(() => AddPresetAction(0), CanEditResponse);
        DeleteActionCommand = new RelayCommand(DeleteSelectedAction, CanEditAction);
        CreateLinkedQuestionCommand = new RelayCommand(CreateLinkedQuestion, CanEditAction);
        ClearLinkCommand = new RelayCommand(ClearLink, CanEditAction);

        ReloadNpcs();
    }

    public ObservableCollection<NpcsModeloDraft> NpcChoices { get; }
    public ObservableCollection<DialogTreeNodeVm> TreeRoots { get; }
    public ObservableCollection<DialogQuestionDraft> QuestionChoices { get; }
    public ObservableCollection<MissionDraft> MissionChoices { get; }

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
            OnPropertyChanged(nameof(InitialQuestionLabel));
            OnPropertyChanged(nameof(HasIncompleteResponses));
            OnPropertyChanged(nameof(SelectedResponseIncomplete));
            NotifyDialogModeUi();
            RefreshSimpleDialogEsUi();
        }
    }

    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;
            OnPropertyChanged(nameof(SelectedQuestion));
            OnPropertyChanged(nameof(SelectedResponse));
            OnPropertyChanged(nameof(SelectedAction));
            OnPropertyChanged(nameof(HasQuestion));
            OnPropertyChanged(nameof(HasResponse));
            OnPropertyChanged(nameof(HasAction));
            OnPropertyChanged(nameof(IsGotoAction));
            OnPropertyChanged(nameof(IsStartQuestAction));
            OnPropertyChanged(nameof(LinkTargetQuestion));
            OnPropertyChanged(nameof(LinkTargetMission));
            OnPropertyChanged(nameof(SelectedResponseIncomplete));
            OnPropertyChanged(nameof(HasIncompleteResponses));
            RaiseEditCommands();
        }
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
            Persist();
            OnPropertyChanged();
            RefreshSimpleDialogEsUi();
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
            if (SelectedNpc.Pregunta == id) return;
            SelectedNpc.Pregunta = id;
            Persist();
            OnPropertyChanged();
            OnPropertyChanged(nameof(InitialQuestionLabel));
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
        get => SelectedAction?.TargetMissionDraftId is Guid id
            ? Workspace.Missions.FindByDraftId(id)
            : null;
        set
        {
            if (SelectedAction is null) return;
            if (value is null)
                Workspace.Dialogs.ClearStartMissionLink(SelectedAction);
            else
                Workspace.Dialogs.LinkStartMission(SelectedAction, value.DraftId);
            PersistAndRefresh();
            OnPropertyChanged();
        }
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
    public RelayCommand AddAdvancedActionCommand { get; }
    public RelayCommand DeleteActionCommand { get; }
    public RelayCommand CreateLinkedQuestionCommand { get; }
    public RelayCommand ClearLinkCommand { get; }

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

        MissionChoices.Clear();
        foreach (var m in Workspace.Missions.Missions)
            MissionChoices.Add(m);

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
            DbReady = true;
            StatusText = $"MAX(npc_preguntas.id) = {max} · próximo = {Workspace.Dialogs.NextQuestionId} · solo lectura BD";
            OnPropertyChanged(nameof(NextQuestionId));
            Persist();
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
        Persist();
        RebuildTreeKeepingSelection();
        OnPropertyChanged(nameof(InitialQuestionLabel));
        OnPropertyChanged(nameof(NpcHeader));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsSimpleDialogComplete));
        OnPropertyChanged(nameof(SelectedResponseIncomplete));
        OnPropertyChanged(nameof(HasIncompleteResponses));
        RefreshSimpleDialogEsUi();
    }

    /// <summary>AI.5 — refresh mode radios + simple text after external draft mutation.</summary>
    public void RefreshAfterExternalDraftChange()
    {
        PersistAndRefresh();
        NotifyDialogModeUi();
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
            if (ownedIds.Contains(npc.Pregunta))
                npc.Pregunta = 0;
            npc.DialogMode = NpcDialogMode.Simple;
            SelectedNode = null;
            StatusText = "Modo diálogo simple: texto nuevo (ID existente opcional). No crea npc_preguntas ni npc_respuestas.";
        }
        else
        {
            if (Workspace.Dialogs.FindQuestion(npc.Pregunta) is null)
                npc.Pregunta = 0;
            npc.DialogMode = NpcDialogMode.Interactive;
            if (npc.Pregunta <= 0 && DbReady)
            {
                var q = Workspace.Dialogs.CreateQuestion(npc.Id);
                Workspace.Dialogs.SetInitialQuestion(npc, q.Id);
            }
            StatusText = "Modo conversación interactiva (CONT.3).";
        }

        PersistAndRefresh();
        NotifyDialogModeUi();
        RefreshSimpleDialogEsUi();
    }

    private void NotifyDialogModeUi()
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsInteractiveMode));
        OnPropertyChanged(nameof(SimpleDialogTextLocal));
        OnPropertyChanged(nameof(SimpleDialogIdText));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsSimpleDialogComplete));
        OnPropertyChanged(nameof(IsDialogEsPublished));
        OnPropertyChanged(nameof(SimplePendingTitle));
        OnPropertyChanged(nameof(SimplePendingDetails));
        OnPropertyChanged(nameof(SimplePublishedDetails));
        OnPropertyChanged(nameof(HasIncompleteResponses));
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
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(SimplePendingTitle));
        OnPropertyChanged(nameof(SimplePendingDetails));
        OnPropertyChanged(nameof(IsDialogEsPublished));
        OnPropertyChanged(nameof(SimplePublishedDetails));
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

        var result = Workspace.Dialogs.TryDeleteQuestion(id, unlinkAndDelete: false, out var blocked);
        if (result == QuestionDeleteResult.HasReferences)
        {
            var msg =
                $"La pregunta {id} está referenciada por {blocked!.Value.ResponseDraftIds.Count} respuesta(s).\n\n" +
                "¿Eliminar enlaces y la pregunta?\n\nCancelar deja todo igual.";
            if (MessageBox.Show(msg, "Pregunta referenciada", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;
            Workspace.Dialogs.TryDeleteQuestion(id, unlinkAndDelete: true, out _);
        }
        else if (result != QuestionDeleteResult.Deleted)
        {
            return;
        }

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

    private void AddPresetAction(int accion)
    {
        if (SelectedResponse is null) return;
        var parentQ = FindParentQuestion(SelectedResponse);
        var responseId = SelectedResponse.DraftId;
        var a = Workspace.Dialogs.AddAction(SelectedResponse, accion);
        PersistAndRefresh();
        if (parentQ is not null)
            SelectAction(parentQ.Id, responseId, a);
        StatusText = $"Acción {accion} añadida";
    }

    private void DeleteSelectedAction()
    {
        if (SelectedAction is null || SelectedNode is not DialogTreeNodeVm node || node.Response is null)
            return;
        Workspace.Dialogs.RemoveAction(node.Response, SelectedAction);
        PersistAndRefresh();
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

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (SelectedNpc is null || SelectedNpc.DialogMode != NpcDialogMode.Interactive) return;

        var questions = Workspace.Dialogs.QuestionsForNpc(SelectedNpc.Id);
        var initialId = SelectedNpc.Pregunta;
        foreach (var q in questions.OrderByDescending(q => q.Id == initialId).ThenBy(q => q.Id))
            TreeRoots.Add(BuildQuestionNode(q, isInitial: q.Id == initialId));
    }

    private void RebuildTreeKeepingSelection()
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
    }

    private static DialogTreeNodeVm BuildQuestionNode(DialogQuestionDraft q, bool isInitial)
    {
        var label = isInitial
            ? $"[Pregunta inicial] #{q.Id} — {Truncate(q.TextLocal)}"
            : $"[Pregunta] #{q.Id} — {Truncate(q.TextLocal)}";
        var node = new DialogTreeNodeVm(DialogTreeNodeKind.Question, label, q, null, null);
        var i = 1;
        foreach (var r in q.Responses)
        {
            var incomplete = DialogDraftBatch.IsResponseIncomplete(r);
            var rLabel = incomplete
                ? $"⚠ INCOMPLETA [Respuesta {i}] {Truncate(r.TextLocal)}"
                : $"[Respuesta {i}] {Truncate(r.TextLocal)}";
            var rNode = new DialogTreeNodeVm(
                DialogTreeNodeKind.Response,
                rLabel,
                q, r, null);
            var ai = 1;
            foreach (var a in r.Actions)
            {
                rNode.Children.Add(new DialogTreeNodeVm(
                    DialogTreeNodeKind.Action, DescribeAction(a, ai), q, r, a));
                ai++;
            }
            node.Children.Add(rNode);
            i++;
        }
        return node;
    }

    private static string DescribeAction(DialogActionDraft a, int index)
    {
        var head = a.Accion switch
        {
            DialogActionCodes.GotoQuestion => "Ir a pregunta",
            DialogActionCodes.StartQuest => "Dar misión",
            DialogActionCodes.Teleport => "Teleport",
            _ => $"Acción {a.Accion}",
        };
        string target;
        if (a.Accion == DialogActionCodes.GotoQuestion && a.TargetQuestionId is int t)
            target = $" → #{t}";
        else if (a.Accion == DialogActionCodes.StartQuest && a.TargetMissionDraftId is Guid mid)
            target = $" → mission {mid.ToString()[..8]}…";
        else
            target = string.IsNullOrWhiteSpace(a.Args) ? "" : $" ({a.Args})";
        return $"[Acción {index}] {head}{target}";
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(sin texto)";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= 40 ? s : s[..40] + "…";
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
                        a.Action.TargetQuestionId == action.TargetQuestionId)
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
        Persist();
        RebuildTreeKeepingSelection();
        RefreshQuestionChoices();
        OnPropertyChanged(nameof(InitialQuestionLabel));
        OnPropertyChanged(nameof(NextQuestionId));
        OnPropertyChanged(nameof(LinkTargetQuestion));
        OnPropertyChanged(nameof(SelectedResponseIncomplete));
        OnPropertyChanged(nameof(HasIncompleteResponses));
        RaiseEditCommands();
    }

    private void Persist()
    {
        if (_injectedWorkspace is null)
            ContentDraftStore.Save();
    }

    private bool CanEditQuestion() => IsInteractiveMode && SelectedQuestion is not null && !IsBusy;
    private bool CanEditResponse() => IsInteractiveMode && SelectedResponse is not null && !IsBusy;
    private bool CanEditAction() => IsInteractiveMode && SelectedAction is not null && !IsBusy;
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
        AddAdvancedActionCommand.RaiseCanExecuteChanged();
        DeleteActionCommand.RaiseCanExecuteChanged();
        CreateLinkedQuestionCommand.RaiseCanExecuteChanged();
        ClearLinkCommand.RaiseCanExecuteChanged();
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
}

public enum DialogTreeNodeKind { Question, Response, Action }

public sealed class DialogTreeNodeVm : ViewModelBase
{
    public DialogTreeNodeVm(
        DialogTreeNodeKind kind,
        string label,
        DialogQuestionDraft? question,
        DialogResponseDraft? response,
        DialogActionDraft? action)
    {
        Kind = kind;
        Label = label;
        Question = question;
        Response = response;
        Action = action;
        Children = new ObservableCollection<DialogTreeNodeVm>();
    }

    public DialogTreeNodeKind Kind { get; }
    public string Label { get; }
    public DialogQuestionDraft? Question { get; }
    public DialogResponseDraft? Response { get; }
    public DialogActionDraft? Action { get; }
    public ObservableCollection<DialogTreeNodeVm> Children { get; }
}
