using System.Diagnostics;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.2C — rasterize SVG → PNG via Inkscape CLI (dev tool, like FFDec).</summary>
public interface IInkscapeSvgRasterizer
{
    InkscapeRunResult RasterizePng(
        string inkscapeCliPath,
        string svgPath,
        string pngPath,
        bool exportAreaDrawing,
        TimeSpan timeout,
        CancellationToken ct = default);
}

public sealed class InkscapeRunResult
{
    public required bool TimedOut { get; init; }
    public required int ExitCode { get; init; }
    public bool PngProduced { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}

/// <summary>
/// Invokes Inkscape 1.x: transparent background, RGBA8.
/// Prefer <c>--export-area-drawing</c> so Stage viewBox does not clip off-stage artwork.
/// </summary>
public sealed class InkscapeCliProcessRunner : IInkscapeSvgRasterizer
{
    public InkscapeRunResult RasterizePng(
        string inkscapeCliPath,
        string svgPath,
        string pngPath,
        bool exportAreaDrawing,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inkscapeCliPath) || !File.Exists(inkscapeCliPath))
            throw new FileNotFoundException(
                "inkscape.exe no encontrado. Pasa --inkscape o RUFUS_INKSCAPE_CLI.",
                inkscapeCliPath);

        if (string.IsNullOrWhiteSpace(svgPath) || !File.Exists(svgPath))
            throw new FileNotFoundException("SVG no encontrado.", svgPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        if (File.Exists(pngPath))
            File.Delete(pngPath);

        // Inkscape 1.x: prefer long options; ArgumentList avoids broken quote parsing on Windows.
        var psi = new ProcessStartInfo
        {
            FileName = inkscapeCliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(inkscapeCliPath) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(svgPath);
        psi.ArgumentList.Add("--export-filename=" + pngPath);
        psi.ArgumentList.Add("--export-type=png");
        psi.ArgumentList.Add("--export-background-opacity=0");
        if (exportAreaDrawing)
            psi.ArgumentList.Add("--export-area-drawing");
        psi.ArgumentList.Add("--export-png-color-mode=RGBA_8");

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException("No se pudo iniciar inkscape.");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var ms = (int)Math.Clamp(timeout.TotalMilliseconds, 1000, 600_000);
        var exited = proc.WaitForExit(ms);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try { proc.WaitForExit(5000); } catch { /* ignore */ }
            return new InkscapeRunResult
            {
                TimedOut = true,
                ExitCode = -1,
                PngProduced = false,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString(),
            };
        }

        ct.ThrowIfCancellationRequested();
        var produced = File.Exists(pngPath) && new FileInfo(pngPath).Length > 0;
        return new InkscapeRunResult
        {
            TimedOut = false,
            ExitCode = proc.ExitCode,
            PngProduced = produced,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
    }

    /// <summary>Common install locations + env override.</summary>
    public static string? TryDiscoverInkscapeCli()
    {
        var env = Environment.GetEnvironmentVariable("RUFUS_INKSCAPE_CLI");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inkscape", "bin", "inkscape.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inkscape", "bin", "inkscape.exe"),
            @"C:\Program Files\Inkscape\bin\inkscape.exe",
            @"C:\Program Files (x86)\Inkscape\bin\inkscape.exe",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }
}
