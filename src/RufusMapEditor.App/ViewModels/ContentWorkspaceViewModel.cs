using System.Windows;
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
    private bool _syncingMissionToggle;
    private bool _identityExpanded = true;
    private bool _interactionsExpanded = true;
    private bool _locationsExpanded;
    private bool _missionExpanded;
    private bool _reviewExpanded;
    private SharedConnectionState _databaseState = SharedConnectionState.Unchecked;
    private SharedConnectionState _sftpState = SharedConnectionState.Unchecked;
    private string _databaseDetail = "";
    private string _sftpDetail = "";
    private bool _checkingDatabase;
    private bool _checkingSftp;

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
        AiAssistant = new ContentAiAssistantViewModel(aiGeneration ?? AiBackendGenerationServiceFactory.CreateForEditor());
        AiAssistant.AttachDraftHost(
            () => Npc.Selected?.Model,
            () => Workspace,
            RefreshUiAfterAiApply);
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
        PublishCommand = new RelayCommand(Publish);
        PublishClientCommand = new RelayCommand(PublishClient);
        CreateMissionCommand = new RelayCommand(CreateMissionForSelectedNpc, () => EditorEnabled && !HasMission);
        RemoveMissionCommand = new RelayCommand(ConfirmRemoveMission, () => EditorEnabled && HasMission);
        RefreshMaxCommand = new RelayCommand(async () => await RefreshAllMaxAsync());
        CheckDatabaseCommand = new RelayCommand(async () => await CheckDatabaseAsync(), () => !CheckingDatabase);
        CheckSftpCommand = new RelayCommand(async () => await CheckSftpAsync(), () => !CheckingSftp);

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
                or nameof(ContentDialogEditorViewModel.SimpleDialogTextLocal))
            {
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
                        AiAssistant.BindToNpc(item.Id, item.Nombre);

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
        };
    }

    public ContentNpcEditorViewModel Npc { get; }
    public ContentDialogEditorViewModel Dialogs { get; }
    public ContentMissionEditorViewModel Missions { get; }
    /// <summary>AI.1 — creative assistant (local request only; no API).</summary>
    public ContentAiAssistantViewModel AiAssistant { get; }

    public RelayCommand NewNpcCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PublishCommand { get; }
    public RelayCommand PublishClientCommand { get; }
    public RelayCommand CreateMissionCommand { get; }
    public RelayCommand RemoveMissionCommand { get; }
    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand CheckDatabaseCommand { get; }
    public RelayCommand CheckSftpCommand { get; }

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

    public string ClientDialogEsStatusLine =>
        IsPendingDialogEs ? "dialog_es: ⚠ Pendiente" : "dialog_es: ✓ Publicado";

    public string ClientNpcEsStatusLine =>
        IsNpcEsIncomplete ? "npc_es: ⚠ Incompleto"
        : IsPendingNpcEs ? "npc_es: ⚠ Pendiente"
        : "npc_es: ✓ Publicado";

    public bool EditorEnabled => Npc.EditorEnabled;


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

    public bool HasMission
    {
        get
        {
            var npc = Npc.Selected?.Model;
            if (npc is null) return false;
            return FindMissionFor(npc.Id) is not null;
        }
        set
        {
            if (_syncingMissionToggle) return;
            var npc = Npc.Selected?.Model;
            if (npc is null) return;
            if (value)
            {
                EnsureMission(npc);
                MissionExpanded = true;
            }
            else
            {
                RemoveMission(npc);
                MissionExpanded = false;
            }
            NotifyMissionUi();
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

    public bool LocationsExpanded
    {
        get => _locationsExpanded;
        set
        {
            if (SetProperty(ref _locationsExpanded, value))
                NotifySectionStatuses();
        }
    }

    public bool MissionExpanded
    {
        get => _missionExpanded;
        set
        {
            if (SetProperty(ref _missionExpanded, value))
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

    public bool ShowCreateMissionCta => EditorEnabled && !HasMission;

    public string IdentitySectionStatus =>
        !EditorEnabled ? "○" : IdentityExpanded ? "● En edición" : "✓ Completo";

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
            if (MissionExpanded) return "● En edición";
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
            var locOk = locs.Count > 0 && locs.All(l => l.MapId > 0 && l.CellId > 0);
            var missionOk = mission is not null
                            && !string.IsNullOrWhiteSpace(mission.Nombre)
                            && stageCount > 0
                            && mission.Stages.All(s => !string.IsNullOrWhiteSpace(s.Nombre));
            var publishedBd = npc.PublishedBd;

            return
            [
                new("NPC", publishedBd ? "✓ Publicado BD" : "✓ Borrador"),
                new("Diálogo", NeedsInitialDialog || HasIncompleteDialogResponses ? "⚠ Falta información" : "✓ Completo"),
                new("Ubicación", locs.Count == 0 ? "○ Opcional" : locOk ? "✓ Completo" : "⚠ Falta información"),
                new("Misión", mission is null ? "○ Opcional" : missionOk ? "✓ Configurada" : "⚠ Falta información"),
                new("Etapas", mission is null ? "○" : stageCount == 0 ? "⚠ 0" : $"✓ {stageCount}"),
                new("Objetivos", mission is null ? "○" : objCount == 0 ? "⚠ 0" : $"✓ {objCount}"),
                new("Recompensas", mission is null ? "○" : hasRewards ? "✓ Configuradas" : "○ Sin recompensas"),
                new("Base de datos", publishedBd ? "✓ Publicada" : "⚠ No publicada"),
                new("Cliente quests_es", "⚠ Pendiente de soporte"),
            ];
        }
    }

    public string StatusText => Npc.StatusText;

    public async Task InitializeAsync()
    {
        await Npc.InitializeAsync();
        await Dialogs.InitializeAsync();
        await Missions.InitializeAsync();
        if (Npc.Selected is null && Npc.Items.Count > 0)
            Npc.Selected = Npc.Items[0];
        SyncFromSelectedNpc();
    }

    private async Task RefreshAllMaxAsync()
    {
        await Npc.RefreshMaxAsync();
        await Dialogs.RefreshMaxAsync();
        await Missions.RefreshMaxAsync();
        SyncFromSelectedNpc();
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

    private void SyncFromSelectedNpc()
    {
        var npc = Npc.Selected?.Model;
        Dialogs.ReloadNpcs();
        Dialogs.SelectedNpc = npc;
        Missions.Reload();
        Missions.Selected = npc is null
            ? null
            : Missions.Items.FirstOrDefault(i => i.Model.StartNpcId == npc.Id);

        // CONT.8: open Identidad + Interacciones; fold the rest when switching NPC.
        _identityExpanded = true;
        _interactionsExpanded = true;
        _locationsExpanded = false;
        _missionExpanded = false;
        _reviewExpanded = false;
        OnPropertyChanged(nameof(IdentityExpanded));
        OnPropertyChanged(nameof(InteractionsExpanded));
        OnPropertyChanged(nameof(LocationsExpanded));
        OnPropertyChanged(nameof(MissionExpanded));
        OnPropertyChanged(nameof(ReviewExpanded));

        Npc.Selected?.RefreshFromModel();
        if (Npc.Selected is not null)
            Npc.Selected.SyncClientActionsForDialog(Workspace);
        // AI.1 — creative name only (never NPC ID in AI request). Does not auto-fill identity/dialog.
        // AI.4A/AI.5 — cancel in-flight generation; bind selected NPC for Usar safety.
        AiAssistant.CancelPendingGeneration();
        AiAssistant.BindToNpc(Npc.Selected?.Id, Npc.Selected?.Nombre);
        OnPropertyChanged(nameof(EditorEnabled));
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
        OnPropertyChanged(nameof(StatusText));
        _syncingMissionToggle = true;
        OnPropertyChanged(nameof(HasMission));
        OnPropertyChanged(nameof(ShowCreateMissionCta));
        _syncingMissionToggle = false;
        NotifySectionStatuses();
        NewNpcCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        CreateMissionCommand.RaiseCanExecuteChanged();
        RemoveMissionCommand.RaiseCanExecuteChanged();
    }

    private void EnsureMission(NpcsModeloDraft npc)
    {
        if (FindMissionFor(npc.Id) is not null)
        {
            SelectMissionFor(npc.Id);
            return;
        }

        var m = Workspace.Missions.CreateMission();
        m.Nombre = string.IsNullOrWhiteSpace(npc.Nombre) ? $"Misión {npc.Id}" : npc.Nombre;
        m.StartNpcId = npc.Id;
        if (npc.Pregunta > 0)
            m.PregDarPreguntaId = npc.Pregunta;
        PersistWorkspace();
        Missions.Reload();
        SelectMissionFor(npc.Id);
        Dialogs.ReloadNpcs();
    }

    private void CreateMissionForSelectedNpc()
    {
        var npc = Npc.Selected?.Model;
        if (npc is null) return;
        EnsureMission(npc);
        MissionExpanded = true;
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
        MissionExpanded = false;
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

    private void SelectMissionFor(int npcId) =>
        Missions.Selected = Missions.Items.FirstOrDefault(i => i.Model.StartNpcId == npcId);

    private MissionDraft? FindMissionFor(int npcId) =>
        Workspace.Missions.Missions.FirstOrDefault(m => m.StartNpcId == npcId);

    private void NotifyMissionUi()
    {
        OnPropertyChanged(nameof(HasMission));
        OnPropertyChanged(nameof(ShowCreateMissionCta));
        OnPropertyChanged(nameof(NeedsInitialDialog));
        OnPropertyChanged(nameof(ReviewLines));
        CreateMissionCommand.RaiseCanExecuteChanged();
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
    }

    private void PublishClient()
    {
        var needDialog = ContentClientRemotePublishService.HasPendingDialogEs(Workspace);
        var needNpc = ContentClientRemotePublishService.HasPendingNpcEs(Workspace);
        if (!needDialog && !needNpc)
        {
            MessageBox.Show(
                "✓ Cliente ya publicado (no hay capas dialog_es / npc_es pendientes).",
                "Publicar cliente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var vm = new ContentClientPublishViewModel(Workspace, _sftpPublishFactory);
        var win = new ContentClientPublishWindow(vm)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        if (win.ShowDialog() == true)
        {
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
        }
    }

    private void Publish()
    {
        var pendingDialogEs = Npc.Items
            .Where(n => !n.Model.PublishedBd && n.Model.IsPendingDialogEs)
            .Select(n => "#" + n.Id)
            .ToList();
        if (pendingDialogEs.Count > 0)
        {
            MessageBox.Show(
                "Hay diálogo simple con texto nuevo pendiente de publicación cliente.\n" +
                "Usa «Publicar cliente» antes de publicar en BD.\n\nNPC: "
                + string.Join(", ", pendingDialogEs),
                "Pendiente de publicación cliente",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var missingSimple = Npc.Items
            .Where(n => !n.Model.PublishedBd
                        && n.Model.DialogMode == NpcDialogMode.Simple
                        && n.Model.Pregunta <= 0)
            .Select(n => "#" + n.Id)
            .ToList();
        if (missingSimple.Count > 0)
        {
            MessageBox.Show(
                "Cada diálogo simple necesita un texto nuevo o un ID existente para reutilizar.\n\nFaltan: "
                + string.Join(", ", missingSimple),
                "Diálogo simple incompleto",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var missingInteractive = Npc.Items
            .Where(n => !n.Model.PublishedBd && n.Model.DialogMode == NpcDialogMode.Interactive)
            .Where(n => n.Model.Pregunta <= 0
                        || Workspace.Dialogs.FindQuestion(n.Model.Pregunta) is null)
            .ToList();
        if (missingInteractive.Count > 0)
        {
            var ids = string.Join(", ", missingInteractive.Select(n => "#" + n.Id));
            MessageBox.Show(
                "Cada conversación interactiva debe tener una pregunta inicial antes de publicar.\n\nFaltan: " + ids,
                "Diálogo obligatorio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var incompleteNpcIds = Npc.Items
            .Where(n => !n.Model.PublishedBd && n.Model.DialogMode == NpcDialogMode.Interactive)
            .Where(n => Workspace.Dialogs.HasIncompleteResponsesForNpc(n.Id))
            .Select(n => "#" + n.Id)
            .ToList();
        if (incompleteNpcIds.Count > 0)
        {
            MessageBox.Show(
                "Hay respuestas sin acción. Añade al menos una acción antes de publicar.\n\nNPC: "
                + string.Join(", ", incompleteNpcIds),
                "Respuesta sin acción",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var win = new ContentPublishWindow
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        win.ShowDialog();
        foreach (var item in Npc.Items)
            item.RefreshFromModel();
        Dialogs.ReloadNpcs();
        Missions.Reload();
        SyncFromSelectedNpc();
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
