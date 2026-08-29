using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

/// <summary>CONT.6C — confirm + publish dialog_es via shared SFTP. Never writes BD.</summary>
public sealed class ContentDialogEsPublishViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace _workspace;
    private readonly Func<LangSftpSettings, string, ILangSftpPublishClient>? _sftpFactory;
    private string _summary = "Preparando…";
    private string _status = "";
    private bool _canPublish;
    private bool _isBusy;
    private DialogEsRemotePublishResult? _preview;

    public ContentDialogEsPublishViewModel(
        ContentDraftWorkspace workspace,
        Func<LangSftpSettings, string, ILangSftpPublishClient>? sftpFactory = null)
    {
        _workspace = workspace;
        _sftpFactory = sftpFactory;
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
        CanPublish = false;
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            {
                Summary = "Configura LANG / SFTP (misma configuración que Mapas).";
                Status = "⚠ No se puede calcular ID dialog_es";
                return;
            }

            var occupancy = await ReadOccupancyAsync().ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new DialogEsRemotePublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                ClientFactory = _sftpFactory,
            };

            var preview = await Task.Run(() => DialogEsRemotePublishService.PreparePreview(request))
                .ConfigureAwait(true);
            _preview = preview;
            if (!preview.Success || preview.Batch is null)
            {
                Summary = "No se puede publicar dialog_es.";
                Status = preview.Error ?? "Error desconocido.";
                CanPublish = false;
                return;
            }

            Summary = BuildSummary(preview);
            Status = "Listo. Confirma para escribir dialog_es en SFTP (sin BD).";
            CanPublish = true;
        }
        catch (Exception ex)
        {
            Summary = "Error preparando publicación dialog_es.";
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
            "¿PUBLICAR dialog_es en el servidor?\n\n" +
            "Se subirá dialog_es_N+1.swf y se actualizará solo el token dialog,es.\n" +
            "NO se escribirá BD ni Mapas.\n" +
            "El SWF anterior se preservará.",
            "Confirmar publicación dialog_es",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        CanPublish = false;
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            var occupancy = await ReadOccupancyAsync().ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new DialogEsRemotePublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                ClientFactory = _sftpFactory,
            };

            Status = "Publicando dialog_es…";
            var outcome = await Task.Run(() => DialogEsRemotePublishService.Publish(request))
                .ConfigureAwait(true);
            if (outcome.Success)
            {
                ContentDraftStore.Save();
                Status = "PUBLICACIÓN dialog_es = OK\n" + string.Join("\n", outcome.LogLines.TakeLast(10));
                MessageBox.Show(
                    $"dialog_es publicado.\n\n" +
                    $"Activo: {outcome.ActiveRemoteVersion}\n" +
                    $"SWF: {outcome.TargetSwfFileName}\n\n" +
                    "El NPC sigue pendiente de publicación BD.",
                    "CONT.6C",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RequestClose?.Invoke(true);
            }
            else
            {
                Status = "ERROR\n" + outcome.Error + "\n" + string.Join("\n", outcome.LogLines.TakeLast(12));
                CanPublish = _preview?.Success == true;
                MessageBox.Show(
                    outcome.Error ?? "Publicación dialog_es fallida.",
                    "CONT.6C",
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

    private static string BuildSummary(DialogEsRemotePublishResult preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PUBLICAR dialog_es (SFTP)");
        sb.AppendLine();
        sb.AppendLine($"dialog_es actual: {preview.SourceVersion}");
        sb.AppendLine($"dialog_es nuevo:  {preview.TargetVersion}");
        sb.AppendLine();
        if (preview.Batch is not null)
            sb.Append(preview.Batch.FormatPreview());
        sb.AppendLine();
        sb.AppendLine("Escrituras BD: 0");
        sb.AppendLine("Mapas: sin cambios");
        sb.AppendLine("Solo se modifica el token dialog,es en versions_es.txt");
        return sb.ToString();
    }

    private static async Task<DialogEsIdOccupancy> ReadOccupancyAsync()
    {
        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return new DialogEsIdOccupancy();

        try
        {
            var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            await using var store = new MysqlContentPublishStore(settings, password);
            var maxes = await store.ReadMaxIdsAsync().ConfigureAwait(true);
            return new DialogEsIdOccupancy
            {
                BdQuestionMax = maxes.NpcPreguntas,
                BdResponseMax = maxes.NpcRespuestas,
            };
        }
        catch
        {
            return new DialogEsIdOccupancy();
        }
    }
}
