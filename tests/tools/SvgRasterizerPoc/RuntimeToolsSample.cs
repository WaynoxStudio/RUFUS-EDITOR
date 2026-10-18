using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.RuntimeTools;

namespace SvgRasterizerPoc;

/// <summary>CONTENT.ITEMS.RUNTIME_JRE.INTEGRATE.1 — managed JRE isolation sample (no ItemPicker).</summary>
internal static class RuntimeToolsSample
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var repoRoot = FindRepoRoot();
        var clips = ResolveClips();
        if (string.IsNullOrWhiteSpace(clips) || !Directory.Exists(clips))
        {
            Console.Error.WriteLine("ERROR: ClipsRoot no resuelto.");
            return 2;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var outRoot = Path.Combine(
            repoRoot, "tests", "tools", "artifacts", "item-icon-prep",
            "runtime-jre-1-" + stamp);
        var staging = Path.Combine(outRoot, "staging");
        var promoteLib = Path.Combine(outRoot, "promote-library-temp");
        var cacheRoot = Path.Combine(outRoot, "tools-cache-temp");
        Directory.CreateDirectory(outRoot);

        var ffdecZip = Path.Combine(outRoot, "ffdec-runtime.zip");
        var jreZip = Path.Combine(outRoot, "jre-runtime.zip");
        var ffdecSrc = Path.Combine(repoRoot, "tools", "_ffdec");
        if (!File.Exists(Path.Combine(ffdecSrc, "ffdec-cli.exe")))
            ffdecSrc = Path.Combine(ffdecSrc, "extracted");
        RuntimeToolsResolver.PackMinimalFfdecZip(ffdecSrc, ffdecZip);

        var officialJreZip = Path.Combine(
            repoRoot, "tools", "_jre",
            "OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip");
        if (!File.Exists(officialJreZip))
        {
            Console.Error.WriteLine("ERROR: falta " + officialJreZip);
            return 2;
        }

        File.Copy(officialJreZip, jreZip, overwrite: true);

        // Isolate this process from system Java env (still may exist on disk; runner must not use it).
        Environment.SetEnvironmentVariable("JAVA_HOME", null);
        Environment.SetEnvironmentVariable("JDK_HOME", null);
        Environment.SetEnvironmentVariable("RUFUS_JAVA_HOME", null);
        var pathParts = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.IndexOf("Java", StringComparison.OrdinalIgnoreCase) < 0
                        && p.IndexOf("jdk", StringComparison.OrdinalIgnoreCase) < 0
                        && p.IndexOf("jre", StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, pathParts));

        var resolver = new RuntimeToolsResolver(
            overrideCacheRoot: cacheRoot,
            overridePayloadZip: ffdecZip,
            overrideDevFfdecDir: Path.Combine(cacheRoot, "no-dev-ffdec"),
            overrideJrePayloadZip: jreZip,
            overrideDevJreDir: Path.Combine(cacheRoot, "no-dev-jre"));

        Console.WriteLine("RUNTIME_JRE.1 SAMPLE");
        Console.WriteLine("Out: " + outRoot);

        var ready = resolver.EnsureReady();
        Console.WriteLine($"EnsureReady: {ready.Availability} javaManaged={ready.JavaManaged}");
        Console.WriteLine("  ffdecVer=" + ready.CacheVersion + " jreVer=" + ready.JreCacheVersion);
        Console.WriteLine("  " + ready.Reason);
        Console.WriteLine("  java=" + ready.JavaExePath);

        if (!ready.CanRunFfdec || ready.JavaExePath is null)
        {
            Console.Error.WriteLine("FAIL: tools not ready");
            return 1;
        }

        var verPsi = new ProcessStartInfo
        {
            FileName = ready.JavaExePath,
            Arguments = "-version",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        verPsi.Environment["PATH"] = Path.GetDirectoryName(ready.JavaExePath) + Path.PathSeparator + @"C:\Windows\System32";
        verPsi.Environment["JAVA_HOME"] = ready.JavaHome ?? "";
        using var verProc = Process.Start(verPsi)!;
        var verOut = verProc.StandardError.ReadToEnd() + verProc.StandardOutput.ReadToEnd();
        verProc.WaitForExit(15000);
        File.WriteAllText(Path.Combine(outRoot, "java-version.txt"), verOut, Encoding.UTF8);
        Console.WriteLine("java -version:\n" + verOut.Trim());

        var again = resolver.EnsureReady();
        Console.WriteLine($"EnsureReady(2): {again.Availability} (lazy skip)");

        const string key = "items/11/92.swf";
        var svc = new ItemIconSinglePreviewService();
        var gen = svc.GenerateOneWithRuntimeTools(
            clips,
            staging,
            key,
            tools: resolver,
            libraryRoot: null,
            processTimeout: TimeSpan.FromSeconds(90));

        var promo = ItemIconSinglePreviewService.PromoteOne(gen, promoteLib, overwrite: true);
        var pngOk = !string.IsNullOrWhiteSpace(gen.OutputPng) && File.Exists(gen.OutputPng);
        if (pngOk)
            File.Copy(gen.OutputPng!, Path.Combine(outRoot, "items_11_92.png"), overwrite: true);

        var usedManagedOnly =
            gen.JavaManaged
            && !string.IsNullOrWhiteSpace(gen.FfdecCliPath)
            && gen.FfdecCliPath!.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase)
            && ready.JavaExePath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase);

        var summary =
            $"key={key}\nstatus={gen.Status}\nmethod={gen.ExportMethod}\ntools={gen.ToolsAvailability}\n" +
            $"javaManaged={gen.JavaManaged}\nusedManagedOnly={usedManagedOnly}\npng={pngOk}\npromote={promo.Ok}\n" +
            $"reason={gen.Reason}\n";
        File.WriteAllText(Path.Combine(outRoot, "summary.txt"), summary, Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(outRoot, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                outRoot,
                ready,
                againAvailability = again.Availability,
                gen.PreviewKey,
                Status = gen.Status.ToString(),
                gen.ExportMethod,
                gen.OutputPng,
                gen.ToolsAvailability,
                gen.JavaManaged,
                gen.FfdecCliPath,
                UsedManagedJavaOnly = usedManagedOnly,
                PromoteOk = promo.Ok,
                promo.DestinationPng,
            }, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        Console.WriteLine(summary);
        Console.WriteLine("DONE → " + outRoot);
        _ = args;
        var ok = gen.Status is NpcGfxPreviewPrepStatus.Ok or NpcGfxPreviewPrepStatus.Review
                 && usedManagedOnly
                 && pngOk;
        return ok ? 0 : 1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RufusMapEditor.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, "src", "RufusMapEditor.LegacyCompatibility")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ResolveClips()
    {
        var env = Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor", "settings.json");
        if (File.Exists(settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settings));
                if (doc.RootElement.TryGetProperty("ClipsRootPath", out var el)
                    && el.ValueKind == JsonValueKind.String
                    && Directory.Exists(el.GetString()!))
                    return el.GetString();
            }
            catch
            {
                // ignore
            }
        }

        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "RUFUS RETRO", "resources", "app", "retroclient", "clips");
        return Directory.Exists(desktop) ? desktop : null;
    }
}
