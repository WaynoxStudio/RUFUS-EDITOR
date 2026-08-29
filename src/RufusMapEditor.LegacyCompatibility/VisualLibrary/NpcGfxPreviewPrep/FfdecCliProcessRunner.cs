using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1 — invoke JPEXS <c>ffdec-cli.exe</c> for frame PNG export.</summary>
public sealed class FfdecCliProcessRunner : IFfdecProcessRunner
{
    public FfdecRunResult RunExportFramePng(
        string ffdecCliPath,
        string swfPath,
        string outputDirectory,
        double zoom,
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

        var args = new StringBuilder();
        args.Append("-format frame:png -ignorebackground ");
        if (zoom > 1.0001)
        {
            args.Append(CultureInfo.InvariantCulture, $"-zoom {zoom} ");
        }

        args.Append(CultureInfo.InvariantCulture,
            $"-onerror abort -export frame \"{outputDirectory}\" \"{swfPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = ffdecCliPath,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ffdecCliPath) ?? Environment.CurrentDirectory,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException("No se pudo iniciar ffdec-cli.");

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
}

/// <summary>Locate the first PNG produced by FFDec under an export folder.</summary>
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
}
