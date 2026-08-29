using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>CONT.6B.2 — READ-ONLY load of active remote dialog_es. No BD/SFTP writes.</summary>
public sealed class ContDialog6b2Tests : IDisposable
{
    private readonly string _workDir =
        Path.Combine(Path.GetTempPath(), "cont6b2-work-" + Guid.NewGuid().ToString("N"));
    private readonly DialogEsSessionCache _session = new();

    public ContDialog6b2Tests() => Directory.CreateDirectory(_workDir);

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* ignore */ }
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

    private static LangSftpSettings Settings() => new()
    {
        Host = "test",
        Port = 22,
        User = "u",
        LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
        SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
    };

    private static string Versions(int dialog = 1292, int maps = 1282) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"11&f=maps,es,{maps}|dialog,es,{dialog}|quests,es,1");

    [Fact]
    public void Parses_dialog_es_token_without_touching_maps_token()
    {
        Assert.True(VersionsEsParser.TryParseDialogVersion(Versions(), out var v, out var err), err);
        Assert.Equal(1292, v);
        Assert.Equal("dialog_es_1292.swf", VersionsEsParser.BuildDialogSwfFileName(v));
        Assert.Equal("dialog,es,1292", VersionsEsParser.ExtractDialogLine(Versions()));
        Assert.True(VersionsEsParser.TryParseMapsVersion(Versions(), out var maps, out _));
        Assert.Equal(1282, maps);
    }

    [Fact]
    public void Fetch_downloads_to_work_dir_parses_and_does_not_write_sftp()
    {
        var swf = Seed(1292, Q(20024, "last"));
        var fake = new FakeLangSftpReadClient();
        fake.SeedDirectory(LangSftpSettings.DefaultLangRemotePath);
        fake.SeedDirectory(LangSftpSettings.DefaultSwfRemotePath);
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions());
        fake.SeedFile("/var/www/html/data/lang/swf/dialog_es_1292.swf", swf);

        var result = DialogEsRemoteLoader.Fetch(new DialogEsRemoteLoadRequest
        {
            Settings = Settings(),
            PlainPassword = "x",
            ClientFactory = (_, _) => fake,
            WorkDirectory = _workDir,
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.RemoteWriteAttempts);
        Assert.Equal(0, fake.WriteAttemptCount);
        Assert.Equal(1292, result.DialogVersion);
        Assert.Equal("dialog,es,1292", result.Token);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(20024, result.Snapshot!.MaxQuestionId);
        Assert.True(File.Exists(Path.Combine(_workDir, "dialog_es_1292.swf")));
        Assert.DoesNotContain("lang-cache", result.LocalTempPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Simple_provisional_id_from_remote_swf_is_not_hardcoded()
    {
        var snap = DialogEsParser.Parse(Seed(1292, Q(20024, "last")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.SimpleDialogTextLocal = "Hola buenas, ¿cómo estás?";
        var state = DialogEsSimpleUiResolver.ForNpc(ws, npc, snap);
        Assert.Equal(20025, state.ProvisionalDqId);
        Assert.Equal(1292, state.ActiveVersion);
        Assert.Equal(1293, state.TargetVersion);
        Assert.DoesNotContain("no disponible", state.FormatDetails(), StringComparison.OrdinalIgnoreCase);

        var other = DialogEsParser.Parse(Seed(40, Q(7, "x")));
        Assert.Equal(8, DialogEsSimpleUiResolver.ForNpc(ws, npc, other).ProvisionalDqId);
    }

    [Fact]
    public void Interactive_ids_use_swf_and_bd_max()
    {
        var snap = DialogEsParser.Parse(Seed(8, Q(10, "q"), A(40, "a")));
        var occ = new DialogEsIdOccupancy { BdQuestionMax = 200, BdResponseMax = 50 };
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.DialogMode = NpcDialogMode.Interactive;
        var q = ws.Dialogs.CreateQuestion(npc.Id);
        q.TextLocal = "P";
        ws.Dialogs.SetInitialQuestion(npc, q.Id);
        var r = ws.Dialogs.AddResponse(q);
        r.TextLocal = "R";
        ws.Dialogs.AddAction(r, DialogActionCodes.Teleport).Args = "1,1";

        var plan = ContentPublishPlanBuilder.Build(ws, new ContentPublishMaxSnapshot
        {
            NpcsModelo = 1,
            NpcPreguntas = 200,
            NpcRespuestas = 50,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        }, snap, occ);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(201, plan.ReservedQuestionIds[0]);
        Assert.Equal(51, plan.ReservedResponseIds[0]);
    }

    [Fact]
    public void Missing_sftp_does_not_invent_id()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(1);
        var npc = ws.Npcs.CreateNew();
        npc.SimpleDialogTextLocal = "Hola";
        var state = DialogEsSimpleUiResolver.ForNpc(ws, npc, null, "SFTP no disponible.");
        Assert.True(state.CannotCalculate);
        Assert.Null(state.ProvisionalDqId);
        Assert.Contains("⚠ No se puede calcular ID dialog_es", state.FormatDetails(), StringComparison.Ordinal);
        Assert.DoesNotContain("dialog_es local no disponible", state.FormatDetails(), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_versions_es_blocks()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedDirectory(LangSftpSettings.DefaultLangRemotePath);
        fake.SeedDirectory(LangSftpSettings.DefaultSwfRemotePath);
        var result = DialogEsRemoteLoader.Fetch(new DialogEsRemoteLoadRequest
        {
            Settings = Settings(),
            PlainPassword = "x",
            ClientFactory = (_, _) => fake,
            WorkDirectory = _workDir,
        });
        Assert.False(result.Success);
        Assert.Contains("versions_es", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
        Assert.Contains("⚠ No se puede calcular ID dialog_es", result.StatusLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_swf_or_bad_parse_blocks()
    {
        var fake = new FakeLangSftpReadClient();
        fake.SeedDirectory(LangSftpSettings.DefaultLangRemotePath);
        fake.SeedDirectory(LangSftpSettings.DefaultSwfRemotePath);
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions());
        var missing = DialogEsRemoteLoader.Fetch(new DialogEsRemoteLoadRequest
        {
            Settings = Settings(),
            PlainPassword = "x",
            ClientFactory = (_, _) => fake,
            WorkDirectory = _workDir,
        });
        Assert.False(missing.Success);
        Assert.Contains("inexistente", missing.Error, StringComparison.OrdinalIgnoreCase);

        fake.SeedFile("/var/www/html/data/lang/swf/dialog_es_1292.swf", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var bad = DialogEsRemoteLoader.Fetch(new DialogEsRemoteLoadRequest
        {
            Settings = Settings(),
            PlainPassword = "x",
            ClientFactory = (_, _) => fake,
            WorkDirectory = _workDir,
        });
        Assert.False(bad.Success);
        Assert.Contains("parsear", bad.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fake.WriteAttemptCount);
    }

    [Fact]
    public void Session_cache_avoids_second_download_until_forced()
    {
        var swf = Seed(10, Q(3, "a"));
        var fake = new FakeLangSftpReadClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt", Versions(dialog: 10));
        fake.SeedFile("/var/www/html/data/lang/swf/dialog_es_10.swf", swf);
        var req = new DialogEsRemoteLoadRequest
        {
            Settings = Settings(),
            PlainPassword = "x",
            ClientFactory = (_, _) => fake,
            WorkDirectory = _workDir,
        };
        var first = _session.GetOrFetch(req, forceRemote: true);
        Assert.True(first.Success, first.Error);
        var downloads = fake.DownloadCount;
        var second = _session.GetOrFetch(req, forceRemote: false);
        Assert.True(second.Success);
        Assert.Equal(downloads, fake.DownloadCount);
        _ = _session.GetOrFetch(req, forceRemote: true);
        Assert.True(fake.DownloadCount > downloads);
    }

    [Fact]
    public async Task Preview_uses_remote_snapshot_and_writes_zero_bd()
    {
        var snap = DialogEsParser.Parse(Seed(1292, Q(20024, "last")));
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.SimpleDialogTextLocal = "Hola buenas, ¿cómo estás?";
        var store = new InMemoryContentPublishStore(new ContentPublishMaxSnapshot
        {
            NpcsModelo = 20061,
            NpcPreguntas = 20023,
            NpcRespuestas = 1,
            Misiones = 1,
            MisionEtapas = 1,
            MisionObjetivos = 1,
        });
        var svc = new ContentPublishService(store, Path.Combine(_workDir, "j"));
        var (plan, _) = await svc.PreparePreviewAsync(
            ws,
            dialogEsSnapshot: snap,
            dialogEsStatusOverride: "dialog_es activo remoto: dialog,es,1292");
        Assert.False(plan.IsValid);
        var line = Assert.Single(plan.DialogEsPreview, p => p.Kind == "simple");
        Assert.Equal(20025, line.DialogQuestionId);
        Assert.Contains("ID D.q provisional: 20025", plan.FormatDialogEsPreviewBlock(), StringComparison.Ordinal);
        Assert.Contains("dialog_es actual: 1292", plan.FormatDialogEsPreviewBlock(), StringComparison.Ordinal);
        Assert.Equal(0, store.InsertCallCount);
    }
}
