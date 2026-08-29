using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.App.ViewModels;

/// <summary>CONT.7B — confirm + publish npc_es via shared SFTP. Never writes BD.</summary>
public sealed class ContentNpcEsPublishViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace _workspace;
    private readonly Func<LangSftpSettings, string, ILangSftpPublishClient>? _sftpFactory;
    private string _summary = "Preparando…";
    private string _status = "";
    private bool _canPublish;
    private bool _isBusy;
    private NpcEsRemotePublishResult? _preview;

    public ContentNpcEsPublishViewModel(
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
                Status = "⚠ No se puede publicar npc_es";
                return;
            }

            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new NpcEsRemotePublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                ClientFactory = _sftpFactory,
            };

            var preview = await Task.Run(() => NpcEsRemotePublishService.PreparePreview(request))
                .ConfigureAwait(true);
            _preview = preview;
            if (!preview.Success || preview.Batch is null)
            {
                Summary = "No se puede publicar npc_es.";
                Status = preview.Error ?? "Error desconocido.";
                CanPublish = false;
                return;
            }

            Summary = BuildSummary(preview);
            if (preview.SourceVersion is int n)
                NpcEsSessionHint.SetActiveVersion(n);
            Status = preview.Batch.NewCount == 0
                ? "Todos ya están en npc_es con el mismo nombre. Confirma para marcar el estado local."
                : "Listo. Confirma para escribir npc_es en SFTP (sin BD).";
            CanPublish = true;
        }
        catch (Exception ex)
        {
            Summary = "Error preparando publicación npc_es.";
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
            "¿PUBLICAR npc_es en el servidor?\n\n" +
            "Se subirá npc_es_N+1.swf (si hay nombres nuevos) y se actualizará solo el token npc,es.\n" +
            "NO se escribirá BD ni dialog_es ni Mapas.\n" +
            "El SWF anterior se preservará.",
            "Confirmar publicación npc_es",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        CanPublish = false;
        try
        {
            var settings = AppSettingsStore.Load().LangSftp ?? new LangSftpSettings();
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new NpcEsRemotePublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                ClientFactory = _sftpFactory,
            };

            Status = "Publicando npc_es…";
            var outcome = await Task.Run(() => NpcEsRemotePublishService.Publish(request))
                .ConfigureAwait(true);
            if (outcome.Success)
            {
                if (outcome.ActiveRemoteVersion is int av)
                    NpcEsSessionHint.SetActiveVersion(av);
                ContentDraftStore.Save();
                Status = "PUBLICACIÓN npc_es = OK\n" + string.Join("\n", outcome.LogLines.TakeLast(10));
                MessageBox.Show(
                    $"npc_es {(outcome.VersionsUpdated ? "publicado" : "actualizado")}.\n\n" +
                    $"Activo: {outcome.ActiveRemoteVersion}\n" +
                    $"SWF: {outcome.TargetSwfFileName ?? outcome.SourceSwfFileName}\n\n" +
                    "Escrituras BD: 0",
                    "CONT.7B",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RequestClose?.Invoke(true);
            }
            else
            {
                Status = "ERROR\n" + outcome.Error + "\n" + string.Join("\n", outcome.LogLines.TakeLast(12));
                CanPublish = _preview?.Success == true;
                MessageBox.Show(
                    outcome.Error ?? "Publicación npc_es fallida.",
                    "CONT.7B",
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

    private static string BuildSummary(NpcEsRemotePublishResult preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PUBLICAR npc_es (SFTP)");
        sb.AppendLine();
        if (preview.Batch is not null)
            sb.Append(preview.Batch.FormatPreview());
        sb.AppendLine();
        sb.AppendLine("Escrituras BD: 0");
        sb.AppendLine("dialog_es: sin cambios");
        sb.AppendLine("quests_es: sin cambios");
        sb.AppendLine("Mapas: sin cambios");
        sb.AppendLine("Solo se modifica el token npc,es en versions_es.txt");
        return sb.ToString();
    }
}
