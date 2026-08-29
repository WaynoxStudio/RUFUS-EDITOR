using System.Collections.ObjectModel;
using System.Windows;
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
    private Func<NpcsModeloDraft?>? _getSelectedNpc;
    private Func<ContentDraftWorkspace>? _getWorkspace;
    private Action? _refreshAfterApply;

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
    private string _validationDebugText = "";
    private bool _isGenerating;

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
            async () => await PrepareAsync(AiCreativeAction.GenerarDialogo),
            () => !IsGenerating);
        GenerateConversationCommand = new RelayCommand(
            async () => await PrepareAsync(AiCreativeAction.GenerarConversacion),
            () => !IsGenerating);
        LoadMockResultCommand = new RelayCommand(LoadMockResult, () => !IsGenerating);
        UseDialogueCommand = new RelayCommand(UseDialogue, () => !IsGenerating && HasDialoguePreview && _getSelectedNpc?.Invoke() is not null);
        UseConversationCommand = new RelayCommand(UseConversation, () => !IsGenerating && HasConversationPreview && _getSelectedNpc?.Invoke() is not null);
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
        Action refreshAfterApply)
    {
        _getSelectedNpc = getSelectedNpc ?? throw new ArgumentNullException(nameof(getSelectedNpc));
        _getWorkspace = getWorkspace ?? throw new ArgumentNullException(nameof(getWorkspace));
        _refreshAfterApply = refreshAfterApply ?? throw new ArgumentNullException(nameof(refreshAfterApply));
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
    public RelayCommand LoadMockResultCommand { get; }
    public RelayCommand UseDialogueCommand { get; }
    public RelayCommand UseConversationCommand { get; }
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
                LoadMockResultCommand.RaiseCanExecuteChanged();
                RegenerateCommand.RaiseCanExecuteChanged();
                UseDialogueCommand.RaiseCanExecuteChanged();
                UseConversationCommand.RaiseCanExecuteChanged();
                foreach (var item in NamePreviewItems)
                    item.RaiseUseCanExecuteChanged();
                OnPropertyChanged(nameof(ServiceStatusLabel));
                OnPropertyChanged(nameof(ExpanderStatusLabel));
            }
        }
    }

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
                OnPropertyChanged(nameof(IsCustomAttitude));
        }
    }

    public string CustomAttitude
    {
        get => _customAttitude;
        set => SetProperty(ref _customAttitude, value ?? "");
    }

    public bool IsCustomAttitude =>
        string.Equals(AttitudePreset, AiCreativePresets.AttitudeCustomLabel, StringComparison.OrdinalIgnoreCase);

    public string NarrativeContext
    {
        get => _narrativeContext;
        set => SetProperty(ref _narrativeContext, value ?? "");
    }

    public string AdditionalInstruction
    {
        get => _additionalInstruction;
        set => SetProperty(ref _additionalInstruction, value ?? "");
    }

    public AiTextLength Length
    {
        get => _length;
        set => SetProperty(ref _length, value);
    }

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
                OnPropertyChanged(nameof(ShowIdentityAiResult));
                OnPropertyChanged(nameof(ShowInteractionsAiResult));
                RegenerateCommand.RaiseCanExecuteChanged();
                UseDialogueCommand.RaiseCanExecuteChanged();
                UseConversationCommand.RaiseCanExecuteChanged();
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

    public string ValidationDebugText
    {
        get => _validationDebugText;
        private set
        {
            if (SetProperty(ref _validationDebugText, value))
            {
                OnPropertyChanged(nameof(ShowIdentityAiResult));
                OnPropertyChanged(nameof(ShowInteractionsAiResult));
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

    public bool HasResultPreviewContent =>
        HasUsablePreview || !string.IsNullOrWhiteSpace(ValidationDebugText);

    /// <summary>ADMIN.UI.3.2 — name result / validation feedback stays in Identidad.</summary>
    public bool ShowIdentityAiResult =>
        HasNamePreview
        || (ShowResultPreview && _lastAction == AiCreativeAction.GenerarNombre && !string.IsNullOrWhiteSpace(ValidationDebugText));

    /// <summary>ADMIN.UI.3.2 — dialogue/conversation results stay in Interacciones.</summary>
    public bool ShowInteractionsAiResult =>
        HasDialoguePreview
        || HasConversationPreview
        || (ShowResultPreview
            && (_lastAction is AiCreativeAction.GenerarDialogo or AiCreativeAction.GenerarConversacion)
            && !string.IsNullOrWhiteSpace(ValidationDebugText));

    public void BindToNpc(int? npcId, string? npcName)
    {
        CancelPendingGeneration();
        _selectedNpcId = npcId;
        CurrentNpcName = npcName ?? "";
        OnPropertyChanged(nameof(ServiceStatusLabel));
        OnPropertyChanged(nameof(ExpanderStatusLabel));
        UseDialogueCommand.RaiseCanExecuteChanged();
        UseConversationCommand.RaiseCanExecuteChanged();
        foreach (var item in NamePreviewItems)
            item.RaiseUseCanExecuteChanged();
    }

    /// <summary>Backward-compatible name-only bind (AI.4).</summary>
    public void BindToNpc(string? npcName) => BindToNpc(_selectedNpcId, npcName);

    public void CancelPendingGeneration()
    {
        try { _generationCts?.Cancel(); }
        catch (ObjectDisposedException) { /* ignore */ }
    }

    public AiCreativeRequest BuildRequest(AiCreativeAction action)
    {
        var npcName = _getSelectedNpc?.Invoke()?.Nombre ?? CurrentNpcName;
        return AiCreativeRequestBuilder.Build(
            action,
            rolePreset: "",
            customRole: "",
            AttitudePreset,
            CustomAttitude,
            NarrativeContext,
            AdditionalInstruction,
            Length,
            npcName);
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
                var c = result.Conversation.Conversacion;
                ConversationNpcText = c.TextoNpc;
                var i = 1;
                foreach (var r in c.RespuestasJugador)
                {
                    ConversationReplyItems.Add(new AiConversationReplyPreviewItemViewModel(i, r.Texto, r.Tono));
                    i++;
                }
                break;
        }

        StatusMessage = "Preview · no aplicado al NPC hasta pulsar Usar";
        ShowResultPreview = true;
        RegenerateCommand.RaiseCanExecuteChanged();
        UseDialogueCommand.RaiseCanExecuteChanged();
        UseConversationCommand.RaiseCanExecuteChanged();
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

    private void UseConversation()
    {
        if (LastGenerationResult?.Conversation?.Conversacion is not AiConversationResult conversation) return;
        var npc = _getSelectedNpc?.Invoke();
        var ws = _getWorkspace?.Invoke();
        if (ws is null)
        {
            StatusMessage = "Sin workspace de contenido.";
            return;
        }

        var result = AiDraftApplier.ApplyConversation(
            ws, npc, conversation,
            replaceConfirmed: false,
            interactiveSwitchConfirmed: false,
            _resultBoundNpcId);

        if (result.Kind == AiDraftApplyKind.NeedsInteractiveSwitch)
        {
            var ok = MessageBox.Show(
                result.Message + "\n\nCancelar = no aplicar.\nOK = usar conversación interactiva.",
                "Modo interactivo",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!ok)
            {
                StatusMessage = "Aplicación de conversación cancelada.";
                return;
            }

            result = AiDraftApplier.ApplyConversation(
                ws, npc, conversation,
                replaceConfirmed: false,
                interactiveSwitchConfirmed: true,
                _resultBoundNpcId);
        }

        if (result.Kind == AiDraftApplyKind.NeedsConfirmation)
        {
            var ok = MessageBox.Show(
                result.Message + "\n\nCancelar = mantener textos actuales.\nOK = sustituir textos creativos.",
                "Sustituir textos",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!ok)
            {
                StatusMessage = "Sustitución de conversación cancelada.";
                return;
            }

            result = AiDraftApplier.ApplyConversation(
                ws, npc, conversation,
                replaceConfirmed: true,
                interactiveSwitchConfirmed: true,
                _resultBoundNpcId);
        }

        FinishApply(result, "Conversación");
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
        // Capture NPC binding at request time (AI.5 — protect against mid-flight NPC switch).
        var boundNpcId = _selectedNpcId;
        if (isRegenerate)
            AiGenerationActivityLog.Info("regeneración iniciada");

        GenerationRequested?.Invoke(action);

        var request = BuildRequest(action);
        var stub = AiCreativeServiceStub.Prepare(request);
        LastRequest = stub.Request;
        LastPromptPackage = stub.Package;
        RequestPreviewText = stub.Preview;
        ShowRequestPreview = true;

        if (!isRegenerate)
        {
            LastGenerationResult = null;
            ClearPreviewCollections();
            DialoguePreviewText = "";
            ConversationNpcText = "";
            ValidationDebugText = "";
            ShowResultPreview = false;
            _resultBoundNpcId = boundNpcId;
        }

        CancelPendingGeneration();
        _generationCts = new CancellationTokenSource();
        var ct = _generationCts.Token;

        IsGenerating = true;
        StatusMessage = !_generationService.IsConfigured
            ? AiBackendGenerationService.NotConfiguredUserMessage
            : isRegenerate ? "Regenerando..." : "Generando...";

        var previousValid = isRegenerate && LastGenerationResult is { IsValid: true };

        try
        {
            var call = await _generationService
                .GenerateAsync(request, stub.Package, ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested)
            {
                StatusMessage = "Generación cancelada.";
                return;
            }

            // Drop late results if the user switched NPC while generating.
            if (boundNpcId is not null && _selectedNpcId != boundNpcId)
            {
                StatusMessage = "Resultado descartado: cambiaste de NPC durante la generación.";
                AiGenerationActivityLog.Info("resultado descartado por cambio de NPC");
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
                PresentGenerationResult(call.Generation, boundNpcId);
                if (isRegenerate)
                    AiGenerationActivityLog.Info("regeneración OK");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = previousValid ? "No se pudo regenerar." : "Generación cancelada.";
        }
        finally
        {
            IsGenerating = false;
            if (_generationService is AiBackendGenerationService backend)
                backend.RefreshStatusIdle();
            OnPropertyChanged(nameof(ServiceStatusLabel));
            OnPropertyChanged(nameof(ExpanderStatusLabel));
        }
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
            AiServiceCallResult.CodeTimeout or "OPENAI_TIMEOUT" =>
                "Timeout del backend IA.",
            AiServiceCallResult.CodeCancelled =>
                "Generación cancelada.",
            AiServiceCallResult.CodeInvalidAi3 or AiServiceCallResult.CodeCorruptJson
                or AiServiceCallResult.CodeWrongAction or "INVALID_AI_RESPONSE" =>
                AiBackendResponseParser.InvalidUserMessage,
            "OPENAI_ERROR" =>
                call.ErrorMessage ?? "Error de OpenAI en el backend.",
            _ => call.ErrorMessage ?? "Error del servicio IA."
        };
    }
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
