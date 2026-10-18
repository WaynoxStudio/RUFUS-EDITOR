using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// CONT-UI.1/2 — single Content workspace with vertical accordion steps.
/// CONT-CONN.1 — BD/SFTP status reuses global Mapas settings (no duplicate credentials).
/// </summary>
public sealed class ContentWorkspaceViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace? _workspace;
    private readonly Func<DatabaseSettings, string, IMapasRepository>? _dbRepoFactory;
    private readonly Func<LangSftpSettings, string, ILangSftpReadClient>? _sftpClientFactory;
    private readonly Func<LangSftpSettings, string, ILangSftpPublishClient>? _sftpPublishFactory;
    private bool _syncingFromSelectedNpc;
    private bool _identityExpanded = true;
    private bool _interactionsExpanded = true;
    private bool _dialogConversationExpanded = true;
    private bool _missionBlockExpanded = true;
    private bool _missionBlockHighlighted;
    private bool _locationsExpanded;
    private bool _reviewExpanded;
    private bool _npcListExpanded = true;
    private bool _showLibrary;
    private SharedConnectionState _databaseState = SharedConnectionState.Unchecked;
    private SharedConnectionState _sftpState = SharedConnectionState.Unchecked;
    private string _databaseDetail = "";
    private string _sftpDetail = "";
    private bool _checkingDatabase;
    private bool _checkingSftp;
    private bool _readingMax;
    private bool _dialogDependentUiRefreshScheduled;
    private bool _dialogDependentUiNotifyAi;
    private bool _dialogDependentUiNotifyGenerateButton;

    private bool _contentMaxReady;
    private ContentPublishHeaderState _publishHeader =
        ContentPublishHeaderEvaluator.Evaluate(new ContentDraftWorkspace(), tryCachedQuestsEs: false);
    private int _publishHeaderDebounceToken;
    private int _publishHeaderEvalGeneration;
    private CancellationTokenSource? _publishHeaderCts;
    private const int PublishHeaderDebounceMs = 400;

    private string _idMaxSummary = "";

    private ContentDraftWorkspace Workspace => _workspace ?? ContentDraftStore.Current;

    public ContentWorkspaceViewModel()
        : this(null)
    {
    }

    public ContentWorkspaceViewModel(
        ContentDraftWorkspace? workspace = null,
        Func<INpcsModeloReadRepository>? npcRepo = null,
        Func<INpcPreguntasReadRepository>? questionRepo = null,
        Func<IMisionEtapasReadRepository>? stageRepo = null,
        Func<IMisionObjetivosReadRepository>? objRepo = null,
        Func<DatabaseSettings, string, IMapasRepository>? dbRepoFactory = null,
        Func<LangSftpSettings, string, ILangSftpReadClient>? sftpClientFactory = null,
        Func<LangSftpSettings, string, ILangSftpPublishClient>? sftpPublishFactory = null,
        IAiGenerationService? aiGeneration = null)
    {
        _workspace = workspace;
        _dbRepoFactory = dbRepoFactory;
        _sftpClientFactory = sftpClientFactory;
        _sftpPublishFactory = sftpPublishFactory;
        Npc = new ContentNpcEditorViewModel(workspace, npcRepo);
        Dialogs = new ContentDialogEditorViewModel(workspace, questionRepo, sftpClientFactory: sftpClientFactory);
        Missions = new ContentMissionEditorViewModel(workspace, stageRepo, objRepo);
        Missions.EmbeddedCompactMode = true;
        Missions.ActionInlineMode = true;
        Missions.MapWorldCoordsLookup = TryLookupMapWorldCoords;
        Missions.BdNpcMapIdLookup = TryLookupBdNpcMapId;
        Npc.LocationMapChanged += OnNpcLocationMapChanged;
        Missions.ConfigureLinkedMissionResolver(() =>
            StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(
                Dialogs.ContextStartQuestAction, Workspace.Missions));
        Missions.ConfigureCompleteMissingDialogRolesHandler(CompleteMissingDialogRolesForPanel);
        Missions.ConfigureRuntimeDebugContext(() => new MissionPanelRuntimeDebugContext(
            Dialogs.ContextStartQuestAction?.Accion,
            Dialogs.ContextStartQuestAction?.TargetMissionDraftId,
            Dialogs.HasLinkedMission));
        Library = new ContentLibraryViewModel(
            () => Workspace,
            OpenLibraryEntry,
            DuplicateLibraryEntry,
            RemoveLibraryEntry,
            RemoveLibraryEntries);
        Missions.DraftEdited += () =>
        {
            SyncMissionQuestionRoles();
            Dialogs.RefreshTreeDisplay();
            OnPropertyChanged(nameof(ShowOrphanMissionWarning));
            NotifyMissionUi();
            Library.Refresh();
        };
        Missions.MissionRoleQuestionCreated += questionId =>
        {
            Dialogs.ReloadNpcs();
            Dialogs.SelectQuestionOnCanvas(questionId);
            SyncMissionQuestionRoles();
            Missions.RefreshDialogRoleBindings();
        };
        Dialogs.StartQuestActionNeedsLink += RepairStartQuestActionLink;
        Dialogs.StartQuestMissionSelected += SeedSelectedStartQuestMission;
        Dialogs.StartQuestLinkChanged += SyncMissionPanelFromLink;
        Dialogs.StartQuestSecondaryRolesCleared += draftId =>
        {
            if (draftId is Guid id)
            {
                Missions.SyncFromMissionDraft(id);
                Missions.SyncInlineLinkedMissionDraft(
                    Dialogs.ContextStartQuestAction?.TargetMissionDraftId == id ? id : null);
            }
            else
            {
                Missions.SyncInlineLinkedMissionDraft(null);
                Missions.RefreshDialogRoleBindings();
            }
            SyncMissionQuestionRoles();
            if (Dialogs.ContextStartQuestAction is not null)
                SyncMissionPanelFromLink();
            PersistWorkspace();
        };
        AiAssistant = new ContentAiAssistantViewModel(aiGeneration ?? AiBackendGenerationServiceFactory.CreateForEditor());
        AiAssistant.AttachDraftHost(
            () => Npc.Selected?.Model,
            () => Workspace,
            RefreshUiAfterAiApply,
            () => Dialogs.SelectedQuestion,
            () => Dialogs.SelectedResponse,
            PersistWorkspace,
            () => Dialogs.IsSimpleMode);
        AiAssistant.GenerationRequested += action =>
        {
            if (action == AiCreativeAction.GenerarNombre)
                IdentityExpanded = true;
            else
                InteractionsExpanded = true;
        };

        NewNpcCommand = new RelayCommand(CreateNpc, () => Npc.NewNpcCommand.CanExecute(null));
        DuplicateCommand = new RelayCommand(DuplicateNpc, () => Npc.DuplicateCommand.CanExecute(null));
        DeleteCommand = new RelayCommand(DeleteNpc, () => Npc.DeleteCommand.CanExecute(null));
        PublishCommand = new RelayCommand(
            async () => await PublishAsync(),
            () => ShowPublishBdButton && EnablePublishBdButton);
        PublishClientCommand = new RelayCommand(
            PublishClient,
            () => ShowPublishClientButton && EnablePublishClientButton);
        CreateMissionCommand = new RelayCommand(() => { }, () => false);
        CreateMissionFromActionCommand = new RelayCommand(
            CreateMissionFromAction,
            () => EditorEnabled && Dialogs.IsStartQuestAction && Dialogs.CanCreateMissionForNpc);
        LinkOrphanMissionCommand = new RelayCommand(
            LinkOrphanMission,
            () => EditorEnabled && ShowOrphanMissionWarning && Dialogs.SelectedResponse is not null);
        RemoveMissionCommand = new RelayCommand(ConfirmRemoveMission, () => EditorEnabled && HasMission);
        RefreshMaxCommand = new RelayCommand(
            async () => await RefreshAllMaxAsync(),
            () => !IsReadingMax);
        CheckDatabaseCommand = new RelayCommand(async () => await CheckDatabaseAsync(), () => !CheckingDatabase);
        CheckSftpCommand = new RelayCommand(async () => await CheckSftpAsync(), () => !CheckingSftp);
        ToggleLibraryCommand = new RelayCommand(() => ShowLibrary = !ShowLibrary);
        ShowEditorCommand = new RelayCommand(() => ShowLibrary = false);
        ShowLibraryCommand = new RelayCommand(() => ShowLibrary = true);

        SchedulePublishHeaderRefresh(immediate: true);

        Npc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ContentNpcEditorViewModel.Selected)
                or nameof(ContentNpcEditorViewModel.HasSelection)
                or nameof(ContentNpcEditorViewModel.EditorEnabled))
            {
                SyncFromSelectedNpc();
            }
            if (e.PropertyName is nameof(ContentNpcEditorViewModel.StatusText))
                OnPropertyChanged(nameof(StatusText));
            if (e.PropertyName is nameof(ContentNpcEditorViewModel.IsBusy)
                or nameof(ContentNpcEditorViewModel.DbReady))
            {
                NewNpcCommand.RaiseCanExecuteChanged();
                DuplicateCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        };

        Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ContentDialogEditorViewModel.InitialQuestionLabel)
                or nameof(ContentDialogEditorViewModel.SelectedNpc)
                or nameof(ContentDialogEditorViewModel.HasIncompleteResponses)
                or nameof(ContentDialogEditorViewModel.SelectedResponseIncomplete)
                or nameof(ContentDialogEditorViewModel.IsPendingDialogEs)
                or nameof(ContentDialogEditorViewModel.IsDialogEsPublished)
                or nameof(ContentDialogEditorViewModel.IsSimpleDialogComplete)
                or nameof(ContentDialogEditorViewModel.IsSimpleMode)
                or nameof(ContentDialogEditorViewModel.IsInteractiveMode)
                or nameof(ContentDialogEditorViewModel.SimpleDialogIdText)
                or nameof(ContentDialogEditorViewModel.SimpleDialogTextLocal)
                or nameof(ContentDialogEditorViewModel.SelectedNode)
                or nameof(ContentDialogEditorViewModel.HasQuestion)
                or nameof(ContentDialogEditorViewModel.HasResponse)
                or nameof(ContentDialogEditorViewModel.LinkTargetMission)
                or nameof(ContentDialogEditorViewModel.HasLinkedMission)
                or nameof(ContentDialogEditorViewModel.IsStartQuestAction)
                or nameof(ContentDialogEditorViewModel.UseWideMissionLayout)
                or nameof(ContentDialogEditorViewModel.ShowFriendlyQuestEditor)
                or nameof(ContentDialogEditorViewModel.CanCreateMissionForNpc)
                or nameof(ContentDialogEditorViewModel.ShowCreateMissionInActionPanel))
            {
                ScheduleDialogDependentUiRefresh(
                    notifyAiSelection: e.PropertyName is nameof(ContentDialogEditorViewModel.SelectedNode)
                        or nameof(ContentDialogEditorViewModel.IsSimpleMode)
                        or nameof(ContentDialogEditorViewModel.IsInteractiveMode),
                    notifyGenerateButton: e.PropertyName is nameof(ContentDialogEditorViewModel.IsSimpleMode)
                        or nameof(ContentDialogEditorViewModel.IsInteractiveMode));
            }
        };

        Dialogs.MissionPanelRuntimeDebugRefresh += () => Missions.RefreshDebugMissionState();

        Missions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ContentMissionEditorViewModel.Selected))
            {
                SyncMissionQuestionRoles();
                if (Dialogs.ContextStartQuestAction is not null)
                    Missions.RefreshDialogRoleBindings();
            }
        };

        // CONT.7B.1 — refresh npc_es banners when client actions change on selected NPC.
        void HookNpcItem(NpcDraftItemViewModel item) =>
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(NpcDraftItemViewModel.ClientActionsSummary)
                    or nameof(NpcDraftItemViewModel.ClientActionsCompactSummary)
                    or nameof(NpcDraftItemViewModel.Nombre))
                {
                    if (e.PropertyName is nameof(NpcDraftItemViewModel.Nombre)
                        && ReferenceEquals(item, Npc.Selected))
                    {
                        AiAssistant.BindToNpc(item.Id, item.Nombre);
                        Dialogs.NotifyNpcNameChanged();
                    }

                    OnPropertyChanged(nameof(IsPendingNpcEs));
                    OnPropertyChanged(nameof(IsNpcEsPublished));
                    OnPropertyChanged(nameof(IsNpcEsIncomplete));
                    OnPropertyChanged(nameof(NpcEsPendingDetails));
                    OnPropertyChanged(nameof(NpcEsPublishedDetails));
                    OnPropertyChanged(nameof(NpcEsIncompleteDetails));
                    OnPropertyChanged(nameof(ClientNpcEsStatusLine));
                    NotifySectionStatuses();
                }
            };
        foreach (var item in Npc.Items)
            HookNpcItem(item);
        Npc.Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (NpcDraftItemViewModel item in e.NewItems)
                HookNpcItem(item);
            Library.Refresh();
        };
    }

    public ContentNpcEditorViewModel Npc { get; }
    public ContentDialogEditorViewModel Dialogs { get; }
    public ContentMissionEditorViewModel Missions { get; }
    /// <summary>CONTENT.LIBRARY.WORKSPACE.UI.1 — proyección UI del workspace.json.</summary>
    public ContentLibraryViewModel Library { get; }
    /// <summary>AI.1 — creative assistant (local request only; no API).</summary>
    public ContentAiAssistantViewModel AiAssistant { get; }

    public RelayCommand NewNpcCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PublishCommand { get; }
    public RelayCommand PublishClientCommand { get; }
    public RelayCommand CreateMissionCommand { get; }
    public RelayCommand CreateMissionFromActionCommand { get; }
    public RelayCommand LinkOrphanMissionCommand { get; }
    public RelayCommand RemoveMissionCommand { get; }
    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand CheckDatabaseCommand { get; }
    public RelayCommand CheckSftpCommand { get; }
    public RelayCommand ToggleLibraryCommand { get; }
    public RelayCommand ShowEditorCommand { get; }
    public RelayCommand ShowLibraryCommand { get; }

    public bool ShowLibrary
    {
        get => _showLibrary;
        set
        {
            if (!SetProperty(ref _showLibrary, value))
                return;
            OnPropertyChanged(nameof(ShowEditor));
            if (value)
                Library.Refresh();
        }
    }

    public bool ShowEditor => !ShowLibrary;

    /// <summary>True when mission editor has local unsaved edits (SaveDraft not pressed).</summary>
    public bool HasUnsavedMissionDraft => Missions.HasUnsavedLocalDraft;

    /// <summary>Confirm leave workspace / close ADMIN. False = user cancelled.</summary>
    public bool ConfirmLeaveIfDirty() => Missions.ConfirmLeaveIfDirty();

    public bool ShowPublishBdButton => _publishHeader.ShowBdButton;
    public bool EnablePublishBdButton => _publishHeader.EnableBdButton;
    public bool ShowPublishClientButton => _publishHeader.ShowClientButton;
    public bool EnablePublishClientButton => _publishHeader.EnableClientButton;
    public string BdPublishStatusLabel => _publishHeader.BdStatusLabel;
    public string ClientPublishStatusLabel => _publishHeader.ClientStatusLabel;
    public bool ShowBdPublishStatus => _publishHeader.ShowBdStatus;
    public bool ShowClientPublishStatus => _publishHeader.ShowClientStatus;

    public string DatabaseStatusLabel =>
        ContentSharedConnectionProbe.FormatStateLabel(_databaseState, database: true);

    public string SftpStatusLabel =>
        ContentSharedConnectionProbe.FormatStateLabel(_sftpState, database: false);

    public string DatabaseDetail
    {
        get => _databaseDetail;
        private set => SetProperty(ref _databaseDetail, value);
    }

    public string SftpDetail
    {
        get => _sftpDetail;
        private set => SetProperty(ref _sftpDetail, value);
    }

    public bool CheckingDatabase
    {
        get => _checkingDatabase;
        private set
        {
            if (SetProperty(ref _checkingDatabase, value))
                CheckDatabaseCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CheckingSftp
    {
        get => _checkingSftp;
        private set
        {
            if (SetProperty(ref _checkingSftp, value))
                CheckSftpCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsReadingMax
    {
        get => _readingMax;
        private set
        {
            if (SetProperty(ref _readingMax, value))
                RefreshMaxCommand.RaiseCanExecuteChanged();
        }
    }

    public string ClientDialogEsStatusLine =>
        IsPendingDialogEs ? "dialog_es: ⚠ Pendiente" : "dialog_es: ✓ Publicado";

    public string ClientNpcEsStatusLine =>
        IsNpcEsIncomplete ? "npc_es: ⚠ Incompleto"
        : IsPendingNpcEs ? "npc_es: ⚠ Pendiente"
        : "npc_es: ✓ Publicado";

    public bool EditorEnabled => Npc.EditorEnabled;

    /// <summary>Simple-dialog generation stays in the lower assistant; interactive uses per-bubble IA.</summary>
    public bool ShowGenerateDialogButton => Dialogs.IsSimpleMode;


    public bool NeedsInitialDialog
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null || npc.PublishedBd) return false;
            if (npc.DialogMode == NpcDialogMode.Simple)
            {
                // Pending dialog_es has its own banner; still incomplete without ID.
                return npc.Pregunta <= 0 && string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal);
            }
            return npc.Pregunta <= 0
                   || Workspace.Dialogs.FindQuestion(npc.Pregunta) is null;
        }
    }

    /// <summary>CONT-DIALOG.3 — simple text without dialog_es ID.</summary>
    public bool IsPendingDialogEs
    {
        get
        {
            var npc = Npc.Selected?.Model;
            return npc is not null && !npc.PublishedBd && npc.IsPendingDialogEs;
        }
    }

    /// <summary>CONT.6C — dialog_es already published; BD still pending.</summary>
    public bool IsDialogEsPublishedPendingBd
    {
        get
        {
            var npc = Npc.Selected?.Model;
            return npc is not null
                   && !npc.PublishedBd
                   && npc.DialogEsPublished
                   && !npc.IsPendingDialogEs;
        }
    }

    /// <summary>CONT.7B — NPC name pending npc_es SFTP publish.</summary>
    public bool IsPendingNpcEs
    {
        get
        {
            var npc = Npc.Selected?.Model;
            return npc is not null && npc.IsPendingNpcEsFor(Workspace) && !npc.IsNpcEsIncompleteFor(Workspace);
        }
    }

    /// <summary>CONT.7B.1 — published name OK but actions missing expected (e.g. Hablar).</summary>
    public bool IsNpcEsIncomplete
    {
        get
        {
            var npc = Npc.Selected?.Model;
            return npc is not null && npc.IsNpcEsIncompleteFor(Workspace);
        }
    }

    /// <summary>CONT.7B — npc_es already published for selected NPC.</summary>
    public bool IsNpcEsPublished
    {
        get
        {
            var npc = Npc.Selected?.Model;
            return npc is not null
                   && npc.NpcEsPublished
                   && !npc.IsPendingNpcEsFor(Workspace)
                   && !npc.IsNpcEsIncompleteFor(Workspace);
        }
    }

    public string NpcEsPendingDetails
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null || !IsPendingNpcEs) return "";
            var expected = NpcEsActionResolver.ResolveExpected(Workspace, npc);
            var ver = NpcEsSessionHint.LastKnownActiveVersion?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "—";
            var next = NpcEsSessionHint.LastKnownActiveVersion is int n
                ? (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "N+1";
            return $"NPC ID: {npc.Id}\nNombre: {npc.Nombre}\nAcciones: {NpcEsClientActions.FormatList(expected)}\nnpc_es activo: {ver}\nVersión prevista: {next}";
        }
    }

    public string NpcEsPublishedDetails
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null || !IsNpcEsPublished) return "";
            var ver = npc.NpcEsPublishedVersion?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "—";
            return $"NPC ID: {npc.Id}\nNombre: {npc.Nombre}\nAcciones: {NpcEsClientActions.FormatList(npc.NpcEsPublishedActionIds)}\nnpc_es activo: {ver}";
        }
    }

    public string NpcEsIncompleteDetails
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null || !IsNpcEsIncomplete) return "";
            var expected = NpcEsActionResolver.ResolveExpected(Workspace, npc);
            var missing = expected.Where(e => !npc.NpcEsPublishedActionIds.Contains(e)).ToList();
            return $"NPC ID: {npc.Id}\nNombre: {npc.Nombre}\nFalta acción:\n{NpcEsClientActions.FormatList(missing)}\nPublicado: {NpcEsClientActions.FormatList(npc.NpcEsPublishedActionIds)}";
        }
    }

    /// <summary>CONT-DIALOG.1 — same CONT.5 rule: responses must have ≥1 action (Interactive only).</summary>
    public bool HasIncompleteDialogResponses
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null || npc.PublishedBd) return false;
            if (npc.DialogMode == NpcDialogMode.Simple) return false;
            return Workspace.Dialogs.HasIncompleteResponsesForNpc(npc.Id);
        }
    }

    /// <summary>MISSIONS.LIFECYCLE.1B — derived from acción 44 TargetMissionDraftId, not editable.</summary>
    public bool HasMission
    {
        get
        {
            if (Dialogs.IsStartQuestAction && Dialogs.SelectedAction is not null)
                return StartQuestLinkUi.HasValidLink(Dialogs.SelectedAction, Workspace.Missions);

            var npc = Npc.Selected?.Model;
            if (npc is null) return false;
            return StartQuestLinkUi.HasValidLinkedMissionForNpc(npc.Id, Workspace.Dialogs, Workspace.Missions);
        }
    }

    public bool IdentityExpanded
    {
        get => _identityExpanded;
        set
        {
            if (SetProperty(ref _identityExpanded, value))
                NotifySectionStatuses();
        }
    }

    public bool InteractionsExpanded
    {
        get => _interactionsExpanded;
        set
        {
            if (SetProperty(ref _interactionsExpanded, value))
                NotifySectionStatuses();
        }
    }

    public bool DialogConversationExpanded
    {
        get => _dialogConversationExpanded;
        set => SetProperty(ref _dialogConversationExpanded, value);
    }

    public bool MissionBlockExpanded
    {
        get => _missionBlockExpanded;
        set
        {
            if (SetProperty(ref _missionBlockExpanded, value))
                NotifySectionStatuses();
        }
    }

    public bool MissionBlockHighlighted
    {
        get => _missionBlockHighlighted;
        set => SetProperty(ref _missionBlockHighlighted, value);
    }

    public bool LocationsExpanded
    {
        get => _locationsExpanded;
        set
        {
            if (SetProperty(ref _locationsExpanded, value))
                NotifySectionStatuses();
        }
    }

    public bool ReviewExpanded
    {
        get => _reviewExpanded;
        set
        {
            if (SetProperty(ref _reviewExpanded, value))
                NotifySectionStatuses();
        }
    }

    /// <summary>ADMIN.UI.CONTENT.LAYOUT.1 — NPC list rail (layout only; selection preserved when collapsed).</summary>
    public bool NpcListExpanded
    {
        get => _npcListExpanded;
        set => SetProperty(ref _npcListExpanded, value);
    }

    public bool ShowCreateMissionCta => EditorEnabled && !HasMission;

    public bool ShowOrphanMissionWarning
    {
        get
        {
            if (!EditorEnabled) return false;
            var npc = Npc.Selected?.Model;
            if (npc is null) return false;
            foreach (var m in Workspace.Missions.Missions)
            {
                if (m.StartNpcId != npc.Id) continue;
                if (Workspace.Dialogs.FindResponseRefsToMission(m.DraftId).Count == 0)
                    return true;
            }

            return false;
        }
    }

    public string IdentitySectionStatus
    {
        get
        {
            if (!EditorEnabled) return "○";
            var loc = Npc.Selected?.Model.Locations.FirstOrDefault();
            if (loc is not null)
            {
                var mapSet = loc.MapId > 0;
                var cellSet = loc.CellId > 0;
                if (mapSet != cellSet)
                    return "⚠ Falta información";
            }

            return IdentityExpanded ? "● En edición" : "✓ Completo";
        }
    }
    public string InteractionsSectionStatus
    {
        get
        {
            if (!EditorEnabled) return "○";
            if (NeedsInitialDialog || HasIncompleteDialogResponses)
                return "⚠ Falta información";
            if (IsPendingDialogEs || IsPendingNpcEs || IsNpcEsIncomplete)
                return "⚠ Pendiente cliente";
            if (InteractionsExpanded) return "● En edición";
            return "✓ Completo";
        }
    }

    public string LocationsSectionStatus
    {
        get
        {
            if (!EditorEnabled) return "○";
            var locs = Npc.Selected?.Model.Locations;
            if (locs is null || locs.Count == 0)
                return LocationsExpanded ? "● En edición" : "○ Opcional";
            if (locs.Any(l => l.MapId <= 0 || l.CellId <= 0))
                return "⚠ Falta información";
            return LocationsExpanded ? "● En edición" : "✓ Completo";
        }
    }

    public string MissionSectionStatus
    {
        get
        {
            if (!EditorEnabled) return "○";
            if (!HasMission) return "○ Opcional";
            var m = FindMissionFor(Npc.Selected!.Model.Id);
            if (m is null) return "○ Opcional";
            if (Dialogs.IsStartQuestAction && Dialogs.HasLinkedMission) return "● En edición";
            if (Dialogs.IsStartQuestAction && Dialogs.IsStartQuestUnlinked) return "⚠ Acción 44 sin misión";
            if (ShowOrphanMissionWarning) return "⚠ Sin acción 44";
            if (string.IsNullOrWhiteSpace(m.Nombre) || m.Stages.Count == 0
                || m.Stages.Any(s => string.IsNullOrWhiteSpace(s.Nombre)))
                return "⚠ Falta información";
            return "✓ Borrador válido";
        }
    }

    public string ReviewSectionStatus =>
        !EditorEnabled ? "○" : ReviewExpanded ? "● En edición" : "✓ Resumen";

    public IReadOnlyList<ContentReviewLine> ReviewLines
    {
        get
        {
            if (!EditorEnabled)
                return [new("NPC", "○ Sin selección")];

            var npc = Npc.Selected!.Model;
            var mission = FindMissionFor(npc.Id);
            var stageCount = mission?.Stages.Count ?? 0;
            var objCount = mission?.Stages.Sum(s => s.Objectives.Count) ?? 0;
            var hasRewards = mission?.Stages.Any(s =>
                s.Rewards.Exp > 0 || s.Rewards.Kamas > 0 || s.Rewards.Objetos.Count > 0) == true;
            var locs = npc.Locations;
            var loc = locs.FirstOrDefault();
            var mapSet = loc is { MapId: > 0 };
            var cellSet = loc is { CellId: > 0 };
            var locStatus = loc is null || (!mapSet && !cellSet)
                ? "○ Opcional"
                : mapSet && cellSet
                    ? "✓ Completo"
                    : "⚠ Falta información";
            var missionOk = mission is not null
                            && !string.IsNullOrWhiteSpace(mission.Nombre)
                            && stageCount > 0
                            && mission.Stages.All(s => !string.IsNullOrWhiteSpace(s.Nombre));
            var publishedBd = npc.PublishedBd;
            var questsEsStatus = mission switch
            {
                null => "○ Sin misión",
                { QuestsEsPublished: true } => "✓ Publicado cliente",
                { PublishedBd: true, PublishedQuestId: not null } => "⚠ Pendiente cliente",
                { PublishedBd: true } => "⚠ Sin Quest ID",
                _ => "○ Requiere BD",
            };

            return
            [
                new("NPC", publishedBd ? "✓ Publicado BD" : "✓ Borrador"),
                new("Diálogo", NeedsInitialDialog || HasIncompleteDialogResponses ? "⚠ Falta información" : "✓ Completo"),
                new("Ubicación", locStatus),
                new("Misión", mission is null ? "○ Opcional" : missionOk ? "✓ Configurada" : "⚠ Falta información"),
                new("Etapas", mission is null ? "○" : stageCount == 0 ? "⚠ 0" : $"✓ {stageCount}"),
                new("Objetivos", mission is null ? "○" : objCount == 0 ? "⚠ 0" : $"✓ {objCount}"),
                new("Recompensas", mission is null ? "○" : hasRewards ? "✓ Configuradas" : "○ Sin recompensas"),
                new("Base de datos", publishedBd ? "✓ Publicada" : "⚠ No publicada"),
                new("Cliente quests_es", questsEsStatus),
            ];
        }
    }

    public string StatusText =>
        string.IsNullOrWhiteSpace(_idMaxSummary) ? Npc.StatusText : _idMaxSummary;

    public bool ContentMaxReady => _contentMaxReady;

    public string IdMaxSummary => _idMaxSummary;

    public Task InitializeAsync()
    {
        TryApplyLocalMaxCache();
        Dialogs.ReloadNpcs();
        Missions.Reload();
        if (Npc.Selected is null && Npc.Items.Count > 0)
            Npc.Selected = Npc.Items[0];
        SyncFromSelectedNpc();
        Library.Refresh();

        // Startup is deliberately local-only. The cached MAX values are enough
        // to edit immediately; «Leer MAX» and the connection checks perform the
        // explicit remote refresh when the user needs fresh server state.
        return Task.CompletedTask;
    }

    private bool TryApplyLocalMaxCache()
    {
        var settings = AppSettingsStore.Load().Database;
        if (settings is null
            || string.IsNullOrWhiteSpace(settings.Host)
            || string.IsNullOrWhiteSpace(settings.User)
            || string.IsNullOrWhiteSpace(settings.Database))
            return false;

        if (!ContentIdMaxLocalCache.TryLoad(settings, out var catalog))
            return false;

        ApplyMaxCatalog(catalog);
        return true;
    }

    private void ApplyMaxCatalog(ContentIdMaxCatalog catalog)
    {
        catalog.ApplyToWorkspace(Workspace);
        Npc.NotifyDbMaxApplied(catalog.NpcMax);
        Dialogs.NotifyDbMaxApplied(catalog.QuestionMax, catalog.ResponseMax);
        Missions.NotifyDbMaxApplied(catalog.QuestMax, catalog.StageMax, catalog.ObjectiveMax);

        var next = ContentIdReservation.ComputeNext(Workspace);
        _contentMaxReady = true;
        _idMaxSummary = next.FormatSummary() + " · caché local";
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IdMaxSummary));
        OnPropertyChanged(nameof(ContentMaxReady));
        RefreshPublishHeaderState();
    }

    private async Task RefreshContentDbCatalogAsync()
    {
        var repo = TryCreateDbCatalogRepo();
        await Missions.RefreshDbCatalogAsync(repo).ConfigureAwait(true);
        if (Dialogs.ContextStartQuestAction is not null)
            SyncMissionEditorFromAction();
        else
            Missions.RefreshDialogRoleBindings();
    }

    private readonly Dictionary<int, (int X, int Y)> _mapWorldCoordsCache = new();
    private readonly Dictionary<int, int?> _npcMapIdBdCache = new();
    private MapWorldCoordinatesResolver? _mapWorldResolver;

    /// <summary>COORDS_AND_PUBLISHED_REPAIR.6 — mapas.X/Y for MapId (cached).</summary>
    private (int X, int Y)? TryLookupMapWorldCoords(int mapId)
    {
        if (mapId <= 0) return null;
        _mapWorldResolver ??= new MapWorldCoordinatesResolver(LookupMapWorldCoordsUncached);
        return _mapWorldResolver.TryResolve(mapId);
    }

    private (int X, int Y)? LookupMapWorldCoordsUncached(int mapId)
    {
        if (_mapWorldCoordsCache.TryGetValue(mapId, out var cached))
            return cached;

        try
        {
            var settings = AppSettingsStore.Load().Database;
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
                return null;
            var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var repo = _dbRepoFactory?.Invoke(settings, password) ?? new MysqlMapasRepository(settings, password);
            var row = Task.Run(() => repo.TryGetAsync(mapId)).GetAwaiter().GetResult();
            if (row is null) return null;
            var coords = (row.X, row.Y);
            _mapWorldCoordsCache[mapId] = coords;
            return coords;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>npcId → npcs_ubicacion.mapa (cached). Cell ID ignored.</summary>
    private int? TryLookupBdNpcMapId(int npcId)
    {
        if (npcId <= 0) return null;
        if (_npcMapIdBdCache.TryGetValue(npcId, out var cached))
            return cached;

        try
        {
            var settings = AppSettingsStore.Load().Database;
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            {
                _npcMapIdBdCache[npcId] = null;
                return null;
            }

            var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var repo = new MysqlNpcUbicacionMapReadRepository(settings, password);
            var mapId = Task.Run(() => repo.TryGetMapIdAsync(npcId)).GetAwaiter().GetResult();
            _npcMapIdBdCache[npcId] = mapId;
            return mapId;
        }
        catch
        {
            _npcMapIdBdCache[npcId] = null;
            return null;
        }
    }

    private void OnNpcLocationMapChanged(int npcId)
    {
        if (npcId <= 0) return;
        _npcMapIdBdCache.Remove(npcId);
        foreach (var mission in Workspace.Missions.Missions)
        {
            if (mission.StartNpcId != npcId) continue;
            if (MissionAutoReturnSync.IsPublishedLocked(mission)) continue;
            Missions.SyncAutoReturnsForNpcLocationChange(mission);
        }
    }

    private static IContentDbCatalogReadRepository? TryCreateDbCatalogRepo()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlContentDbCatalogReadRepository(settings, password);
    }

    private async Task RefreshAllMaxAsync()
    {
        if (IsReadingMax) return;
        IsReadingMax = true;
        _idMaxSummary = "Leyendo MAX(id) desde BD…";
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IdMaxSummary));

        try
        {
            IContentPublishStore store;
            try
            {
                store = CreatePublishStoreOrThrow();
            }
            catch (Exception ex)
            {
                _contentMaxReady = false;
                _idMaxSummary = "Configura MySQL para leer MAX(id) de contenido.";
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IdMaxSummary));
                OnPropertyChanged(nameof(ContentMaxReady));
                MessageBox.Show(
                    "No se pudo conectar a MySQL para leer MAX.\n\n" +
                    ContentSharedConnectionProbe.SanitizeError(ex) +
                    "\n\nUsa Archivo → Configuración BD… (la misma que Mapas) y «Comprobar BD».",
                    "Leer MAX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var reader = new ContentIdMaxReader(store);
            var result = await reader.ReadAsync().ConfigureAwait(true);
            if (!result.Success || result.Catalog is null)
            {
                _contentMaxReady = false;
                _idMaxSummary = result.FailureMessage;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IdMaxSummary));
                OnPropertyChanged(nameof(ContentMaxReady));
                MessageBox.Show(
                    result.FailureMessage,
                    "Leer MAX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            result.Catalog.ApplyToWorkspace(Workspace);
            Npc.NotifyDbMaxApplied(result.Catalog.NpcMax);
            Dialogs.NotifyDbMaxApplied(result.Catalog.QuestionMax, result.Catalog.ResponseMax);
            Missions.NotifyDbMaxApplied(result.Catalog.QuestMax, result.Catalog.StageMax, result.Catalog.ObjectiveMax);

            var dbSettings = AppSettingsStore.Load().Database;
            if (dbSettings is not null)
                ContentIdMaxLocalCache.Save(dbSettings, result.Catalog);

            var next = ContentIdReservation.ComputeNext(Workspace);
            _contentMaxReady = true;
            _idMaxSummary = next.FormatSummary() + " · BD";
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IdMaxSummary));
            OnPropertyChanged(nameof(ContentMaxReady));
            SyncFromSelectedNpc();
            RefreshPublishHeaderState();
            // Success stays in IdMaxSummary / StatusText — no popup needed.
        }
        catch (Exception ex)
        {
            _contentMaxReady = false;
            var msg = ContentSharedConnectionProbe.SanitizeError(ex);
            _idMaxSummary = "Error leyendo MAX: " + msg;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IdMaxSummary));
            OnPropertyChanged(nameof(ContentMaxReady));
            MessageBox.Show(
                "Error al leer MAX de contenido.\n\n" + msg,
                "Leer MAX",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsReadingMax = false;
        }
    }

    private static IContentPublishStore CreatePublishStoreOrThrow()
    {
        var settings = AppSettingsStore.Load().Database
                       ?? throw new InvalidOperationException("Sin configuración Database en settings.");
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            throw new InvalidOperationException("Falta Host o User en la configuración MySQL.");
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlContentPublishStore(settings, password);
    }

    private static IContentPublishStore? TryCreatePublishStore()
    {
        try
        {
            return CreatePublishStoreOrThrow();
        }
        catch
        {
            return null;
        }
    }

    private void CreateNpc()
    {
        Npc.NewNpcCommand.Execute(null);
        EnsureInitialDialog();
        Dialogs.ReloadNpcs();
        SyncFromSelectedNpc();
    }

    private void DuplicateNpc()
    {
        Npc.DuplicateCommand.Execute(null);
        EnsureInitialDialog();
        Dialogs.ReloadNpcs();
        SyncFromSelectedNpc();
    }

    private void DeleteNpc()
    {
        var deletingId = Npc.Selected?.Id;
        if (deletingId is int npcId)
            Dialogs.OnNpcDeleted(npcId);
        Npc.DeleteCommand.Execute(null);
        Dialogs.ReloadNpcs();
        Missions.Reload();
        SyncFromSelectedNpc();
    }

    private void EnsureInitialDialog()
    {
        var npc = Npc.Selected?.Model;
        if (npc is null) return;
        // CONT-DIALOG.3 — Simple mode never auto-creates npc_preguntas drafts.
        if (npc.DialogMode == NpcDialogMode.Simple)
            return;
        if (npc.Pregunta > 0 && Workspace.Dialogs.FindQuestion(npc.Pregunta) is not null)
            return;
        Dialogs.ReloadNpcs();
        Dialogs.SelectedNpc = npc;
        if (Dialogs.NewQuestionCommand.CanExecute(null))
            Dialogs.NewQuestionCommand.Execute(null);
        Npc.Selected?.RefreshFromModel();
        OnPropertyChanged(nameof(NeedsInitialDialog));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsDialogEsPublishedPendingBd));
        NotifySectionStatuses();
    }

    private bool _syncingMissionEditorFromAction;
    private bool _repairingStartQuestLink;
    private bool _syncMissionEditorScheduled;

    private void ScheduleDialogDependentUiRefresh(bool notifyAiSelection, bool notifyGenerateButton)
    {
        _dialogDependentUiNotifyAi |= notifyAiSelection;
        _dialogDependentUiNotifyGenerateButton |= notifyGenerateButton;
        if (_dialogDependentUiRefreshScheduled) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            RefreshDialogDependentUi();
            return;
        }

        _dialogDependentUiRefreshScheduled = true;
        dispatcher.BeginInvoke(() =>
        {
            _dialogDependentUiRefreshScheduled = false;
            RefreshDialogDependentUi();
        }, DispatcherPriority.Background);
    }

    private void RefreshDialogDependentUi()
    {
        var notifyAiSelection = _dialogDependentUiNotifyAi;
        var notifyGenerateButton = _dialogDependentUiNotifyGenerateButton;
        _dialogDependentUiNotifyAi = false;
        _dialogDependentUiNotifyGenerateButton = false;

        if (notifyAiSelection)
            AiAssistant.NotifyDialogSelectionChanged();
        if (notifyGenerateButton)
            OnPropertyChanged(nameof(ShowGenerateDialogButton));

        Npc.Selected?.RefreshFromModel();
        Npc.Selected?.SyncClientActionsForDialog(Workspace);
        OnPropertyChanged(nameof(NeedsInitialDialog));
        OnPropertyChanged(nameof(HasIncompleteDialogResponses));
        OnPropertyChanged(nameof(IsPendingDialogEs));
        OnPropertyChanged(nameof(IsDialogEsPublishedPendingBd));
        OnPropertyChanged(nameof(IsPendingNpcEs));
        OnPropertyChanged(nameof(IsNpcEsPublished));
        OnPropertyChanged(nameof(IsNpcEsIncomplete));
        OnPropertyChanged(nameof(NpcEsPendingDetails));
        OnPropertyChanged(nameof(NpcEsPublishedDetails));
        OnPropertyChanged(nameof(NpcEsIncompleteDetails));
        OnPropertyChanged(nameof(ClientDialogEsStatusLine));
        OnPropertyChanged(nameof(ClientNpcEsStatusLine));
        NotifySectionStatuses();
        UpdateMissionOwnerNpcContext();
        SyncMissionEditorFromAction();
        OnPropertyChanged(nameof(ShowOrphanMissionWarning));
        LinkOrphanMissionCommand.RaiseCanExecuteChanged();
        CreateMissionFromActionCommand.RaiseCanExecuteChanged();
        Missions.RefreshDebugMissionState();
    }

    private void ScheduleSyncMissionEditorFromAction()
    {
        if (_syncMissionEditorScheduled) return;
        _syncMissionEditorScheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _syncMissionEditorScheduled = false;
            SyncMissionEditorFromAction();
        }, DispatcherPriority.Background);
    }

    private void SyncFromSelectedNpc()
    {
        if (_syncingFromSelectedNpc) return;
        _syncingFromSelectedNpc = true;
        try
        {
            var npc = Npc.Selected?.Model;
            // Bind dialog + mission selection BEFORE OwnerNpcId so SyncStartNpcFromOwner
            // cannot steal the previous NPC's mission onto the newly selected one.
            Dialogs.ReloadNpcs();
            Dialogs.SelectedNpc = npc;
            Missions.Reload();
            Missions.DiscardUnsavedLocalDraftFlag();

            if (npc is null)
            {
                Dialogs.SelectedNode = null;
                Dialogs.ResetMissionRoleDisplayCache();
                Missions.Selected = null;
            }
            else if (Dialogs.IsStartQuestAction)
            {
                SyncMissionEditorFromAction();
            }
            else
            {
                Missions.Selected = Missions.Items.FirstOrDefault(i => i.Model.StartNpcId == npc.Id);
            }

            UpdateMissionOwnerNpcContext();

            // CONT.8: open Identidad + Interacciones; fold the rest when switching NPC.
            _identityExpanded = true;
            _interactionsExpanded = true;
            _dialogConversationExpanded = true;
            _locationsExpanded = false;
            _reviewExpanded = false;
            OnPropertyChanged(nameof(DialogConversationExpanded));
            OnPropertyChanged(nameof(IdentityExpanded));
            OnPropertyChanged(nameof(InteractionsExpanded));
            OnPropertyChanged(nameof(LocationsExpanded));
            OnPropertyChanged(nameof(ReviewExpanded));

            SyncMissionQuestionRoles();

            Npc.Selected?.RefreshFromModel();
            if (Npc.Selected is not null)
                Npc.Selected.SyncClientActionsForDialog(Workspace);
            AiAssistant.CancelPendingGeneration();
            AiAssistant.BindToNpc(Npc.Selected?.Id, Npc.Selected?.Nombre);
            OnPropertyChanged(nameof(EditorEnabled));
            OnPropertyChanged(nameof(ShowGenerateDialogButton));
            OnPropertyChanged(nameof(NeedsInitialDialog));
            OnPropertyChanged(nameof(HasIncompleteDialogResponses));
            OnPropertyChanged(nameof(IsPendingDialogEs));
            OnPropertyChanged(nameof(IsDialogEsPublishedPendingBd));
            OnPropertyChanged(nameof(IsPendingNpcEs));
            OnPropertyChanged(nameof(IsNpcEsPublished));
            OnPropertyChanged(nameof(IsNpcEsIncomplete));
            OnPropertyChanged(nameof(NpcEsPendingDetails));
            OnPropertyChanged(nameof(NpcEsPublishedDetails));
            OnPropertyChanged(nameof(NpcEsIncompleteDetails));
            OnPropertyChanged(nameof(ClientDialogEsStatusLine));
            OnPropertyChanged(nameof(ClientNpcEsStatusLine));
            NotifySectionStatuses();
            NotifyMissionUi();
            CreateMissionCommand.RaiseCanExecuteChanged();
            CreateMissionFromActionCommand.RaiseCanExecuteChanged();
            LinkOrphanMissionCommand.RaiseCanExecuteChanged();
            RemoveMissionCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasMission));
            OnPropertyChanged(nameof(ShowCreateMissionCta));
            NewNpcCommand.RaiseCanExecuteChanged();
            DuplicateCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            _syncingFromSelectedNpc = false;
        }
    }

    private MissionDraft CreateNewMissionForNpc(NpcsModeloDraft npc, bool seedAllRoles)
    {
        var m = Workspace.Missions.CreateMission();
        m.Nombre = MissionDialogRolesUi.DefaultMissionName;
        m.StartNpcId = npc.Id;

        if (seedAllRoles)
        {
            var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(
                m, npc, Workspace, Missions.DbCatalog);
            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? MissionDialogRolesUi.ReservationFailedMessage,
                    "Diálogo de misión",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (result.CreatedQuestionIds.Count > 0)
            {
                Dialogs.ReloadNpcs();
                Dialogs.RefreshTreeDisplay();
                SyncMissionQuestionRoles();
            }
        }

        PersistWorkspace();
        Missions.Reload();
        Missions.Selected = Missions.Items.FirstOrDefault(i => i.Model.DraftId == m.DraftId);
        return m;
    }

    private void CreateMissionFromAction()
    {
        var npc = Npc.Selected?.Model;
        if (npc is null || !Dialogs.IsStartQuestAction || !Dialogs.CanCreateMissionForNpc) return;
        var m = CreateNewMissionForNpc(npc, seedAllRoles: true);
        if (Dialogs.SelectedAction is not null)
            Workspace.Dialogs.LinkStartMission(Dialogs.SelectedAction, m.DraftId);
        Dialogs.SetStartQuestLinkAmbiguous(false);
        Dialogs.ScheduleRefreshAfterMissionLink(reloadMissionChoices: true);
        SyncMissionEditorFromAction();
        NotifyMissionUi();
    }

    private void LinkOrphanMission()
    {
        var npc = Npc.Selected?.Model;
        if (npc is null) return;
        var m = FindMissionFor(npc.Id);
        if (m is null) return;
        if (!Dialogs.TryLinkMissionToSelectedResponse(m))
            return;
        SyncMissionEditorFromAction();
        NotifyMissionUi();
    }

    private void ConfirmRemoveMission()
    {
        var npc = Npc.Selected?.Model;
        if (npc is null || !HasMission) return;
        var result = MessageBox.Show(
            "¿Quitar la misión de este NPC?\n\nSolo se elimina del borrador local. No se modifica la BD.",
            "Quitar misión",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
            return;
        RemoveMission(npc);
        SyncMissionQuestionRoles();
        NotifyMissionUi();
    }

    private void RemoveMission(NpcsModeloDraft npc)
    {
        var m = FindMissionFor(npc.Id);
        if (m is null) return;
        var item = Missions.Items.FirstOrDefault(i => i.Model.DraftId == m.DraftId);
        if (item is null) return;
        Missions.Selected = item;
        if (Missions.DeleteMissionCommand.CanExecute(null))
            Missions.DeleteMissionCommand.Execute(null);
        Dialogs.ReloadNpcs();
    }

    private void SelectMissionFor(int npcId)
    {
        var mission = FindMissionFor(npcId);
        Missions.Selected = mission is null
            ? null
            : Missions.Items.FirstOrDefault(i => i.Model.DraftId == mission.DraftId);
    }

    private MissionDraft? FindMissionFor(int npcId)
    {
        DialogActionDraft? action = Dialogs.ContextStartQuestAction is not null && Npc.Selected?.Model?.Id == npcId
            ? Dialogs.ContextStartQuestAction
            : null;
        return StartQuestLinkUi.ResolveMissionSourceOfTruth(action, Workspace.Missions, npcId);
    }

    private NpcsModeloDraft? ResolveDialogOwnerNpc() =>
        Dialogs.SelectedNpc ?? Npc.Selected?.Model;

    private void UpdateMissionOwnerNpcContext()
    {
        Missions.OwnerNpcId = ResolveDialogOwnerNpc()?.Id ?? Npc.Selected?.Model?.Id;
    }

    private MissionDraft? ResolveLinkedMissionFromAction() =>
        StartQuestLinkUi.ResolveLinkedMission(Dialogs.SelectedAction, Workspace.Missions);

    private bool CompleteMissingDialogRolesForPanel()
    {
        var mission = Missions.ResolveDialogRoleMission();
        var npc = ResolveDialogOwnerNpc();
        if (mission is null || npc is null) return false;
        return ApplyMissionDialogRolesSeed(mission, npc);
    }

    private bool ApplyMissionDialogRolesSeed(MissionDraft mission, NpcsModeloDraft npc)
    {
        var result = MissionDialogRolesUi.EnsureAllDialogRolesIfEmpty(
            mission, npc, Workspace, Missions.DbCatalog);
        if (!result.Success)
        {
            MessageBox.Show(
                result.ErrorMessage ?? MissionDialogRolesUi.ReservationFailedMessage,
                "Diálogo de misión",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!result.Changed)
            return false;

        Missions.OwnerNpcId = npc.Id;
        Missions.SyncFromMissionDraft(mission.DraftId);
        if (result.CreatedQuestionIds.Count > 0)
        {
            Dialogs.ReloadNpcs();
            Dialogs.RefreshTreeDisplay();
        }

        PersistWorkspace();
        SyncMissionQuestionRoles();
        return true;
    }

    private void SyncMissionQuestionRoles()
    {
        var npcId = ResolveDialogOwnerNpc()?.Id ?? Npc.Selected?.Model?.Id;
        DialogActionDraft? action = Dialogs.ContextStartQuestAction;
        var m = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, Workspace.Missions, npcId);
        Dialogs.SetMissionQuestionRoles(
            m?.PregDarPreguntaId,
            m?.PregIncompletaPreguntaId,
            m?.PregCompletadaPreguntaId);
    }

    private void SyncMissionPanelFromLink()
    {
        UpdateMissionOwnerNpcContext();
        Missions.EmbeddedCompactMode = true;
        var draftId = Dialogs.ContextStartQuestAction?.TargetMissionDraftId;
        Missions.SyncInlineLinkedMissionDraft(draftId);
        var linked = draftId is Guid id
            ? Workspace.Missions.FindByDraftId(id)
            : null;
        RefreshMissionPanelState(linked);
        if (linked is not null)
            SyncMissionQuestionRoles();
        Missions.RefreshDebugMissionState();
    }

    private void SeedSelectedStartQuestMission(DialogActionDraft action)
    {
        if (action.Accion != DialogActionCodes.StartQuest) return;
        var npc = ResolveDialogOwnerNpc();
        var mission = StartQuestLinkUi.ResolveLinkedMission(action, Workspace.Missions);
        if (npc is null || mission is null) return;

        if (!ApplyMissionDialogRolesSeed(mission, npc))
            PersistWorkspace();
    }

    private void RefreshMissionPanelState(MissionDraft? linkedMission)
    {
        if (linkedMission is null)
        {
            Missions.RefreshInlinePanelMissionState();
            return;
        }

        if (Missions.Items.All(i => i.Model.DraftId != linkedMission.DraftId))
            Missions.Reload();

        var next = Missions.Items.FirstOrDefault(i => i.Model.DraftId == linkedMission.DraftId);
        if (next is not null && !ReferenceEquals(Missions.Selected, next))
            Missions.Selected = next;
        Missions.RefreshInlinePanelMissionState();
    }

    private void SyncMissionEditorFromAction()
    {
        if (_syncingMissionEditorFromAction) return;
        if (Dialogs.ContextStartQuestAction is null)
        {
            Missions.SyncInlineLinkedMissionDraft(null);
            Missions.RefreshDebugMissionState();
            return;
        }

        var npc = ResolveDialogOwnerNpc();
        if (npc is null)
        {
            Dialogs.SelectedNode = null;
            Missions.Selected = null;
            Missions.RefreshInlinePanelMissionState();
            Missions.RefreshDebugMissionState();
            return;
        }

        _syncingMissionEditorFromAction = true;
        try
        {
            SyncMissionPanelFromLink();

            Missions.LogDialogRoleDiagnostics("action44");
            NotifyMissionUi();

            OnPropertyChanged(nameof(ShowOrphanMissionWarning));
            CreateMissionFromActionCommand.RaiseCanExecuteChanged();
            LinkOrphanMissionCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            _syncingMissionEditorFromAction = false;
        }
    }

    private void RepairStartQuestActionLink(DialogActionDraft action)
    {
        if (action.Accion != DialogActionCodes.StartQuest) return;
        if (_repairingStartQuestLink) return;

        _repairingStartQuestLink = true;
        try
        {
            if (StartQuestLinkUi.HasValidLink(action, Workspace.Missions))
            {
                Dialogs.SetStartQuestLinkAmbiguous(false);
                Dialogs.ScheduleRefreshAfterMissionLink(reloadMissionChoices: true);
                var npcLinked = ResolveDialogOwnerNpc();
                if (npcLinked is not null
                    && action.TargetMissionDraftId is Guid linkedId
                    && Workspace.Missions.FindByDraftId(linkedId) is MissionDraft linkedMission)
                    ApplyMissionDialogRolesSeed(linkedMission, npcLinked);
                SyncMissionEditorFromAction();
                return;
            }

            var npc = ResolveDialogOwnerNpc();
            if (npc is null) return;

            var resolved = StartQuestLinkUi.ResolveMissionForNpc(npc, Workspace.Missions.Missions);
            if (resolved.Outcome == StartQuestLinkUi.ResolveOutcome.Ambiguous)
            {
                Dialogs.SetStartQuestLinkAmbiguous(true);
                Dialogs.ReloadMissionChoices();
                NotifyMissionUi();
                return;
            }

            Dialogs.SetStartQuestLinkAmbiguous(false);
            MissionDraft? mission;
            if (resolved.Outcome == StartQuestLinkUi.ResolveOutcome.NeedsCreate)
            {
                mission = CreateNewMissionForNpc(npc, seedAllRoles: true);
            }
            else
            {
                mission = resolved.Mission;
            }

            if (mission is null) return;

            Workspace.Dialogs.LinkStartMission(action, mission.DraftId);
            ApplyMissionDialogRolesSeed(mission, npc);
            PersistWorkspace();
            Missions.Reload();
            Missions.Selected = Missions.Items.FirstOrDefault(i => i.Model.DraftId == mission.DraftId);
            Dialogs.ScheduleRefreshAfterMissionLink(reloadMissionChoices: true);
            SyncMissionEditorFromAction();
            NotifyMissionUi();
        }
        finally
        {
            _repairingStartQuestLink = false;
        }
    }

    public void NotifyMissionUi()
    {
        OnPropertyChanged(nameof(HasMission));
        OnPropertyChanged(nameof(ShowCreateMissionCta));
        OnPropertyChanged(nameof(NeedsInitialDialog));
        OnPropertyChanged(nameof(ReviewLines));
        OnPropertyChanged(nameof(ShowOrphanMissionWarning));
        CreateMissionCommand.RaiseCanExecuteChanged();
        CreateMissionFromActionCommand.RaiseCanExecuteChanged();
        LinkOrphanMissionCommand.RaiseCanExecuteChanged();
        RemoveMissionCommand.RaiseCanExecuteChanged();
        NotifySectionStatuses();
    }

    private void NotifySectionStatuses()
    {
        OnPropertyChanged(nameof(IdentitySectionStatus));
        OnPropertyChanged(nameof(InteractionsSectionStatus));
        OnPropertyChanged(nameof(LocationsSectionStatus));
        OnPropertyChanged(nameof(MissionSectionStatus));
        OnPropertyChanged(nameof(ReviewSectionStatus));
        OnPropertyChanged(nameof(ReviewLines));
        SchedulePublishHeaderRefresh();
    }

    /// <summary>Debounced async header refresh — keeps last known state while computing.</summary>
    public void SchedulePublishHeaderRefresh(bool immediate = false)
    {
        var token = Interlocked.Increment(ref _publishHeaderDebounceToken);
        if (immediate)
        {
            _ = RunPublishHeaderRefreshAsync(token, skipDebounce: true);
            return;
        }

        _ = RunPublishHeaderRefreshAsync(token, skipDebounce: false);
    }

    /// <summary>Immediate schedule (publish / Leer MAX). Prefer over sync Evaluate on UI thread.</summary>
    public void RefreshPublishHeaderState() => SchedulePublishHeaderRefresh(immediate: true);

    private async Task RunPublishHeaderRefreshAsync(int debounceToken, bool skipDebounce)
    {
        if (!skipDebounce)
        {
            try
            {
                await Task.Delay(PublishHeaderDebounceMs).ConfigureAwait(true);
            }
            catch
            {
                return;
            }

            if (debounceToken != Volatile.Read(ref _publishHeaderDebounceToken))
                return;
        }

        var evalGen = Interlocked.Increment(ref _publishHeaderEvalGeneration);
        _publishHeaderCts?.Cancel();
        _publishHeaderCts?.Dispose();
        var cts = new CancellationTokenSource();
        _publishHeaderCts = cts;
        var ready = _contentMaxReady;
        var workspace = Workspace;

        ContentPublishHeaderState state;
        try
        {
            state = await Task.Run(
                    () => ContentPublishHeaderEvaluator.Evaluate(workspace, contentMaxReady: ready),
                    cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (evalGen != Volatile.Read(ref _publishHeaderEvalGeneration))
            return;
        if (cts.IsCancellationRequested)
            return;

        ApplyPublishHeaderState(state);
    }

    private void ApplyPublishHeaderState(ContentPublishHeaderState state)
    {
        _publishHeader = state;
        OnPropertyChanged(nameof(ShowPublishBdButton));
        OnPropertyChanged(nameof(EnablePublishBdButton));
        OnPropertyChanged(nameof(ShowPublishClientButton));
        OnPropertyChanged(nameof(EnablePublishClientButton));
        OnPropertyChanged(nameof(BdPublishStatusLabel));
        OnPropertyChanged(nameof(ClientPublishStatusLabel));
        OnPropertyChanged(nameof(ShowBdPublishStatus));
        OnPropertyChanged(nameof(ShowClientPublishStatus));
        PublishCommand.RaiseCanExecuteChanged();
        PublishClientCommand.RaiseCanExecuteChanged();
    }

    public void NotifyLocationsUiChanged() => NotifySectionStatuses();

    /// <summary>AI.5 — refresh identity/dialog UI after applying creative text to the draft.</summary>
    private void RefreshUiAfterAiApply()
    {
        PersistWorkspace();
        Npc.Selected?.RefreshFromModel();
        Dialogs.ReloadNpcs();
        Dialogs.SelectedNpc = Npc.Selected?.Model;
        Dialogs.RefreshAfterExternalDraftChange();
        Npc.Selected?.SyncClientActionsForDialog(Workspace);
        OnPropertyChanged(nameof(NeedsInitialDialog));
        OnPropertyChanged(nameof(HasIncompleteDialogResponses));
        OnPropertyChanged(nameof(StatusText));
        NotifySectionStatuses();
        if (Npc.Selected is not null)
            AiAssistant.BindToNpc(Npc.Selected.Id, Npc.Selected.Nombre);
    }

    private void PersistWorkspace()
    {
        if (_workspace is null)
            ContentDraftStore.Save();
        Library.Refresh();
    }

    private void OpenLibraryEntry(ContentLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        switch (entry.Kind)
        {
            case ContentLibraryEntryKind.Npc:
                OpenLibraryNpc(entry.NpcId);
                break;
            case ContentLibraryEntryKind.Mission:
                OpenLibraryMission(entry.MissionDraftId, entry.NpcId);
                break;
            case ContentLibraryEntryKind.Dialog:
                OpenLibraryDialog(entry.NpcId);
                break;
        }
    }

    private void DuplicateLibraryEntry(ContentLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != ContentLibraryEntryKind.Mission || entry.MissionDraftId is not Guid draftId)
            return;

        Missions.SyncFromMissionDraft(draftId);
        if (Missions.Selected is null)
            return;
        Missions.DuplicateMissionCommand.Execute(null);
        Library.Refresh();
    }

    private void RemoveLibraryEntry(ContentLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.CanRemove)
            return;

        switch (entry.Kind)
        {
            case ContentLibraryEntryKind.Mission when entry.MissionDraftId is Guid draftId:
                RemoveLibraryMission(draftId, confirm: true);
                break;
            case ContentLibraryEntryKind.Npc when entry.NpcId is int npcId:
                RemoveLibraryNpc(npcId, confirm: true);
                break;
        }
    }

    private void RemoveLibraryEntries(IReadOnlyList<ContentLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.Where(e => e.CanRemove).ToList();
        if (list.Count == 0)
            return;
        if (list.Count == 1)
        {
            RemoveLibraryEntry(list[0]);
            return;
        }

        var draftCount = list.Count(e => e.CanDeleteDraft);
        var localCopyCount = list.Count(e => e.CanRemoveLocalCopy);
        var names = list.Take(12).Select(e => e.Title).ToList();
        var body =
            $"¿Quitar {list.Count} elemento(s) del workspace local?\n\n" +
            $"· Eliminar borrador: {draftCount}\n" +
            $"· Quitar copia local (sigue en servidor): {localCopyCount}\n\n" +
            "No se publica nada. No se ejecuta DELETE en BD ni se modifica el cliente.\n\n" +
            string.Join("\n", names.Select(n => "· " + n));
        if (list.Count > names.Count)
            body += $"\n· … y {list.Count - names.Count} más";

        if (MessageBox.Show(body, "Eliminar seleccionados", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK)
            return;

        var touchesOpenMission = list.Any(e =>
            e.Kind == ContentLibraryEntryKind.Mission
            && e.MissionDraftId == Missions.Selected?.Model.DraftId);
        var touchesOpenNpc = list.Any(e =>
            e.Kind == ContentLibraryEntryKind.Npc && e.NpcId == Npc.Selected?.Id);
        if ((touchesOpenMission || touchesOpenNpc) && Missions.HasUnsavedLocalDraft
            && !ConfirmDiscardOpenDraft())
            return;

        if (touchesOpenMission || touchesOpenNpc)
            Missions.DiscardUnsavedLocalDraftFlag();

        foreach (var entry in list)
        {
            switch (entry.Kind)
            {
                case ContentLibraryEntryKind.Mission when entry.MissionDraftId is Guid draftId:
                    RemoveLibraryMission(draftId, confirm: false);
                    break;
                case ContentLibraryEntryKind.Npc when entry.NpcId is int npcId:
                    RemoveLibraryNpc(npcId, confirm: false);
                    break;
            }
        }

        PersistWorkspace();
        ReloadNpcListKeepingSelection(preferNpcId: null);
        Missions.Reload();
        Dialogs.ReloadNpcs();
        SyncFromSelectedNpc();
        Library.Refresh();
    }

    private void RemoveLibraryMission(Guid draftId, bool confirm)
    {
        var plan = ContentLibraryLocalCleanup.PlanMission(Workspace, draftId);
        if (!plan.Allowed)
        {
            if (confirm)
                MessageBox.Show(plan.Message, plan.Caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (confirm && !ConfirmLibraryRemoval(plan, isOpenMission: Missions.Selected?.Model.DraftId == draftId))
            return;

        if (Missions.Selected?.Model.DraftId == draftId)
            Missions.DiscardUnsavedLocalDraftFlag();

        if (!ContentLibraryLocalCleanup.RemoveMissionLocal(Workspace, draftId))
            return;

        if (!confirm)
            return;

        PersistWorkspace();
        Missions.Reload();
        Dialogs.ReloadNpcs();
        SyncFromSelectedNpc();
        Library.Refresh();
    }

    private void RemoveLibraryNpc(int npcId, bool confirm)
    {
        var plan = ContentLibraryLocalCleanup.PlanNpc(Workspace, npcId);
        if (!plan.Allowed)
        {
            if (confirm)
                MessageBox.Show(plan.Message, plan.Caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (confirm && !ConfirmLibraryRemoval(plan, isOpenMission: false))
            return;

        var removeLinked = plan.Kind == ContentLibraryRemovalKind.DeleteUnpublishedDraft
            && plan.HasLinkedLocalContent;

        if (confirm && Npc.Selected?.Id == npcId)
        {
            if (Missions.HasUnsavedLocalDraft
                && Missions.Selected?.Model.StartNpcId == npcId
                && !ConfirmDiscardOpenDraft())
                return;
            Missions.DiscardUnsavedLocalDraftFlag();
        }

        if (!ContentLibraryLocalCleanup.RemoveNpcLocal(Workspace, npcId, removeLinkedUnpublished: removeLinked))
            return;

        if (!confirm)
            return;

        PersistWorkspace();
        ReloadNpcListKeepingSelection(preferNpcId: null);
        Missions.Reload();
        Dialogs.ReloadNpcs();
        SyncFromSelectedNpc();
        Library.Refresh();
    }

    private bool ConfirmLibraryRemoval(ContentLibraryRemovalPlan plan, bool isOpenMission)
    {
        if (isOpenMission && Missions.HasUnsavedLocalDraft && !ConfirmDiscardOpenDraft())
            return false;

        var body = plan.Message;
        if (plan.SummaryLines.Count > 0)
        {
            body += "\n\nSe eliminará localmente:\n";
            foreach (var line in plan.SummaryLines)
                body += "· " + line + "\n";
        }

        var result = MessageBox.Show(
            body.TrimEnd(),
            plan.Caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.OK;
    }

    private static bool ConfirmDiscardOpenDraft()
    {
        var r = MessageBox.Show(
            "Este borrador está abierto con cambios sin guardar.\n\n" +
            "¿Continuar y descartar los cambios locales?\n" +
            "(No se publica nada.)",
            "Cambios sin guardar",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        return r == MessageBoxResult.OK;
    }

    private void ReloadNpcListKeepingSelection(int? preferNpcId)
    {
        var keepId = preferNpcId ?? Npc.Selected?.Id;
        Npc.ReloadFromWorkspace();
        if (keepId is int id)
        {
            var item = Npc.Items.FirstOrDefault(n => n.Id == id);
            if (item is not null)
                Npc.Selected = item;
        }
    }

    private void OpenLibraryNpc(int? npcId)
    {
        if (npcId is not int id)
            return;
        var item = Npc.Items.FirstOrDefault(n => n.Id == id);
        if (item is null)
            return;
        Npc.Selected = item;
        IdentityExpanded = true;
        ShowLibrary = false;
    }

    private void OpenLibraryMission(Guid? missionDraftId, int? startNpcId)
    {
        if (missionDraftId is not Guid draftId)
            return;
        if (startNpcId is int sid)
        {
            var item = Npc.Items.FirstOrDefault(n => n.Id == sid);
            if (item is not null)
                Npc.Selected = item;
        }

        Missions.SyncFromMissionDraft(draftId);
        MissionBlockExpanded = true;
        ShowLibrary = false;
    }

    private void OpenLibraryDialog(int? npcId)
    {
        if (npcId is not int id)
            return;
        var item = Npc.Items.FirstOrDefault(n => n.Id == id);
        if (item is null)
            return;
        Npc.Selected = item;
        InteractionsExpanded = true;
        DialogConversationExpanded = true;
        ShowLibrary = false;
    }

    private void PublishClient()
    {
        // Use last computed header state — do not re-parse SWF on the UI thread.
        if (!ShowPublishClientButton && ClientPublishStatusLabel == ContentPublishHeaderEvaluator.ClientUpToDate)
        {
            MessageBox.Show(
                "✓ Cliente al día (no hay dialog_es / npc_es / quests_es pendientes).",
                "Publicar cliente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SchedulePublishHeaderRefresh(immediate: true);
            return;
        }

        var vm = new ContentClientPublishViewModel(
            Workspace,
            _sftpPublishFactory,
            TryLookupMapWorldCoords,
            TryLookupBdNpcMapId);
        var win = new ContentClientPublishWindow(vm)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        if (win.ShowDialog() == true)
        {
            ContentPublishHeaderEvaluator.InvalidateCachedQuestsEs();
            Dialogs.ReloadNpcs();
            SyncFromSelectedNpc();
            OnPropertyChanged(nameof(IsPendingDialogEs));
            OnPropertyChanged(nameof(IsDialogEsPublishedPendingBd));
            OnPropertyChanged(nameof(IsPendingNpcEs));
            OnPropertyChanged(nameof(IsNpcEsPublished));
            OnPropertyChanged(nameof(IsNpcEsIncomplete));
            OnPropertyChanged(nameof(NpcEsPendingDetails));
            OnPropertyChanged(nameof(NpcEsPublishedDetails));
            OnPropertyChanged(nameof(NpcEsIncompleteDetails));
            NotifySectionStatuses();
            SchedulePublishHeaderRefresh(immediate: true);
        }
        else
            SchedulePublishHeaderRefresh(immediate: true);
    }

    private async Task PublishAsync()
    {
        if (!_contentMaxReady)
        {
            MessageBox.Show(
                "No hay MAX de BD válidos. Pulsa «Leer MAX» antes de publicar.\n" +
                "Sin MAX reales no se reservan IDs.",
                "IDs no listos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Preflight gates moved into ContentPublishPlanBuilder for the *selected* NPCs
        // only (publish window checkboxes). Do not block opening the dialog because
        // another unpublished NPC still needs cliente / diálogo.

        var win = new ContentPublishWindow
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        win.ShowDialog();
        foreach (var item in Npc.Items)
            item.RefreshFromModel();
        Dialogs.ReloadNpcs();
        Missions.Reload();
        await RefreshContentDbCatalogAsync().ConfigureAwait(true);
        SyncFromSelectedNpc();
        RefreshPublishHeaderState();
    }

    private async Task CheckDatabaseAsync()
    {
        CheckingDatabase = true;
        DatabaseDetail = "Comprobando…";
        try
        {
            var settings = AppSettingsStore.Load();
            var db = settings.Database ?? new DatabaseSettings();
            string password;
            try
            {
                password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
            }
            catch (Exception ex)
            {
                _databaseState = SharedConnectionState.Error;
                DatabaseDetail = ContentSharedConnectionProbe.SanitizeError(ex);
                OnPropertyChanged(nameof(DatabaseStatusLabel));
                return;
            }

            await ContentSharedConnectionProbe.ProbeDatabaseAsync(db, password, _dbRepoFactory)
                .ConfigureAwait(true);
            _databaseState = SharedConnectionState.Connected;
            DatabaseDetail = string.IsNullOrWhiteSpace(db.Host)
                ? "OK"
                : $"{db.Host}:{db.Port} · {db.User} · {db.Database}";
            OnPropertyChanged(nameof(DatabaseStatusLabel));
            ObjetosModeloNameCache.Shared = ObjetosModeloNameCache.FromRepository(
                new MysqlObjetosModeloNameReadRepository(db, password));
            MobsModeloNameCache.Shared = MobsModeloNameCache.FromRepository(
                new MysqlMobsModeloNameReadRepository(db, password));
            await RefreshContentDbCatalogAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _databaseState = SharedConnectionState.Error;
            DatabaseDetail = ContentSharedConnectionProbe.SanitizeError(ex);
            OnPropertyChanged(nameof(DatabaseStatusLabel));
        }
        finally
        {
            CheckingDatabase = false;
        }
    }

    private async Task CheckSftpAsync()
    {
        CheckingSftp = true;
        SftpDetail = "Comprobando…";
        try
        {
            var settings = AppSettingsStore.Load();
            var sftp = settings.LangSftp ?? new LangSftpSettings();
            string password;
            try
            {
                password = LangSftpPasswordProtector.Unprotect(sftp.PasswordProtectedBase64);
            }
            catch (Exception ex)
            {
                _sftpState = SharedConnectionState.Error;
                SftpDetail = ContentSharedConnectionProbe.SanitizeError(ex);
                OnPropertyChanged(nameof(SftpStatusLabel));
                return;
            }

            // Offload blocking SSH.NET connect off the UI thread.
            var message = await Task.Run(() =>
                    ContentSharedConnectionProbe.ProbeSftp(sftp, password, _sftpClientFactory))
                .ConfigureAwait(true);
            _sftpState = SharedConnectionState.Connected;
            SftpDetail = string.IsNullOrWhiteSpace(sftp.Host)
                ? message
                : $"{sftp.Host}:{sftp.Port} · {sftp.User} · {message}";
            OnPropertyChanged(nameof(SftpStatusLabel));
        }
        catch (Exception ex)
        {
            _sftpState = SharedConnectionState.Error;
            SftpDetail = ContentSharedConnectionProbe.SanitizeError(ex);
            OnPropertyChanged(nameof(SftpStatusLabel));
        }
        finally
        {
            CheckingSftp = false;
        }
    }
}

public sealed record ContentReviewLine(string Label, string Mark);
