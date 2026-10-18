using System.Globalization;
using System.Text;
using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

namespace SvgRasterizerPoc;

/// <summary>CONTENT.ITEMS.SVG_RASTERIZER.INTEGRATE.1 — controlled real-pipeline sample (not ItemPicker).</summary>
internal static class IntegratePipelineSample
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var repoRoot = FindRepoRoot();
        var ffdec = Path.Combine(repoRoot, "tools", "_ffdec", "extracted", "ffdec-cli.exe");
        if (!File.Exists(ffdec))
            ffdec = Path.Combine(repoRoot, "tools", "_ffdec", "ffdec-cli.exe");
        if (!File.Exists(ffdec))
        {
            Console.Error.WriteLine("ERROR: ffdec-cli no encontrado.");
            return 2;
        }

        var clips = ResolveClips();
        if (string.IsNullOrWhiteSpace(clips) || !Directory.Exists(clips))
        {
            Console.Error.WriteLine("ERROR: ClipsRoot no resuelto.");
            return 2;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var outRoot = Path.Combine(
            repoRoot, "tests", "tools", "artifacts", "item-icon-prep",
            "svg-skia-integrate-1-" + stamp);
        var staging = Path.Combine(outRoot, "staging");
        var promoteLib = Path.Combine(outRoot, "promote-library-temp");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(promoteLib);

        var keys = new[]
        {
            "items/11/92.swf",
            "items/114/2.swf",
            "items/82/1.swf",
            "items/82/10.swf",
            "items/10/13.swf",
            "items/10/16.swf",
            "items/1/127.swf", // frame
            "items/1/137.swf", // frame
            "items/1/106.swf", // sprite
            "items/1/113.swf", // sprite
        };

        Console.WriteLine("INTEGRATE.1 SAMPLE");
        Console.WriteLine("Out: " + outRoot);
        Console.WriteLine("FFDec: " + ffdec);
        Console.WriteLine("Clips: " + clips);

        var svc = new ItemIconSinglePreviewService();
        var rows = new List<object>();
        var sb = new StringBuilder();
        sb.AppendLine("CONTENT.ITEMS.SVG_RASTERIZER.INTEGRATE.1 — sample");
        sb.AppendLine("Staging: " + staging);
        sb.AppendLine("Promote temp lib: " + promoteLib);
        sb.AppendLine();

        foreach (var key in keys)
        {
            Console.WriteLine("=== " + key + " ===");
            var gen = svc.GenerateOne(ffdec, clips, staging, key, libraryRoot: null, TimeSpan.FromSeconds(90));
            var promo = ItemIconSinglePreviewService.PromoteOne(gen, promoteLib, overwrite: true, includeReview: true);

            var pngCopy = "";
            if (!string.IsNullOrWhiteSpace(gen.OutputPng) && File.Exists(gen.OutputPng))
            {
                var dest = Path.Combine(outRoot, "png", (gen.PreviewStem ?? "x") + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(gen.OutputPng, dest, overwrite: true);
                pngCopy = dest;
            }

            var line =
                $"{key} status={gen.Status} method={gen.ExportMethod ?? "-"} png={(gen.OutputPng is null ? "NO" : "YES")} promote={(promo.Ok ? "OK" : "NO")}";
            Console.WriteLine("  " + line);
            if (!string.IsNullOrWhiteSpace(gen.Reason))
                Console.WriteLine("  reason: " + gen.Reason);
            sb.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(gen.Reason))
                sb.AppendLine("  " + gen.Reason);

            rows.Add(new
            {
                gen.PreviewKey,
                gen.PreviewStem,
                Status = gen.Status.ToString().ToUpperInvariant(),
                gen.ExportMethod,
                gen.ExportSpriteId,
                gen.OutputPng,
                ArtifactPng = pngCopy,
                gen.Width,
                gen.Height,
                gen.FileBytes,
                gen.Reason,
                PromoteOk = promo.Ok,
                promo.DestinationPng,
                PromoteReason = promo.Reason,
            });
        }

        var failed = rows.Count(r =>
        {
            var s = (string)r.GetType().GetProperty("Status")!.GetValue(r)!;
            return s is "FAILED" or "NOARTWORK";
        });

        sb.AppendLine();
        sb.AppendLine($"FAILED/NO_ARTWORK count (by Status string): ver manifest");
        File.WriteAllText(Path.Combine(outRoot, "summary.txt"), sb.ToString(), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(outRoot, "manifest.json"),
            JsonSerializer.Serialize(new { OutRoot = outRoot, Clips = clips, Ffdec = ffdec, Entries = rows },
                new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine("DONE → " + outRoot);
        _ = failed;
        _ = args;
        return 0;
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
                    && !string.IsNullOrWhiteSpace(el.GetString())
                    && Directory.Exists(el.GetString()!))
                {
                    return el.GetString();
                }
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
