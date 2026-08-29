using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.7B.1 — npc_es client actions a:[...], Hablar[3], incomplete repair.</summary>
public sealed class ContNpc7b1Tests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "cont7b1-w-" + Guid.NewGuid().ToString("N"));
    private readonly string _backup = Path.Combine(Path.GetTempPath(), "cont7b1-b-" + Guid.NewGuid().ToString("N"));

    public ContNpc7b1Tests()
    {
        Directory.CreateDirectory(_work);
        Directory.CreateDirectory(_backup);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { /* ignore */ }
        try { Directory.Delete(_backup, true); } catch { /* ignore */ }
    }

    private static NpcEsAssignment N(int id, string name, params int[] actions) =>
        new() { Id = id, Name = name, Actions = actions };

    private static byte[] Seed(int version, params NpcEsAssignment[] entries) =>
        NpcEsSeed.Create(version, entries);

    private static string Versions(int npc = 1287, int dialog = 1293, int maps = 1283) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"11&f=maps,es,{maps}|dialog,es,{dialog}|quests,es,1|npc,es,{npc}");

    private static FakeLangSftpPublishClient SeedRemote(int npcVersion, byte[] swfBytes)
    {
        var fake = new FakeLangSftpPublishClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions(npcVersion));
        fake.SeedFile($"/var/www/html/data/lang/swf/npc_es_{npcVersion}.swf", swfBytes);
        return fake;
    }

    private static ContentDraftWorkspace Draft(int id, string name, Action<NpcsModeloDraft, ContentDraftWorkspace>? setup = null)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(id - 1);
        var npc = ws.Npcs.CreateNew();
        npc.Id = id;
        npc.Nombre = name;
        setup?.Invoke(npc, ws);
        return ws;
    }

    private NpcEsRemotePublishRequest MakeRequest(FakeLangSftpPublishClient fake, ContentDraftWorkspace ws) =>
        new()
        {
            Settings = new LangSftpSettings
            {
                Host = "test",
                User = "u",
                LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
            },
            PlainPassword = "x",
            Workspace = ws,
            WorkDirectory = _work,
            BackupDirectory = _backup,
            ClientFactory = (_, _) => fake,
        };

    [Fact]
    public void Catalog_has_exactly_eight_confirmed_actions()
    {
        Assert.Equal(8, NpcEsClientActions.All.Count);
        Assert.Equal("Comprar/Vender", NpcEsClientActions.LabelOf(1));
        Assert.Equal("Intercambiar", NpcEsClientActions.LabelOf(2));
        Assert.Equal("Hablar", NpcEsClientActions.LabelOf(3));
        Assert.Equal("Dejar/Recoger a una mascota", NpcEsClientActions.LabelOf(4));
        Assert.Equal("Vender", NpcEsClientActions.LabelOf(5));
        Assert.Equal("Comprar", NpcEsClientActions.LabelOf(6));
        Assert.Equal("Resucitar a una mascota", NpcEsClientActions.LabelOf(7));
        Assert.Equal("Intercambiar una montura", NpcEsClientActions.LabelOf(8));
    }

    [Fact]
    public void SameSet_order_insensitive()
    {
        Assert.True(NpcEsClientActions.SameSet(new[] { 1, 3 }, new[] { 3, 1 }));
        Assert.False(NpcEsClientActions.SameSet(new[] { 1, 3 }, new[] { 1 }));
    }

    [Fact]
    public void Normalize_sorts_dedupes_and_drops_invalid()
    {
        Assert.Equal(new[] { 1, 3, 8 }, NpcEsClientActions.Normalize(new[] { 3, 1, 3, 9, 8 }));
    }

    [Fact]
    public void New_npc_without_actions_emits_name_only()
    {
        var src = Seed(10, N(1, "A"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { N(20063, "RUFUS PRUEBA") },
            OutputDirectory = _work,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Empty(gen.OutputSnapshot!.ActionsOf(20063));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Single_action_roundtrip(int actionId)
    {
        var src = Seed(10, N(1, "A"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { N(20010 + actionId, "Npc", actionId) },
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(new[] { actionId }, gen.OutputSnapshot!.ActionsOf(20010 + actionId));
    }

    [Fact]
    public void Multi_select_emits_sorted_a_without_duplicates()
    {
        var src = Seed(10, N(1, "A"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { N(20063, "RUFUS PRUEBA", 3, 1, 1, 3) },
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(new[] { 1, 3 }, gen.OutputSnapshot!.ActionsOf(20063));
        Assert.Equal("[1,3]", NpcEsClientActions.FormatArrayLiteral(gen.OutputSnapshot.ActionsOf(20063)));
    }

    [Fact]
    public void Simple_dialog_forces_hablar_3()
    {
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "Hola";
            npc.NpcEsActionIds = new List<int> { 1 };
        });
        var expected = NpcEsActionResolver.ResolveExpected(ws, ws.Npcs.FindById(20063)!);
        Assert.Equal(new[] { 1, 3 }, expected);
    }

    [Fact]
    public void Interactive_dialog_forces_hablar_3()
    {
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, w) =>
        {
            npc.DialogMode = NpcDialogMode.Interactive;
            var q = w.Dialogs.CreateQuestion(npc.Id);
            q.TextLocal = "¿Qué deseas?";
            npc.Pregunta = q.Id;
        });
        var expected = NpcEsActionResolver.ResolveExpected(ws, ws.Npcs.FindById(20063)!);
        Assert.Equal(new[] { 3 }, expected);
    }

    [Fact]
    public void No_dialog_does_not_invent_hablar()
    {
        var ws = Draft(20063, "Mercader", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.NpcEsActionIds = new List<int> { 6 };
        });
        Assert.Equal(new[] { 6 }, NpcEsActionResolver.ResolveExpected(ws, ws.Npcs.FindById(20063)!));
    }

    [Fact]
    public void Preserve_existing_actions_when_adding_hablar()
    {
        var snap = NpcEsParser.Parse(Seed(20, N(20063, "RUFUS PRUEBA", 1, 6)));
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.NpcEsActionIds = new List<int> { 1, 6 };
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "Hola";
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedName = "RUFUS PRUEBA";
            npc.NpcEsPublishedActionIds = new List<int> { 1, 6 };
        });
        var batch = NpcEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("; ", batch.Errors));
        Assert.Equal("update", batch.Bindings[0].Kind);
        Assert.Equal(new[] { 1, 3, 6 }, batch.Additions[0].Actions);
    }

    [Fact]
    public void Incomplete_npc_with_dialog_missing_hablar_detected()
    {
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "Hola";
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedName = "RUFUS PRUEBA";
            npc.NpcEsPublishedActionIds = new List<int>();
            npc.NpcEsPublishedVersion = 1287;
        });
        var npc = ws.Npcs.FindById(20063)!;
        Assert.True(npc.IsNpcEsIncompleteFor(ws));
        Assert.True(npc.IsPendingNpcEsFor(ws));
    }

    [Fact]
    public void Repair_incomplete_emits_a_3_and_preserves_other_npcs()
    {
        var src = Seed(1287, N(20001, "Pitor"), N(20063, "RUFUS PRUEBA"));
        var snap = NpcEsParser.Parse(src);
        Assert.Empty(snap.ActionsOf(20063));

        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "Hola";
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedName = "RUFUS PRUEBA";
            npc.NpcEsPublishedActionIds = new List<int>();
        });

        var batch = NpcEsPublishBatchBuilder.Build(ws, snap);
        Assert.Equal("update", batch.Bindings[0].Kind);
        Assert.Equal(new[] { 3 }, batch.Additions[0].Actions);

        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = batch.Additions,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(new[] { 3 }, gen.OutputSnapshot!.ActionsOf(20063));
        Assert.Equal("Pitor", gen.OutputSnapshot.Names[20001]);
        Assert.Empty(gen.OutputSnapshot.ActionsOf(20001));
        Assert.Equal(1288, gen.TargetVersion);
    }

    [Fact]
    public void Preview_lists_actions_and_repair_states()
    {
        var snap = NpcEsParser.Parse(Seed(9, N(20063, "RUFUS PRUEBA")));
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "x";
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedName = "RUFUS PRUEBA";
        });
        var preview = NpcEsPublishBatchBuilder.Build(ws, snap).FormatPreview();
        Assert.Contains("[3] Hablar", preview, StringComparison.Ordinal);
        Assert.Contains("a: [3]", preview, StringComparison.Ordinal);
        Assert.Contains("Reparación", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_detected_in_preview()
    {
        var snap = NpcEsParser.Parse(Seed(9, N(20063, "Viejo")));
        var ws = Draft(20063, "Nuevo");
        var preview = NpcEsPublishBatchBuilder.Build(ws, snap).FormatPreview();
        Assert.Contains("Cambio de nombre", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_repair_bumps_npc_only_keeps_dialog_maps()
    {
        var src = Seed(1287, N(20063, "RUFUS PRUEBA"));
        var fake = SeedRemote(1287, src);
        var ws = Draft(20063, "RUFUS PRUEBA", (npc, _) =>
        {
            npc.DialogMode = NpcDialogMode.Simple;
            npc.SimpleDialogTextLocal = "Hola";
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedName = "RUFUS PRUEBA";
            npc.NpcEsPublishedActionIds = new List<int>();
            npc.NpcEsPublishedVersion = 1287;
        });

        var result = NpcEsRemotePublishService.Publish(MakeRequest(fake, ws));
        Assert.True(result.Success, result.Error);
        Assert.Equal(1288, result.TargetVersion);
        Assert.True(result.VersionsUpdated);
        Assert.Equal(0, result.DeleteAttemptCount);

        var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
        Assert.Contains("npc,es,1288", versions, StringComparison.Ordinal);
        Assert.Contains("dialog,es,1293", versions, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", versions, StringComparison.Ordinal);
        Assert.Contains("quests,es,1", versions, StringComparison.Ordinal);

        var npc = ws.Npcs.FindById(20063)!;
        Assert.True(npc.NpcEsPublished);
        Assert.Equal(new[] { 3 }, npc.NpcEsPublishedActionIds);
        Assert.False(npc.IsNpcEsIncompleteFor(ws));

        var remote = NpcEsParser.Parse(File.ReadAllBytes(result.LocalGeneratedSwfPath!));
        Assert.Equal(new[] { 3 }, remote.ActionsOf(20063));
    }

    [Fact]
    public void Invalid_action_rejected()
    {
        var src = Seed(1, N(1, "A"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { new NpcEsAssignment { Id = 2, Name = "X", Actions = new[] { 99 } } },
        });
        Assert.False(gen.Success);
        Assert.Contains("inválida", gen.Error, StringComparison.OrdinalIgnoreCase);
    }
}
