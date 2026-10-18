using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1/2B/2C — invoke JPEXS <c>ffdec-cli.exe</c> for frame/sprite PNG or frame SVG.</summary>
public sealed class FfdecCliProcessRunner : IFfdecProcessRunner
{
    /// <summary>Optional managed JAVA_HOME (tools-cache JRE). When set, PATH/JAVA_HOME are injected for the child process.</summary>
    public string? JavaHome { get; set; }

    /// <summary>Optional absolute java.exe; when set with <see cref="JavaHome"/>, bin is prepended to PATH.</summary>
    public string? JavaExePath { get; set; }

    public FfdecCliProcessRunner(string? javaHome = null, string? javaExePath = null)
    {
        JavaHome = javaHome;
        JavaExePath = javaExePath;
    }

    public FfdecRunResult RunExportPng(
        string ffdecCliPath,
        string swfPath,
        string outputDirectory,
        SwfArtworkExportTargetSelector.ExportKind kind,
        int? spriteCharacterId,
        double zoom,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (kind == SwfArtworkExportTargetSelector.ExportKind.FullRootSvg)
            throw new ArgumentException("FullRootSvg usa RunExportFrameSvg + Svg.Skia, no RunExportPng.", nameof(kind));

        if (string.IsNullOrWhiteSpace(ffdecCliPath) || !File.Exists(ffdecCliPath))
            throw new FileNotFoundException(
                "ffdec-cli.exe no encontrado. Pasa --ffdec con la ruta real (herramienta de desarrollo).",
                ffdecCliPath);

        if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
            throw new FileNotFoundException("SWF no encontrado.", swfPath);

        if (kind == SwfArtworkExportTargetSelector.ExportKind.Sprite
            && (spriteCharacterId is null or <= 0))
            throw new ArgumentException("spriteCharacterId requerido para export sprite.", nameof(spriteCharacterId));

        Directory.CreateDirectory(outputDirectory);

        var args = new StringBuilder();
        if (kind == SwfArtworkExportTargetSelector.ExportKind.Sprite)
        {
            args.Append(CultureInfo.InvariantCulture,
                $"-format sprite:png -ignorebackground -selectid {spriteCharacterId!.Value} ");
        }
        else
        {
            args.Append("-format frame:png -ignorebackground ");
        }

        if (zoom > 1.0001)
            args.Append(CultureInfo.InvariantCulture, $"-zoom {zoom} ");

        var exportType = kind == SwfArtworkExportTargetSelector.ExportKind.Sprite ? "sprite" : "frame";
        args.Append(CultureInfo.InvariantCulture,
            $"-onerror abort -export {exportType} \"{outputDirectory}\" \"{swfPath}\"");

        return RunFfdec(ffdecCliPath, args.ToString(), timeout, ct);
    }

    public FfdecRunResult RunExportFrameSvg(
        string ffdecCliPath,
        string swfPath,
        string outputDirectory,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ffdecCliPath) || !File.Exists(ffdecCliPath))
            throw new FileNotFoundException(
                "ffdec-cli.exe no encontrado. Pasa --ffdec con la ruta real (herramienta de desarrollo).",
                ffdecCliPath);

        if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
            throw new FileNotFoundException("SWF no encontrado.", swfPath);

        Directory.CreateDirectory(outputDirectory);
        var args = string.Create(CultureInfo.InvariantCulture,
            $"-format frame:svg -ignorebackground -onerror abort -export frame \"{outputDirectory}\" \"{swfPath}\"");
        return RunFfdec(ffdecCliPath, args, timeout, ct);
    }

    private FfdecRunResult RunFfdec(
        string ffdecCliPath,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var workDir = Path.GetDirectoryName(ffdecCliPath) ?? Environment.CurrentDirectory;
        var jar = Path.Combine(workDir, "ffdec.jar");
        ProcessStartInfo psi;

        // Managed JRE: invoke java.exe -jar ffdec.jar (bypass Launch4j registry / PATH / Program Files).
        if (!string.IsNullOrWhiteSpace(JavaExePath) && File.Exists(JavaExePath) && File.Exists(jar))
        {
            var home = !string.IsNullOrWhiteSpace(JavaHome) && Directory.Exists(JavaHome)
                ? JavaHome!
                : Directory.GetParent(Path.GetDirectoryName(JavaExePath)!)?.FullName ?? "";

            psi = new ProcessStartInfo
            {
                FileName = JavaExePath,
                Arguments = string.Create(
                    CultureInfo.InvariantCulture,
                    $"-Djava.awt.headless=true -Djna.nosys=true -Djava.net.preferIPv4Stack=true -jar \"{jar}\" {arguments}"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };

            psi.Environment["JAVA_HOME"] = home;
            var bin = Path.GetDirectoryName(JavaExePath) ?? "";
            psi.Environment["PATH"] = bin + Path.PathSeparator + @"C:\Windows\System32";
        }
        else
        {
            // DEV fallback: ffdec-cli.exe (may use system Java via Launch4j).
            psi = new ProcessStartInfo
            {
                FileName = ffdecCliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };
            ApplyManagedJavaEnvironment(psi);
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException("No se pudo iniciar FFDec.");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var ms = (int)Math.Clamp(timeout.TotalMilliseconds, 1000, 600_000);
        var exited = proc.WaitForExit(ms);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try { proc.WaitForExit(5000); } catch { /* ignore */ }
            return new FfdecRunResult
            {
                TimedOut = true,
                ExitCode = -1,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
            };
        }

        ct.ThrowIfCancellationRequested();
        return new FfdecRunResult
        {
            TimedOut = false,
            ExitCode = proc.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
    }

    private void ApplyManagedJavaEnvironment(ProcessStartInfo psi)
    {
        var home = JavaHome;
        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
            return;

        psi.Environment["JAVA_HOME"] = home;
        var bin = Path.Combine(home, "bin");
        if (!string.IsNullOrWhiteSpace(JavaExePath))
        {
            var exeDir = Path.GetDirectoryName(JavaExePath);
            if (!string.IsNullOrWhiteSpace(exeDir))
                bin = exeDir;
        }

        if (Directory.Exists(bin))
        {
            var path = psi.Environment.TryGetValue("PATH", out var existing) ? existing : "";
            psi.Environment["PATH"] = bin + Path.PathSeparator + path;
        }
    }
}

/// <summary>Locate the first PNG/SVG produced by FFDec under an export folder.</summary>
public static class FfdecExportLocator
{
    public static string? FindFirstPng(string exportDirectory)
    {
        if (!Directory.Exists(exportDirectory))
            return null;
        return Directory.EnumerateFiles(exportDirectory, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string? FindFirstSvg(string exportDirectory)
    {
        if (!Directory.Exists(exportDirectory))
            return null;
        return Directory.EnumerateFiles(exportDirectory, "*.svg", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
