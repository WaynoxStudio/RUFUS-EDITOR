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
    private string _summary = "Preparando…";
    private string _status = "";
    private bool _canPublish;
    private bool _isBusy;
    private ContentClientPublishResult? _preview;

    public ContentClientPublishViewModel(
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
                Status = "⚠ No se puede publicar cliente";
                return;
            }

            var occupancy = await ReadOccupancyAsync().ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new ContentClientPublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                ClientFactory = _sftpFactory,
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

            if (preview.AlreadyPublished)
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
        var confirm = MessageBox.Show(
            "¿PUBLICAR CLIENTE en el servidor?\n\n" +
            "Se subirán dialog_es y/o npc_es necesarios y se actualizará versions_es UNA sola vez.\n" +
            "NO se escribirá BD ni Mapas ni quests_es.\n" +
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
            var occupancy = await ReadOccupancyAsync().ConfigureAwait(true);
            var password = LangSftpPasswordProtector.Unprotect(settings.PasswordProtectedBase64);
            var request = new ContentClientPublishRequest
            {
                Settings = settings,
                PlainPassword = password,
                Workspace = _workspace,
                Occupancy = occupancy,
                ClientFactory = _sftpFactory,
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
