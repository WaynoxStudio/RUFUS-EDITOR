using System.Globalization;
using System.Text.RegularExpressions;

namespace RufusMapEditor.LegacyCompatibility.Content;

public enum ContentPublishHeaderTone
{
    UpToDate,
    Pending,
    Warning,
}

/// <summary>CONTENT.PUBLISH.SMART_VISIBILITY — header publish affordances from real pending state.</summary>
public sealed class ContentPublishHeaderState
{
    public required bool ShowBdButton { get; init; }
    public required bool EnableBdButton { get; init; }
    public required bool ShowClientButton { get; init; }
    public required bool EnableClientButton { get; init; }
    public required string BdStatusLabel { get; init; }
    public required string ClientStatusLabel { get; init; }
    public required ContentPublishHeaderTone BdTone { get; init; }
    public required ContentPublishHeaderTone ClientTone { get; init; }
    public bool ShowBdStatus => !string.IsNullOrWhiteSpace(BdStatusLabel);
    public bool ShowClientStatus => !string.IsNullOrWhiteSpace(ClientStatusLabel);
}

/// <summary>CONTENT.PUBLISH.SMART_VISIBILITY.PERFORMANCE.FIX.2 — diagnostics (tests / audit).</summary>
public static class ContentPublishHeaderDiagnostics
{
    private static long _evaluateCalls;
    private static long _planBuilds;
    private static long _swfParses;
    private static long _swfCacheHits;
    private static long _clientBatchBuilds;

    public static long EvaluateCalls => Interlocked.Read(ref _evaluateCalls);
    public static long PlanBuilds => Interlocked.Read(ref _planBuilds);
    public static long SwfParses => Interlocked.Read(ref _swfParses);
    public static long SwfCacheHits => Interlocked.Read(ref _swfCacheHits);
    public static long ClientBatchBuilds => Interlocked.Read(ref _clientBatchBuilds);

    public static void Reset()
    {
        Interlocked.Exchange(ref _evaluateCalls, 0);
        Interlocked.Exchange(ref _planBuilds, 0);
        Interlocked.Exchange(ref _swfParses, 0);
        Interlocked.Exchange(ref _swfCacheHits, 0);
        Interlocked.Exchange(ref _clientBatchBuilds, 0);
    }

    internal static void IncEvaluate() => Interlocked.Increment(ref _evaluateCalls);
    internal static void IncPlan() => Interlocked.Increment(ref _planBuilds);
    internal static void IncSwfParse() => Interlocked.Increment(ref _swfParses);
    internal static void IncSwfCacheHit() => Interlocked.Increment(ref _swfCacheHits);
    internal static void IncClientBatch() => Interlocked.Increment(ref _clientBatchBuilds);
}

public static class ContentPublishHeaderEvaluator
{
    public const string BdUpToDate = "✓ BD al día";
    public const string BdPending = "● BD pendiente";
    public const string BdWarning = "⚠ BD requiere revisión";
    /// <summary>Shown when the only BD blocker is pending simple dialog_es (client-first).</summary>
    public const string BdWaitingClient = "⚠ BD: espera Publicar cliente";
    public const string ClientUpToDate = "✓ Cliente al día";
    public const string ClientPending = "● Cliente pendiente";
    public const string ClientWarning = "⚠ Cliente requiere revisión";

    private static readonly object SwfCacheGate = new();
    private static string? _cachedSwfPath;
    private static long _cachedSwfTicks;
    private static QuestsEsSnapshot? _cachedSwfSnap;

    /// <summary>
    /// BD source: ContentPublishPlanBuilder (same candidates + ValidatePlan).
    /// Client source: HasPendingDialogEs / HasPendingNpcEs / IsPendingQuestsEs + cached quests_es repair batch.
    /// Safe to call off the UI thread.
    /// </summary>
    public static ContentPublishHeaderState Evaluate(
        ContentDraftWorkspace workspace,
        ContentPublishMaxSnapshot? maxes = null,
        QuestsEsSnapshot? questsEs = null,
        bool contentMaxReady = false,
        bool tryCachedQuestsEs = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ContentPublishHeaderDiagnostics.IncEvaluate();

        maxes ??= ReadMaxesFromWorkspace(workspace);
        ContentPublishHeaderDiagnostics.IncPlan();
        var plan = ContentPublishPlanBuilder.Build(workspace, maxes);
        var (showBd, enableBd, bdLabel, bdTone) = EvaluateBd(plan, contentMaxReady);

        var questsSnap = questsEs
                         ?? (tryCachedQuestsEs ? TryLoadLatestCachedQuestsEs() : null);
        var (showClient, enableClient, clientLabel, clientTone) =
            EvaluateClient(workspace, questsSnap);

        return new ContentPublishHeaderState
        {
            ShowBdButton = showBd,
            EnableBdButton = enableBd,
            ShowClientButton = showClient,
            EnableClientButton = enableClient,
            BdStatusLabel = bdLabel,
            ClientStatusLabel = clientLabel,
            BdTone = bdTone,
            ClientTone = clientTone,
        };
    }

    public static bool HasBdPublishWork(ContentDraftWorkspace workspace, ContentPublishMaxSnapshot? maxes = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        maxes ??= ReadMaxesFromWorkspace(workspace);
        ContentPublishHeaderDiagnostics.IncPlan();
        return ContentPublishPlanBuilder.Build(workspace, maxes).HasWork;
    }

    public static bool HasClientPublishWork(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot? questsEs = null,
        bool tryCachedQuestsEs = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (ContentClientRemotePublishService.HasPendingDialogEs(workspace))
            return true;
        if (ContentClientRemotePublishService.HasPendingNpcEs(workspace))
            return true;
        // Same rule as EvaluateClient / CONT.8: no quests while diálogo simple is pending;
        // repair alone never counts as client work.
        return ContentClientRemotePublishService.HasPendingQuestsEs(workspace);
    }

    private static (bool show, bool enable, string label, ContentPublishHeaderTone tone) EvaluateBd(
        ContentPublishPlan plan,
        bool contentMaxReady)
    {
        if (!plan.HasWork)
            return (false, false, BdUpToDate, ContentPublishHeaderTone.UpToDate);

        // Keep the button visible when there is work so the user can see where BD publish lives.
        // Disable it while MAX is missing or the plan has errors (e.g. pending dialog_es → client first).
        if (!contentMaxReady)
            return (true, false, BdWarning, ContentPublishHeaderTone.Warning);

        if (plan.Errors.Count > 0)
        {
            var label = IsOnlyWaitingForDialogEs(plan) ? BdWaitingClient : BdWarning;
            return (true, false, label, ContentPublishHeaderTone.Warning);
        }

        return (true, true, BdPending, ContentPublishHeaderTone.Pending);
    }

    /// <summary>
    /// True when every plan error is the CONT-DIALOG.3 «pendiente dialog_es» gate
    /// (simple text exists, Pregunta still 0) — not a broken draft, just client-first order.
    /// </summary>
    internal static bool IsOnlyWaitingForDialogEs(ContentPublishPlan plan)
    {
        if (plan.Errors.Count == 0) return false;
        foreach (var err in plan.Errors)
        {
            if (err.Contains("pendiente de publicación dialog_es", StringComparison.Ordinal))
                continue;
            return false;
        }

        return true;
    }

    private static (bool show, bool enable, string label, ContentPublishHeaderTone tone) EvaluateClient(
        ContentDraftWorkspace workspace,
        QuestsEsSnapshot? questsSnap)
    {
        var dialogPending = ContentClientRemotePublishService.HasPendingDialogEs(workspace);
        var npcPending = ContentClientRemotePublishService.HasPendingNpcEs(workspace);
        // Uses HasPendingQuestsEs (skips quests while diálogo simple is pending).
        var questsPending = ContentClientRemotePublishService.HasPendingQuestsEs(workspace);
        _ = questsSnap;

        var pending = dialogPending || npcPending || questsPending;
        if (!pending)
            return (false, false, ClientUpToDate, ContentPublishHeaderTone.UpToDate);

        return (true, true, ClientPending, ContentPublishHeaderTone.Pending);
    }

    public static ContentPublishMaxSnapshot ReadMaxesFromWorkspace(ContentDraftWorkspace workspace) =>
        new()
        {
            NpcsModelo = workspace.Npcs.DbMaxId,
            NpcPreguntas = workspace.Dialogs.DbMaxQuestionId,
            NpcRespuestas = workspace.Dialogs.DbMaxResponseId,
            Misiones = workspace.Missions.DbMaxQuestId,
            MisionEtapas = workspace.Missions.DbMaxStageId,
            MisionObjetivos = workspace.Missions.DbMaxObjectiveId,
        };

    /// <summary>Invalidate cached SWF snapshot (e.g. after client publish writes a new file).</summary>
    public static void InvalidateCachedQuestsEs()
    {
        lock (SwfCacheGate)
        {
            _cachedSwfPath = null;
            _cachedSwfTicks = 0;
            _cachedSwfSnap = null;
        }
    }

    public static QuestsEsSnapshot? TryLoadLatestCachedQuestsEs()
    {
        try
        {
            var best = FindLatestCachedQuestsEsPath();
            if (best is null) return null;

            var ticks = File.GetLastWriteTimeUtc(best).Ticks;
            lock (SwfCacheGate)
            {
                if (_cachedSwfSnap is not null
                    && string.Equals(_cachedSwfPath, best, StringComparison.OrdinalIgnoreCase)
                    && _cachedSwfTicks == ticks)
                {
                    ContentPublishHeaderDiagnostics.IncSwfCacheHit();
                    return _cachedSwfSnap;
                }
            }

            ContentPublishHeaderDiagnostics.IncSwfParse();
            var snap = QuestsEsParser.Parse(File.ReadAllBytes(best));
            lock (SwfCacheGate)
            {
                _cachedSwfPath = best;
                _cachedSwfTicks = ticks;
                _cachedSwfSnap = snap;
            }

            return snap;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLatestCachedQuestsEsPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor");
        var dirs = new[]
        {
            Path.Combine(root, "content-client-publish"),
            Path.Combine(root, "lang-cache"),
        };

        string? bestPath = null;
        var bestVersion = -1;
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "quests_es_*.swf"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var m = Regex.Match(name, @"quests_es_(\d+)$", RegexOptions.CultureInvariant);
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ver))
                    continue;
                if (ver <= bestVersion) continue;
                bestVersion = ver;
                bestPath = file;
            }
        }

        return bestPath;
    }
}
