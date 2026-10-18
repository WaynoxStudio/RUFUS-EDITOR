using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.ViewModels;

public enum MissionDialogRole
{
    Offer,
    Incomplete,
    Complete,
}

public sealed partial class ContentMissionEditorViewModel
{
    public event Action<int>? MissionRoleQuestionCreated;

    public RelayCommand CreatePregDarCommand { get; private set; } = null!;
    public RelayCommand CreatePregIncCommand { get; private set; } = null!;
    public RelayCommand CreatePregCompCommand { get; private set; } = null!;
    public RelayCommand CompleteMissingDialogRolesCommand { get; private set; } = null!;

    private int? _ownerNpcId;
    private Guid? _inlineLinkedMissionDraftId;
    private Func<MissionDraft?>? _linkedMissionResolver;
    private Func<bool>? _completeMissingDialogRolesHandler;

    /// <summary>MISSIONS.PANEL.STATE.1C — cache TargetMissionDraftId; avoids SelectedAction timing gaps.</summary>
    public void SyncInlineLinkedMissionDraft(Guid? draftId)
    {
        if (!SetProperty(ref _inlineLinkedMissionDraftId, draftId))
            return;
        OnPropertyChanged(nameof(CurrentLinkedMission));
        RefreshInlinePanelMissionState();
    }

    private MissionDraft? ResolveInlineLinkedMission()
    {
        if (_inlineLinkedMissionDraftId is Guid draftId
            && Workspace.Missions.FindByDraftId(draftId) is { } byCache)
            return byCache;
        return _linkedMissionResolver?.Invoke();
    }

    /// <summary>NPC currently being edited (workspace / acción 44 context).</summary>
    public int? OwnerNpcId
    {
        get => _ownerNpcId;
        set
        {
            if (!SetProperty(ref _ownerNpcId, value))
                return;
            SyncStartNpcFromOwner();
            RefreshDialogRoleBindings();
            NotifyPreviewProperties();
        }
    }

    /// <summary>MISSIONS.DIALOG.ROLES.4 — acción 44 linked mission from workspace resolver.</summary>
    public void ConfigureLinkedMissionResolver(Func<MissionDraft?>? resolver)
    {
        _linkedMissionResolver = resolver;
        RefreshDialogRoleBindings();
    }

    /// <summary>MISSIONS.LIFECYCLE.1 — explicit seed of incomplete dialog roles only.</summary>
    public void ConfigureCompleteMissingDialogRolesHandler(Func<bool>? handler)
    {
        _completeMissingDialogRolesHandler = handler;
        CompleteMissingDialogRolesCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Linked MissionDraft in acción 44 inline context (may differ from Selected).</summary>
    public MissionDraft? CurrentLinkedMission =>
        ActionInlineMode ? ResolveInlineLinkedMission() : null;

    /// <summary>Single source for dialog role panel fields.</summary>
    public MissionDraft? ResolveDialogRoleMission() =>
        MissionDialogRolesUi.ResolvePanelMissionDraft(ActionInlineMode, CurrentLinkedMission, Selected?.Model);

    /// <summary>MISSIONS.PANEL.STATE.1B — inline panel nombre (linked draft, not legacy Selected).</summary>
    public string InlineMissionNombre
    {
        get => ResolveDialogRoleMission()?.Nombre ?? "";
        set
        {
            if (ResolveDialogRoleMission() is not { } mission) return;
            if (mission.Nombre == value) return;
            mission.Nombre = value;
            RefreshMissionListItem(mission);
            Persist();
            DraftEdited?.Invoke();
            OnPropertyChanged(nameof(InlineMissionNombre));
        }
    }

    public bool InlineMissionPuedeRepetirse
    {
        get => ResolveDialogRoleMission()?.PuedeRepetirse ?? false;
        set
        {
            if (ResolveDialogRoleMission() is not { } mission) return;
            if (mission.PuedeRepetirse == value) return;
            mission.PuedeRepetirse = value;
            RefreshMissionListItem(mission);
            Persist();
            DraftEdited?.Invoke();
            OnPropertyChanged(nameof(InlineMissionPuedeRepetirse));
        }
    }

    public bool HasOwnerNpc => ResolveEffectiveOwnerNpcId() is int id && id > 0;

    public bool ShowCreatePregDar => HasOwnerNpc && PregDarQuestionIdValue is null;
    public bool ShowCreatePregInc => HasOwnerNpc && !HasPregIncRole;
    public bool ShowCreatePregComp => HasOwnerNpc && !HasPregCompRole;

    public bool ShowCompleteMissingDialogRoles =>
        ResolveDialogRoleMission() is { } mission
        && HasOwnerNpc
        && (mission.PregDarPreguntaId is null or <= 0
            || mission.PregIncompletaPreguntaId is null or <= 0
            || mission.PregCompletadaPreguntaId is null or <= 0);

    public bool HasPregDarRole => PregDarQuestionIdValue is int id && id > 0;
    public bool HasPregIncRole => PregIncQuestionIdValue is int id && id > 0;
    public bool HasPregCompRole => PregCompQuestionIdValue is int id && id > 0;

    public bool ShowPregDarRoleDisplay => HasPregDarRole;
    public bool ShowPregIncRoleDisplay => true;
    public bool ShowPregCompRoleDisplay => true;

    public string PregDarRoleDisplay => FormatRoleQuestionDisplay(PregDarQuestionIdValue);
    public string PregIncRoleDisplay => FormatRoleQuestionDisplay(PregIncQuestionIdValue);
    public string PregCompRoleDisplay => FormatRoleQuestionDisplay(PregCompQuestionIdValue);

    public static string FormatQuestionLabel(DialogQuestionDraft q) =>
        MissionDialogRolesUi.FormatQuestionLabel(q);

    private void EnsureDialogRoleCommands()
    {
        CreatePregDarCommand = new RelayCommand(
            () => CreateQuestionForRole(MissionDialogRole.Offer),
            () => ShowCreatePregDar && !IsBusy);
        CreatePregIncCommand = new RelayCommand(
            () => CreateQuestionForRole(MissionDialogRole.Incomplete),
            () => ShowCreatePregInc && !IsBusy);
        CreatePregCompCommand = new RelayCommand(
            () => CreateQuestionForRole(MissionDialogRole.Complete),
            () => ShowCreatePregComp && !IsBusy);
        CompleteMissingDialogRolesCommand = new RelayCommand(
            CompleteMissingDialogRoles,
            () => ShowCompleteMissingDialogRoles && !IsBusy);
    }

    private void CompleteMissingDialogRoles()
    {
        if (_completeMissingDialogRolesHandler?.Invoke() == true)
            RefreshDialogRoleBindings();
    }

    private int? ResolveEffectiveOwnerNpcId() =>
        OwnerNpcId ?? ResolveDialogRoleMission()?.StartNpcId;

    private void SyncStartNpcFromOwner()
    {
        if (ResolveDialogRoleMission() is not { } mission || OwnerNpcId is not int npcId) return;
        // Do not reassign a mission already bound to another NPC (NPC navigation).
        if (mission.StartNpcId is int sid && sid > 0 && sid != npcId)
            return;
        if (!MissionDraft.SyncStartNpcFromOwner(mission, npcId))
        {
            if (SyncAutoReturnsFor(mission))
            {
                RefreshMissionListItem(mission);
                if (Selected?.Model.DraftId == mission.DraftId)
                    AfterAutoReturnSync();
            }

            return;
        }

        SyncAutoReturnsFor(mission);
        RefreshMissionListItem(mission);
        if (Selected?.Model.DraftId == mission.DraftId)
            AfterAutoReturnSync();
    }

    private void AfterAutoReturnSync()
    {
        SyncObjectiveRows();
        OnPropertyChanged(nameof(ShowAutoReturnBanner));
        OnPropertyChanged(nameof(AutoReturnBannerText));
        OnPropertyChanged(nameof(ShowBoundStartNpcInfo));
        OnPropertyChanged(nameof(BoundStartNpcDisplay));
        OnPropertyChanged(nameof(BoundStartNpcMissing));
        OnPropertyChanged(nameof(ShowStageNoObjectivesHint));
        OnPropertyChanged(nameof(Step2Status));
        OnPropertyChanged(nameof(StageTechnicalPreview));
        if (SelectedObjective is not null
            && MissionShowDeliverNpcBinding.IsBoundTipo(SelectedObjective.Tipo))
            LoadObjectiveEditorFromSelected();
        // Automatic sync — persist disk state without raising the unsaved-navigation flag.
        Persist(markPendingBd: false, markLocalDirty: false);
        DraftEdited?.Invoke();
    }

    private void RefreshMissionListItem(MissionDraft mission)
    {
        if (Selected?.Model.DraftId == mission.DraftId)
            Selected.Refresh();
        else
            Items.FirstOrDefault(i => i.Model.DraftId == mission.DraftId)?.Refresh();
    }

    private void CreateQuestionForRole(MissionDialogRole role)
    {
        if (ResolveDialogRoleMission() is not { } mission || ResolveEffectiveOwnerNpcId() is not int npcId) return;
        SyncStartNpcFromOwner();
        var q = Workspace.Dialogs.CreateQuestion(npcId);
        switch (role)
        {
            case MissionDialogRole.Offer:
                mission.PregDarPreguntaId = q.Id;
                break;
            case MissionDialogRole.Incomplete:
                mission.PregIncompletaPreguntaId = q.Id;
                break;
            case MissionDialogRole.Complete:
                mission.PregCompletadaPreguntaId = q.Id;
                break;
        }

        RefreshDialogRoleBindings();
        NotifyMissionMeta();
        NotifyDialogRoleUi();
        MissionRoleQuestionCreated?.Invoke(q.Id);
    }

    /// <summary>MISSIONS.DIALOG.ROLES.1B — refresh question choices and ID bindings.</summary>
    public void RefreshDialogRoleBindings()
    {
        SyncStartNpcFromOwner();
        RefreshQuestionChoices();
        NotifyInlineMissionMeta();
        NotifyDialogRoleSelectionProperties();
        NotifyPreviewProperties();
        OnPropertyChanged(nameof(EditorEnabled));
        ScheduleQuestionComboResync();
        RefreshDebugMissionState();
    }

    /// <summary>MISSIONS.PANEL.STATE.1 — refresh inline panel after TargetMissionDraftId / link change.</summary>
    public void RefreshInlinePanelMissionState()
    {
        OnPropertyChanged(nameof(CurrentLinkedMission));
        NotifyInlineMissionMeta();
        RefreshDialogRoleBindings();
    }

    private void NotifyInlineMissionMeta()
    {
        OnPropertyChanged(nameof(InlineMissionNombre));
        OnPropertyChanged(nameof(InlineMissionPuedeRepetirse));
    }

    private bool _questionComboResyncScheduled;

    /// <summary>WPF ComboBox SelectedValue often stays blank after ItemsSource refresh.</summary>
    private void ScheduleQuestionComboResync()
    {
        if (_questionComboResyncScheduled) return;
        _questionComboResyncScheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _questionComboResyncScheduled = false;
            NotifyDialogRoleSelectionProperties();
        }, DispatcherPriority.DataBind);
    }

    private void NotifyDialogRoleSelectionProperties()
    {
        OnPropertyChanged(nameof(PregDarQuestionIdValue));
        OnPropertyChanged(nameof(PregIncQuestionIdValue));
        OnPropertyChanged(nameof(PregCompQuestionIdValue));
        OnPropertyChanged(nameof(PregDarRoleDisplay));
        OnPropertyChanged(nameof(PregIncRoleDisplay));
        OnPropertyChanged(nameof(PregCompRoleDisplay));
        OnPropertyChanged(nameof(HasPregDarRole));
        OnPropertyChanged(nameof(HasPregIncRole));
        OnPropertyChanged(nameof(HasPregCompRole));
        OnPropertyChanged(nameof(ShowPregDarRoleDisplay));
        OnPropertyChanged(nameof(ShowPregIncRoleDisplay));
        OnPropertyChanged(nameof(ShowPregCompRoleDisplay));
        OnPropertyChanged(nameof(ShowCompleteMissingDialogRoles));
        NotifyDialogRoleUi();
    }

    private void NotifyDialogRoleUi()
    {
        OnPropertyChanged(nameof(HasOwnerNpc));
        OnPropertyChanged(nameof(ShowCreatePregDar));
        OnPropertyChanged(nameof(ShowCreatePregInc));
        OnPropertyChanged(nameof(ShowCreatePregComp));
        OnPropertyChanged(nameof(ShowCompleteMissingDialogRoles));
        CreatePregDarCommand.RaiseCanExecuteChanged();
        CreatePregIncCommand.RaiseCanExecuteChanged();
        CreatePregCompCommand.RaiseCanExecuteChanged();
        CompleteMissingDialogRolesCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPreviewProperties()
    {
        OnPropertyChanged(nameof(PregDarPreview));
        OnPropertyChanged(nameof(PregIncPreview));
        OnPropertyChanged(nameof(PregCompPreview));
    }

    private string BuildRolePreview(int? questionId) =>
        MissionDraft.BuildPregPair(ResolveEffectiveOwnerNpcId(), questionId);

    private string FormatRoleQuestionDisplay(int? questionId)
    {
        if (questionId is not int id || id <= 0)
            return MissionDialogRolesUi.RoleNotConfiguredLabel;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{id}");
    }
}
