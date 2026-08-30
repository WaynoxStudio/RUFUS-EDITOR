using System.IO;
using System.Text;
using RufusMapEditor.LegacyCompatibility.World;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Astria-compatible project folder for world grids: {Library}/Géopositions/{Name}/{Name}.rufworld
/// </summary>
public static class GeopositionsStore
{
    public const string FolderName = "Géopositions";

    public static string GetRoot(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
            throw new ArgumentException("Se necesita la ruta de Library.", nameof(libraryRoot));
        return Path.Combine(Path.GetFullPath(libraryRoot), FolderName);
    }

    public static string EnsureRoot(string libraryRoot)
    {
        var root = GetRoot(libraryRoot);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string SanitizeProjectName(string name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0)
            return "Mundo";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        var cleaned = sb.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "Mundo" : cleaned;
    }

    public static string ProjectDirectory(string libraryRoot, string projectName) =>
        Path.Combine(EnsureRoot(libraryRoot), SanitizeProjectName(projectName));

    public static string ProjectFilePath(string libraryRoot, string projectName)
    {
        var safe = SanitizeProjectName(projectName);
        return Path.Combine(ProjectDirectory(libraryRoot, safe), safe + RufworldFormat.FileExtension);
    }

    /// <summary>
    /// Astria layout: Géopositions/{Name}/{Name}.rufworld.
    /// Moves a flat Géopositions/{Name}.rufworld into its project folder.
    /// </summary>
    public static string EnsureProjectFolderLayout(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir))
            return full;

        var fileName = Path.GetFileNameWithoutExtension(full);
        var parentName = Path.GetFileName(dir);

        if (string.Equals(parentName, fileName, StringComparison.OrdinalIgnoreCase))
            return full;

        if (!string.Equals(parentName, FolderName, StringComparison.OrdinalIgnoreCase))
            return full;

        var destDir = Path.Combine(dir, fileName);
        var dest = Path.Combine(destDir, Path.GetFileName(full));
        Directory.CreateDirectory(destDir);

        TryMove(full, dest);
        TryMove(full + ".bak", dest + ".bak");
        return dest;
    }

    private static void TryMove(string source, string dest)
    {
        if (!File.Exists(source) || File.Exists(dest)) return;
        try { File.Move(source, dest); }
        catch { /* keep writing to dest anyway */ }
    }

    public static IReadOnlyList<WorldProjectInfo> ListProjects(string libraryRoot)
    {
        var root = EnsureRoot(libraryRoot);
        var list = new List<WorldProjectInfo>();

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            var preferred = Path.Combine(dir, name + RufworldFormat.FileExtension);
            if (File.Exists(preferred))
            {
                list.Add(ToInfo(preferred, name));
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*" + RufworldFormat.FileExtension))
            {
                list.Add(ToInfo(file, name));
                break;
            }
        }

        // Flat .rufworld files directly under Géopositions (manual copies)
        foreach (var file in Directory.EnumerateFiles(root, "*" + RufworldFormat.FileExtension))
            list.Add(ToInfo(file, Path.GetFileNameWithoutExtension(file)));

        return list
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static WorldProjectInfo ToInfo(string path, string name) => new()
    {
        Name = name,
        FilePath = path,
        ModifiedUtc = File.GetLastWriteTimeUtc(path),
    };
}

public sealed class WorldProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public DateTime ModifiedUtc { get; init; }
}
