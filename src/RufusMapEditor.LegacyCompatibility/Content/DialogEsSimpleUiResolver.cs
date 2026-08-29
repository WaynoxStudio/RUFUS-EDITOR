using System.Globalization;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.6B.1 — provisional D.q for Simple dialog UI. Never writes BD/SFTP.</summary>
public sealed class DialogEsSimpleUiState
{
    public required bool IsPending { get; init; }
    public int? ProvisionalDqId { get; init; }
    public int? ActiveVersion { get; init; }
    public int? TargetVersion { get; init; }
    public string? CacheStatus { get; init; }
    public bool Loading { get; init; }
    public bool CannotCalculate { get; init; }

    public string BannerTitle => "⚠ Pendiente de publicación dialog_es";

    public string FormatDetails()
    {
        if (!IsPending)
            return "";
        if (Loading)
            return "Cargando dialog_es activo…";
        if (CannotCalculate || ProvisionalDqId is null)
        {
            var sb = new StringBuilder();
            sb.Append(DialogEsRemoteLoadResult.CannotCalculateMessage);
            if (!string.IsNullOrWhiteSpace(CacheStatus))
            {
                sb.AppendLine();
                sb.Append(CacheStatus);
            }
            return sb.ToString();
        }

        var id = ProvisionalDqId.Value.ToString(CultureInfo.InvariantCulture);
        var n = ActiveVersion?.ToString(CultureInfo.InvariantCulture) ?? "—";
        var n1 = TargetVersion?.ToString(CultureInfo.InvariantCulture) ?? "—";
        var sbOk = new StringBuilder();
        sbOk.AppendLine($"ID D.q provisional: {id}");
        sbOk.AppendLine($"dialog_es activo: {n}");
        sbOk.Append($"Versión local prevista: {n1}");
        return sbOk.ToString();
    }
}

public static class DialogEsSimpleUiResolver
{
    public static DialogEsSnapshot? TryLoadSnapshot(string? cacheDirectory, out string status)
    {
        var dir = cacheDirectory ?? LangRemoteSyncService.DefaultCacheDirectory;
        if (!DialogEsLocalCache.TryLoadLatest(dir, out var bytes, out var path, out var err))
        {
            status = err ?? "Sin caché local de dialog_es.";
            return null;
        }

        try
        {
            var snap = DialogEsParser.Parse(bytes);
            status = Path.GetFileName(path) ?? "dialog_es";
            return snap;
        }
        catch (Exception ex)
        {
            status = "Caché dialog_es ilegible: " + ex.Message;
            return null;
        }
    }

    public static DialogEsSimpleUiState ForNpc(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc,
        DialogEsSnapshot? snapshot,
        string? cacheStatus = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var active = snapshot?.Version;
        var target = snapshot is null ? (int?)null : snapshot.Version + 1;
        if (npc.DialogMode != NpcDialogMode.Simple || npc.PublishedBd || !npc.IsPendingDialogEs)
        {
            return new DialogEsSimpleUiState
            {
                IsPending = false,
                ActiveVersion = active,
                TargetVersion = target,
                CacheStatus = cacheStatus,
            };
        }

        int? dq = null;
        var cannot = snapshot is null;
        if (snapshot is not null)
        {
            var occupancy = new DialogEsIdOccupancy
            {
                BdQuestionMax = workspace.Dialogs.DbMaxQuestionId,
            };
            var resolver = new DialogEsIdResolver(snapshot, occupancy);
            var npcs = workspace.Npcs.Drafts.Where(n => !n.PublishedBd).ToList();
            var interactiveIds = npcs
                .Where(n => n.DialogMode == NpcDialogMode.Interactive)
                .Select(n => n.Id)
                .ToHashSet();
            foreach (var q in workspace.Dialogs.Questions.Where(q =>
                         !q.PublishedBd && interactiveIds.Contains(q.OwnerNpcId)))
                resolver.ReserveInteractiveQuestion();

            foreach (var n in npcs)
            {
                if (n.DialogMode != NpcDialogMode.Simple || !n.IsPendingDialogEs)
                    continue;
                var id = resolver.ReserveSimpleQuestion();
                if (n.Id == npc.Id)
                    dq = id;
            }
        }

        return new DialogEsSimpleUiState
        {
            IsPending = true,
            ProvisionalDqId = dq,
            ActiveVersion = active,
            TargetVersion = target,
            CacheStatus = cacheStatus,
            CannotCalculate = cannot,
        };
    }
}
