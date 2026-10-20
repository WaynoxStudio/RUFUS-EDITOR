using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class ContentClientPublishRequest
{
    public required LangSftpSettings Settings { get; init; }
    public required string PlainPassword { get; init; }
    public required ContentDraftWorkspace Workspace { get; init; }
    public DialogEsIdOccupancy Occupancy { get; init; } = new();
    /// <summary>Same plan as Publicar BD (MAX+1). Required for quests_es pre-BD preview.</summary>
    public ContentPublishPlan? BdPublishPlan { get; init; }
    /// <summary>Raw BD MAX (before quests_es merge). Preferred when rebuilding plan against live SWF.</summary>
    public ContentPublishMaxSnapshot? BdMaxSnapshot { get; init; }
    public Func<LangSftpSettings, string, ILangSftpPublishClient>? ClientFactory { get; init; }
    public string? WorkDirectory { get; init; }

    /// <summary>mapId → mapas.X/Y for auto-return coords / Q.o[4663] repair.</summary>
    public Func<int, (int X, int Y)?>? MapWorldCoordsLookup { get; init; }

    /// <summary>npcId → npcs_ubicacion.mapa (BD).</summary>
    public Func<int, int?>? BdNpcMapIdLookup { get; init; }

    /// <summary>When set, only these NPC draft IDs enter dialog_es / npc_es (and related quests).</summary>
    public IReadOnlySet<int>? NpcIdFilter { get; init; }
}

public sealed class ContentClientPublishResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string StatusLabel { get; init; } = "";
    public bool AlreadyPublished { get; init; }

    public bool DialogChanged { get; init; }
    public bool NpcChanged { get; init; }
    public bool QuestsChanged { get; init; }
    /// <summary>quests_es IDs from BD plan; remote write blocked until BD publish.</summary>
    public bool QuestsPreviewPendingBd { get; init; }

    public int? DialogSourceVersion { get; init; }
    public int? DialogTargetVersion { get; init; }
    public int? NpcSourceVersion { get; init; }
    public int? NpcTargetVersion { get; init; }
    public int? QuestsSourceVersion { get; init; }
    public int? QuestsTargetVersion { get; init; }

    public bool VersionsUpdated { get; init; }
    public int AtomicVersionsReplaceCount { get; init; }
    public int DeleteAttemptCount { get; init; }

    public DialogEsPublishBatch? DialogBatch { get; init; }
    public NpcEsPublishBatch? NpcBatch { get; init; }
    public QuestsEsPublishBatch? QuestsBatch { get; init; }
    public IReadOnlyList<string> LogLines { get; init; } = Array.Empty<string>();

    public string FormatPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CONTENIDO → PUBLICAR CLIENTE");
        sb.AppendLine();

        if (AlreadyPublished)
        {
            sb.AppendLine("✓ Cliente ya publicado");
            sb.AppendLine();
            sb.AppendLine("dialog_es: SIN CAMBIOS");
            sb.AppendLine("npc_es: SIN CAMBIOS");
            sb.AppendLine("quests_es: SIN CAMBIOS");
            sb.AppendLine();
            sb.AppendLine("Escrituras BD: 0");
            return sb.ToString();
        }

        var npcs = NpcBatch?.Bindings
            .Select(b => (b.NpcId, b.Name))
            .Distinct()
            .ToList() ?? new List<(int, string)>();
        if (npcs.Count == 0 && DialogBatch is not null)
        {
            foreach (var id in DialogBatch.Bindings
                         .Where(b => b.OwnerNpcDraftId is int)
                         .Select(b => b.OwnerNpcDraftId!.Value)
                         .Distinct())
            {
                npcs.Add((id, "(ver lote)"));
            }
        }

        foreach (var (id, name) in npcs.Take(8))
        {
            sb.AppendLine("NPC");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"ID: {id}"));
            sb.AppendLine($"Nombre: {name}");
            sb.AppendLine();
        }

        sb.AppendLine("--------------------------------");
        sb.AppendLine("DIALOG_ES");
        sb.AppendLine("--------------------------------");
        if (!DialogChanged || DialogBatch is null)
        {
            sb.AppendLine("SIN CAMBIOS");
            if (DialogSourceVersion is int d)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Actual: {d}"));
        }
        else
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Actual: {DialogSourceVersion}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Nuevo: {DialogTargetVersion}"));
            sb.AppendLine();
            foreach (var b in DialogBatch.Bindings)
            {
                var space = b.Assignment.Space == DialogEsSpace.Question ? "D.q" : "D.a";
                var suffix = b.Assignment.ReplaceExisting ? " (reemplazo)" : "";
                sb.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{space}[{b.Assignment.Id}] = \"{Truncate(b.Assignment.Text, 48)}\"{suffix}"));
            }
        }

        sb.AppendLine();
        sb.AppendLine("--------------------------------");
        sb.AppendLine("NPC_ES");
        sb.AppendLine("--------------------------------");
        if (!NpcChanged || NpcBatch is null)
        {
            sb.AppendLine("SIN CAMBIOS");
            if (NpcSourceVersion is int n)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Actual: {n}"));
        }
        else
        {
            sb.Append(NpcBatch.FormatPreview());
        }

        sb.AppendLine();
        sb.AppendLine("--------------------------------");
        sb.AppendLine("QUESTS_ES");
        sb.AppendLine("--------------------------------");
        if (QuestsBatch is { Bindings.Count: > 0 } || QuestsPreviewPendingBd)
        {
            if (QuestsBatch is not null)
                sb.Append(QuestsBatch.FormatPreview());
            else
                sb.AppendLine("PREVISIÓN no disponible (sin lote).");
        }
        else if (!QuestsChanged)
        {
            sb.AppendLine("SIN CAMBIOS");
            if (QuestsSourceVersion is int q)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Actual: {q}"));
        }
        else
        {
            sb.Append(QuestsBatch!.FormatPreview());
        }

        sb.AppendLine();
        sb.AppendLine("versions_es:");
        if (DialogChanged)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"dialog,es,{DialogSourceVersion} → {DialogTargetVersion}"));
        else
            sb.AppendLine("dialog,es: sin cambios");
        if (NpcChanged)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"npc,es,{NpcSourceVersion} → {NpcTargetVersion}"));
        else
            sb.AppendLine("npc,es: sin cambios");
        if (QuestsChanged || QuestsPreviewPendingBd)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"quests,es,{QuestsSourceVersion} → {QuestsTargetVersion}"));
        else
            sb.AppendLine("quests,es: sin cambios");
        sb.AppendLine();
        sb.AppendLine("Escrituras BD: 0");
        sb.AppendLine("Mapas: sin cambios");
        return sb.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>
/// CONT.8 — unified client publish (dialog_es + npc_es + quests_es, one versions_es write).
/// Never writes BD / Mapas.
/// </summary>
public static class ContentClientRemotePublishService
{
    public static string DefaultWorkDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish");

    public static bool HasPendingDialogEs(
        ContentDraftWorkspace workspace,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        foreach (var n in workspace.Npcs.Drafts)
        {
            if (!ContentPublishNpcSelection.Includes(n.Id, npcIdFilter))
                continue;
            // Simple: local text without dialog_es yet (pre-BD) or BD pregunta assigned but SWF pending/repair.
            if (n.DialogMode == NpcDialogMode.Simple)
            {
                if (n.IsPendingDialogEs)
                    return true;
                if (n.Pregunta > 0 && !string.IsNullOrWhiteSpace(n.SimpleDialogTextLocal))
                    return true;
            }

            if (n.DialogMode == NpcDialogMode.Interactive
                && workspace.Dialogs.QuestionsForNpc(n.Id).Any(q =>
                    !string.IsNullOrWhiteSpace(q.TextLocal)))
                return true;
        }

        return false;
    }

    public static bool HasPendingNpcEs(
        ContentDraftWorkspace workspace,
        IReadOnlySet<int>? npcIdFilter = null) =>
        workspace.Npcs.Drafts.Any(n =>
            ContentPublishNpcSelection.Includes(n.Id, npcIdFilter)
            && n.IsPendingNpcEsFor(workspace));

    /// <summary>
    /// True when CONT.8 should write quests_es.
    /// Never when there is pending <b>diálogo simple</b> work — that path is dialog_es (+ npc_es) only.
    /// Otherwise only first-write missions (<see cref="QuestsEsPublishBatchBuilder.IsPendingQuestsEs"/>).
    /// </summary>
    public static bool HasPendingQuestsEs(
        ContentDraftWorkspace workspace,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (HasPendingSimpleDialogEs(workspace, npcIdFilter))
            return false;
        return workspace.Missions.Missions.Any(m =>
            QuestsEsPublishBatchBuilder.IsPendingQuestsEs(m)
            && (npcIdFilter is null
                || (m.StartNpcId is int sid
                    && ContentPublishNpcSelection.Includes(sid, npcIdFilter))));
    }

    /// <summary>Any simple-mode NPC still needing dialog_es (new text / provisional D.q).</summary>
    public static bool HasPendingSimpleDialogEs(
        ContentDraftWorkspace workspace,
        IReadOnlySet<int>? npcIdFilter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        foreach (var n in workspace.Npcs.Drafts)
        {
            if (!ContentPublishNpcSelection.Includes(n.Id, npcIdFilter))
                continue;
            if (n.DialogMode != NpcDialogMode.Simple)
                continue;
            if (n.IsPendingDialogEs)
                return true;
            if (n.Pregunta > 0 && !string.IsNullOrWhiteSpace(n.SimpleDialogTextLocal) && !n.DialogEsPublished)
                return true;
        }

        return false;
    }

    public static bool NeedsQuestsEsClientPreview(
        ContentDraftWorkspace workspace,
        IReadOnlySet<int>? npcIdFilter = null) =>
        HasPendingQuestsEs(workspace, npcIdFilter)
        || (!HasPendingSimpleDialogEs(workspace, npcIdFilter)
            && QuestsEsPublishBatchBuilder.HasUnpublishedBdMissions(workspace));

    public static ContentClientPublishResult PreparePreview(ContentClientPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var log = new List<string>();
        try
        {
            using var client = (request.ClientFactory ?? LangSftpPublishClientFactory.Create)(
                request.Settings, request.PlainPassword);
            client.Connect();
            return BuildPlan(request, client, log, previewOnly: true);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, log);
        }
    }

    public static ContentClientPublishResult Publish(ContentClientPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var log = new List<string>();
        void Info(string m) { log.Add("INFO " + m); RufusLog.Info(m); }
        void Ok(string m) { log.Add("OK " + m); RufusLog.Ok(m); }
        void Err(string m) { log.Add("ERROR " + m); RufusLog.Error(m); }

        if (string.IsNullOrWhiteSpace(request.Settings.Host) || string.IsNullOrWhiteSpace(request.Settings.User))
            return Fail("Configuración SFTP incompleta (Host/Usuario).", log);

        var workRoot = string.IsNullOrWhiteSpace(request.WorkDirectory)
            ? DefaultWorkDirectory
            : request.WorkDirectory!;
        Directory.CreateDirectory(workRoot);

        if (QuestsEsPublishBatchBuilder.HasUnpublishedBdMissions(request.Workspace))
        {
            return Fail(
                "Vista previa completa disponible. Publica primero el contenido en BD para confirmar los IDs de misión.",
                log);
        }

        ILangSftpPublishClient? client = null;
        try
        {
            client = (request.ClientFactory ?? LangSftpPublishClientFactory.Create)(
                request.Settings, request.PlainPassword);
            client.Connect();

            var plan = BuildPlan(request, client, log, previewOnly: false);
            if (!plan.Success)
                return plan;

            if (plan.AlreadyPublished)
            {
                if (plan.NpcBatch is not null && plan.NpcSourceVersion is int av and > 0)
                    NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, plan.NpcBatch, av);
                if (plan.QuestsBatch is not null && plan.QuestsSourceVersion is int qv and > 0)
                    QuestsEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, plan.QuestsBatch, qv);
                Ok("Cliente ya publicado — sin escrituras remotas");
                return plan;
            }

            var langPath = NormalizeDir(BlankTo(request.Settings.LangRemotePath, LangSftpSettings.DefaultLangRemotePath));
            var swfDir = NormalizeDir(BlankTo(request.Settings.SwfRemotePath, LangSftpSettings.DefaultSwfRemotePath));
            var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);

            var versionsText = client.ReadAllText(versionsRemote);
            var versionsSha = Sha256Hex(Encoding.UTF8.GetBytes(versionsText));
            if (!VersionsEsParser.TryParseDialogVersion(versionsText, out var dialogN, out var dErr))
                return Fail(dErr ?? "dialog,es ilegible.", log);
            if (!VersionsEsParser.TryParseNpcVersion(versionsText, out var npcN, out var nErr))
                return Fail(nErr ?? "npc,es ilegible.", log);
            if (!VersionsEsParser.TryParseQuestsVersion(versionsText, out var questsN, out var qErr))
                return Fail(qErr ?? "quests,es ilegible.", log);

            var npcFilter = request.NpcIdFilter;
            var wantDialog = HasPendingDialogEs(request.Workspace, npcFilter);
            var wantNpc = HasPendingNpcEs(request.Workspace, npcFilter);
            var wantQuests = HasPendingQuestsEs(request.Workspace, npcFilter);

            byte[]? dialogSourceBytes = null;
            string? dialogSourceSha = null;
            string? dialogSourceRemote = null;
            DialogEsSnapshot? dialogSnap = null;
            if (wantDialog)
            {
                dialogSourceRemote = Combine(swfDir, VersionsEsParser.BuildDialogSwfFileName(dialogN));
                dialogSourceBytes = client.DownloadBytes(dialogSourceRemote);
                dialogSourceSha = Sha256Hex(dialogSourceBytes);
                dialogSnap = DialogEsParser.Parse(dialogSourceBytes);
                if (dialogSnap.Version != dialogN)
                    return Fail($"VERSION dialog SWF ({dialogSnap.Version}) != versions ({dialogN}).", log);
            }

            byte[]? npcSourceBytes = null;
            string? npcSourceSha = null;
            string? npcSourceRemote = null;
            NpcEsSnapshot? npcSnap = null;
            if (wantNpc)
            {
                npcSourceRemote = Combine(swfDir, VersionsEsParser.BuildNpcSwfFileName(npcN));
                npcSourceBytes = client.DownloadBytes(npcSourceRemote);
                npcSourceSha = Sha256Hex(npcSourceBytes);
                npcSnap = NpcEsParser.Parse(npcSourceBytes);
                if (npcSnap.Version != npcN)
                    return Fail($"VERSION npc SWF ({npcSnap.Version}) != versions ({npcN}).", log);
            }

            byte[]? questsSourceBytes = null;
            string? questsSourceSha = null;
            string? questsSourceRemote = null;
            QuestsEsSnapshot? questsSnap = null;
            if (wantQuests)
            {
                questsSourceRemote = Combine(swfDir, VersionsEsParser.BuildQuestsSwfFileName(questsN));
                questsSourceBytes = client.DownloadBytes(questsSourceRemote);
                questsSourceSha = Sha256Hex(questsSourceBytes);
                questsSnap = QuestsEsParser.Parse(questsSourceBytes);
                if (questsSnap.Version != questsN)
                    return Fail($"VERSION quests SWF ({questsSnap.Version}) != versions ({questsN}).", log);
            }

            // Rebuild batches against live snapshots (IDs may shift).
            DialogEsPublishBatch? dialogBatch = null;
            NpcEsPublishBatch? npcBatch = null;
            QuestsEsPublishBatch? questsBatch = null;
            if (wantDialog)
            {
                dialogBatch = DialogEsPublishBatchBuilder.Build(
                    request.Workspace, dialogSnap!, request.Occupancy, npcFilter);
                if (dialogBatch.Errors.Count > 0)
                    return Fail(string.Join("\n", dialogBatch.Errors), log);
            }

            if (wantNpc)
            {
                npcBatch = NpcEsPublishBatchBuilder.Build(request.Workspace, npcSnap!, npcFilter);
                if (!npcBatch.IsValid)
                    return Fail(string.Join("\n", npcBatch.Errors), log);
            }

            if (wantQuests)
            {
                questsBatch = QuestsEsPublishBatchBuilder.Build(
                    request.Workspace,
                    questsSnap!,
                    request.MapWorldCoordsLookup,
                    request.BdNpcMapIdLookup,
                    npcIdFilter: npcFilter);
                if (!questsBatch.IsValid)
                    return Fail(string.Join("\n", questsBatch.Errors), log);
            }

            var needDialog = dialogBatch is { Bindings.Count: > 0 };
            var needNpc = npcBatch is { NewCount: > 0 };
            var needQuests = questsBatch is { NewMissionCount: > 0 };
            if (!needDialog && !needNpc && !needQuests)
            {
                if (npcBatch is not null)
                    NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, npcBatch, npcN);
                if (questsBatch is not null)
                    QuestsEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, questsBatch, questsN);
                Ok("Nada que subir — marcado local");
                return new ContentClientPublishResult
                {
                    Success = true,
                    StatusLabel = "YA_PUBLICADO",
                    AlreadyPublished = true,
                    DialogSourceVersion = dialogN,
                    NpcSourceVersion = npcN,
                    QuestsSourceVersion = questsN,
                    DialogBatch = dialogBatch,
                    NpcBatch = npcBatch,
                    QuestsBatch = questsBatch,
                    LogLines = log,
                };
            }

            // Generate ALL SWFs before any versions touch.
            byte[]? dialogOut = null;
            string? dialogOutSha = null;
            string? dialogTargetName = null;
            string? dialogTargetRemote = null;
            if (needDialog)
            {
                Info("Generando dialog_es N+1");
                var gen = DialogEsService.Generate(new DialogEsGenerateRequest
                {
                    SourceSwfBytes = dialogSourceBytes!,
                    Additions = dialogBatch!.Additions,
                    OutputDirectory = workRoot,
                });
                if (!gen.Success)
                    return Fail("dialog_es: " + (gen.Error ?? "generación fallida"), log);
                dialogOut = gen.OutputBytes;
                dialogOutSha = Sha256Hex(dialogOut!);
                dialogTargetName = VersionsEsParser.BuildDialogSwfFileName(dialogN + 1);
                dialogTargetRemote = Combine(swfDir, dialogTargetName);
                Ok("dialog_es validado localmente → " + dialogTargetName);
            }

            byte[]? npcOut = null;
            string? npcOutSha = null;
            string? npcTargetName = null;
            string? npcTargetRemote = null;
            if (needNpc)
            {
                Info("Generando npc_es N+1");
                var gen = NpcEsService.Generate(new NpcEsGenerateRequest
                {
                    SourceSwfBytes = npcSourceBytes!,
                    Additions = npcBatch!.Additions,
                    OutputDirectory = workRoot,
                });
                if (!gen.Success)
                    return Fail("npc_es: " + (gen.Error ?? "generación fallida"), log);
                npcOut = gen.OutputBytes;
                npcOutSha = Sha256Hex(npcOut!);
                npcTargetName = VersionsEsParser.BuildNpcSwfFileName(npcN + 1);
                npcTargetRemote = Combine(swfDir, npcTargetName);
                Ok("npc_es validado localmente → " + npcTargetName);
            }

            byte[]? questsOut = null;
            string? questsOutSha = null;
            string? questsTargetName = null;
            string? questsTargetRemote = null;
            if (needQuests)
            {
                Info("Generando quests_es N+1");
                var gen = QuestsEsService.Generate(new QuestsEsGenerateRequest
                {
                    SourceSwfBytes = questsSourceBytes!,
                    Additions = questsBatch!.Additions,
                    OutputDirectory = workRoot,
                });
                if (!gen.Success)
                    return Fail("quests_es: " + (gen.Error ?? "generación fallida"), log);
                questsOut = gen.OutputBytes;
                questsOutSha = Sha256Hex(questsOut!);
                questsTargetName = VersionsEsParser.BuildQuestsSwfFileName(questsN + 1);
                questsTargetRemote = Combine(swfDir, questsTargetName);
                Ok("quests_es validado localmente → " + questsTargetName);
            }

            // Concurrency
            Info("Comprobación de concurrencia");
            var versionsNow = client.ReadAllText(versionsRemote);
            if (!string.Equals(Sha256Hex(Encoding.UTF8.GetBytes(versionsNow)), versionsSha, StringComparison.Ordinal))
                return Fail("versions_es remoto cambió desde la sincronización. Recalcula.", log);
            if (!VersionsEsParser.TryParseDialogVersion(versionsNow, out var dNow, out _) || dNow != dialogN)
                return Fail("dialog,es remoto cambió. Abortado.", log);
            if (!VersionsEsParser.TryParseNpcVersion(versionsNow, out var nNow, out _) || nNow != npcN)
                return Fail("npc,es remoto cambió. Abortado.", log);
            if (!VersionsEsParser.TryParseQuestsVersion(versionsNow, out var qNow, out _) || qNow != questsN)
                return Fail("quests,es remoto cambió. Abortado.", log);

            if (needDialog)
            {
                var srcNow = client.DownloadBytes(dialogSourceRemote!);
                if (!string.Equals(Sha256Hex(srcNow), dialogSourceSha, StringComparison.Ordinal))
                    return Fail("SWF dialog_es activo cambió. Abortado.", log);
                if (client.FileExists(dialogTargetRemote!))
                    return Fail($"SWF destino ya existe: {dialogTargetName}", log);
            }

            if (needNpc)
            {
                var srcNow = client.DownloadBytes(npcSourceRemote!);
                if (!string.Equals(Sha256Hex(srcNow), npcSourceSha, StringComparison.Ordinal))
                    return Fail("SWF npc_es activo cambió. Abortado.", log);
                if (client.FileExists(npcTargetRemote!))
                    return Fail($"SWF destino ya existe: {npcTargetName}", log);
            }

            if (needQuests)
            {
                var srcNow = client.DownloadBytes(questsSourceRemote!);
                if (!string.Equals(Sha256Hex(srcNow), questsSourceSha, StringComparison.Ordinal))
                    return Fail("SWF quests_es activo cambió. Abortado.", log);
                if (client.FileExists(questsTargetRemote!))
                    return Fail($"SWF destino ya existe: {questsTargetName}", log);
            }

            Ok("Concurrencia OK");

            // Upload SWFs (versions still untouched)
            if (needDialog)
            {
                Info("Subiendo " + dialogTargetName);
                client.UploadNewFile(dialogTargetRemote!, dialogOut!);
                if (!VerifyUploadedSwf(
                        client, dialogTargetRemote!, dialogOut!, dialogOutSha!, dialogN + 1,
                        bytes => DialogEsParser.Parse(bytes).Version,
                        (src, dst) => DialogEsService.ValidateGenerated(src, dst, dialogBatch!.Additions),
                        dialogSourceBytes!,
                        out var dialogVerifyErr))
                    return Fail(dialogVerifyErr!, log);
                Ok("dialog_es remoto verificado");
            }

            if (needNpc)
            {
                Info("Subiendo " + npcTargetName);
                client.UploadNewFile(npcTargetRemote!, npcOut!);
                if (!VerifyUploadedSwf(
                        client, npcTargetRemote!, npcOut!, npcOutSha!, npcN + 1,
                        bytes => NpcEsParser.Parse(bytes).Version,
                        (src, dst) => NpcEsService.ValidateGenerated(src, dst, npcBatch!.Additions),
                        npcSourceBytes!,
                        out var npcVerifyErr))
                    return Fail(npcVerifyErr!, log);
                Ok("npc_es remoto verificado");
            }

            if (needQuests)
            {
                Info("Subiendo " + questsTargetName);
                client.UploadNewFile(questsTargetRemote!, questsOut!);
                if (!VerifyUploadedSwf(
                        client, questsTargetRemote!, questsOut!, questsOutSha!, questsN + 1,
                        bytes => QuestsEsParser.Parse(bytes).Version,
                        (src, dst) => QuestsEsService.ValidateGenerated(src, dst, questsBatch!.Additions),
                        questsSourceBytes!,
                        out var questsVerifyErr))
                    return Fail(questsVerifyErr!, log);
                Ok("quests_es remoto verificado");
            }

            // Single versions_es update
            Info("Actualizando versions_es.txt (una sola vez)");
            if (!VersionsEsParser.TryBumpContentClientVersions(
                    versionsNow,
                    needDialog ? dialogN : null,
                    needDialog ? dialogN + 1 : null,
                    needNpc ? npcN : null,
                    needNpc ? npcN + 1 : null,
                    out var bumped,
                    out var bumpErr,
                    needQuests ? questsN : null,
                    needQuests ? questsN + 1 : null))
                return Fail(bumpErr ?? "No se pudo construir versions_es.", log);

            var prevRemote = Combine(langPath, VersionsEsParser.VersionsEsEphemeralPrevName);
            client.ReplaceFileAtomically(versionsRemote, Encoding.UTF8.GetBytes(bumped), prevRemote);

            if (client.FileExists(prevRemote) || client.FileExists(versionsRemote + ".rufus-tmp"))
                return Fail("Temporales versions_es no eliminados tras el replace.", log);

            var finalVersions = client.ReadAllText(versionsRemote);
            if (needDialog)
            {
                if (!VersionsEsParser.TryParseDialogVersion(finalVersions, out var fd, out _) || fd != dialogN + 1)
                    return Fail($"Verificación final dialog,es={fd}, esperado {dialogN + 1}.", log);
            }
            else if (!VersionsEsParser.TryParseDialogVersion(finalVersions, out var fdKeep, out _) || fdKeep != dialogN)
                return Fail("dialog,es alterado inesperadamente.", log);

            if (needNpc)
            {
                if (!VersionsEsParser.TryParseNpcVersion(finalVersions, out var fn, out _) || fn != npcN + 1)
                    return Fail($"Verificación final npc,es={fn}, esperado {npcN + 1}.", log);
            }
            else if (!VersionsEsParser.TryParseNpcVersion(finalVersions, out var fnKeep, out _) || fnKeep != npcN)
                return Fail("npc,es alterado inesperadamente.", log);

            if (needQuests)
            {
                if (!VersionsEsParser.TryParseQuestsVersion(finalVersions, out var fq, out _) || fq != questsN + 1)
                    return Fail($"Verificación final quests,es={fq}, esperado {questsN + 1}.", log);
            }
            else if (!VersionsEsParser.TryParseQuestsVersion(finalVersions, out var fqKeep, out _) || fqKeep != questsN)
                return Fail("quests,es alterado inesperadamente.", log);

            if (VersionsEsParser.TryParseMapsVersion(versionsNow, out var mapsBefore, out _)
                && VersionsEsParser.TryParseMapsVersion(finalVersions, out var mapsAfter, out _)
                && mapsBefore != mapsAfter)
                return Fail("maps,es fue alterado (no permitido).", log);

            if (client.DeleteAttemptCount != 0)
                return Fail("Abortado: se detectaron intentos DELETE de SWF.", log);

            if (needDialog)
            {
                DialogEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, dialogBatch!, dialogN + 1);
                DialogEsSessionCache.Shared.Clear();
            }

            if (needNpc)
                NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, npcBatch!, npcN + 1);
            else if (npcBatch is not null)
                NpcEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, npcBatch, npcN);

            if (needQuests)
                QuestsEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, questsBatch!, questsN + 1);
            else if (questsBatch is not null)
                QuestsEsPublishBatchBuilder.ApplyToWorkspace(request.Workspace, questsBatch, questsN);

            Ok("Publicación cliente completada");
            return new ContentClientPublishResult
            {
                Success = true,
                StatusLabel = "PUBLICADO",
                DialogChanged = needDialog,
                NpcChanged = needNpc,
                QuestsChanged = needQuests,
                DialogSourceVersion = dialogN,
                DialogTargetVersion = needDialog ? dialogN + 1 : dialogN,
                NpcSourceVersion = npcN,
                NpcTargetVersion = needNpc ? npcN + 1 : npcN,
                QuestsSourceVersion = questsN,
                QuestsTargetVersion = needQuests ? questsN + 1 : questsN,
                VersionsUpdated = true,
                AtomicVersionsReplaceCount = 1,
                DeleteAttemptCount = client.DeleteAttemptCount,
                DialogBatch = dialogBatch,
                NpcBatch = npcBatch,
                QuestsBatch = questsBatch,
                LogLines = log,
            };
        }
        catch (Exception ex)
        {
            Err(ex.Message);
            return Fail(ex.Message, log);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static ContentClientPublishResult BuildPlan(
        ContentClientPublishRequest request,
        ILangSftpPublishClient client,
        List<string> log,
        bool previewOnly)
    {
        var langPath = NormalizeDir(BlankTo(request.Settings.LangRemotePath, LangSftpSettings.DefaultLangRemotePath));
        var swfDir = NormalizeDir(BlankTo(request.Settings.SwfRemotePath, LangSftpSettings.DefaultSwfRemotePath));
        var versionsRemote = Combine(langPath, LangSftpSettings.VersionsFileName);
        if (!client.FileExists(versionsRemote))
            return Fail("versions_es.txt inexistente en remoto.", log);

        var versionsText = client.ReadAllText(versionsRemote);
        if (!VersionsEsParser.TryParseDialogVersion(versionsText, out var dialogN, out var dErr))
            return Fail(dErr ?? "dialog,es ilegible.", log);
        if (!VersionsEsParser.TryParseNpcVersion(versionsText, out var npcN, out var nErr))
            return Fail(nErr ?? "npc,es ilegible.", log);
        if (!VersionsEsParser.TryParseQuestsVersion(versionsText, out var questsN, out var qErr))
            return Fail(qErr ?? "quests,es ilegible.", log);

        var npcFilter = request.NpcIdFilter;
        var needDialog = HasPendingDialogEs(request.Workspace, npcFilter);
        var needNpc = HasPendingNpcEs(request.Workspace, npcFilter);
        var needQuestsWrite = HasPendingQuestsEs(request.Workspace, npcFilter);
        var needQuestsPreview = previewOnly
            && QuestsEsPublishBatchBuilder.HasUnpublishedBdMissions(request.Workspace);
        var loadQuests = needQuestsWrite || needQuestsPreview;

        DialogEsPublishBatch? dialogBatch = null;
        NpcEsPublishBatch? npcBatch = null;
        QuestsEsPublishBatch? questsBatch = null;

        if (needDialog)
        {
            var dialogRemote = Combine(swfDir, VersionsEsParser.BuildDialogSwfFileName(dialogN));
            var dialogBytes = client.DownloadBytes(dialogRemote);
            var dialogSnap = DialogEsParser.Parse(dialogBytes);
            dialogBatch = DialogEsPublishBatchBuilder.Build(
                request.Workspace, dialogSnap, request.Occupancy, npcFilter);
            if (dialogBatch.Errors.Count > 0)
                return Fail(string.Join("\n", dialogBatch.Errors), log);
        }

        if (needNpc)
        {
            var npcRemote = Combine(swfDir, VersionsEsParser.BuildNpcSwfFileName(npcN));
            var npcBytes = client.DownloadBytes(npcRemote);
            var npcSnap = NpcEsParser.Parse(npcBytes);
            npcBatch = NpcEsPublishBatchBuilder.Build(request.Workspace, npcSnap, npcFilter);
            if (!npcBatch.IsValid)
                return Fail(string.Join("\n", npcBatch.Errors), log);
        }

        if (loadQuests)
        {
            var questsRemote = Combine(swfDir, VersionsEsParser.BuildQuestsSwfFileName(questsN));
            var questsBytes = client.DownloadBytes(questsRemote);
            var questsSnap = QuestsEsParser.Parse(questsBytes);

            if (needQuestsPreview)
            {
                var bdMaxes = request.BdMaxSnapshot
                              ?? request.BdPublishPlan?.Maxes;
                if (bdMaxes is null)
                {
                    return Fail(
                        "No hay ContentPublishPlan BD para previsualizar quests_es. Revisa MySQL / MAX.",
                        log);
                }

                var livePlan = ContentPublishPlanBuilder.Build(
                    request.Workspace,
                    bdMaxes,
                    dialogEs: null,
                    dialogEsOccupancy: request.Occupancy,
                    questsEs: questsSnap,
                    npcIdFilter: npcFilter);
                questsBatch = QuestsEsPublishBatchBuilder.BuildFromBdPlan(
                    request.Workspace, questsSnap, livePlan);
                if (!ValidateQuestsBatchMatchesBdPlan(questsBatch, livePlan, out var mismatch))
                    return Fail(mismatch!, log);
            }
            else
            {
                questsBatch = QuestsEsPublishBatchBuilder.Build(
                    request.Workspace,
                    questsSnap,
                    request.MapWorldCoordsLookup,
                    request.BdNpcMapIdLookup,
                    npcIdFilter: npcFilter);
            }

            if (!questsBatch.IsValid)
                return Fail(string.Join("\n", questsBatch.Errors), log);
        }

        var dialogWillChange = dialogBatch is { Bindings.Count: > 0 };
        var npcWillChange = npcBatch is { NewCount: > 0 };
        var questsWillChange = questsBatch is { NewMissionCount: > 0 };
        var questsPreviewPendingBd = questsBatch is { IsPreviewPendingBd: true };

        // Local-only npc "already" still counts as pending until Apply — preview shows SIN CAMBIOS upload.
        if (!dialogWillChange && !npcWillChange && !questsWillChange && !questsPreviewPendingBd
            && !needDialog && !needNpc && !needQuestsWrite && !needQuestsPreview)
        {
            return new ContentClientPublishResult
            {
                Success = true,
                StatusLabel = "YA_PUBLICADO",
                AlreadyPublished = true,
                DialogSourceVersion = dialogN,
                NpcSourceVersion = npcN,
                QuestsSourceVersion = questsN,
                DialogBatch = dialogBatch,
                NpcBatch = npcBatch,
                QuestsBatch = questsBatch,
                LogLines = log,
            };
        }

        if (!dialogWillChange && !npcWillChange && !questsWillChange && !questsPreviewPendingBd
            && (needNpc || needQuestsWrite) && (npcBatch is not null || questsBatch is not null))
        {
            // All bindings are "already" — still success preview (mark local on publish).
            return new ContentClientPublishResult
            {
                Success = true,
                StatusLabel = "YA_PUBLICADO",
                AlreadyPublished = true,
                DialogSourceVersion = dialogN,
                NpcSourceVersion = npcN,
                QuestsSourceVersion = questsN,
                DialogBatch = dialogBatch,
                NpcBatch = npcBatch,
                QuestsBatch = questsBatch,
                LogLines = log,
            };
        }

        return new ContentClientPublishResult
        {
            Success = true,
            StatusLabel = questsPreviewPendingBd ? "PREVIEW_PENDIENTE_BD" : "PREVIEW",
            DialogChanged = dialogWillChange,
            NpcChanged = npcWillChange,
            QuestsChanged = questsWillChange || questsPreviewPendingBd,
            QuestsPreviewPendingBd = questsPreviewPendingBd,
            DialogSourceVersion = dialogN,
            DialogTargetVersion = dialogWillChange ? dialogN + 1 : dialogN,
            NpcSourceVersion = npcN,
            NpcTargetVersion = npcWillChange ? npcN + 1 : npcN,
            QuestsSourceVersion = questsN,
            QuestsTargetVersion = questsWillChange ? questsN + 1 : questsN,
            DialogBatch = dialogBatch,
            NpcBatch = npcBatch,
            QuestsBatch = questsBatch,
            LogLines = log,
        };
    }

    /// <summary>Client preview IDs must match the BD ContentPublishPlan exactly.</summary>
    private static bool ValidateQuestsBatchMatchesBdPlan(
        QuestsEsPublishBatch batch,
        ContentPublishPlan plan,
        out string? error)
    {
        error = null;
        foreach (var b in batch.Bindings)
        {
            if (b.Assignment is null)
                continue;
            if (!plan.QuestIdMap.TryGetValue(b.MissionDraftId, out var expectedQuest)
                || expectedQuest != b.Assignment.Quest.QuestId)
            {
                error = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"ERROR discrepancia Quest ID: plan BD vs preview cliente (misión {b.MissionDraftId}). Publicación bloqueada.");
                return false;
            }

            foreach (var st in b.Assignment.Stages)
            {
                if (!plan.StageIdMap.ContainsValue(st.StageId))
                {
                    error = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"ERROR discrepancia Etapa ID {st.StageId}: no está en el plan BD. Publicación bloqueada.");
                    return false;
                }
            }

            foreach (var o in b.Assignment.Objectives)
            {
                if (!plan.ObjectiveIdMap.ContainsValue(o.ObjectiveId))
                {
                    error = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"ERROR discrepancia Objetivo ID {o.ObjectiveId}: no está en el plan BD. Publicación bloqueada.");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool VerifyUploadedSwf(
        ILangSftpPublishClient client,
        string remotePath,
        byte[] localBytes,
        string localSha,
        int expectedVersion,
        Func<byte[], int> readVersion,
        Func<byte[], byte[], string?> validate,
        byte[] sourceBytes,
        out string? error)
    {
        error = null;
        var remoteBytes = client.DownloadBytes(remotePath);
        if (remoteBytes.Length != localBytes.Length)
        {
            error = $"Tamaño remoto distinto ({remoteBytes.Length} vs {localBytes.Length}). versions_es NO modificado.";
            return false;
        }

        var remoteSha = Sha256Hex(remoteBytes);
        if (!string.Equals(localSha, remoteSha, StringComparison.Ordinal))
        {
            error = "Hash remoto distinto del local. versions_es NO modificado.";
            return false;
        }

        try
        {
            if (readVersion(remoteBytes) != expectedVersion)
            {
                error = $"VERSION interna remota != {expectedVersion}. versions_es NO modificado.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = "Parser remoto falló; versions_es NO modificado: " + ex.Message;
            return false;
        }

        var val = validate(sourceBytes, remoteBytes);
        if (val is not null)
        {
            error = "Validación semántica remota falló; versions_es NO modificado: " + val;
            return false;
        }

        return true;
    }

    private static ContentClientPublishResult Fail(string error, List<string> log)
    {
        log.Add("ERROR " + error);
        RufusLog.Error(error);
        return new ContentClientPublishResult
        {
            Success = false,
            Error = error,
            StatusLabel = "ERROR",
            LogLines = log,
        };
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeDir(string path)
    {
        var p = path.Replace('\\', '/').TrimEnd('/') + "/";
        return p == "/" ? "/" : p;
    }

    private static string Combine(string dir, string file) =>
        NormalizeDir(dir).TrimEnd('/') + "/" + file.TrimStart('/');

    private static string BlankTo(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
