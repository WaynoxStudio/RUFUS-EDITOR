using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONTENT.PUBLISH.SMART_VISIBILITY.PERFORMANCE.FIX.2</summary>
public sealed class ContentPublishSmartVisibilityPerfTests
{
    [Fact]
    public void Cached_quests_swf_not_reparsed_on_second_evaluate()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor",
            "content-client-publish",
            "quests_es_1277.swf");
        if (!File.Exists(path))
            return;

        ContentPublishHeaderEvaluator.InvalidateCachedQuestsEs();
        ContentPublishHeaderDiagnostics.Reset();

        var ws = new ContentDraftWorkspace();
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 1,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        };

        _ = ContentPublishHeaderEvaluator.Evaluate(ws, maxes, contentMaxReady: true, tryCachedQuestsEs: true);
        var parsesAfterFirst = ContentPublishHeaderDiagnostics.SwfParses;
        Assert.True(parsesAfterFirst >= 1);

        _ = ContentPublishHeaderEvaluator.Evaluate(ws, maxes, contentMaxReady: true, tryCachedQuestsEs: true);
        Assert.Equal(parsesAfterFirst, ContentPublishHeaderDiagnostics.SwfParses);
        Assert.True(ContentPublishHeaderDiagnostics.SwfCacheHits >= 1);
        Assert.Equal(2, ContentPublishHeaderDiagnostics.EvaluateCalls);
    }

    [Fact]
    public void Explicit_quests_snapshot_skips_disk_parse()
    {
        ContentPublishHeaderEvaluator.InvalidateCachedQuestsEs();
        ContentPublishHeaderDiagnostics.Reset();

        var snap = QuestsEsParser.Parse(QuestsEsSeed.Create(3));
        var ws = new ContentDraftWorkspace();
        var maxes = new ContentPublishMaxSnapshot
        {
            NpcsModelo = 1,
            NpcPreguntas = 1,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        };

        _ = ContentPublishHeaderEvaluator.Evaluate(
            ws, maxes, questsEs: snap, contentMaxReady: true, tryCachedQuestsEs: false);

        Assert.Equal(0, ContentPublishHeaderDiagnostics.SwfParses);
        Assert.Equal(1, ContentPublishHeaderDiagnostics.PlanBuilds);
    }

    [Fact]
    public void Getters_remain_pure_via_cached_state_object()
    {
        // UI binds to ContentPublishHeaderState fields — Evaluate must not run from property getters.
        var state = ContentPublishHeaderEvaluator.Evaluate(
            new ContentDraftWorkspace(),
            new ContentPublishMaxSnapshot(),
            QuestsEsParser.Parse(QuestsEsSeed.Create(2)),
            contentMaxReady: true,
            tryCachedQuestsEs: false);

        ContentPublishHeaderDiagnostics.Reset();
        _ = state.ShowBdButton;
        _ = state.ShowClientButton;
        _ = state.BdStatusLabel;
        _ = state.ClientStatusLabel;
        Assert.Equal(0, ContentPublishHeaderDiagnostics.EvaluateCalls);
        Assert.Equal(0, ContentPublishHeaderDiagnostics.PlanBuilds);
        Assert.Equal(0, ContentPublishHeaderDiagnostics.SwfParses);
    }
}
