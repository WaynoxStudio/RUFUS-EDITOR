using System.Text.RegularExpressions;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.Swf;

/// <summary>
/// Reads map metadata from an Astria-exported SWF via Flasm disassembly.
/// Recovers fields missing from SQL (notably <c>backgroundNum</c>) and optionally MapData.
/// </summary>
public static partial class FlasmSwfMetadataReader
{
    public sealed class SwfMapMetadata
    {
        public int Id { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int BackgroundNum { get; init; }
        public int AmbianceId { get; init; }
        public int MusicId { get; init; }
        public bool Outdoor { get; init; }
        public int Capabilities { get; init; }
        /// <summary>MapData string when requested / successfully parsed; otherwise empty.</summary>
        public string MapData { get; init; } = "";
        public string SourcePath { get; init; } = "";
    }

    public static SwfMapMetadata Read(string swfPath, string flasmExePath, bool includeMapData = false)
    {
        if (!File.Exists(swfPath))
            throw new FileNotFoundException("SWF not found.", swfPath);
        if (!File.Exists(flasmExePath))
            throw new FileNotFoundException("flasm.exe not found.", flasmExePath);

        var run = FlasmProcessRunner.Run(
            flasmExePath,
            new[] { "-d", swfPath },
            workingDirectory: Path.GetDirectoryName(Path.GetFullPath(flasmExePath)) ?? Environment.CurrentDirectory);

        if (run.TimedOut)
            throw new TimeoutException("flasm disassembly timed out.");
        if (run.ExitCode != 0 && string.IsNullOrWhiteSpace(run.StdOut))
            throw new InvalidOperationException($"flasm failed ({run.ExitCode}): {run.StdErr}");

        return ParseDisassembly(run.StdOut, swfPath, includeMapData);
    }

    public static SwfMapMetadata ParseDisassembly(string disassembly, string? sourcePath = null, bool includeMapData = false)
    {
        var text = disassembly.Replace("\r\n", "\n");
        return new SwfMapMetadata
        {
            Id = ReadInt(text, "id"),
            Width = ReadInt(text, "width"),
            Height = ReadInt(text, "height"),
            BackgroundNum = ReadInt(text, "backgroundNum"),
            AmbianceId = ReadInt(text, "ambianceId"),
            MusicId = ReadInt(text, "musicId"),
            Outdoor = ReadBool(text, "bOutdoor"),
            Capabilities = ReadInt(text, "capabilities"),
            MapData = includeMapData ? ReadMapData(text) : "",
            SourcePath = sourcePath ?? "",
        };
    }

    public static void ApplyToDocument(MapDocument map, SwfMapMetadata meta)
    {
        map.BackgroundId = meta.BackgroundNum; map.BackgroundDefined = true;
        map.MusicId = meta.MusicId; map.MusicDefined = true;
        map.AmbianceId = meta.AmbianceId; map.AmbianceDefined = true;
        map.Capabilities = meta.Capabilities; map.CapabilitiesDefined = true;
        map.Outdoor = meta.Outdoor;
    }

    public static string? ResolvePreferredSwf(string mapFolder, int mapId)
    {
        var ame = Path.Combine(mapFolder, $"{mapId}_AME.swf");
        if (File.Exists(ame)) return ame;

        var plain = Path.Combine(mapFolder, $"{mapId}.swf");
        if (File.Exists(plain)) return plain;

        return Directory.Exists(mapFolder)
            ? Directory.GetFiles(mapFolder, "*.swf")
                .OrderByDescending(f => Path.GetFileName(f).Contains("_AME", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault()
            : null;
    }

    private static string ReadMapData(string text)
    {
        // Prefer constants pool: ...'mapData', 'ACTUAL'...
        var constMatch = ConstantsMapDataRegex().Match(text);
        if (constMatch.Success)
            return constMatch.Groups["md"].Value;

        // push 'mapData'\n    push '...'
        foreach (Match match in MultilineMapDataRegex().Matches(text))
        {
            if (match.Groups["n"].Value == "mapData")
                return match.Groups["v"].Value;
        }

        return "";
    }

    private static int ReadInt(string text, string name)
    {
        foreach (Match match in MultilineIntRegex().Matches(text))
        {
            if (match.Groups["n"].Value == name)
                return int.Parse(match.Groups["v"].Value);
        }

        foreach (Match match in InlineIntRegex().Matches(text))
        {
            if (match.Groups["n"].Value == name)
                return int.Parse(match.Groups["v"].Value);
        }

        return 0;
    }

    private static bool ReadBool(string text, string name)
    {
        foreach (Match match in MultilineBoolRegex().Matches(text))
        {
            if (match.Groups["n"].Value == name)
                return match.Groups["v"].Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        }

        foreach (Match match in InlineBoolRegex().Matches(text))
        {
            if (match.Groups["n"].Value == name)
                return match.Groups["v"].Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [GeneratedRegex(@"push '(?<n>[^']+)'\s*\n\s*push (?<v>-?\d+)", RegexOptions.Multiline)]
    private static partial Regex MultilineIntRegex();

    [GeneratedRegex(@"push '(?<n>[^']+)',\s*(?<v>-?\d+)")]
    private static partial Regex InlineIntRegex();

    [GeneratedRegex(@"push '(?<n>[^']+)'\s*\n\s*push (?<v>TRUE|FALSE)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex MultilineBoolRegex();

    [GeneratedRegex(@"push '(?<n>[^']+)',\s*(?<v>TRUE|FALSE)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineBoolRegex();

    [GeneratedRegex(@"'mapData',\s*'(?<md>[^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex ConstantsMapDataRegex();

    [GeneratedRegex(@"push '(?<n>[^']+)'\s*\n\s*push '(?<v>[^']*)'", RegexOptions.Multiline)]
    private static partial Regex MultilineMapDataRegex();
}
