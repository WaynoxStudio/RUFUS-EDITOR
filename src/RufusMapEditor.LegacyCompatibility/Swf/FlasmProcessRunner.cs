using System.Diagnostics;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Swf;

public sealed class FlasmRunResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public required string StdErr { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required string FileName { get; init; }
    public required string Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public bool TimedOut { get; init; }
}

/// <summary>
/// Runs flasm.exe via Process (no Thread.Sleep, no cmd.exe).
/// </summary>
public static class FlasmProcessRunner
{
    public const int DefaultTimeoutMs = 120_000;

    public static FlasmRunResult Run(
        string flasmExePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(flasmExePath))
            throw new FileNotFoundException("Flasm no encontrado.", flasmExePath);
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Directorio de trabajo Flasm inexistente: {workingDirectory}");

        var psi = new ProcessStartInfo
        {
            FileName = flasmExePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("No se pudo iniciar flasm.exe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var reg = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        });

        var exited = process.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            process.WaitForExit(5_000);
            sw.Stop();
            return new FlasmRunResult
            {
                ExitCode = -1,
                StdOut = SafeGet(stdoutTask),
                StdErr = SafeGet(stderrTask) + "\n(timeout)",
                Elapsed = sw.Elapsed,
                FileName = flasmExePath,
                Arguments = string.Join(" ", arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                WorkingDirectory = workingDirectory,
                TimedOut = true,
            };
        }

        // Ensure async readers finish
        Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 5_000);
        sw.Stop();

        return new FlasmRunResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdoutTask.Result,
            StdErr = stderrTask.Result,
            Elapsed = sw.Elapsed,
            FileName = flasmExePath,
            Arguments = string.Join(" ", arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            WorkingDirectory = workingDirectory,
            TimedOut = false,
        };
    }

    private static string SafeGet(Task<string> task)
    {
        try { return task.IsCompletedSuccessfully ? task.Result : ""; }
        catch { return ""; }
    }
}
