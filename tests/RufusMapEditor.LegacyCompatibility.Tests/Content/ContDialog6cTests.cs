using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.6C — safe dialog_es SFTP publish. No BD writes. No Mapas token changes.</summary>
public sealed class ContDialog6cTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "cont6c-w-" + Guid.NewGuid().ToString("N"));
    private readonly string _backup = Path.Combine(Path.GetTempPath(), "cont6c-b-" + Guid.NewGuid().ToString("N"));

    public ContDialog6cTests()
    {
        Directory.CreateDirectory(_work);
        Directory.CreateDirectory(_backup);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { /* ignore */ }
        try { Directory.Delete(_backup, true); } catch { /* ignore */ }
    }

    private static byte[] Seed(int version, params DialogEsAssignment[] entries) =>
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

    private static string Versions(int dialog = 1292, int maps = 1282) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"11&f=maps,es,{maps}|dialog,es,{dialog}|quests,es,1|npc,es,9");

    private static FakeLangSftpPublishClient SeedRemote(int dialogVersion, byte[] swfBytes)
    {
        var fake = new FakeLangSftpPublishClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions(dialogVersion));
        fake.SeedFile($"/var/www/html/data/lang/swf/dialog_es_{dialogVersion}.swf", swfBytes);
        return fake;
    }

    private static ContentDraftWorkspace SimplePendingWorkspace(string text = "Hola buenas, ¿cómo estás?")
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Simple;
        npc.SimpleDialogTextLocal = text;
        return ws;
    }

    private DialogEsRemotePublishRequest MakeRequest(
        FakeLangSftpPublishClient fake,
        ContentDraftWorkspace ws,
        DialogEsIdOccupancy? occ = null) =>
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
            Occupancy = occ ?? new DialogEsIdOccupancy { BdQuestionMax = 20023, BdResponseMax = 90001 },
            WorkDirectory = _work,
            BackupDirectory = _backup,
            ClientFactory = (_, _) => fake,
        };

    [Fact]
    public void Bump_dialog_token_preserves_maps_and_others()
    {
        var src = Versions(1292, 1282);
        Assert.True(VersionsEsParser.TryBumpDialogVersion(src, 1292, 1293, out var bumped, out var err), err);
        Assert.Contains("dialog,es,1293", bumped, StringComparison.Ordinal);
        Assert.Contains("maps,es,1282", bumped, StringComparison.Ordinal);
        Assert.Contains("quests,es,1", bumped, StringComparison.Ordinal);
        Assert.Contains("npc,es,9", bumped, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog,es,1292", bumped, StringComparison.Ordinal);
        Assert.True(VersionsEsParser.TryParseMapsVersion(bumped, out var maps, out _));
        Assert.Equal(1282, maps);
    }

    [Fact]
    public void Publish_simple_recalculates_ids_uploads_bumps_dialog_only()
    {
        var src = Seed(1292, Q(20024, "last"), A(90, "a"));
        var fake = SeedRemote(1292, src);
        var ws = SimplePendingWorkspace();
        var result = DialogEsRemotePublishService.Publish(MakeRequest(fake, ws));

        Assert.True(result.Success, result.Error);
        Assert.Equal(1292, result.SourceVersion);
        Assert.Equal(1293, result.TargetVersion);
        Assert.True(result.SwfUploaded);
        Assert.True(result.VersionsUpdated);
        Assert.Equal(1293, result.ActiveRemoteVersion);
        Assert.Equal(0, result.DeleteAttemptCount);
        Assert.Equal(result.LocalSwfSha256, result.RemoteSwfSha256);

        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_1292.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_1293.swf"));
        var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
        Assert.Contains("dialog,es,1293", versions, StringComparison.Ordinal);
        Assert.Contains("maps,es,1282", versions, StringComparison.Ordinal);
        Assert.Contains("quests,es,1", versions, StringComparison.Ordinal);
        Assert.Contains("npc,es,9", versions, StringComparison.Ordinal);

        var npc = ws.Npcs.Drafts[0];
        Assert.Equal(20025, npc.Pregunta);
        Assert.True(npc.DialogEsPublished);
        Assert.Equal(1293, npc.DialogEsPublishedVersion);
        Assert.False(npc.IsPendingDialogEs);
        Assert.False(npc.PublishedBd);

        Assert.False(string.IsNullOrWhiteSpace(result.LocalGeneratedSwfPath));
        var remoteBytes = File.ReadAllBytes(result.LocalGeneratedSwfPath!);
        var snap = DialogEsParser.Parse(remoteBytes);
        Assert.Equal(1293, snap.Version);
        Assert.Equal("Hola buenas, ¿cómo estás?", snap.Questions[20025]);
        Assert.Equal("last", snap.Questions[20024]);
    }

    [Fact]
    public void Publish_aborts_on_concurrency_without_versions_bump()
    {
        var src = Seed(10, Q(3, "x"));
        var fake = SeedRemote(10, src);
        var ws = SimplePendingWorkspace("nuevo");
        var flipping = new FlippingVersionsClient(fake, Versions(10), Versions(11));

        var result = DialogEsRemotePublishService.Publish(new DialogEsRemotePublishRequest
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
            Occupancy = new DialogEsIdOccupancy(),
            WorkDirectory = _work,
            BackupDirectory = _backup,
            ClientFactory = (_, _) => flipping,
        });

        Assert.False(result.Success);
        Assert.False(result.VersionsUpdated);
        Assert.False(result.SwfUploaded);
        Assert.Contains("cambiado", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(ws.Npcs.Drafts[0].DialogEsPublished);
        Assert.Contains("dialog,es,10", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_refuses_existing_target_swf()
    {
        var src = Seed(5, Q(1, "a"));
        var fake = SeedRemote(5, src);
        fake.SeedFile("/var/www/html/data/lang/swf/dialog_es_6.swf", Seed(6, Q(1, "a")));
        var result = DialogEsRemotePublishService.Publish(MakeRequest(fake, SimplePendingWorkspace("t")));
        Assert.False(result.Success);
        Assert.False(result.VersionsUpdated);
        Assert.Contains("ya existe", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dialog,es,5", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_mismatch_leaves_versions_at_N_and_preserves_source_swf()
    {
        var src = Seed(7, Q(2, "old"));
        var fake = SeedRemote(7, src);
        var corrupt = new CorruptDownloadAfterUploadClient(fake);
        var ws = SimplePendingWorkspace("texto nuevo");
        var result = DialogEsRemotePublishService.Publish(new DialogEsRemotePublishRequest
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
            Occupancy = new DialogEsIdOccupancy { BdQuestionMax = 2 },
            WorkDirectory = _work,
            BackupDirectory = _backup,
            ClientFactory = (_, _) => corrupt,
        });

        Assert.False(result.Success);
        Assert.True(result.SwfUploaded);
        Assert.False(result.VersionsUpdated);
        Assert.Contains("Hash", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dialog,es,7", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_7.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_8.swf"));
        Assert.False(ws.Npcs.Drafts[0].DialogEsPublished);
    }

    [Fact]
    public void Interactive_batch_and_apply_keeps_shared_response_id()
    {
        var snap = DialogEsParser.Parse(Seed(8, Q(4, "q"), A(40, "a")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "Pregunta";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "Resp";
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1,1";
        ws.Dialogs.AddAction(r, DialogActionCodes.StartQuest).Args = "1";

        var batch = DialogEsPublishBatchBuilder.Build(
            ws,
            snap,
            new DialogEsIdOccupancy { BdQuestionMax = 10, BdResponseMax = 40 });
        Assert.True(batch.IsValid, string.Join("; ", batch.Errors));
        Assert.Equal(1, batch.NewQuestionCount);
        Assert.Equal(1, batch.NewAnswerCount);
        Assert.Equal(11, batch.Bindings.Single(b => b.Kind == "interactive-question").Assignment.Id);
        Assert.Equal(41, batch.Bindings.Single(b => b.Kind == "interactive-answer").Assignment.Id);

        DialogEsPublishBatchBuilder.ApplyToWorkspace(ws, batch, publishedVersion: 9);
        Assert.Equal(11, ws.Dialogs.Questions[0].Id);
        Assert.Equal(11, npc.Pregunta);
        Assert.Equal(41, r.PublishedResponseId);
        Assert.True(npc.DialogEsPublished);
        Assert.Equal(9, npc.DialogEsPublishedVersion);
    }

    [Fact]
    public void Preview_does_not_write_sftp()
    {
        var src = Seed(1292, Q(20024, "last"));
        var fake = SeedRemote(1292, src);
        var beforeUploads = fake.UploadNewCount;
        var beforeReplace = fake.AtomicReplaceCount;
        var preview = DialogEsRemotePublishService.PreparePreview(MakeRequest(fake, SimplePendingWorkspace()));
        Assert.True(preview.Success, preview.Error);
        Assert.Equal(20025, preview.Batch!.Bindings[0].Assignment.Id);
        Assert.Equal(beforeUploads, fake.UploadNewCount);
        Assert.Equal(beforeReplace, fake.AtomicReplaceCount);
        Assert.False(fake.PeekExists("/var/www/html/data/lang/swf/dialog_es_1293.swf"));
    }

    [Fact]
    public void After_publish_simple_bd_plan_no_longer_blocked_by_pending_dialog_es()
    {
        var src = Seed(1292, Q(20024, "last"));
        var fake = SeedRemote(1292, src);
        var ws = SimplePendingWorkspace();
        Assert.True(DialogEsRemotePublishService.Publish(MakeRequest(fake, ws)).Success);

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot());
        Assert.DoesNotContain(
            plan.Errors,
            e => e.Contains("pendiente de publicación dialog_es", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(20025, ws.Npcs.Drafts[0].Pregunta);
    }

    [Fact]
    public void Maps_fixture_unaffected()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps_es_1282.swf");
        if (!File.Exists(path))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var c = Path.Combine(dir.FullName, "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures", "maps_es_1282.swf");
                if (File.Exists(c)) { path = c; break; }
                dir = dir.Parent;
            }
        }

        Assert.True(File.Exists(path));
        Assert.Equal(1282, LangMapsSwfService.Inspect(path).Version);
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
                return _versionsReads == 1 ? _first : _second;
            }

            return _inner.ReadAllText(remotePath);
        }
    }

    /// <summary>After UploadNewFile of N+1, subsequent downloads of that path return corrupted bytes.</summary>
    private sealed class CorruptDownloadAfterUploadClient : ILangSftpPublishClient
    {
        private readonly FakeLangSftpPublishClient _inner;
        private string? _corruptPath;

        public CorruptDownloadAfterUploadClient(FakeLangSftpPublishClient inner) => _inner = inner;

        public int WriteAttemptCount => _inner.WriteAttemptCount;
        public int DeleteAttemptCount => _inner.DeleteAttemptCount;
        public void Connect() => _inner.Connect();
        public void Dispose() => _inner.Dispose();
        public bool FileExists(string remotePath) => _inner.FileExists(remotePath);
        public bool DirectoryExists(string remotePath) => _inner.DirectoryExists(remotePath);
        public string ReadAllText(string remotePath) => _inner.ReadAllText(remotePath);
        public long GetFileLength(string remotePath) => _inner.GetFileLength(remotePath);
        public void ReplaceFileAtomically(string remotePath, byte[] content, string backupRemotePath) =>
            _inner.ReplaceFileAtomically(remotePath, content, backupRemotePath);

        public void UploadNewFile(string remotePath, byte[] content)
        {
            _inner.UploadNewFile(remotePath, content);
            _corruptPath = remotePath.Replace('\\', '/');
        }

        public byte[] DownloadBytes(string remotePath)
        {
            var bytes = _inner.DownloadBytes(remotePath);
            if (_corruptPath is not null
                && string.Equals(remotePath.Replace('\\', '/'), _corruptPath, StringComparison.Ordinal))
            {
                var copy = (byte[])bytes.Clone();
                copy[^1] ^= 0xFF;
                return copy;
            }

            return bytes;
        }
    }
}
