using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.7B — safe npc_es SFTP publish. No BD writes. No dialog/maps token changes.</summary>
public sealed class ContNpc7bTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "cont7b-w-" + Guid.NewGuid().ToString("N"));
    private readonly string _backup = Path.Combine(Path.GetTempPath(), "cont7b-b-" + Guid.NewGuid().ToString("N"));

    public ContNpc7bTests()
    {
        Directory.CreateDirectory(_work);
        Directory.CreateDirectory(_backup);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { /* ignore */ }
        try { Directory.Delete(_backup, true); } catch { /* ignore */ }
    }

    private static byte[] Seed(int version, params NpcEsAssignment[] entries) =>
        NpcEsSeed.Create(version, entries);

    private static NpcEsAssignment N(int id, string name) => new() { Id = id, Name = name };

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

    private static ContentDraftWorkspace PendingNpc(int id, string name)
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(id - 1);
        var npc = ws.Npcs.CreateNew();
        // Force exact id (CreateNew may allocate id-1+1 = id)
        npc.Id = id;
        npc.Nombre = name;
        return ws;
    }

    private NpcEsRemotePublishRequest MakeRequest(
        FakeLangSftpPublishClient fake,
        ContentDraftWorkspace ws) =>
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
    public void Seed_and_parser_roundtrip_names()
    {
        var bytes = Seed(10, N(251, "Guerrero"), N(20001, "Pitor"));
        var snap = NpcEsParser.Parse(bytes);
        Assert.Equal(10, snap.Version);
        Assert.Equal("Guerrero", snap.Names[251]);
        Assert.Equal("Pitor", snap.Names[20001]);
        Assert.Equal("Hablar", snap.ActionLabels[3]);
        Assert.True(snap.HasFileEnd);
    }

    [Fact]
    public void Bump_npc_preserves_maps_and_dialog()
    {
        var src = Versions(1287, 1293, 1283);
        Assert.True(VersionsEsParser.TryBumpNpcVersion(src, 1287, 1288, out var bumped, out var err), err);
        Assert.Contains("npc,es,1288", bumped, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", bumped, StringComparison.Ordinal);
        Assert.Contains("dialog,es,1293", bumped, StringComparison.Ordinal);
        Assert.Contains("quests,es,1", bumped, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_appends_only_new_Nd_and_bumps_version()
    {
        var src = Seed(1287, N(20001, "Pitor Reo"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { N(20062, "Prueba") },
            OutputDirectory = _work,
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal(1288, gen.TargetVersion);
        var outSnap = gen.OutputSnapshot!;
        Assert.Equal("Pitor Reo", outSnap.Names[20001]);
        Assert.Equal("Prueba", outSnap.Names[20062]);
        Assert.Equal("Hablar", outSnap.ActionLabels[3]);
        Assert.False(outSnap.Names.ContainsKey(20063));
    }

    [Fact]
    public void Generate_allows_rename_via_append_overwrite()
    {
        var src = Seed(5, N(20062, "Viejo"));
        var gen = NpcEsService.Generate(new NpcEsGenerateRequest
        {
            SourceSwfBytes = src,
            Additions = new[] { N(20062, "Nuevo") },
        });
        Assert.True(gen.Success, gen.Error);
        Assert.Equal("Nuevo", gen.OutputSnapshot!.Names[20062]);
    }

    [Fact]
    public void Batch_uses_exact_npc_id_not_max_plus_one()
    {
        var snap = NpcEsParser.Parse(Seed(9, N(20003, "Salazar")));
        var ws = PendingNpc(20062, "Prueba");
        var batch = NpcEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("; ", batch.Errors));
        Assert.Equal(1, batch.NewCount);
        Assert.Equal(20062, batch.Additions[0].Id);
        Assert.Equal("Prueba", batch.Additions[0].Name);
    }

    [Fact]
    public void Batch_marks_same_name_as_already_without_duplicate()
    {
        var snap = NpcEsParser.Parse(Seed(9, N(20062, "Prueba")));
        var ws = PendingNpc(20062, "Prueba");
        var batch = NpcEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid);
        Assert.Equal(0, batch.NewCount);
        Assert.Single(batch.AlreadyPublished);
    }

    [Fact]
    public void Batch_rename_is_update_with_clear_kind()
    {
        var snap = NpcEsParser.Parse(Seed(9, N(20062, "Otro")));
        var ws = PendingNpc(20062, "Prueba");
        var batch = NpcEsPublishBatchBuilder.Build(ws, snap);
        Assert.True(batch.IsValid, string.Join("; ", batch.Errors));
        Assert.Equal(1, batch.NewCount);
        Assert.Equal("rename", batch.Bindings[0].Kind);
        Assert.Contains("Cambio de nombre", batch.FormatPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_uploads_bumps_npc_only_preserves_source()
    {
        var src = Seed(1287, N(20001, "Pitor Reo"));
        var fake = SeedRemote(1287, src);
        var ws = PendingNpc(20062, "Prueba");
        var result = NpcEsRemotePublishService.Publish(MakeRequest(fake, ws));

        Assert.True(result.Success, result.Error);
        Assert.Equal(1287, result.SourceVersion);
        Assert.Equal(1288, result.TargetVersion);
        Assert.True(result.SwfUploaded);
        Assert.True(result.VersionsUpdated);
        Assert.Equal(0, result.DeleteAttemptCount);

        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/npc_es_1287.swf"));
        Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/npc_es_1288.swf"));
        var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
        Assert.Contains("npc,es,1288", versions, StringComparison.Ordinal);
        Assert.Contains("dialog,es,1293", versions, StringComparison.Ordinal);
        Assert.Contains("maps,es,1283", versions, StringComparison.Ordinal);

        var npc = ws.Npcs.FindById(20062)!;
        Assert.True(npc.NpcEsPublished);
        Assert.Equal(1288, npc.NpcEsPublishedVersion);
        Assert.False(npc.IsPendingNpcEs);

        var remote = NpcEsParser.Parse(File.ReadAllBytes(result.LocalGeneratedSwfPath!));
        Assert.Equal("Prueba", remote.Names[20062]);
        Assert.Equal("Pitor Reo", remote.Names[20001]);
    }

    [Fact]
    public void Publish_aborts_concurrency_without_bump()
    {
        var src = Seed(10, N(1, "A"));
        var fake = SeedRemote(10, src);
        var flipping = new FlippingVersionsClient(fake, Versions(10), Versions(11));
        var result = NpcEsRemotePublishService.Publish(new NpcEsRemotePublishRequest
        {
            Settings = new LangSftpSettings
            {
                Host = "t",
                User = "u",
                LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
            },
            PlainPassword = "x",
            Workspace = PendingNpc(20062, "Prueba"),
            WorkDirectory = _work,
            BackupDirectory = _backup,
            ClientFactory = (_, _) => flipping,
        });
        Assert.False(result.Success);
        Assert.False(result.VersionsUpdated);
        Assert.Contains("cambiado", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("npc,es,10", fake.PeekText("/var/www/html/data/lang/versions_es.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_refuses_existing_target()
    {
        var src = Seed(5, N(1, "A"));
        var fake = SeedRemote(5, src);
        fake.SeedFile("/var/www/html/data/lang/swf/npc_es_6.swf", Seed(6, N(1, "A")));
        var result = NpcEsRemotePublishService.Publish(MakeRequest(fake, PendingNpc(9, "X")));
        Assert.False(result.Success);
        Assert.Contains("ya existe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_does_not_write()
    {
        var fake = SeedRemote(1287, Seed(1287, N(1, "A")));
        var beforeU = fake.UploadNewCount;
        var beforeR = fake.AtomicReplaceCount;
        var preview = NpcEsRemotePublishService.PreparePreview(MakeRequest(fake, PendingNpc(20062, "Prueba")));
        Assert.True(preview.Success, preview.Error);
        Assert.Equal(beforeU, fake.UploadNewCount);
        Assert.Equal(beforeR, fake.AtomicReplaceCount);
    }

    [Fact]
    public void Latin1_reject_unrepresentable()
    {
        Assert.Throws<DialogEsEncodingException>(() =>
            DialogEsLatin1.Validate("emoji😀", "n"));
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
}
