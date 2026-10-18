using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

/// <summary>CONT.8 — unified dialog_es + npc_es SFTP publish. Never writes BD.</summary>
public sealed class ContentClientPublishViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace _workspace;
    private readonly Func<LangSftpSettings, string, ILangSftpPublishClient>? _sftpFactory;
    private readonly Func<int, (int X, int Y)?>? _mapWorldCoordsLookup;
    private readonly Func<int, int?>? _bdNpcMapIdLookup;
    private string _summary = "Preparando…";
    private string _status = "";
    private bool _canPublish;
    private bool _isBusy;
    private bool _hasNpcChoices;
    private ContentClientPublishResult? _preview;
    private bool _suppressSelectionRebuild;

    public ContentClientPublishViewModel(
        ContentDraftWorkspace workspace,
        Func<LangSftpSettings, string, ILangSftpPublishClient>? sftpFactory = null,
        Func<int, (int X, int Y)?>? mapWorldCoordsLookup = null,
        Func<int, int?>? bdNpcMapIdLookup = null)
    {
        _workspace = workspace;
        _sftpFactory = sftpFactory;
        _mapWorldCoordsLookup = mapWorldCoordsLookup;
        _bdNpcMapIdLookup = bdNpcMapIdLookup;
        NpcChoices = new ObservableCollection<ContentPublishNpcChoiceItem>();
        PublishCommand = new RelayCommand(async () => await PublishAsync(), () => CanPublish && !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => HasNpcChoices && !IsBusy);
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => HasNpcChoices && !IsBusy);
    }

    public ObservableCollection<ContentPublishNpcChoiceItem> NpcChoices { get; }

    public bool HasNpcChoices
    {
        get => _hasNpcChoices;
        private set
        {
            if (SetProperty(ref _hasNpcChoices, value))
            {
                SelectAllCommand.RaiseCanExecuteChanged();
                SelectNoneCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool CanPublish
    {
        get => _canPublish;
        private set
        {
            if (SetProperty(ref _canPublish, value))
                PublishCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                PublishCommand.RaiseCanExecuteChanged();
                SelectAllCommand.RaiseCanExecuteChanged();
                SelectNoneCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand PublishCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public Action<bool>? RequestClose { get; set; }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            LoadNpcChoices();
            await RebuildPreviewAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadNpcChoices()
    {
        _suppressSelectionRebuild = true;
        try
        {
            NpcChoices.Clear();
            foreach (var c in ContentPublishNpcSelection.ListClientCandidates(_workspace))
                NpcChoices.Add(new ContentPublishNpcChoiceItem(c, isSelected: true, OnNpcSelectionChanged));
            HasNpcChoices = NpcChoices.Count > 0;
        }
        finally
        {
            _suppressSelectionRebuild = false;
        }
    }

    private void OnNpcSelectionChanged()
    {
        if (_suppressSelectionRebuild || IsBusy)
            return;
        _ = RebuildPreviewAsync();
    }

    private void SetAllSelected(bool selected)
    {
        _suppressSelectionRebuild = true;
        try
        {
            foreach (var c in NpcChoices)
                c.IsSelected = selected;
        }
        finally
        {
            _suppressSelectionRebuild = false;
        }

        _ = RebuildPreviewAsync();
    }

    private HashSet<int>? CurrentFilter()
    {
        if (NpcChoices.Count == 0)
            return null;
        return NpcChoices.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet();
    }

    private async Task RebuildPreviewAsync()
    {
        IsBusy = true;
        CanPublish = false;
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            {
                Summary = "Configura LANG / SFTP (misma configuración que Mapas).";
                Status = "⚠ No se puede publicar cliente";
                return;
            }

            var filter = CurrentFilter();
            if (NpcChoices.Count > 0 && (filter is null || filter.Count == 0))
            {
                _preview = null;
                Summary = "Selecciona al menos un NPC para publicar en cliente.";
                Status = "Ningún NPC seleccionado.";
                return;
            }

            var (occupancy, bdPlan, bdMax) = await ReadOccupancyAndBdPlanAsync(_workspace, filter)
                .ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new ContentClientPublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                BdPublishPlan = bdPlan,
                BdMaxSnapshot = bdMax,
                ClientFactory = _sftpFactory,
                MapWorldCoordsLookup = _mapWorldCoordsLookup,
                BdNpcMapIdLookup = _bdNpcMapIdLookup,
                NpcIdFilter = filter,
            };

            var preview = await Task.Run(() => ContentClientRemotePublishService.PreparePreview(request))
                .ConfigureAwait(true);
            _preview = preview;
            if (!preview.Success)
            {
                Summary = "No se puede publicar cliente.";
                Status = preview.Error ?? "Error desconocido.";
                CanPublish = false;
                return;
            }

            Summary = preview.FormatPreview();
            if (preview.NpcSourceVersion is int n)
                NpcEsSessionHint.SetActiveVersion(n);

            ContentDraftStore.Save();

            if (preview.QuestsPreviewPendingBd)
            {
                Status =
                    "Vista previa completa disponible. Publica primero el contenido en BD para confirmar los IDs de misión.";
                CanPublish = false;
            }
            else if (preview.AlreadyPublished)
            {
                Status = "✓ Cliente ya publicado. Confirma para sincronizar estado local si aplica.";
                CanPublish = true;
            }
            else
            {
                Status = "Listo. Confirma para escribir SWF cliente en SFTP (sin BD).";
                CanPublish = true;
            }
        }
        catch (Exception ex)
        {
            Summary = "Error preparando publicación cliente.";
            Status = ex.Message;
            CanPublish = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PublishAsync()
    {
        if (!CanPublish) return;
        var filter = CurrentFilter();
        if (NpcChoices.Count > 0 && (filter is null || filter.Count == 0))
        {
            Status = "Selecciona al menos un NPC.";
            return;
        }

        if (QuestsEsPublishBatchBuilder.HasUnpublishedBdMissions(_workspace))
        {
            Status =
                "Vista previa completa disponible. Publica primero el contenido en BD para confirmar los IDs de misión.";
            CanPublish = false;
            return;
        }

        var confirm = MessageBox.Show(
            "¿PUBLICAR CLIENTE en el servidor?\n\n" +
            $"NPCs seleccionados: {(filter?.Count ?? 0)}\n" +
            "Se subirán dialog_es y/o npc_es necesarios y se actualizará versions_es UNA sola vez.\n" +
            "NO se escribirá BD ni Mapas.\n" +
            "Con diálogo simple: solo dialog_es / npc_es (nunca quests_es).\n" +
            "quests_es solo con misiones nuevas pendientes y sin diálogo simple pendiente.\n" +
            "Los SWF anteriores se preservarán.",
            "Confirmar publicación cliente",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        CanPublish = false;
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            var (occupancy, bdPlan, bdMax) = await ReadOccupancyAndBdPlanAsync(_workspace, filter)
                .ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new ContentClientPublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                BdPublishPlan = bdPlan,
                BdMaxSnapshot = bdMax,
                ClientFactory = _sftpFactory,
                MapWorldCoordsLookup = _mapWorldCoordsLookup,
                BdNpcMapIdLookup = _bdNpcMapIdLookup,
                NpcIdFilter = filter,
            };

            Status = "Publicando cliente…";
            var outcome = await Task.Run(() => ContentClientRemotePublishService.Publish(request))
                .ConfigureAwait(true);
            if (outcome.Success)
            {
                if (outcome.NpcTargetVersion is int nv)
                    NpcEsSessionHint.SetActiveVersion(nv);
                ContentDraftStore.Save();
                Status = "PUBLICACIÓN CLIENTE = OK\n" + string.Join("\n", outcome.LogLines.TakeLast(12));
                var sb = new StringBuilder();
                sb.AppendLine(outcome.AlreadyPublished ? "Cliente ya estaba publicado." : "Cliente publicado.");
                sb.AppendLine();
                if (outcome.DialogChanged)
                    sb.AppendLine($"dialog_es → {outcome.DialogTargetVersion}");
                else
                    sb.AppendLine("dialog_es: sin cambios");
                if (outcome.NpcChanged)
                    sb.AppendLine($"npc_es → {outcome.NpcTargetVersion}");
                else
                    sb.AppendLine("npc_es: sin cambios");
                if (outcome.QuestsChanged)
                    sb.AppendLine($"quests_es → {outcome.QuestsTargetVersion}");
                else
                    sb.AppendLine("quests_es: sin cambios");
                sb.AppendLine();
                sb.AppendLine("Escrituras BD: 0");
                MessageBox.Show(sb.ToString(), "CONT.8", MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke(true);
            }
            else
            {
                Status = "ERROR\n" + outcome.Error + "\n" + string.Join("\n", outcome.LogLines.TakeLast(12));
                CanPublish = _preview?.Success == true;
                MessageBox.Show(
                    outcome.Error ?? "Publicación cliente fallida.",
                    "CONT.8",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Status = "ERROR: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<(DialogEsIdOccupancy Occupancy, ContentPublishPlan? BdPlan, ContentPublishMaxSnapshot? BdMax)>
        ReadOccupancyAndBdPlanAsync(ContentDraftWorkspace workspace, IReadOnlySet<int>? npcIdFilter)
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return (new DialogEsIdOccupancy(), null, null);

        try
        {
            var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            await using var store = new MysqlContentPublishStore(settings, password);
            var maxes = await store.ReadMaxIdsAsync().ConfigureAwait(true);
            var occupancy = new DialogEsIdOccupancy
            {
                BdQuestionMax = maxes.NpcPreguntas,
                BdResponseMax = maxes.NpcRespuestas,
            };
            QuestsEsLocalCache.TryParseLatest(
                LangRemoteSyncService.DefaultCacheDirectory,
                out var questsEs,
                out _,
                out _);
            var plan = ContentPublishPlanBuilder.Build(
                workspace,
                maxes,
                dialogEs: null,
                dialogEsOccupancy: occupancy,
                questsEs: questsEs,
                npcIdFilter: npcIdFilter);
            return (occupancy, plan, maxes);
        }
        catch
        {
            return (new DialogEsIdOccupancy(), null, null);
        }
    }
}
