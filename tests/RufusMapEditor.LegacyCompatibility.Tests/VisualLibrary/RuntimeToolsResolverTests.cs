using RufusMapEditor.LegacyCompatibility.VisualLibrary.RuntimeTools;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class RuntimeToolsResolverTests
{
    [Fact]
    public void EnsureReady_missing_payload_returns_soft_failure()
    {
        var cache = Path.Combine(Path.GetTempPath(), "rufus-tools-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resolver = new RuntimeToolsResolver(
                overrideCacheRoot: cache,
                overridePayloadZip: Path.Combine(cache, "missing.zip"),
                overrideDevFfdecDir: Path.Combine(cache, "no-dev"),
                overrideJrePayloadZip: Path.Combine(cache, "missing-jre.zip"),
                overrideDevJreDir: Path.Combine(cache, "no-jre"));

            var result = resolver.EnsureReady();
            Assert.False(result.CanRunFfdec);
            Assert.True(
                result.Availability is RuntimeToolsAvailability.FfdecUnavailable
                    or RuntimeToolsAvailability.ToolsUnavailable
                    or RuntimeToolsAvailability.PrepareFailed
                    or RuntimeToolsAvailability.JavaUnavailable);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EnsureReady_from_dev_ffdec_and_jre_prepares_cache_with_version_markers()
    {
        var repo = FindRepoRoot();
        var ffdec = Path.Combine(repo, "tools", "_ffdec");
        if (!File.Exists(Path.Combine(ffdec, "ffdec-cli.exe")))
            ffdec = Path.Combine(ffdec, "extracted");
        if (!File.Exists(Path.Combine(ffdec, "ffdec-cli.exe")))
            return;

        var jre = Path.Combine(repo, "tools", "_jre", "temurin-21.0.12.1+1-jre");
        if (!File.Exists(Path.Combine(jre, "bin", "java.exe")))
            return;

        var cache = Path.Combine(Path.GetTempPath(), "rufus-tools-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var resolver = new RuntimeToolsResolver(
                overrideCacheRoot: cache,
                overridePayloadZip: Path.Combine(cache, "none.zip"),
                overrideDevFfdecDir: ffdec,
                overrideJrePayloadZip: Path.Combine(cache, "none-jre.zip"),
                overrideDevJreDir: jre);

            var first = resolver.EnsureReady();
            Assert.True(first.CanRunFfdec, first.Reason);
            Assert.True(File.Exists(first.FfdecCliPath));
            Assert.True(File.Exists(first.JavaExePath));
            Assert.True(first.JavaManaged);
            Assert.Equal(RuntimeToolsPaths.FfdecPayloadVersion, first.CacheVersion);
            Assert.Equal(RuntimeToolsPaths.JrePayloadVersion, first.JreCacheVersion);

            var markerBefore = File.GetLastWriteTimeUtc(
                Path.Combine(cache, "jre", RuntimeToolsPaths.VersionMarkerFileName));
            Thread.Sleep(20);
            var second = resolver.EnsureReady();
            Assert.True(second.CanRunFfdec);
            var markerAfter = File.GetLastWriteTimeUtc(
                Path.Combine(cache, "jre", RuntimeToolsPaths.VersionMarkerFileName));
            Assert.Equal(markerBefore, markerAfter);
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* ignore */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "_ffdec")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
