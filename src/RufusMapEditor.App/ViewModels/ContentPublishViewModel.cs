using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

public sealed class ContentPublishViewModel : ViewModelBase
{
    private string _summary = "Preparando…";
    private string _status = "";
    private bool _canPublish;
    private bool _isBusy;
    private ContentPublishPlan? _plan;

    public ContentPublishViewModel()
    {
        PublishCommand = new RelayCommand(async () => await PublishAsync(), () => CanPublish && !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
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
                PublishCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand PublishCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Action<bool>? RequestClose { get; set; }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var store = CreateStore();
            if (store is null)
            {
                Summary = "Configura MySQL (Archivo → Configuración BD).";
                CanPublish = false;
                return;
            }

            try
            {
                var svc = new ContentPublishService(store);
                DialogEsSnapshot? remoteSnap = null;
                string? remoteStatus = null;
                try
                {
                    var sftp = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
                    var sftpPassword = LangSftpPasswordProtector.Unprotect(sftp.PasswordProtectedBase64);
                    var remote = await Task.Run(() => DialogEsSessionCache.Shared.GetOrFetch(
                        new DialogEsRemoteLoadRequest
                        {
                            Settings = sftp,
                            PlainPassword = sftpPassword,
                        },
                        forceRemote: true)).ConfigureAwait(true);
                    remoteStatus = remote.StatusLabel;
                    if (remote.Success)
                        remoteSnap = remote.Snapshot;
                    if (remote.RemoteWriteAttempts != 0)
                        throw new InvalidOperationException("Cliente SFTP escribió al leer dialog_es.");
                }
                catch (Exception ex)
                {
                    remoteStatus = DialogEsRemoteLoadResult.CannotCalculateMessage + "\n" + ex.Message;
                }

                var (plan, engines) = await svc.PreparePreviewAsync(
                    ContentDraftStore.Current,
                    dialogEsSnapshot: remoteSnap,
                    dialogEsStatusOverride: remoteStatus).ConfigureAwait(true);
                _plan = plan;
                Summary = BuildSummary(plan, engines);
                CanPublish = plan.IsValid;
                Status = plan.IsValid
                    ? "Listo. Confirma para escribir SOLO contenido nuevo en estaticos."
                    : "Publicación bloqueada:\n" + string.Join("\n", plan.Errors);
            }
            finally
            {
                if (store is IAsyncDisposable d)
                    await d.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Summary = "Error preparando publicación.";
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
        var confirm = MessageBox.Show(
            "¿PUBLICAR contenido nuevo en estaticos?\n\nNo se modificará SWF/SFTP/Mapas.\nSolo INSERT de borradores pendientes.",
            "Confirmar publicación BD",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        CanPublish = false;
        try
        {
            var store = CreateStore();
            if (store is null)
            {
                Status = "Sin conexión BD.";
                return;
            }

            try
            {
                var svc = new ContentPublishService(store);
                Status = "Publicando…";
                var outcome = await svc.PublishAsync(ContentDraftStore.Current).ConfigureAwait(true);
                if (outcome.Success)
                {
                    ContentDraftStore.Save();
                    Status = "PUBLICACIÓN BD = OK\n" + string.Join("\n", outcome.LogLines.TakeLast(8));
                    MessageBox.Show("Publicación BD completada.", "CONT.5", MessageBoxButton.OK, MessageBoxImage.Information);
                    RequestClose?.Invoke(true);
                }
                else
                {
                    Status = "ERROR\n" + outcome.Error + "\n" + string.Join("\n", outcome.LogLines.TakeLast(12));
                    CanPublish = _plan?.IsValid == true;
                }
            }
            finally
            {
                if (store is IAsyncDisposable d)
                    await d.DisposeAsync().ConfigureAwait(true);
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

    private static string BuildSummary(ContentPublishPlan plan, IReadOnlyList<ContentTableEngineInfo> engines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PUBLICAR CONTENIDO");
        sb.AppendLine();
        sb.AppendLine($"NPCs:        {plan.Npcs.Count}");
        sb.AppendLine($"Ubicaciones: {plan.Locations.Count}");
        sb.AppendLine($"Preguntas:   {plan.Questions.Count}");
        sb.AppendLine($"Respuestas:  {plan.LogicalResponseCount} lógicas / {plan.ResponseActionRowCount} filas acción");
        sb.AppendLine($"Misiones:    {plan.Missions.Count}");
        sb.AppendLine($"Etapas:      {plan.Stages.Count}");
        sb.AppendLine($"Objetivos:   {plan.Objectives.Count}");
        sb.AppendLine();
        sb.AppendLine("IDs asignados (MAX+1):");
        sb.AppendLine($"NPC        {plan.FormatIdRange(plan.ReservedNpcIds)}");
        sb.AppendLine($"Pregunta   {plan.FormatIdRange(plan.ReservedQuestionIds)}");
        sb.AppendLine($"Respuesta  {plan.FormatIdRange(plan.ReservedResponseIds)}");
        sb.AppendLine($"Quest      {plan.FormatIdRange(plan.ReservedQuestIds)}");
        sb.AppendLine($"Etapa      {plan.FormatIdRange(plan.ReservedStageIds)}");
        sb.AppendLine($"Objetivo   {plan.FormatIdRange(plan.ReservedObjectiveIds)}");
        sb.AppendLine();
        sb.AppendLine("Motores:");
        foreach (var e in engines.OrderBy(x => x.Table))
            sb.AppendLine($"  {e.Table}: {e.Engine}");
        sb.AppendLine();
        sb.AppendLine(plan.ConcurrencyMode == ContentPublishConcurrencyMode.Transaction
            ? "Modo: TRANSACCIÓN InnoDB"
            : "Modo: LOCK TABLES (MyISAM / mixto)");
        sb.AppendLine();
        sb.Append(plan.FormatDialogEsPreviewBlock());
        return sb.ToString();
    }

    private static IContentPublishStore? CreateStore()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlContentPublishStore(settings, password);
    }
}
