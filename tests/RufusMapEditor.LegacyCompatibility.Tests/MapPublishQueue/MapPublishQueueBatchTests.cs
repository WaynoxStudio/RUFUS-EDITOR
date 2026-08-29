using RufusMapEditor.LegacyCompatibility.LangMaps;
using RufusMapEditor.LegacyCompatibility.MapPublishQueue;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapPublishQueue;

public sealed class MapPublishQueueBatchTests
{
    private static string FixtureSwf()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps_es_1282.swf");
        if (!File.Exists(path))
        {
            path = Path.Combine(FindRepoRoot(), "tests", "RufusMapEditor.LegacyCompatibility.Tests", "Fixtures",
                "maps_es_1282.swf");
        }

        Assert.True(File.Exists(path), "Fixture maps_es_1282.swf missing");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "rufus-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Queue_empty_then_add_persist_no_duplicates()
    {
        var lib = TempDir();
        try
        {
            var store = new MapPublishQueueStore();
            store.ConfigureLibraryRoot(lib);
            Assert.Equal(0, store.Count);

            // Fake official save path
            var mapDir = Path.Combine(lib, "Maps", "10420");
            Directory.CreateDirectory(mapDir);
            var rufmap = Path.Combine(mapDir, "10420.rufmap");
            File.WriteAllText(rufmap, "fake-a");

            var sha = MapPublishQueueStore.TryComputeRufmapSha256(lib, 10420)!;
            Assert.True(store.Upsert(new MapPublishQueueItem
            {
                MapId = 10420,
                RufmapSha256 = sha,
                DateMapSnapshot = "1",
                RufmapUtcTicks = 1,
                QueuedUtc = DateTimeOffset.UtcNow,
                SubAreaDefined = true,
                SubArea = 10,
                Ep = 2,
                WorldCoordinatesSet = true,
            }));
            Assert.Equal(1, store.Count);

            File.WriteAllText(rufmap, "fake-b");
            var sha2 = MapPublishQueueStore.TryComputeRufmapSha256(lib, 10420)!;
            Assert.False(store.Upsert(new MapPublishQueueItem
            {
                MapId = 10420,
                RufmapSha256 = sha2,
                DateMapSnapshot = "2",
                RufmapUtcTicks = 2,
                QueuedUtc = DateTimeOffset.UtcNow,
                SubAreaDefined = true,
                SubArea = 11,
                Ep = 3,
                WorldCoordinatesSet = true,
            }));
            Assert.Equal(1, store.Count);
            Assert.True(store.TryGet(10420, out var item));
            Assert.Equal(sha2, item!.RufmapSha256);
            Assert.Equal(11, item.SubArea);

            // Persist across new store instance
            var store2 = new MapPublishQueueStore();
            store2.ConfigureLibraryRoot(lib);
            Assert.Equal(1, store2.Count);
            Assert.True(File.Exists(MapPublishQueueStore.GetQueuePath(lib)));

            store2.Remove(10420);
            Assert.Equal(0, store2.Count);
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Detects_modified_after_queued_and_unsaved_flag()
    {
        var lib = TempDir();
        try
        {
            var mapDir = Path.Combine(lib, "Maps", "10421");
            Directory.CreateDirectory(mapDir);
            var rufmap = Path.Combine(mapDir, "10421.rufmap");
            File.WriteAllText(rufmap, "v1");
            var sha = MapPublishQueueStore.TryComputeRufmapSha256(lib, 10421)!;
            var item = new MapPublishQueueItem
            {
                MapId = 10421,
                RufmapSha256 = sha,
                QueuedUtc = DateTimeOffset.UtcNow,
                SubAreaDefined = true,
                SubArea = 1,
                Ep = 1,
                WorldCoordinatesSet = true,
                WorldX = 0,
                WorldY = 0,
            };

            Assert.Equal(
                MapPublishQueueItemStatus.Ready,
                MapPublishQueueStore.EvaluateStatus(item, lib, hasUnsavedChangesForMap: false));

            File.WriteAllText(rufmap, "v2");
            Assert.Equal(
                MapPublishQueueItemStatus.ModifiedAfterQueued,
                MapPublishQueueStore.EvaluateStatus(item, lib, hasUnsavedChangesForMap: false));

            Assert.Equal(
                MapPublishQueueItemStatus.UnsavedChanges,
                MapPublishQueueStore.EvaluateStatus(item, lib, hasUnsavedChangesForMap: true));
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GenerateBatch_five_maps_single_version_bump()
    {
        var src = FixtureSwf();
        var outDir = TempDir();
        try
        {
            var result = LangMapsSwfService.GenerateBatch(new LangMapsBatchGenerateRequest
            {
                SourceSwfPath = src,
                OutputDirectory = outDir,
                Entries =
                [
                    new LangMapsBatchEntry { MapId = 30057, X = 1, Y = 2, SubArea = 10, Ep = 2 },
                    new LangMapsBatchEntry { MapId = 30058, X = 3, Y = 4, SubArea = 11, Ep = 2 },
                    new LangMapsBatchEntry { MapId = 30059, X = 5, Y = 6, SubArea = 12, Ep = 2 },
                    new LangMapsBatchEntry { MapId = 31324, X = 7, Y = 8, SubArea = 13, Ep = 4 }, // update existing
                    new LangMapsBatchEntry { MapId = 30060, X = 9, Y = 10, SubArea = 14, Ep = 2 },
                ],
            });

            Assert.True(result.Success, result.Error);
            Assert.Equal(1282, result.SourceVersion);
            Assert.Equal(1283, result.TargetVersion);
            Assert.Equal(Path.Combine(outDir, "maps_es_1283.swf"), result.OutputPath);

            var inspect = LangMapsSwfService.Inspect(result.OutputPath!);
            Assert.Equal(1283, inspect.Version);
            Assert.Contains(inspect.Entries, e => e.MapId == 30057 && e.X == 1 && e.Y == 2);
            Assert.Contains(inspect.Entries, e => e.MapId == 30060 && e.X == 9);
            var updated = Assert.Single(inspect.Entries, e => e.MapId == 31324);
            Assert.Equal(7, updated.X);
            Assert.Equal(8, updated.Y);
            Assert.Equal(13, updated.SubArea);
            Assert.Equal(4, updated.Ep);

            // Must NOT have created maps_es_1284..1287
            Assert.False(File.Exists(Path.Combine(outDir, "maps_es_1284.swf")));
            Assert.False(File.Exists(Path.Combine(outDir, "maps_es_1285.swf")));
            Assert.False(File.Exists(Path.Combine(outDir, "maps_es_1286.swf")));
            Assert.False(File.Exists(Path.Combine(outDir, "maps_es_1287.swf")));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PublishBatch_remote_one_versions_bump()
    {
        var swf = File.ReadAllBytes(FixtureSwf());
        var fake = new FakeLangSftpPublishClient();
        fake.SeedFile("/var/www/html/data/lang/versions_es.txt",
            "11&f=maps,es,1282|quests,es,1275|spells,es,1308|names,es,15");
        fake.SeedFile("/var/www/html/data/lang/swf/maps_es_1282.swf", swf);

        var work = TempDir();
        var backup = TempDir();
        try
        {
            var result = LangRemotePublishService.PublishBatch(new LangRemoteBatchPublishRequest
            {
                Settings = new LangSftpSettings
                {
                    Host = "t",
                    User = "u",
                    LangRemotePath = LangSftpSettings.DefaultLangRemotePath,
                    SwfRemotePath = LangSftpSettings.DefaultSwfRemotePath,
                },
                PlainPassword = "x",
                Entries =
                [
                    new LangMapsBatchEntry { MapId = 30057, X = 1, Y = 2, SubArea = 10, Ep = 5 },
                    new LangMapsBatchEntry { MapId = 30058, X = 3, Y = 4, SubArea = 11, Ep = 5 },
                ],
                WorkDirectory = work,
                BackupDirectory = backup,
                ClientFactory = (_, _) => fake,
            });

            Assert.True(result.Success, result.Error);
            Assert.Equal(1282, result.SourceVersion);
            Assert.Equal(1283, result.TargetVersion);
            Assert.True(result.VersionsUpdated);
            Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1283.swf"));
            Assert.True(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1282.swf"));
            Assert.False(fake.PeekExists("/var/www/html/data/lang/swf/maps_es_1284.swf"));

            var versions = fake.PeekText("/var/www/html/data/lang/versions_es.txt");
            Assert.Contains("maps,es,1283", versions, StringComparison.Ordinal);
            Assert.DoesNotContain("maps,es,1282", versions, StringComparison.Ordinal);
            Assert.Contains("quests,es,1275", versions, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* ignore */ }
            try { Directory.Delete(backup, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Queue_allows_pending_sa_and_blocks_publish_until_complete()
    {
        var lib = TempDir();
        try
        {
            var mapDir = Path.Combine(lib, "Maps", "10420");
            Directory.CreateDirectory(mapDir);
            File.WriteAllText(Path.Combine(mapDir, "10420.rufmap"), "v1");
            var sha = MapPublishQueueStore.TryComputeRufmapSha256(lib, 10420)!;

            var item = new MapPublishQueueItem
            {
                MapId = 10420,
                RufmapSha256 = sha,
                QueuedUtc = DateTimeOffset.UtcNow,
                Ep = MapPublishQueueItem.DefaultEp,
                SubAreaDefined = false,
                WorldCoordinatesSet = true,
                WorldX = 1,
                WorldY = 2,
            };

            Assert.Equal(MapPublishQueueItemStatus.MissingPublishFields,
                MapPublishQueueStore.EvaluateStatus(item, lib, false));
            Assert.Contains("Falta SubArea", MapPublishQueueStore.StatusLabel(
                MapPublishQueueItemStatus.MissingPublishFields, item));
            Assert.Contains(MapPublishQueueStore.GetPublishBlockers(item, lib, false),
                s => s.Contains("SubArea", StringComparison.OrdinalIgnoreCase));

            item.SubAreaDefined = true;
            item.SubArea = 42;
            Assert.Equal(MapPublishQueueItemStatus.Ready,
                MapPublishQueueStore.EvaluateStatus(item, lib, false));
            Assert.Empty(MapPublishQueueStore.GetPublishBlockers(item, lib, false));
            Assert.Equal(2, MapPublishQueueItem.DefaultEp);
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { /* ignore */ }
        }
    }
}
