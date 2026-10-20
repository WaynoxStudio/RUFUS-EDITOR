using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// AI.1–AI.5 — creative NPC assistant: generate via backend, preview, apply to local draft, regenerate.
/// Never publishes BD/SFTP/SWF and never invents technical IDs.
/// </summary>
public sealed class ContentAiAssistantViewModel : ViewModelBase
{
    private readonly IAiGenerationService _generationService;
    private CancellationTokenSource? _generationCts;
    private DispatcherTimer? _conversationWaitTimer;
    private bool _conversationProgressActive;
    private Func<NpcsModeloDraft?>? _getSelectedNpc;
    private Func<ContentDraftWorkspace>? _getWorkspace;
    private Func<DialogQuestionDraft?>? _getSelectedQuestion;
    private Func<DialogResponseDraft?>? _getSelectedResponse;
    private Func<bool>? _getIsSimpleDialog;
    private Action? _refreshAfterApply;
    private Action? _persistDrafts;
    private bool _suppressProfileWrite;
    private AiBubbleKind _bubbleKind;
    private DialogQuestionDraft? _bubbleTargetQuestion;
    private DialogResponseDraft? _bubbleTargetResponse;
    private string _bubbleInstruction = "";
    private bool _bubbleAiPanelOpen;

    private string _rolePreset = AiCreativePresets.Roles[0];
    private string _customRole = "";
    private string _attitudePreset = AiCreativePresets.Attitudes[0];
    private string _customAttitude = "";
    private string _narrativeContext = "";
    private string _additionalInstruction = "";
    private AiTextLength _length = AiTextLength.Corta;
    private string _currentNpcName = "";
    private int? _selectedNpcId;
    private int? _resultBoundNpcId;
    private bool _showRequestPreview;
    private string _requestPreviewText = "";
    private string _statusMessage = "";
    private AiCreativeRequest? _lastRequest;
    private AiPromptPackage? _lastPromptPackage;
    private AiCreativeAction _lastAction = AiCreativeAction.GenerarNombre;
    private AiGenerationResult? _lastGenerationResult;
    private bool _showResultPreview;
    private string _dialoguePreviewText = "";
    private string _conversationNpcText = "";
    private string _conversationIncompleteText = "";
    private string _conversationCompleteText = "";
    private string _validationDebugText = "";
    private bool _isGenerating;
    private double _generationProgress;
    private string _generationProgressText = "";
    private bool _isGenerationProgressVisible;

    public ContentAiAssistantViewModel()
        : this(AiBackendGenerationServiceFactory.CreateForEditor())
    {
    }

    public ContentAiAssistantViewModel(IAiGenerationService generationService)
    {
        _generationService = generationService ?? throw new ArgumentNullException(nameof(generationService));

        RolePresets = new ObservableCollection<string>(AiCreativePresets.Roles);
        AttitudePresets = new ObservableCollection<string>(AiCreativePresets.Attitudes);
        LengthOptions = new ObservableCollection<AiTextLength>(AiCreativePresets.Lengths);
        NamePreviewItems = new ObservableCollection<AiNamePreviewItemViewModel>();
        ConversationReplyItems = new ObservableCollection<AiConversationReplyPreviewItemViewModel>();

        GenerateNameCommand = new RelayCommand(
            async () => await PrepareAsync(AiCreativeAction.GenerarNombre),
            () => !IsGenerating);
        GenerateDialogCommand = new RelayCommand(
            async () =>
            {
                _bubbleKind = AiBubbleKind.None;
                await PrepareAsync(AiCreativeAction.GenerarDialogo);
            },
            () => !IsGenerating);
        GenerateConversationCommand = new RelayCommand(
            async () => await PrepareAsync(AiCreativeAction.GenerarConversacion),
            () => !IsGenerating);
        GenerateNpcBubbleCommand = new RelayCommand(
            async () => await GenerateBubbleAsync(
                _getIsSimpleDialog?.Invoke() == true ? AiBubbleKind.SimpleDialog : AiBubbleKind.NpcQuestion),
            () => !IsGenerating
                  && (_getIsSimpleDialog?.Invoke() == true || _getSelectedQuestion?.Invoke() is not null));
        GeneratePlayerReplyCommand = new RelayCommand(
            async () => await GenerateBubbleAsync(AiBubbleKind.PlayerReply),
            () => !IsGenerating && _getSelectedResponse?.Invoke() is not null);
        LoadMockResultCommand = new RelayCommand(LoadMockResult, () => !IsGenerating);
        UseDialogueCommand = new RelayCommand(UseDialogue, () => !IsGenerating && HasDialoguePreview && _bubbleKind == AiBubbleKind.None && _getSelectedNpc?.Invoke() is not null);
        UseConversationCommand = new RelayCommand(UseConversation, () => !IsGenerating && HasConversationPreview && _getSelectedNpc?.Invoke() is not null);
        UseBubbleTextCommand = new RelayCommand(UseBubbleText, () => !IsGenerating && HasBubblePreview);
        CancelBubbleResultCommand = new RelayCommand(CancelBubbleResult, () => HasBubblePreview || ShowBubbleAiResult);
        RegenerateCommand = new RelayCommand(
            async () => await PrepareAsync(_lastAction, isRegenerate: true),
            () => !IsGenerating && HasUsablePreview);
    }

    /// <summary>ADMIN.UI.3.2 — raised when a generation starts (expand the matching Content section).</summary>
    public event Action<AiCreativeAction>? GenerationRequested;

    /// <summary>Raised after a generated name is applied to the draft (close popup).</summary>
    public event Action? NameApplied;

    /// <summary>Dev-only mock loader; hidden in Release USER/ADMIN builds.</summary>
    public bool ShowMockTools =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Wires draft NPC + workspace from Contenido (AI.5).</summary>
    public void AttachDraftHost(
        Func<NpcsModeloDraft?> getSelectedNpc,
        Func<ContentDraftWorkspace> getWorkspace,
        Action refreshAfterApply,
        Func<DialogQuestionDraft?>? getSelectedQuestion = null,
        Func<DialogResponseDraft?>? getSelectedResponse = null,
        Action? persistDrafts = null,
        Func<bool>? getIsSimpleDialog = null)
    {
        _getSelectedNpc = getSelectedNpc ?? throw new ArgumentNullException(nameof(getSelectedNpc));
        _getWorkspace = getWorkspace ?? throw new ArgumentNullException(nameof(getWorkspace));
        _refreshAfterApply = refreshAfterApply ?? throw new ArgumentNullException(nameof(refreshAfterApply));
        _getSelectedQuestion = getSelectedQuestion;
        _getSelectedResponse = getSelectedResponse;
        _persistDrafts = persistDrafts;
        _getIsSimpleDialog = getIsSimpleDialog;
    }

    public IAiGenerationService GenerationService => _generationService;

    public string? BackendUrlDisplay =>
        _generationService is AiBackendGenerationService backend
            ? backend.Settings.BackendUrl
            : null;

    public bool HasBackendUrl => !string.IsNullOrWhiteSpace(BackendUrlDisplay);

    public ObservableCollection<string> RolePresets { get; }
    public ObservableCollection<string> AttitudePresets { get; }
    public ObservableCollection<AiTextLength> LengthOptions { get; }
    public ObservableCollection<AiNamePreviewItemViewModel> NamePreviewItems { get; }
    public ObservableCollection<AiConversationReplyPreviewItemViewModel> ConversationReplyItems { get; }

    public string StyleLabel => AiCreativeStyle.RufusDofusRetro;

    public RelayCommand GenerateNameCommand { get; }
    public RelayCommand GenerateDialogCommand { get; }
    public RelayCommand GenerateConversationCommand { get; }
    public RelayCommand GenerateNpcBubbleCommand { get; }
    public RelayCommand GeneratePlayerReplyCommand { get; }
    public RelayCommand LoadMockResultCommand { get; }
    public RelayCommand UseDialogueCommand { get; }
    public RelayCommand UseConversationCommand { get; }
    public RelayCommand UseBubbleTextCommand { get; }
    public RelayCommand CancelBubbleResultCommand { get; }
    public RelayCommand RegenerateCommand { get; }

    public string ServiceStatusLabel
    {
        get
        {
            if (IsGenerating)
                return "IA: Generando...";
            return _generationService.Status switch
            {
                AiGenerationServiceStatus.NotConfigured => "IA: No configurada",
                AiGenerationServiceStatus.Available => "IA: Disponible",
                AiGenerationServiceStatus.Generating => "IA: Generando...",
                AiGenerationServiceStatus.Error => "IA: Error",
                _ => "IA: No configurada"
            };
        }
    }

    public string ExpanderStatusLabel => ServiceStatusLabel;

    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                GenerateNameCommand.RaiseCanExecuteChanged();
                GenerateDialogCommand.RaiseCanExecuteChanged();
                GenerateConversationCommand.RaiseCanExecuteChanged();
                GenerateNpcBubbleCommand.RaiseCanExecuteChanged();
                GeneratePlayerReplyCommand.RaiseCanExecuteChanged();
                LoadMockResultCommand.RaiseCanExecuteChanged();
                RegenerateCommand.RaiseCanExecuteChanged();
                UseDialogueCommand.RaiseCanExecuteChanged();
                UseConversationCommand.RaiseCanExecuteChanged();
                UseBubbleTextCommand.RaiseCanExecuteChanged();
                foreach (var item in NamePreviewItems)
                    item.RaiseUseCanExecuteChanged();
                OnPropertyChanged(nameof(ServiceStatusLabel));
                OnPropertyChanged(nameof(ExpanderStatusLabel));
            }
        }
    }

    /// <summary>DIALOG.AI.GENERATION.PROGRESS.1 — estimated 0–100 for GenerarConversacion only.</summary>
    public double GenerationProgress
    {
        get => _generationProgress;
        private set
        {
            if (SetProperty(ref _generationProgress, value))
                OnPropertyChanged(nameof(GenerationProgressPercentLabel));
        }
    }

    public string GenerationProgressText
    {
        get => _generationProgressText;
        private set => SetProperty(ref _generationProgressText, value ?? "");
    }

    public bool IsGenerationProgressVisible
    {
        get => _isGenerationProgressVisible;
        private set => SetProperty(ref _isGenerationProgressVisible, value);
    }

    public string GenerationProgressPercentLabel =>
        $"{Math.Clamp((int)Math.Round(GenerationProgress), 0, 100)} %";

    public string RolePreset
    {
        get => _rolePreset;
        set
        {
            if (SetProperty(ref _rolePreset, value ?? ""))
                OnPropertyChanged(nameof(IsCustomRole));
        }
    }

    public string CustomRole
    {
        get => _customRole;
        set => SetProperty(ref _customRole, value ?? "");
    }

    public bool IsCustomRole =>
        string.Equals(RolePreset, AiCreativePresets.RoleCustomLabel, StringComparison.OrdinalIgnoreCase);

    public string AttitudePreset
    {
        get => _attitudePreset;
        set
        {
            if (SetProperty(ref _attitudePreset, value ?? ""))
            {
                OnPropertyChanged(nameof(IsCustomAttitude));
                WriteProfileToSelectedNpc();
            }
        }
    }

    public string CustomAttitude
    {
        get => _customAttitude;
        set
        {
            if (SetProperty(ref _customAttitude, value ?? ""))
                WriteProfileToSelectedNpc();
        }
    }

    public bool IsCustomAttitude =>
        string.Equals(AttitudePreset, AiCreativePresets.AttitudeCustomLabel, StringComparison.OrdinalIgnoreCase);

    public string NarrativeContext
    {
        get => _narrativeContext;
        set
        {
            if (SetProperty(ref _narrativeContext, value ?? ""))
                WriteProfileToSelectedNpc();
        }
    }

    public string AdditionalInstruction
    {
        get => _additionalInstruction;
        set => SetProperty(ref _additionalInstruction, value ?? "");
    }

    public AiTextLength Length
    {
        get => _length;
        set
        {
            if (SetProperty(ref _length, value))
                WriteProfileToSelectedNpc();
        }
    }

    public string BubbleInstruction
    {
        get => _bubbleInstruction;
        set => SetProperty(ref _bubbleInstruction, value ?? "");
    }

    public bool BubbleAiPanelOpen
    {
        get => _bubbleAiPanelOpen;
        set => SetProperty(ref _bubbleAiPanelOpen, value);
    }

    public string BubbleInstructionPrompt =>
        _getSelectedResponse?.Invoke() is not null
            ? "¿Cómo quieres que responda el jugador?"
            : "¿Qué quieres que diga el NPC?";

    public string GenerateBubbleButtonLabel =>
        _getSelectedResponse?.Invoke() is not null
            ? "✨ Generar respuesta"
            : "✨ Generar texto";

    public string CurrentNpcName
    {
        get => _currentNpcName;
        set => SetProperty(ref _currentNpcName, value ?? "");
    }

    public bool ShowRequestPreview
    {
        get => _showRequestPreview;
        set => SetProperty(ref _showRequestPreview, value);
    }

    public string RequestPreviewText
    {
        get => _requestPreviewText;
        private set => SetProperty(ref _requestPreviewText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public AiCreativeRequest? LastRequest
    {
        get => _lastRequest;
        private set => SetProperty(ref _lastRequest, value);
    }

    public AiPromptPackage? LastPromptPackage
    {
        get => _lastPromptPackage;
        private set => SetProperty(ref _lastPromptPackage, value);
    }

    public AiGenerationResult? LastGenerationResult
    {
        get => _lastGenerationResult;
        private set
        {
            if (SetProperty(ref _lastGenerationResult, value))
            {
                OnPropertyChanged(nameof(HasNamePreview));
                OnPropertyChanged(nameof(HasDialoguePreview));
                OnPropertyChanged(nameof(HasConversationPreview));
                OnPropertyChanged(nameof(HasResultPreviewContent));
                OnPropertyChanged(nameof(HasUsablePreview));
                OnPropertyChanged(nameof(HasBubblePreview));
                OnPropertyChanged(nameof(ShowIdentityAiResult));
                OnPropertyChanged(nameof(ShowInteractionsAiResult));
                OnPropertyChanged(nameof(ShowBubbleAiResult));
                RegenerateCommand.RaiseCanExecuteChanged();
                UseDialogueCommand.RaiseCanExecuteChanged();
                UseConversationCommand.RaiseCanExecuteChanged();
                UseBubbleTextCommand.RaiseCanExecuteChanged();
                CancelBubbleResultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowResultPreview
    {
        get => _showResultPreview;
        set
        {
            if (SetProperty(ref _showResultPreview, value))
            {
                OnPropertyChanged(nameof(ShowIdentityAiResult));
                OnPropertyChanged(nameof(ShowInteractionsAiResult));
                OnPropertyChanged(nameof(ShowBubbleAiResult));
            }
        }
    }

    public string DialoguePreviewText
    {
        get => _dialoguePreviewText;
        private set => SetProperty(ref _dialoguePreviewText, value);
    }

    public string ConversationNpcText
    {
        get => _conversationNpcText;
        private set => SetProperty(ref _conversationNpcText, value);
    }

    public string ConversationIncompleteText
    {
        get => _conversationIncompleteText;
        private set => SetProperty(ref _conversationIncompleteText, value);
    }

    public string ConversationCompleteText
    {
        get => _conversationCompleteText;
        private set => SetProperty(ref _conversationCompleteText, value);
    }

    public string ValidationDebugText
    {
        get => _validationDebugText;
        private set
        {
            if (SetProperty(ref _validationDebugText, value))
            {
                OnPropertyChanged(nameof(ShowIdentityAiResult));
                OnPropertyChanged(nameof(ShowInteractionsAiResult));
                OnPropertyChanged(nameof(ShowBubbleAiResult));
                OnPropertyChanged(nameof(HasResultPreviewContent));
            }
        }
    }

    public bool HasNamePreview =>
        LastGenerationResult is { IsValid: true, Action: AiCreativeAction.GenerarNombre, Names: not null };

    public bool HasDialoguePreview =>
        LastGenerationResult is { IsValid: true, Action: AiCreativeAction.GenerarDialogo, Dialogue: not null };

    public bool HasConversationPreview =>
        LastGenerationResult is { IsValid: true, Action: AiCreativeAction.GenerarConversacion, Conversation: not null };

    public bool HasUsablePreview => HasNamePreview || HasDialoguePreview || HasConversationPreview;

    public bool HasBubblePreview =>
        _bubbleKind != AiBubbleKind.None
        && LastGenerationResult is { IsValid: true, Action: AiCreativeAction.GenerarDialogo, Dialogue: not null };

    public bool HasResultPreviewContent =>
        HasUsablePreview || !string.IsNullOrWhiteSpace(ValidationDebugText);

    /// <summary>ADMIN.UI.3.2 — name result / validation feedback stays in Identidad.</summary>
    public bool ShowIdentityAiResult =>
        HasNamePreview
        || (ShowResultPreview && _lastAction == AiCreativeAction.GenerarNombre && !string.IsNullOrWhiteSpace(ValidationDebugText));

    /// <summary>ADMIN.UI.3.2 — dialogue/conversation results stay in Interacciones (not bubble panel).</summary>
    public bool ShowInteractionsAiResult =>
        _bubbleKind == AiBubbleKind.None
        && (HasDialoguePreview
            || HasConversationPreview
            || (ShowResultPreview
                && (_lastAction is AiCreativeAction.GenerarDialogo or AiCreativeAction.GenerarConversacion)
                && !string.IsNullOrWhiteSpace(ValidationDebugText)));

    public bool ShowBubbleAiResult =>
        _bubbleKind != AiBubbleKind.None
        && (HasBubblePreview
            || (ShowResultPreview && !string.IsNullOrWhiteSpace(ValidationDebugText)));

    public void BindToNpc(int? npcId, string? npcName)
    {
        var npcChanged = npcId != _selectedNpcId;
        CancelPendingGeneration();
        _selectedNpcId = npcId;
        CurrentNpcName = npcName ?? "";
        ReadProfileFromSelectedNpc();
        if (npcChanged)
        {
            BubbleInstruction = "";
            _bubbleKind = AiBubbleKind.None;
            _bubbleTargetQuestion = null;
            _bubbleTargetResponse = null;
            NotifyBubbleUi();
        }

        OnPropertyChanged(nameof(ServiceStatusLabel));
        OnPropertyChanged(nameof(ExpanderStatusLabel));
        UseDialogueCommand.RaiseCanExecuteChanged();
        UseConversationCommand.RaiseCanExecuteChanged();
        UseBubbleTextCommand.RaiseCanExecuteChanged();
        GenerateNpcBubbleCommand.RaiseCanExecuteChanged();
        GeneratePlayerReplyCommand.RaiseCanExecuteChanged();
        foreach (var item in NamePreviewItems)
            item.RaiseUseCanExecuteChanged();
    }

    /// <summary>Backward-compatible name-only bind (AI.4).</summary>
    public void BindToNpc(string? npcName) => BindToNpc(_selectedNpcId, npcName);

    public void CancelPendingGeneration()
    {
        try { _generationCts?.Cancel(); }
        catch (ObjectDisposedException) { /* ignore */ }
        StopConversationWaitPulse();
        if (_conversationProgressActive)
            ResetConversationProgress();
    }

    public void OpenBubbleAiPanel()
    {
        BubbleAiPanelOpen = true;
        NotifyDialogSelectionChanged();
    }

    public void NotifyDialogSelectionChanged()
    {
        NotifyBubbleUi();
        GenerateNpcBubbleCommand.RaiseCanExecuteChanged();
        GeneratePlayerReplyCommand.RaiseCanExecuteChanged();
        UseBubbleTextCommand.RaiseCanExecuteChanged();
    }

    public AiCreativeRequest BuildRequest(AiCreativeAction action)
    {
        var npcName = _getSelectedNpc?.Invoke()?.Nombre ?? CurrentNpcName;
        var additional = AdditionalInstruction;
        string? prior = null;
        string? parent = null;
        var playerReply = false;
        var ws = _getWorkspace?.Invoke();
        var npc = _getSelectedNpc?.Invoke();

        if (action == AiCreativeAction.GenerarDialogo && _bubbleKind != AiBubbleKind.None)
        {
            additional = BubbleInstruction;
            var mission = ws is not null && npc is not null
                ? AiMissionRoleContextBuilder.ResolveMission(ws, npc)
                : null;

            if (_bubbleKind == AiBubbleKind.NpcQuestion)
            {
                var q = _bubbleTargetQuestion ?? _getSelectedQuestion?.Invoke();
                if (ws is not null && npc is not null && q is not null)
                    prior = AiDialogThreadContextBuilder.BuildPriorForQuestion(ws, npc, q);

                var roleHint = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, q, isPlayerReply: false);
                if (!string.IsNullOrWhiteSpace(roleHint))
                {
                    additional = string.IsNullOrWhiteSpace(additional)
                        ? roleHint
                        : roleHint + Environment.NewLine + additional.Trim();
                }
            }
            else if (_bubbleKind == AiBubbleKind.PlayerReply)
            {
                playerReply = true;
                var response = _bubbleTargetResponse ?? _getSelectedResponse?.Invoke();
                var q = _bubbleTargetQuestion
                    ?? _getSelectedQuestion?.Invoke()
                    ?? (ws is not null && response is not null
                        ? AiDialogThreadContextBuilder.FindParentQuestion(ws, response)
                        : null);
                parent = q?.TextLocal ?? "";
                if (ws is not null && npc is not null && q is not null)
                    prior = AiDialogThreadContextBuilder.BuildPriorForResponse(ws, npc, q);

                var roleHint = AiMissionRoleContextBuilder.BuildBubbleRoleHint(mission, q, isPlayerReply: true);
                if (!string.IsNullOrWhiteSpace(roleHint))
                {
                    additional = string.IsNullOrWhiteSpace(additional)
                        ? roleHint
                        : roleHint + Environment.NewLine + additional.Trim();
                }
            }

            if (ws is not null && npc is not null)
            {
                var snapshot = AiMissionSnapshotBuilder.Build(ws, npc, mission);
                additional = string.IsNullOrWhiteSpace(additional)
                    ? snapshot
                    : additional.Trim() + Environment.NewLine + Environment.NewLine + snapshot;
            }
        }
        else if (action == AiCreativeAction.GenerarConversacion && ws is not null && npc is not null)
        {
            var mission = AiMissionRoleContextBuilder.ResolveMission(ws, npc);
            prior = AiMissionRoleContextBuilder.BuildConversationSnapshotWithRoles(ws, npc, mission);
            var missionBlock = AiMissionSnapshotBuilder.Build(ws, npc, mission);
            var glossary = AiMissionRoleContextBuilder.BuildRoleGlossary();
            additional = string.IsNullOrWhiteSpace(additional)
                ? glossary + Environment.NewLine + Environment.NewLine + missionBlock
                : additional.Trim()
                  + Environment.NewLine + Environment.NewLine + glossary
                  + Environment.NewLine + Environment.NewLine + missionBlock;
        }

        return AiCreativeRequestBuilder.Build(
            action,
            rolePreset: "",
            customRole: "",
            AttitudePreset,
            CustomAttitude,
            NarrativeContext,
            additional,
            Length,
            npcName,
            prior,
            parent,
            playerReply);
    }

    public void PresentGenerationResult(AiGenerationResult result, int? boundNpcId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastGenerationResult = result;
        _lastAction = result.Action;
        if (boundNpcId is not null)
            _resultBoundNpcId = boundNpcId;
        else if (_resultBoundNpcId is null)
            _resultBoundNpcId = _selectedNpcId;

        ClearPreviewCollections();

        if (!result.IsValid)
        {
            ValidationDebugText = result.ErrorDetail ?? AiBackendResponseParser.InvalidUserMessage;
            StatusMessage = AiBackendResponseParser.InvalidUserMessage;
            ShowResultPreview = true;
            return;
        }

        ValidationDebugText = $"Validación OK · {AiCreativeRequestPreview.FormatAction(result.Action)}";
        switch (result.Action)
        {
            case AiCreativeAction.GenerarNombre when result.Names is not null:
                foreach (var n in result.Names.Nombres)
                    NamePreviewItems.Add(new AiNamePreviewItemViewModel(this, n.Nombre, n.Motivo ?? ""));
                break;
            case AiCreativeAction.GenerarDialogo when result.Dialogue?.Dialogo is not null:
                DialoguePreviewText = result.Dialogue.Dialogo.Texto;
                break;
            case AiCreativeAction.GenerarConversacion when result.Conversation?.Conversacion is not null:
                // DIALOG.AI.AUTO_APPLY.SINGLE_REPLY.2 — apply directly; no conversation preview.
                AutoApplyConversation(result.Conversation.Conversacion);
                ShowResultPreview = false;
                NotifyBubbleUi();
                return;
        }

        StatusMessage = _bubbleKind == AiBubbleKind.None
            ? "Preview · no aplicado al NPC hasta pulsar Usar"
            : "Preview · no aplicado al NPC hasta pulsar Usar texto";
        ShowResultPreview = true;
        NotifyBubbleUi();
        RegenerateCommand.RaiseCanExecuteChanged();
        UseDialogueCommand.RaiseCanExecuteChanged();
        UseConversationCommand.RaiseCanExecuteChanged();
        UseBubbleTextCommand.RaiseCanExecuteChanged();
        CancelBubbleResultCommand.RaiseCanExecuteChanged();
    }

    /// <summary>DIALOG.AI.AUTO_APPLY.SINGLE_REPLY.2 — apply validated conversation without preview/confirm UX.</summary>
    private void AutoApplyConversation(AiConversationResult conversation)
    {
        var npc = _getSelectedNpc?.Invoke();
        var ws = _getWorkspace?.Invoke();
        if (ws is null)
        {
            StatusMessage = "Sin workspace de contenido.";
            if (_conversationProgressActive)
                SetConversationProgress(GenerationProgress, "Error al generar la conversación.");
            return;
        }

        if (_conversationProgressActive)
            SetConversationProgress(97, "Aplicando conversación...");

        var result = AiDraftApplier.ApplyConversation(
            ws, npc, conversation,
            replaceConfirmed: true,
            interactiveSwitchConfirmed: true,
            _resultBoundNpcId);

        if (result.Kind == AiDraftApplyKind.Applied)
        {
            if (_conversationProgressActive)
                SetConversationProgress(100, "Conversación generada.");
            StatusMessage = "Conversación generada.";
            AiGenerationActivityLog.Info("resultado aplicado: Conversación");
            _refreshAfterApply?.Invoke();
            return;
        }

        StatusMessage = result.Message;
        AiGenerationActivityLog.Error(_lastAction, $"Conversación no aplicada: {result.Kind}");
        if (_conversationProgressActive)
            SetConversationProgress(GenerationProgress, "Error al generar la conversación.");
    }

    internal void UseName(string nombre)
    {
        var npc = _getSelectedNpc?.Invoke();
        // ADMIN.UI.3.2 — apply immediately; Usar implies replace.
        var result = AiDraftApplier.ApplyName(npc, nombre, replaceConfirmed: true, _resultBoundNpcId);
        FinishApply(result, "Nombre");
    }

    private void UseDialogue()
    {
        if (LastGenerationResult?.Dialogue?.Dialogo?.Texto is not string text) return;
        var npc = _getSelectedNpc?.Invoke();
        var ws = _getWorkspace?.Invoke();
        if (ws is null)
        {
            StatusMessage = "Sin workspace de contenido.";
            return;
        }

        var result = AiDraftApplier.ApplyDialogue(ws, npc, text, replaceConfirmed: false, _resultBoundNpcId);
        if (result.Kind == AiDraftApplyKind.NeedsConfirmation)
        {
            var ok = MessageBox.Show(
                result.Message + "\n\nCancelar = mantener el diálogo actual.\nSustituir = aplicar el texto IA.",
                "Sustituir diálogo",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!ok)
            {
                StatusMessage = "Sustitución de diálogo cancelada.";
                return;
            }
            result = AiDraftApplier.ApplyDialogue(ws, npc, text, replaceConfirmed: true, _resultBoundNpcId);
        }

        FinishApply(result, "Diálogo");
    }

    private void UseBubbleText()
    {
        if (LastGenerationResult?.Dialogue?.Dialogo?.Texto is not string text)
            return;

        AiDraftApplyResult result;
        if (_bubbleKind == AiBubbleKind.SimpleDialog)
        {
            UseDialogue();
            return;
        }

        if (_bubbleKind == AiBubbleKind.PlayerReply)
        {
            var response = _bubbleTargetResponse ?? _getSelectedResponse?.Invoke();
            result = AiDraftApplier.ApplyResponseText(response, text);
        }
        else
        {
            var question = _bubbleTargetQuestion ?? _getSelectedQuestion?.Invoke();
            result = AiDraftApplier.ApplyQuestionText(question, text);
        }

        FinishApply(result, "Texto");
    }

    private void CancelBubbleResult()
    {
        DialoguePreviewText = "";
        ValidationDebugText = "";
        ShowResultPreview = false;
        LastGenerationResult = null;
        StatusMessage = "Resultado IA descartado.";
        NotifyBubbleUi();
    }

    private async Task GenerateBubbleAsync(AiBubbleKind kind)
    {
        _bubbleKind = kind;
        _bubbleTargetQuestion = kind == AiBubbleKind.NpcQuestion ? _getSelectedQuestion?.Invoke() : null;
        _bubbleTargetResponse = kind == AiBubbleKind.PlayerReply ? _getSelectedResponse?.Invoke() : null;
        if (kind == AiBubbleKind.PlayerReply && _bubbleTargetQuestion is null)
        {
            var ws = _getWorkspace?.Invoke();
            if (ws is not null && _bubbleTargetResponse is not null)
                _bubbleTargetQuestion = AiDialogThreadContextBuilder.FindParentQuestion(ws, _bubbleTargetResponse);
        }

        BubbleAiPanelOpen = true;
        await PrepareAsync(AiCreativeAction.GenerarDialogo);
    }

    private void UseConversation()
    {
        if (LastGenerationResult?.Conversation?.Conversacion is not AiConversationResult conversation) return;
        AutoApplyConversation(conversation);
    }

    private void FinishApply(AiDraftApplyResult result, string label)
    {
        if (result.Kind == AiDraftApplyKind.Applied)
        {
            StatusMessage = result.Message;
            AiGenerationActivityLog.Info($"resultado aplicado: {label}");
            _refreshAfterApply?.Invoke();
            if (label == "Nombre" && _getSelectedNpc?.Invoke() is { } npc)
                CurrentNpcName = npc.Nombre;
            if (label == "Nombre")
                NameApplied?.Invoke();
            return;
        }

        StatusMessage = result.Message;
        AiGenerationActivityLog.Error(
            _lastAction,
            $"{label} no aplicado: {result.Kind}");
    }

    private async Task PrepareAsync(AiCreativeAction action, bool isRegenerate = false)
    {
        _lastAction = action;
        if (!isRegenerate && action != AiCreativeAction.GenerarDialogo)
            _bubbleKind = AiBubbleKind.None;

        // Capture NPC binding at request time (AI.5 — protect against mid-flight NPC switch).
        var boundNpcId = _selectedNpcId;
        if (isRegenerate)
            AiGenerationActivityLog.Info("regeneración iniciada");

        GenerationRequested?.Invoke(action);

        var trackProgress = action == AiCreativeAction.GenerarConversacion
                            && _generationService.IsConfigured;

        CancelPendingGeneration();
        _generationCts = new CancellationTokenSource();
        var ct = _generationCts.Token;

        if (trackProgress)
        {
            BeginConversationProgress();
            SetConversationProgress(10, "Preparando contexto...");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        var request = BuildRequest(action);
        var stub = AiCreativeServiceStub.Prepare(request);
        LastRequest = stub.Request;
        LastPromptPackage = stub.Package;
        RequestPreviewText = stub.Preview;
        ShowRequestPreview = true;

        if (trackProgress)
        {
            SetConversationProgress(25, "Preparando misión...");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        if (!isRegenerate)
        {
            LastGenerationResult = null;
            ClearPreviewCollections();
            DialoguePreviewText = "";
            ConversationNpcText = "";
            ConversationIncompleteText = "";
            ConversationCompleteText = "";
            ValidationDebugText = "";
            ShowResultPreview = false;
            _resultBoundNpcId = boundNpcId;
        }

        IsGenerating = true;
        StatusMessage = !_generationService.IsConfigured
            ? AiBackendGenerationService.NotConfiguredUserMessage
            : action == AiCreativeAction.GenerarConversacion
                ? "Generando conversación..."
                : isRegenerate ? "Regenerando..." : "Generando...";

        if (trackProgress)
        {
            SetConversationProgress(35, "Enviando a la IA...");
            StartConversationWaitPulse();
        }

        var previousValid = isRegenerate && LastGenerationResult is { IsValid: true };
        // Conversation auto-applies: canvas is source of truth; do not treat prior preview as salvageable.
        var preserveConversationCanvas = action == AiCreativeAction.GenerarConversacion;
        var conversationCompletedOk = false;

        try
        {
            var call = await _generationService
                .GenerateAsync(request, stub.Package, ct)
                .ConfigureAwait(true);

            StopConversationWaitPulse();

            if (ct.IsCancellationRequested)
            {
                StatusMessage = "Generación cancelada.";
                if (trackProgress)
                    await FailConversationProgressAsync(ct).ConfigureAwait(true);
                return;
            }

            // Drop late results if the user switched NPC while generating.
            if (boundNpcId is not null && _selectedNpcId != boundNpcId)
            {
                StatusMessage = "Resultado descartado: cambiaste de NPC durante la generación.";
                AiGenerationActivityLog.Info("resultado descartado por cambio de NPC");
                if (trackProgress)
                    await FailConversationProgressAsync(ct).ConfigureAwait(true);
                return;
            }

            if (call.OutboundRequest is not null)
            {
                RequestPreviewText = stub.Preview
                    + Environment.NewLine + Environment.NewLine
                    + "═══ REQUEST BACKEND (AI.4A) ═══"
                    + Environment.NewLine
                    + AiBackendRequestBuilder.Serialize(call.OutboundRequest);
            }

            if (!call.Success)
            {
                if (preserveConversationCanvas)
                {
                    StatusMessage = FormatUserError(call);
                    AiGenerationActivityLog.Info("conversación ERROR (canvas anterior conservado)");
                    if (trackProgress)
                        await FailConversationProgressAsync(ct).ConfigureAwait(true);
                    return;
                }

                if (previousValid)
                {
                    StatusMessage = "No se pudo regenerar.";
                    AiGenerationActivityLog.Info("regeneración ERROR (preview anterior preservado)");
                    ShowResultPreview = true;
                }
                else
                {
                    StatusMessage = FormatUserError(call);
                    if (call.ErrorCode is AiServiceCallResult.CodeInvalidAi3
                        or AiServiceCallResult.CodeCorruptJson
                        or AiServiceCallResult.CodeWrongAction
                        or "INVALID_AI_RESPONSE")
                    {
                        ValidationDebugText = call.ErrorMessage ?? AiBackendResponseParser.InvalidUserMessage;
                        ShowResultPreview = true;
                    }
                }
                return;
            }

            if (call.Generation is not null)
            {
                if (trackProgress)
                    SetConversationProgress(92, "Procesando respuesta...");

                PresentGenerationResult(call.Generation, boundNpcId);
                if (isRegenerate)
                    AiGenerationActivityLog.Info("regeneración OK");

                if (trackProgress)
                {
                    if (Math.Abs(GenerationProgress - 100) < 0.5)
                    {
                        conversationCompletedOk = true;
                        await CompleteConversationProgressAsync(ct).ConfigureAwait(true);
                    }
                    else
                    {
                        await FailConversationProgressAsync(ct).ConfigureAwait(true);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            StopConversationWaitPulse();
            StatusMessage = previousValid ? "No se pudo regenerar." : "Generación cancelada.";
            if (trackProgress)
                await FailConversationProgressAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            StopConversationWaitPulse();
            IsGenerating = false;
            if (!conversationCompletedOk && trackProgress && _conversationProgressActive
                && Math.Abs(GenerationProgress - 100) >= 0.5)
            {
                // Ensure bar does not linger if an early return skipped fail/complete.
                ResetConversationProgress();
            }

            if (_generationService is AiBackendGenerationService backend)
                backend.RefreshStatusIdle();
            OnPropertyChanged(nameof(ServiceStatusLabel));
            OnPropertyChanged(nameof(ExpanderStatusLabel));
        }
    }

    private void BeginConversationProgress()
    {
        _conversationProgressActive = true;
        IsGenerationProgressVisible = true;
        SetConversationProgress(0, "Inicio");
    }

    private void SetConversationProgress(double value, string text)
    {
        if (!_conversationProgressActive && value < 100)
            return;
        GenerationProgress = Math.Clamp(value, 0, 100);
        GenerationProgressText = text;
    }

    private void StartConversationWaitPulse()
    {
        StopConversationWaitPulse();
        _conversationWaitTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _conversationWaitTimer.Tick += ConversationWaitTimerOnTick;
        _conversationWaitTimer.Start();
    }

    private void StopConversationWaitPulse()
    {
        if (_conversationWaitTimer is null)
            return;
        _conversationWaitTimer.Stop();
        _conversationWaitTimer.Tick -= ConversationWaitTimerOnTick;
        _conversationWaitTimer = null;
    }

    private void ConversationWaitTimerOnTick(object? sender, EventArgs e)
    {
        if (!_conversationProgressActive || !_isGenerating)
            return;

        var p = GenerationProgress;
        if (p >= 90)
            return;

        double step = p switch
        {
            < 50 => 4.5,
            < 65 => 3.0,
            < 75 => 2.0,
            < 82 => 1.2,
            < 88 => 0.7,
            _ => 0.35
        };

        var next = Math.Min(90, p + step);
        SetConversationProgress(next, "Generando conversación...");
    }

    private async Task CompleteConversationProgressAsync(CancellationToken ct)
    {
        StopConversationWaitPulse();
        SetConversationProgress(100, "Conversación generada.");
        StatusMessage = "Conversación generada.";
        try
        {
            await Task.Delay(1500, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing/cancel: hide immediately.
        }

        ResetConversationProgress();
    }

    private async Task FailConversationProgressAsync(CancellationToken ct)
    {
        StopConversationWaitPulse();
        if (!_conversationProgressActive)
            return;

        // Never reach 100 % on error.
        if (GenerationProgress >= 100)
            GenerationProgress = 90;
        GenerationProgressText = "Error al generar la conversación.";
        IsGenerationProgressVisible = true;
        try
        {
            await Task.Delay(1200, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        ResetConversationProgress();
    }

    private void ResetConversationProgress()
    {
        StopConversationWaitPulse();
        _conversationProgressActive = false;
        IsGenerationProgressVisible = false;
        GenerationProgress = 0;
        GenerationProgressText = "";
    }

    private void LoadMockResult()
    {
        var action = LastRequest?.Action ?? _lastAction;
        var result = AiMockResponses.LoadValidated(action);
        PresentGenerationResult(result, _selectedNpcId);
    }

    private void ClearPreviewCollections()
    {
        NamePreviewItems.Clear();
        ConversationReplyItems.Clear();
    }

    private void NotifyBubbleUi()
    {
        OnPropertyChanged(nameof(HasBubblePreview));
        OnPropertyChanged(nameof(ShowBubbleAiResult));
        OnPropertyChanged(nameof(ShowInteractionsAiResult));
        OnPropertyChanged(nameof(BubbleInstructionPrompt));
        OnPropertyChanged(nameof(GenerateBubbleButtonLabel));
        CancelBubbleResultCommand.RaiseCanExecuteChanged();
        UseBubbleTextCommand.RaiseCanExecuteChanged();
    }

    private void WriteProfileToSelectedNpc()
    {
        if (_suppressProfileWrite)
            return;
        var npc = _getSelectedNpc?.Invoke();
        if (npc is null)
            return;
        npc.AiAttitudePreset = AttitudePreset ?? "";
        npc.AiCustomAttitude = CustomAttitude ?? "";
        npc.AiNarrativeContext = NarrativeContext ?? "";
        npc.AiLength = Length;
        _persistDrafts?.Invoke();
    }

    private void ReadProfileFromSelectedNpc()
    {
        var npc = _getSelectedNpc?.Invoke();
        _suppressProfileWrite = true;
        try
        {
            AttitudePreset = string.IsNullOrWhiteSpace(npc?.AiAttitudePreset)
                ? AiCreativePresets.Attitudes[0]
                : npc!.AiAttitudePreset;
            CustomAttitude = npc?.AiCustomAttitude ?? "";
            NarrativeContext = npc?.AiNarrativeContext ?? "";
            Length = npc?.AiLength ?? AiTextLength.Corta;
        }
        finally
        {
            _suppressProfileWrite = false;
        }
    }

    private static string FormatUserError(AiServiceCallResult call)
    {
        if (call.ErrorCode == AiServiceCallResult.CodeNotConfigured
            || call.ErrorCode == "AI_NOT_CONFIGURED")
            return call.ErrorMessage is { Length: > 0 } m && m.Contains("OPENAI", StringComparison.OrdinalIgnoreCase)
                ? "El backend IA está activo, pero falta OPENAI_API_KEY en el entorno del backend."
                : AiBackendGenerationService.NotConfiguredUserMessage;

        return call.ErrorCode switch
        {
            AiServiceCallResult.CodeUnauthorized or "UNAUTHORIZED" =>
                AiBackendGenerationService.UnauthorizedUserMessage,
            AiServiceCallResult.CodeUnavailable or AiServiceCallResult.CodeHttpError =>
                "Backend IA no disponible.",
            AiServiceCallResult.CodeTimeout =>
                AiBackendGenerationService.TimeoutUserMessage,
            "OPENAI_TIMEOUT" =>
                AiBackendGenerationService.OpenAiTimeoutUserMessage,
            "OPENAI_AUTH_ERROR" =>
                "OpenAI rechazó la autenticación en el backend.",
            "OPENAI_RATE_LIMIT" =>
                "OpenAI está limitando peticiones. Inténtalo de nuevo más tarde.",
            "OPENAI_CONNECTION_ERROR" =>
                "No se pudo conectar con OpenAI desde el backend.",
            "OPENAI_BAD_REQUEST" or "OPENAI_RESPONSE_INVALID" or "INVALID_AI_RESPONSE" =>
                AiBackendResponseParser.InvalidUserMessage,
            "OPENAI_CLIENT_ERROR" or "INTERNAL_ERROR" =>
                "Error interno del cliente OpenAI en el backend.",
            AiServiceCallResult.CodeCancelled =>
                AiBackendGenerationService.CancelledUserMessage,
            AiServiceCallResult.CodeInvalidAi3 or AiServiceCallResult.CodeCorruptJson
                or AiServiceCallResult.CodeWrongAction =>
                AiBackendResponseParser.InvalidUserMessage,
            "OPENAI_ERROR" =>
                call.ErrorMessage ?? "Error de OpenAI en el backend.",
            _ => call.ErrorMessage ?? "Error del servicio IA."
        };
    }
}

internal enum AiBubbleKind
{
    None,
    NpcQuestion,
    PlayerReply,
    SimpleDialog
}

public sealed class AiNamePreviewItemViewModel
{
    private readonly ContentAiAssistantViewModel _owner;

    public AiNamePreviewItemViewModel(ContentAiAssistantViewModel owner, string nombre, string motivo)
    {
        _owner = owner;
        Nombre = nombre;
        Motivo = motivo;
        UseCommand = new RelayCommand(() => _owner.UseName(Nombre), () => !_owner.IsGenerating);
    }

    public string Nombre { get; }
    public string Motivo { get; }
    public RelayCommand UseCommand { get; }

    public void RaiseUseCanExecuteChanged() => UseCommand.RaiseCanExecuteChanged();
}

public sealed class AiConversationReplyPreviewItemViewModel
{
    public AiConversationReplyPreviewItemViewModel(int index, string texto, string tono)
    {
        Index = index;
        Texto = texto;
        Tono = tono;
        Display = $"{index}. {texto}";
    }

    public int Index { get; }
    public string Texto { get; }
    public string Tono { get; }
    public string Display { get; }
}
