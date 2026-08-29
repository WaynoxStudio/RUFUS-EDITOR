using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.Swf;

namespace RufusMapEditor.LegacyCompatibility.Portable;

public sealed class PortableLibraryValidation
{
    public required string LibraryRoot { get; init; }
    public bool IsValidForEditor { get; init; }
    public bool HasFlasmExport { get; init; }
    public int MapCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Validates a RUFUS/Astria-compatible library folder for portable distribution.
/// </summary>
public static class PortableLibraryValidator
{
    public static PortableLibraryValidation Validate(string libraryRoot)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
        {
            errors.Add("La carpeta Library no existe.");
            return new PortableLibraryValidation
            {
                LibraryRoot = libraryRoot ?? "",
                IsValidForEditor = false,
                HasFlasmExport = false,
                MapCount = 0,
                Errors = errors,
            };
        }

        var mapsDir = Path.Combine(libraryRoot, "Maps");
        if (!Directory.Exists(mapsDir))
            errors.Add("Falta Maps\\");
        else if (!Directory.EnumerateDirectories(mapsDir).Any())
            warnings.Add("Maps\\ está vacío (el editor arranca, pero no hay mapas).");

        foreach (var (label, path) in RequiredImageDirectories(libraryRoot))
        {
            if (!Directory.Exists(path))
                errors.Add($"Falta {label}");
        }

        var groundsXml = AstriaGfxLibraryLayout.GroundsXmlPath(libraryRoot);
        var objectsXml = AstriaGfxLibraryLayout.ObjectsXmlPath(libraryRoot);
        if (!File.Exists(groundsXml))
            errors.Add("Falta XML\\grounds.xml");
        if (!File.Exists(objectsXml))
            errors.Add("Falta XML\\objects.xml");

        var mapCount = CountMaps(mapsDir);
        var flasm = SwfMapExporter.ResolveFlasmExe(libraryRoot);
        var blank = SwfMapExporter.ResolveBlankSwf(libraryRoot);
        var hasFlasm = flasm is not null && blank is not null;
        if (flasm is null)
            warnings.Add("Flasm no encontrado (Export SWF AME deshabilitado).");
        if (blank is null)
            warnings.Add("blank.swf no encontrado (Export SWF AME deshabilitado).");

        return new PortableLibraryValidation
        {
            LibraryRoot = libraryRoot,
            IsValidForEditor = errors.Count == 0,
            HasFlasmExport = hasFlasm,
            MapCount = mapCount,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static IEnumerable<(string Label, string Path)> RequiredImageDirectories(string root)
    {
        yield return ("Images\\backgrounds\\", AstriaGfxLibraryLayout.BackgroundsDirectory(root));
        yield return ("Images\\grounds\\", AstriaGfxLibraryLayout.GroundsDirectory(root));
        yield return ("Images\\objects\\", AstriaGfxLibraryLayout.ObjectsDirectory(root));
    }

    private static int CountMaps(string mapsDir)
    {
        if (!Directory.Exists(mapsDir))
            return 0;

        var count = 0;
        foreach (var dir in Directory.EnumerateDirectories(mapsDir))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var id))
                continue;
            // Official Save uses .rufmap; legacy portable packs may still ship .sql.
            if (File.Exists(Path.Combine(dir, $"{id}.rufmap"))
                || File.Exists(Path.Combine(dir, $"{id}.sql")))
                count++;
        }

        return count;
    }
}
