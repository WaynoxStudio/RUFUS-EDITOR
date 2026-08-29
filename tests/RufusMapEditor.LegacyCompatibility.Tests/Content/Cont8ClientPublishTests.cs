using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.8 — unified client publish + no permanent versions_es.bak.</summary>
public sealed class Cont8ClientPublishTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "cont8-w-" + Guid.NewGuid().ToString("N"));

    public Cont8ClientPublishTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { /* ignore */ }
    }

    private static string Versions(int dialog = 1294, int npc = 1289, int maps = 1283) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"11&f=maps,es,{maps}|dialog,es,{dialog}|quests,es,1|npc,es,{npc}");

    private static byte[] DialogSeed(int v, params DialogEsAssignment[] a) => DialogEsSeed.Create(v, a);
    private static byte[] NpcSeed(int v, params NpcEsAssignment[] a) => NpcEsSeed.Create(v, a);

    private static FakeLangSftpPublishClient SeedRemote(int dialogV, byte[] dialogSwf, int npcV, byte[] npcSwf)
    {
        var fake = new FakeLangSftpPublishClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions(dialogV, npcV));
        fake.SeedFile($"/var/www/html/data/lang/swf/dialog_es_{dialogV}.swf", dialogSwf);
        fake.SeedFile($"/var/www/html/data/lang/swf/npc_es_{npcV}.swf", npcSwf);
        return fake;
    }

    private ContentClientPublishRequest Req(FakeLangSftpPublishClient fake, ContentDraftWorkspace ws) =>
        new()
        {
            Settings = new LangSftpSettings
            {
                Host = "t",
                User = "u",
                LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
            },
            PlainPassword = "x",
            Workspace = ws,
            WorkDirectory = _work,
            ClientFactory = (_, _) => fake,
        };

    private static ContentDraftWorkspace SimpleNpcWithDialog(int id, string name, string text)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(id - 1);
        var npc = ws.Npcs.CreateNew();
        npc.Id = id;
        npc.Nombre = name;
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = text;
        return ws;
    }

    [Fact]
    public void Bump_client_both_tokens_preserves_maps_quests()
    {
        var src = Versions(1294, 1289, 1283);
        Assert.True(VersionsEsParser.TryBumpContentClientVersions(
            src, 1294, 1295, 1289, 1290, out var bumped, out var err), err);
        Assert.Contains("dialog,es,1295", bumped, StringComparison.Ordinal);
        Assert.Contains("npc,es,1290", bumped, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", bumped, StringComparison.Ordinal);
        Assert.Contains("quests,es,1", bumped, StringComparison.Ordinal);
    }

    [Fact]
    public void Bump_client_dialog_only()
    {
        var src = Versions();
        Assert.True(VersionsEsParser.TryBumpContentClientVersions(
            src, 1294, 1295, null, null, out var bumped, out var err), err);
        Assert.Contains("dialog,es,1295", bumped, StringComparison.Ordinal);
        Assert.Contains("npc,es,1289", bumped, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_both_layers_updates_versions_once_no_bak()
    {
        var fake = SeedRemote(1294, DialogSeed(1294), 1289, NpcSeed(1289, new NpcEsAssignment { Id = 1, Name = "A" }));
        var ws = SimpleNpcWithDialog(20065, "Nombre NPC", "Texto nuevo");
        var result = ContentClientRemotePublishService.Publish(Req(fake, ws));

        Assert.True(result.Success, result.Error);
        Assert.True(result.DialogChanged);
        Assert.True(result.NpcChanged);
        Assert.Equal(1, result.AtomicVersionsReplaceCount);
        Assert.Equal(1, fake.AtomicReplaceCount);
        Assert.Equal(0, result.DeleteAttemptCount);

        var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
        Assert.Contains("dialog,es,1295", versions, StringComparison.Ordinal);
        Assert.Contains("npc,es,1290", versions, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", versions, StringComparison.Ordinal);

        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_1294.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_1295.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/npc_es_1289.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/npc_es_1290.swf"));

        Assert.DoesNotContain(fake.PeekPaths(), p => p.Contains(".bak.", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.PeekPaths(), p => p.EndsWith(".rufus-tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.PeekPaths(), p => p.EndsWith(".rufus-prev", StringComparison.Ordinal));

        var npc = ws.Npcs.FindById(20065)!;
        Assert.True(npc.DialogEsPublished);
        Assert.True(npc.NpcEsPublished);
        Assert.Equal(new[] { 3 }, npc.NpcEsPublishedActionIds);
        Assert.True(npc.Pregunta > 0);
    }

    [Fact]
    public void Publish_npc_only_when_dialog_already_done()
    {
        var fake = SeedRemote(10, DialogSeed(10), 5, NpcSeed(5));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 20065;
        npc.Nombre = "SoloNpc";
        npc.DialogMode = NpcDialogMode.Simple;
        npc.Pregunta = 99;
        npc.DialogEsPublished = true;
        npc.DialogEsPublishedVersion = 10;

        var result = ContentClientRemotePublishService.Publish(Req(fake, ws));
        Assert.True(result.Success, result.Error);
        Assert.False(result.DialogChanged);
        Assert.True(result.NpcChanged);
        var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
        Assert.Contains("dialog,es,10", versions, StringComparison.Ordinal);
        Assert.Contains("npc,es,6", versions, StringComparison.Ordinal);
        Assert.False(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_11.swf"));
    }

    [Fact]
    public void Publish_already_published_no_new_versions()
    {
        var fake = SeedRemote(10, DialogSeed(10), 5,
            NpcSeed(5, new NpcEsAssignment { Id = 20065, Name = "X", Actions = new[] { 3 } }));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20064);
        var npc = ws.Npcs.CreateNew();
        npc.Id = 20065;
        npc.Nombre = "X";
        npc.DialogMode = NpcDialogMode.Simple;
        npc.Pregunta = 1;
        npc.DialogEsPublished = true;
        npc.NpcEsPublished = true;
        npc.NpcEsPublishedName = "X";
        npc.NpcEsActionIds = new List<int> { 3 };
        npc.NpcEsPublishedActionIds = new List<int> { 3 };
        npc.NpcEsPublishedVersion = 5;

        Assert.False(ContentClientRemotePublishService.HasPendingDialogEs(ws));
        Assert.False(ContentClientRemotePublishService.HasPendingNpcEs(ws));

        var result = ContentClientRemotePublishService.Publish(Req(fake, ws));
        Assert.True(result.Success, result.Error);
        Assert.True(result.AlreadyPublished);
        Assert.Equal(0, fake.AtomicReplaceCount);
        Assert.Equal(0, fake.UploadNewCount);
        Assert.Contains("npc,es,5", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_aborts_dialog_concurrency_without_versions_bump()
    {
        var fake = SeedRemote(10, DialogSeed(10), 5, NpcSeed(5));
        var flipping = new FlippingVersionsClient(fake, Versions(10, 5), Versions(11, 5));
        var ws = SimpleNpcWithDialog(20065, "N", "T");
        var result = ContentClientRemotePublishService.Publish(new ContentClientPublishRequest
        {
            Settings = new LangSftpSettings
            {
                Host = "t",
                User = "u",
                LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
            },
            PlainPassword = "x",
            Workspace = ws,
            WorkDirectory = _work,
            ClientFactory = (_, _) => flipping,
        });
        Assert.False(result.Success);
        Assert.False(result.VersionsUpdated);
        Assert.Contains("dialog,es,10", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_lists_both_layers()
    {
        var fake = SeedRemote(1294, DialogSeed(1294), 1289, NpcSeed(1289));
        var ws = SimpleNpcWithDialog(20065, "Nombre NPC", "Texto nuevo");
        var preview = ContentClientRemotePublishService.PreparePreview(Req(fake, ws));
        Assert.True(preview.Success, preview.Error);
        var text = preview.FormatPreview();
        Assert.Contains("DIALOG_ES", text, StringComparison.Ordinal);
        Assert.Contains("NPC_ES", text, StringComparison.Ordinal);
        Assert.Contains("Escrituras BD: 0", text, StringComparison.Ordinal);
        Assert.Equal(0, fake.UploadNewCount);
        Assert.Equal(0, fake.AtomicReplaceCount);
    }

    [Fact]
    public void Hablar_auto_when_dialog()
    {
        var ws = SimpleNpcWithDialog(1, "A", "Hola");
        Assert.Equal(new[] { 3 }, NpcEsActionResolver.ResolveExpected(ws, ws.Npcs.FindById(1)!));
    }

    private sealed class FlippingVersionsClient : ILangSftpPublishClient
    {
        private readonly FakeLangSftpPublishClient _inner;
        private readonly string _first;
        private readonly string _second;
        private int _versionsReads;

        public FlippingVersionsClient(FakeLangSftpPublishClient inner, string first, string second)
        {
            _inner = inner;
            _first = first;
            _second = second;
        }

        public int WriteAttemptCount => _inner.WriteAttemptCount;
        public int DeleteAttemptCount => _inner.DeleteAttemptCount;
        public void Connect() => _inner.Connect();
        public void Dispose() => _inner.Dispose();
        public bool FileExists(string remotePath) => _inner.FileExists(remotePath);
        public bool DirectoryExists(string remotePath) => _inner.DirectoryExists(remotePath);
        public byte[] DownloadBytes(string remotePath) => _inner.DownloadBytes(remotePath);
        public long GetFileLength(string remotePath) => _inner.GetFileLength(remotePath);
        public void UploadNewFile(string remotePath, byte[] content) => _inner.UploadNewFile(remotePath, content);
        public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath) =>
            _inner.ReplaceFileAtomically(remotePath, content, backupRemotePath);

        public string ReadAllText(string remotePath)
        {
            if (remotePath.EndsWith("versions_es.txt", StringComparison.Ordinal))
            {
                _versionsReads++;
                return _versionsReads <= 2 ? _first : _second;
            }

            return _inner.ReadAllText(remotePath);
        }
    }
}
