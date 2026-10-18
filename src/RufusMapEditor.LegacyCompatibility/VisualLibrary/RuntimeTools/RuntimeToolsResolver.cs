using System.IO.Compression;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.RuntimeTools;

/// <summary>
/// CONTENT.ITEMS.RUNTIME_TOOLS / RUNTIME_JRE — LocalAppData tools-cache for FFDec + managed JRE.
/// </summary>
public static class RuntimeToolsPaths
{
    public const string AppFolderName = "RufusMapEditor";
    public const string ToolsCacheFolderName = "tools-cache";
    public const string FfdecFolderName = "ffdec";
    public const string JreFolderName = "jre";
    public const string PayloadFileName = "ffdec-runtime.zip";
    public const string JrePayloadFileName = "jre-runtime.zip";
    public const string VersionMarkerFileName = "runtime.version";

    /// <summary>Payload id written to ffdec/runtime.version after a successful extract.</summary>
    public const string FfdecPayloadVersion = "ffdec-26.2.1-min1";

    /// <summary>Payload id written to jre/runtime.version (Eclipse Temurin JRE 21).</summary>
    public const string JrePayloadVersion = "temurin-jre-21.0.12.1+1";

    public static string AppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

    public static string ToolsCacheRoot =>
        Path.Combine(AppDataRoot, ToolsCacheFolderName);

    public static string FfdecCacheDirectory =>
        Path.Combine(ToolsCacheRoot, FfdecFolderName);

    public static string JreCacheDirectory =>
        Path.Combine(ToolsCacheRoot, JreFolderName);

    public static string FfdecVersionMarkerPath =>
        Path.Combine(FfdecCacheDirectory, VersionMarkerFileName);

    public static string JreVersionMarkerPath =>
        Path.Combine(JreCacheDirectory, VersionMarkerFileName);

    public static string FfdecCliPath =>
        Path.Combine(FfdecCacheDirectory, "ffdec-cli.exe");
}

public enum RuntimeToolsAvailability
{
    Ready,
    ToolsUnavailable,
    FfdecUnavailable,
    JavaUnavailable,
    PrepareFailed,
}

public sealed class RuntimeToolsResolveResult
{
    public required RuntimeToolsAvailability Availability { get; init; }
    public string? FfdecCliPath { get; init; }
    public string? JavaHome { get; init; }
    public string? JavaExePath { get; init; }
    public bool FfdecPrepared { get; init; }
    public bool JavaManaged { get; init; }
    public string? CacheVersion { get; init; }
    public string? JreCacheVersion { get; init; }
    public string? Reason { get; init; }

    /// <summary>True only when FFDec + managed JRE are ready (no system Java fallback).</summary>
    public bool CanRunFfdec =>
        Availability == RuntimeToolsAvailability.Ready
        && !string.IsNullOrWhiteSpace(FfdecCliPath)
        && File.Exists(FfdecCliPath!)
        && !string.IsNullOrWhiteSpace(JavaExePath)
        && File.Exists(JavaExePath!)
        && JavaManaged;
}

/// <summary>Lazy prepare / resolve FFDec + managed Temurin JRE under tools-cache.</summary>
public sealed class RuntimeToolsResolver
{
    private readonly string? _overrideCacheRoot;
    private readonly string? _overridePayloadZip;
    private readonly string? _overrideJrePayloadZip;
    private readonly string? _overrideDevFfdecDir;
    private readonly string? _overrideDevJreDir;

    public RuntimeToolsResolver(
        string? overrideCacheRoot = null,
        string? overridePayloadZip = null,
        string? overrideDevFfdecDir = null,
        string? overrideJrePayloadZip = null,
        string? overrideDevJreDir = null)
    {
        _overrideCacheRoot = overrideCacheRoot;
        _overridePayloadZip = overridePayloadZip;
        _overrideDevFfdecDir = overrideDevFfdecDir;
        _overrideJrePayloadZip = overrideJrePayloadZip;
        _overrideDevJreDir = overrideDevJreDir;
    }

    public string CacheRoot =>
        string.IsNullOrWhiteSpace(_overrideCacheRoot)
            ? RuntimeToolsPaths.ToolsCacheRoot
            : _overrideCacheRoot!;

    public string FfdecDir => Path.Combine(CacheRoot, RuntimeToolsPaths.FfdecFolderName);
    public string JreDir => Path.Combine(CacheRoot, RuntimeToolsPaths.JreFolderName);

    /// <summary>
    /// Ensure FFDec + JRE caches are prepared (lazy). Does not download.
    /// Soft result — never throws for missing tools.
    /// </summary>
    public RuntimeToolsResolveResult EnsureReady()
    {
        try
        {
            var ffdecPrep = EnsureFfdecPrepared();
            if (!ffdecPrep.Ok)
            {
                return new RuntimeToolsResolveResult
                {
                    Availability = RuntimeToolsAvailability.FfdecUnavailable,
                    FfdecPrepared = false,
                    Reason = ffdecPrep.Error ?? "FFDec no disponible",
                };
            }

            var cli = Path.Combine(FfdecDir, "ffdec-cli.exe");
            if (!File.Exists(cli) || !File.Exists(Path.Combine(FfdecDir, "ffdec.jar")))
            {
                return new RuntimeToolsResolveResult
                {
                    Availability = RuntimeToolsAvailability.FfdecUnavailable,
                    FfdecPrepared = false,
                    Reason = "ffdec-cli.exe / ffdec.jar ausente tras prepare",
                };
            }

            var jrePrep = EnsureJrePrepared();
            if (!jrePrep.Ok)
            {
                return new RuntimeToolsResolveResult
                {
                    Availability = RuntimeToolsAvailability.JavaUnavailable,
                    FfdecCliPath = cli,
                    FfdecPrepared = true,
                    JavaManaged = false,
                    CacheVersion = ReadFfdecVersionMarker(),
                    Reason = jrePrep.Error ?? "JavaUnavailable — JRE gestionado ausente",
                };
            }

            var java = TryResolveManagedJavaFromCacheOnly();
            if (java.JavaHome is null || java.JavaExePath is null)
            {
                return new RuntimeToolsResolveResult
                {
                    Availability = RuntimeToolsAvailability.JavaUnavailable,
                    FfdecCliPath = cli,
                    FfdecPrepared = true,
                    JavaManaged = false,
                    CacheVersion = ReadFfdecVersionMarker(),
                    JreCacheVersion = ReadJreVersionMarker(),
                    Reason = "JavaUnavailable — java.exe no resuelto en tools-cache/jre",
                };
            }

            return new RuntimeToolsResolveResult
            {
                Availability = RuntimeToolsAvailability.Ready,
                FfdecCliPath = cli,
                JavaHome = java.JavaHome,
                JavaExePath = java.JavaExePath,
                FfdecPrepared = true,
                JavaManaged = true,
                CacheVersion = ReadFfdecVersionMarker(),
                JreCacheVersion = ReadJreVersionMarker(),
                Reason = "OK",
            };
        }
        catch (Exception ex)
        {
            return new RuntimeToolsResolveResult
            {
                Availability = RuntimeToolsAvailability.PrepareFailed,
                Reason = "PrepareFailed: " + ex.GetType().Name + " — " + ex.Message,
            };
        }
    }

    private (bool Ok, string? Error) EnsureFfdecPrepared()
    {
        var marker = Path.Combine(FfdecDir, RuntimeToolsPaths.VersionMarkerFileName);
        var cli = Path.Combine(FfdecDir, "ffdec-cli.exe");
        var jar = Path.Combine(FfdecDir, "ffdec.jar");
        var lib = Path.Combine(FfdecDir, "lib");

        if (File.Exists(marker)
            && File.Exists(cli)
            && File.Exists(jar)
            && Directory.Exists(lib)
            && string.Equals(
                File.ReadAllText(marker).Trim(),
                RuntimeToolsPaths.FfdecPayloadVersion,
                StringComparison.Ordinal))
        {
            return (true, null);
        }

        var zip = FindFfdecPayloadZip();
        if (!string.IsNullOrWhiteSpace(zip) && File.Exists(zip))
        {
            ExtractZipFlatteningSingleRoot(zip, FfdecDir, "ffdec-cli.exe");
            WriteFfdecVersionMarker();
            return ValidateFfdecLayout();
        }

        var devDir = FindDevFfdecDirectory();
        if (!string.IsNullOrWhiteSpace(devDir) && Directory.Exists(devDir))
        {
            CopyMinimalFfdec(devDir!, FfdecDir);
            WriteFfdecVersionMarker();
            return ValidateFfdecLayout();
        }

        return (false, "ToolsUnavailable — sin payload ffdec-runtime.zip ni tools/_ffdec DEV");
    }

    private (bool Ok, string? Error) EnsureJrePrepared()
    {
        var marker = Path.Combine(JreDir, RuntimeToolsPaths.VersionMarkerFileName);
        var javaExe = FindJavaExeUnder(JreDir);
        if (File.Exists(marker)
            && javaExe is not null
            && string.Equals(
                File.ReadAllText(marker).Trim(),
                RuntimeToolsPaths.JrePayloadVersion,
                StringComparison.Ordinal))
        {
            return (true, null);
        }

        var zip = FindJrePayloadZip();
        if (!string.IsNullOrWhiteSpace(zip) && File.Exists(zip))
        {
            ExtractZipFlatteningSingleRoot(zip, JreDir, Path.Combine("bin", "java.exe"));
            WriteJreVersionMarker();
            return ValidateJreLayout();
        }

        var devDir = FindDevJreDirectory();
        if (!string.IsNullOrWhiteSpace(devDir) && Directory.Exists(devDir))
        {
            if (Directory.Exists(JreDir))
            {
                try { Directory.Delete(JreDir, true); } catch { /* ignore */ }
            }

            CopyDirectory(devDir!, JreDir);
            WriteJreVersionMarker();
            return ValidateJreLayout();
        }

        return (false, "JavaUnavailable — sin payload jre-runtime.zip ni tools/_jre DEV");
    }

    private (bool Ok, string? Error) ValidateFfdecLayout()
    {
        var cli = Path.Combine(FfdecDir, "ffdec-cli.exe");
        var jar = Path.Combine(FfdecDir, "ffdec.jar");
        var lib = Path.Combine(FfdecDir, "lib");
        if (!File.Exists(cli))
            return (false, "ffdec-cli.exe faltante");
        if (!File.Exists(jar))
            return (false, "ffdec.jar faltante");
        if (!Directory.Exists(lib) || !Directory.EnumerateFiles(lib, "*.jar").Any())
            return (false, "lib/ faltante o vacía");
        if (!File.Exists(Path.Combine(FfdecDir, RuntimeToolsPaths.VersionMarkerFileName)))
            WriteFfdecVersionMarker();
        return (true, null);
    }

    private (bool Ok, string? Error) ValidateJreLayout()
    {
        var javaExe = FindJavaExeUnder(JreDir);
        if (javaExe is null)
            return (false, "bin/java.exe faltante en JRE cache");
        if (!File.Exists(Path.Combine(JreDir, RuntimeToolsPaths.VersionMarkerFileName)))
            WriteJreVersionMarker();
        return (true, null);
    }

    private (string? JavaHome, string? JavaExePath) TryResolveManagedJavaFromCacheOnly()
    {
        // Prefer RUFUS_JAVA_HOME only when it points inside our cache root (test overrides).
        var envHome = Environment.GetEnvironmentVariable("RUFUS_JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            var fullHome = Path.GetFullPath(envHome.Trim());
            var exe = Path.Combine(fullHome, "bin", "java.exe");
            if (File.Exists(exe)
                && fullHome.StartsWith(Path.GetFullPath(CacheRoot), StringComparison.OrdinalIgnoreCase))
            {
                return (fullHome, exe);
            }
        }

        var javaExe = FindJavaExeUnder(JreDir);
        if (javaExe is null)
            return (null, null);

        var bin = Path.GetDirectoryName(javaExe);
        var home = bin is null ? null : Directory.GetParent(bin)?.FullName;
        return string.IsNullOrWhiteSpace(home) ? (null, null) : (home, javaExe);
    }

    private static string? FindJavaExeUnder(string root)
    {
        if (!Directory.Exists(root))
            return null;

        var direct = Path.Combine(root, "bin", "java.exe");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories)
            .OrderBy(p => p.Length)
            .FirstOrDefault();
    }

    private string? FindFfdecPayloadZip()
    {
        if (_overridePayloadZip is not null)
            return File.Exists(_overridePayloadZip) ? _overridePayloadZip : null;

        var env = Environment.GetEnvironmentVariable("RUFUS_FFDEC_PAYLOAD_ZIP");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        foreach (var baseDir in EnumerateProbeDirectories())
        {
            foreach (var c in new[]
                     {
                         Path.Combine(baseDir, "payload", RuntimeToolsPaths.PayloadFileName),
                         Path.Combine(baseDir, RuntimeToolsPaths.PayloadFileName),
                     })
            {
                if (File.Exists(c))
                    return c;
            }
        }

        return null;
    }

    private string? FindJrePayloadZip()
    {
        if (_overrideJrePayloadZip is not null)
            return File.Exists(_overrideJrePayloadZip) ? _overrideJrePayloadZip : null;

        var env = Environment.GetEnvironmentVariable("RUFUS_JRE_PAYLOAD_ZIP");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        foreach (var baseDir in EnumerateProbeDirectories())
        {
            foreach (var c in new[]
                     {
                         Path.Combine(baseDir, "payload", RuntimeToolsPaths.JrePayloadFileName),
                         Path.Combine(baseDir, RuntimeToolsPaths.JrePayloadFileName),
                     })
            {
                if (File.Exists(c))
                    return c;
            }
        }

        return null;
    }

    private string? FindDevFfdecDirectory()
    {
        if (_overrideDevFfdecDir is not null)
            return Directory.Exists(_overrideDevFfdecDir) ? _overrideDevFfdecDir : null;

        var env = Environment.GetEnvironmentVariable("RUFUS_FFDEC_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        foreach (var baseDir in EnumerateProbeDirectories())
        {
            var candidate = Path.Combine(baseDir, "tools", "_ffdec");
            if (File.Exists(Path.Combine(candidate, "ffdec-cli.exe"))
                && File.Exists(Path.Combine(candidate, "ffdec.jar")))
                return candidate;

            var extracted = Path.Combine(candidate, "extracted");
            if (File.Exists(Path.Combine(extracted, "ffdec-cli.exe"))
                && File.Exists(Path.Combine(extracted, "ffdec.jar")))
                return extracted;
        }

        return null;
    }

    private string? FindDevJreDirectory()
    {
        if (_overrideDevJreDir is not null)
            return Directory.Exists(_overrideDevJreDir) && File.Exists(Path.Combine(_overrideDevJreDir, "bin", "java.exe"))
                ? _overrideDevJreDir
                : null;

        var env = Environment.GetEnvironmentVariable("RUFUS_JRE_DIR");
        if (!string.IsNullOrWhiteSpace(env)
            && File.Exists(Path.Combine(env.Trim(), "bin", "java.exe")))
            return Path.GetFullPath(env.Trim());

        foreach (var baseDir in EnumerateProbeDirectories())
        {
            var root = Path.Combine(baseDir, "tools", "_jre");
            var named = Path.Combine(root, "temurin-21.0.12.1+1-jre");
            if (File.Exists(Path.Combine(named, "bin", "java.exe")))
                return named;

            if (File.Exists(Path.Combine(root, "bin", "java.exe")))
                return root;

            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, "bin", "java.exe")))
                        return dir;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProbeDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Push(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full = Path.GetFullPath(p);
                if (seen.Add(full))
                    list.Add(full);
            }
            catch
            {
                // ignore
            }
        }

        Push(AppContext.BaseDirectory);
        Push(Environment.CurrentDirectory);

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
                {
                    Push(dir.FullName);
                    if (Directory.Exists(Path.Combine(dir.FullName, "tools", "_ffdec")))
                        break;
                }
            }
            catch
            {
                // ignore
            }
        }

        return list;
    }

    private static void ExtractZipFlatteningSingleRoot(string zipPath, string destDir, string sentinelRelative)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destDir))!);
        if (Directory.Exists(destDir))
        {
            try { Directory.Delete(destDir, recursive: true); }
            catch
            {
                // best-effort
            }
        }

        Directory.CreateDirectory(destDir);
        ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);

        var sentinel = Path.Combine(destDir, sentinelRelative);
        if (File.Exists(sentinel))
            return;

        var nested = Directory.GetDirectories(destDir);
        if (nested.Length == 1 && File.Exists(Path.Combine(nested[0], sentinelRelative)))
        {
            var staging = destDir + ".flatten-" + Guid.NewGuid().ToString("N");
            Directory.Move(nested[0], staging);
            foreach (var leftover in Directory.GetFileSystemEntries(destDir))
            {
                try
                {
                    if (Directory.Exists(leftover)) Directory.Delete(leftover, true);
                    else File.Delete(leftover);
                }
                catch
                {
                    // ignore
                }
            }

            foreach (var entry in Directory.GetFileSystemEntries(staging))
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(destDir, name);
                Directory.Move(entry, target);
            }

            try { Directory.Delete(staging, true); } catch { /* ignore */ }
        }
    }

    private static void CopyMinimalFfdec(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var name in new[] { "ffdec-cli.exe", "ffdec-cli.jar", "ffdec.jar" })
        {
            var src = Path.Combine(sourceDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(destDir, name), overwrite: true);
        }

        var libSrc = Path.Combine(sourceDir, "lib");
        var libDst = Path.Combine(destDir, "lib");
        if (Directory.Exists(libSrc))
        {
            if (Directory.Exists(libDst))
                Directory.Delete(libDst, true);
            CopyDirectory(libSrc, libDst);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private void WriteFfdecVersionMarker()
    {
        Directory.CreateDirectory(FfdecDir);
        File.WriteAllText(
            Path.Combine(FfdecDir, RuntimeToolsPaths.VersionMarkerFileName),
            RuntimeToolsPaths.FfdecPayloadVersion + Environment.NewLine,
            Encoding.UTF8);
    }

    private void WriteJreVersionMarker()
    {
        Directory.CreateDirectory(JreDir);
        File.WriteAllText(
            Path.Combine(JreDir, RuntimeToolsPaths.VersionMarkerFileName),
            RuntimeToolsPaths.JrePayloadVersion + Environment.NewLine,
            Encoding.UTF8);
    }

    private string? ReadFfdecVersionMarker()
    {
        var path = Path.Combine(FfdecDir, RuntimeToolsPaths.VersionMarkerFileName);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path).Trim(); }
        catch { return null; }
    }

    private string? ReadJreVersionMarker()
    {
        var path = Path.Combine(JreDir, RuntimeToolsPaths.VersionMarkerFileName);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path).Trim(); }
        catch { return null; }
    }

    /// <summary>Build a minimal FFDec zip (CLI + jar + lib) for ADMIN payload packaging.</summary>
    public static void PackMinimalFfdecZip(string ffdecSourceDir, string zipPath)
    {
        if (!Directory.Exists(ffdecSourceDir))
            throw new DirectoryNotFoundException(ffdecSourceDir);

        var staging = Path.Combine(Path.GetTempPath(), "ffdec-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyMinimalFfdec(ffdecSourceDir, staging);
            File.WriteAllText(
                Path.Combine(staging, RuntimeToolsPaths.VersionMarkerFileName),
                RuntimeToolsPaths.FfdecPayloadVersion + Environment.NewLine,
                Encoding.UTF8);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { /* ignore */ }
        }
    }
}
