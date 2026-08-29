using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.6B — local dialog_es N+1 mutator. No SFTP, no production BD, no Mapas writes.</summary>
public sealed class ContDialog6bTests
{
    private static ContentPublishMaxSnapshot Maxes(
        int preguntas = 20023,
        int respuestas = 90001) => new()
    {
        NpcsModelo = 20061,
        NpcPreguntas = preguntas,
        NpcRespuestas = respuestas,
        Misiones = 100003,
        MisionEtapas = 5500,
        MisionObjetivos = 4214,
    };

    private static byte[] Seed(
        int version = 10,
        params DialogEsAssignment[] entries) =>
        DialogEsSeed.Create(version, entries);

    private static DialogEsAssignment Q(int id, string text) => new()
    {
        Space = DialogEsSpace.Question,
        Id = id,
        Text = text,
    };

    private static DialogEsAssignment A(int id, string text) => new()
    {
        Space = DialogEsSpace.Answer,
        Id = id,
        Text = text,
    };

    [Fact]
    public void Parser_reused_on_seed_swf()
    {
        var bytes = Seed(10, Q(1, "hola"), A(1, "adios"));
        var snap = DialogEsParser.Parse(bytes);
        Assert.Equal("CWS", snap.Signature);
        Assert.Equal(6, snap.SwfVersion);
        Assert.True(snap.WasCompressed);
        Assert.Equal(10, snap.Version);
        Assert.True(snap.HasFileEnd);
        Assert.Equal(1, snap.DoActionCount);
        Assert.Equal("hola", snap.Questions[1]);
        Assert.Equal("adios", snap.Answers[1]);
        Assert.Equal(1, snap.MaxQuestionId);
        Assert.Equal(1, snap.MaxAnswerId);
    }

    [Fact]
    public void Resolver_simple_is_max_q_plus_one_not_hardcoded()
    {
        var snap = DialogEsParser.Parse(Seed(10, Q(7, "x")));
        var r = new DialogEsIdResolver(snap);
        Assert.Equal(8, r.ReserveSimpleQuestion());
        Assert.Equal(9, r.ReserveSimpleQuestion());
    }

    [Fact]
    public void Resolver_interactive_question_skips_bd_and_swf_collisions()
    {
        var snap = DialogEsParser.Parse(Seed(10, Q(10, "swf")));
        var occ = new DialogEsIdOccupancy
        {
            BdQuestionMax = 10,
            BdOccupiedQuestions = new HashSet<int> { 11 },
        };
        var r = new DialogEsIdResolver(snap, occ);
        Assert.Equal(12, r.ReserveInteractiveQuestion());
    }

    [Fact]
    public void Resolver_interactive_answer_skips_bd_and_swf_collisions()
    {
        var snap = DialogEsParser.Parse(Seed(10, A(5, "swf")));
        var occ = new DialogEsIdOccupancy
        {
            BdResponseMax = 5,
            BdOccupiedResponses = new HashSet<int> { 6 },
        };
        var r = new DialogEsIdResolver(snap, occ);
        Assert.Equal(7, r.ReserveInteractiveAnswer());
    }

    [Fact]
    public void Batch_does_not_reuse_ids()
    {
        var snap = DialogEsParser.Parse(Seed(10, Q(1, "a"), A(1, "b")));
        var r = new DialogEsIdResolver(snap);
        var q1 = r.ReserveSimpleQuestion();
        var q2 = r.ReserveSimpleQuestion();
        var a1 = r.ReserveInteractiveAnswer();
        Assert.Equal(2, q1);
        Assert.Equal(3, q2);
        Assert.Equal(2, a1);
        Assert.NotEqual(q1, q2);
    }

    [Fact]
    public void Question_and_answer_spaces_may_share_the_same_number()
    {
        var src = Seed(10, Q(1, "old-q"), A(1, "old-a"));
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(5, "nq"), A(5, "na") },
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal("nq", result.OutputSnapshot!.Questions[5]);
        Assert.Equal("na", result.OutputSnapshot.Answers[5]);
        Assert.Equal("old-q", result.OutputSnapshot.Questions[1]);
        Assert.Equal("old-a", result.OutputSnapshot.Answers[1]);
    }

    [Fact]
    public void Mutate_one_simple_dialog()
    {
        var src = Seed(10, Q(1, "keep"));
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(2, "Hola! esto es una prueba") },
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal(10, result.SourceVersion);
        Assert.Equal(11, result.TargetVersion);
        Assert.Equal("keep", result.OutputSnapshot!.Questions[1]);
        Assert.Equal("Hola! esto es una prueba", result.OutputSnapshot.Questions[2]);
        Assert.Empty(result.OutputSnapshot.Answers);
    }

    [Fact]
    public void Mutate_several_questions_and_answers()
    {
        var src = Seed(3);
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(1, "uno"), Q(2, "dos"), A(10, "a10"), A(11, "a11") },
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal("uno", result.OutputSnapshot!.Questions[1]);
        Assert.Equal("dos", result.OutputSnapshot.Questions[2]);
        Assert.Equal("a10", result.OutputSnapshot.Answers[10]);
        Assert.Equal("a11", result.OutputSnapshot.Answers[11]);
        Assert.Equal(result.SourceSnapshot!.ConstantPoolCount, result.OutputSnapshot.ConstantPoolCount);
    }

    [Fact]
    public void Mixed_batch_from_workspace_resolver()
    {
        var src = Seed(1292, Q(20024, "max-q"), A(90, "max-a"));
        var snap = DialogEsParser.Parse(src);
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var simple = ws.Npcs.CreateNew();
        simple.DialogMode = NpcDialogMode.Simple;
        simple.SimpleDialogTextLocal = "Hola! esto es una prueba";
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        ws.Dialogs.SetDbMaxQuestionId(20023);
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "¿Misión?";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Sí";
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1,2";
        ws.Dialogs.AddAction(r, DialogActionCodes.GotoQuestion);
        // second action: raw goto without target still counts as multi-action same logical id
        r.Actions[1].Args = "0";

        var occ = new DialogEsIdOccupancy { BdQuestionMax = 20023, BdResponseMax = 90001 };
        var plan = ContentPublishPlanBuilder.Build(ws, Maxes(), snap, occ);
        Assert.Contains(plan.DialogEsPreview, p => p.Kind == "simple" && p.DialogQuestionId == 20026);
        Assert.Equal(20025, plan.Npcs.Single(n => n.ProvisionalId == npc.Id).Pregunta);
        Assert.Single(plan.ResponseIdMap.Values.Distinct());
        Assert.Equal(2, plan.ResponseActionRowCount);
        Assert.All(plan.ResponseActions, a => Assert.Equal(plan.ReservedResponseIds[0], a.Id));

        var gen = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = plan.DialogEsAdditions,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(1293, gen.TargetVersion);
        Assert.Equal("¿Misión?", gen.OutputSnapshot!.Questions[20025]);
        Assert.Equal("Hola! esto es una prueba", gen.OutputSnapshot.Questions[20026]);
        Assert.Equal("Sí", gen.OutputSnapshot.Answers[plan.ReservedResponseIds[0]]);
    }

    [Fact]
    public void Latin1_valid_is_accepted()
    {
        var src = Seed(1);
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(1, "¡Hola! ¿ña?") },
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal("¡Hola! ¿ña?", result.OutputSnapshot!.Questions[1]);
    }

    [Fact]
    public void Latin1_unrepresentable_is_blocked_without_silent_replace()
    {
        var src = Seed(1);
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(1, "precio €") },
        });
        Assert.False(result.Success);
        Assert.Contains("Latin1", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("U+20AC", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void Version_n_to_n_plus_one_and_originals_intact_and_reparseable()
    {
        var src = Seed(50, Q(3, "orig"), A(8, "ans"));
        var dir = Path.Combine(Path.GetTempPath(), "cont6b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "dialog_es_50.swf");
        File.WriteAllBytes(basePath, src);

        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(4, "nuevo") },
            OutputDirectory = dir,
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal(51, result.TargetVersion);
        Assert.True(File.Exists(basePath));
        Assert.True(File.Exists(Path.Combine(dir, "dialog_es_51.swf")));
        Assert.Equal(src, File.ReadAllBytes(basePath));
        var again = DialogEsParser.Parse(result.OutputBytes!);
        Assert.Equal(51, again.Version);
        Assert.Equal("orig", again.Questions[3]);
        Assert.Equal("nuevo", again.Questions[4]);
        Assert.Equal("ans", again.Answers[8]);
        Assert.Equal("CWS", again.Signature);
        Assert.Equal(6, again.SwfVersion);
        Assert.True(again.HasFileEnd);
    }

    [Fact]
    public void Generate_does_not_write_bd_or_sftp()
    {
        var store = new InMemoryContentPublishStore(Maxes());
        var src = Seed(2, Q(1, "a"));
        var result = DialogEsService.Generate(new DialogEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { Q(2, "b") },
        });
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, store.InsertCallCount);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.Empty(store.Npcs);
    }

    [Fact]
    public void Preview_simple_pending_shows_provisional_ids_and_still_blocks_bd()
    {
        var snap = DialogEsParser.Parse(Seed(1292, Q(20024, "last")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Hola! esto es una prueba";
        var plan = ContentPublishPlanBuilder.Build(ws, Maxes(), snap);
        Assert.False(plan.IsValid);
        Assert.True(plan.DialogEsIdsAreProvisional);
        var line = Assert.Single(plan.DialogEsPreview, p => p.Kind == "simple");
        Assert.Equal(20025, line.DialogQuestionId);
        Assert.Equal(20025, line.NpcPreguntaColumn);
        Assert.Empty(plan.Questions);
        Assert.Equal(0, plan.LogicalResponseCount);
        var text = plan.FormatDialogEsPreviewBlock();
        Assert.Contains("Texto nuevo:", text, StringComparison.Ordinal);
        Assert.Contains("ID D.q provisional: 20025", text, StringComparison.Ordinal);
        Assert.Contains("dialog_es actual: 1292", text, StringComparison.Ordinal);
        Assert.Contains("dialog_es previsto: 1293", text, StringComparison.Ordinal);
        Assert.Contains("Tabla: npcs_modelo", text, StringComparison.Ordinal);
        Assert.Contains("Columna: pregunta", text, StringComparison.Ordinal);
        Assert.Contains("Preguntas BD: 0", text, StringComparison.Ordinal);
        Assert.Contains("BLOQUEADA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("npcs_modelo.pregunta", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_interactive_shows_shared_ids_and_multiaction_keeps_one_logical_id()
    {
        var snap = DialogEsParser.Parse(Seed(8, Q(4, "q"), A(40, "a")));
        var occ = new DialogEsIdOccupancy
        {
            BdQuestionMax = 10,
            BdOccupiedQuestions = new HashSet<int> { 11 },
            BdResponseMax = 40,
            BdOccupiedResponses = new HashSet<int> { 41 },
        };
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Pregunta";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Resp";
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1,1";
        ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest);
        r.Actions[1].Args = "1";

        var plan = ContentPublishPlanBuilder.Build(ws, Maxes(preguntas: 10, respuestas: 40), snap, occ);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(12, plan.ReservedQuestionIds[0]);
        Assert.Equal(42, plan.ReservedResponseIds[0]);
        Assert.Equal(2, plan.ResponseActionRowCount);
        Assert.Single(plan.ResponseActions.Select(x => x.Id).Distinct());
        var preview = plan.FormatDialogEsPreviewBlock();
        Assert.Contains("D.q / npc_preguntas", preview, StringComparison.Ordinal);
        Assert.Contains("D.a / npc_respuestas", preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreparePreview_reads_local_cache_only()
    {
        var cache = Path.Combine(Path.GetTempPath(), "cont6b-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "dialog_es_4.swf"), Seed(4, Q(9, "z")));

        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = "Hola! esto es una prueba";

        var store = new InMemoryContentPublishStore(Maxes());
        var svc = new ContentPublishService(store, Path.Combine(Path.GetTempPath(), "j-" + Guid.NewGuid().ToString("N")));
        var (plan, _) = await svc.PreparePreviewAsync(ws, dialogEsCacheDirectory: cache);
        Assert.Equal(0, store.InsertCallCount);
        Assert.Contains("dialog_es_4.swf", plan.DialogEsCacheStatus);
        var line = Assert.Single(plan.DialogEsPreview, p => p.Kind == "simple");
        Assert.Equal(10, line.DialogQuestionId);
    }

    [Fact]
    public void Maps_fixture_still_parses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps_es_1282.swf");
        if (!File.Exists(path))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures", "maps_es_1282.swf");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        Assert.True(File.Exists(path), "Fixture maps_es_1282.swf missing");
        var info = LangMapsSwfService.Inspect(path);
        Assert.True(info.WasCompressed);
        Assert.Equal(1282, info.Version);
    }
}
