using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionDbCatalogUiTests
{
    [Fact]
    public void Preg_pair_codec_parses_npcId_semicolon_preguntaId()
    {
        Assert.True(MissionPregPairCodec.TryParse("20066;20024", out var npc, out var q));
        Assert.Equal(20066, npc);
        Assert.Equal(20024, q);
    }

    [Fact]
    public void Preg_pair_codec_rejects_invalid()
    {
        Assert.False(MissionPregPairCodec.TryParse("", out _, out _));
        Assert.False(MissionPregPairCodec.TryParse("abc;20024", out _, out _));
        Assert.False(MissionPregPairCodec.TryParse("20066", out _, out _));
    }

    [Fact]
    public void Merge_npc_choices_deduplicates_by_id_local_wins()
    {
        var local = new NpcsModeloDraft { Id = 100, Nombre = "Local NPC" };
        var catalog = new ContentDbCatalogSnapshot(
            [new DbNpcCatalogRow { Id = 100, Nombre = "BD NPC", Pregunta = 0 },
             new DbNpcCatalogRow { Id = 200, Nombre = "Solo BD", Pregunta = 0 }],
            [], [], null, true);

        var merged = MissionDbCatalogUi.MergeNpcChoices([local], catalog);
        Assert.Equal(2, merged.Count);
        Assert.Equal("Local NPC", merged.First(n => n.Id == 100).Nombre);
        Assert.True(merged.First(n => n.Id == 200).IsDbCatalogSnapshot);
    }

    [Fact]
    public void Build_question_choices_includes_local_and_bd()
    {
        var ws = new ContentDraftWorkspace();
        var npc = ws.Npcs.CreateNew();
        var localQ = ws.Dialogs.CreateQuestion(npc.Id);
        localQ.TextLocal = "Local nueva";

        var catalog = new ContentDbCatalogSnapshot(
            [new DbNpcCatalogRow { Id = npc.Id, Nombre = "NPC", Pregunta = 9001 }],
            [new DbQuestionCatalogRow { Id = 9001, Respuestas = "1" },
             new DbQuestionCatalogRow { Id = 9002, Respuestas = "2" }],
            [new DbMissionCatalogRow
            {
                Id = 1,
                Nombre = "M",
                PregDarMision = $"{npc.Id};9002",
            }],
            null,
            true);

        var items = MissionDbCatalogUi.BuildQuestionChoiceItems(
            ws.Dialogs, catalog, npc.Id, pregDarId: 9002, pregIncId: null, pregCompId: null);

        Assert.Contains(items, i => i.Id == localQ.Id);
        Assert.Contains(items, i => i.Id == 9001);
        Assert.Contains(items, i => i.Id == 9002);
    }

    [Fact]
    public void Assigned_pregDar_only_in_bd_is_visible()
    {
        var ws = new ContentDraftWorkspace();
        var catalog = new ContentDbCatalogSnapshot(
            [],
            [new DbQuestionCatalogRow { Id = 20024, Respuestas = "" }],
            [],
            null,
            true);

        var items = MissionDbCatalogUi.BuildQuestionChoiceItems(
            ws.Dialogs, catalog, startNpcId: null, pregDarId: 20024, pregIncId: null, pregCompId: null);

        Assert.Single(items);
        Assert.Equal(20024, items[0].Id);
    }

    [Fact]
    public void Dialog_es_text_applied_to_label()
    {
        var snap = new DialogEsSnapshot
        {
            Version = 1,
            SwfVersion = 1,
            WasCompressed = false,
            Signature = "test",
            Questions = new Dictionary<int, string> { [20024] = "Esto es una prueba" },
            Answers = new Dictionary<int, string>(),
            QuestionAssignmentCount = 1,
            AnswerAssignmentCount = 0,
            HasFileEnd = true,
            ConstantPoolCount = 0,
            DoActionCount = 0,
        };
        var catalog = new ContentDbCatalogSnapshot(
            [],
            [new DbQuestionCatalogRow { Id = 20024 }],
            [],
            snap,
            true);

        var q = MissionDbCatalogUi.FromDbQuestionRow(catalog.FindQuestion(20024)!, catalog);
        var label = MissionDbCatalogUi.FormatQuestionChoiceLabel(q, catalog);
        Assert.Equal("#20024 · Esto es una prueba", label);
    }

    [Fact]
    public void Question_label_fallback_id_only_without_text()
    {
        var catalog = ContentDbCatalogSnapshot.Empty;
        var q = new DialogQuestionDraft { Id = 42, TextLocal = "" };
        var label = MissionDbCatalogUi.FormatQuestionChoiceLabel(q, catalog);
        Assert.Equal("#42 · (sin texto)", label);
    }

    [Fact]
    public void Mission_catalog_row_display_label()
    {
        var row = new DbMissionCatalogRow { Id = 10015, Nombre = "Pieles Mojadas, Sabiduría Seca" };
        Assert.Equal("#10015 · Pieles Mojadas, Sabiduría Seca", row.DisplayLabel);
    }

    [Fact]
    public void Catalog_read_failure_returns_empty_snapshot()
    {
        var repo = new ThrowingDbCatalogRepo();
        var result = ContentDbCatalogReader.ReadAsync(repo).GetAwaiter().GetResult();
        Assert.False(result.Success);
        Assert.Empty(result.Snapshot.Npcs);
    }

    [Fact]
    public void Catalog_read_failure_does_not_clear_local_drafts()
    {
        var ws = new ContentDraftWorkspace();
        ws.Missions.CreateMission().Nombre = "Local mission";
        Assert.Single(ws.Missions.Missions);
    }

    private sealed class ThrowingDbCatalogRepo : IContentDbCatalogReadRepository
    {
        public Task<IReadOnlyList<DbNpcCatalogRow>> LoadNpcsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("BD down");

        public Task<IReadOnlyList<DbQuestionCatalogRow>> LoadQuestionsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("BD down");

        public Task<IReadOnlyList<DbMissionCatalogRow>> LoadMissionsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("BD down");
    }
}
