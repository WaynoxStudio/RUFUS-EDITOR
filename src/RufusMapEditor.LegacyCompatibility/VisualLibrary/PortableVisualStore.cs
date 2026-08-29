using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using RufusMapEditor.LegacyCompatibility.Portable;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4.3 — visual asset categories under <c>Library/Visuals/</c>.</summary>
public enum VisualAssetCategory
{
    Mobs = 0,
    /// <summary>ADMIN.UI.4B — fallback PNG under Library/Visuals/Items/{gfxId}.png.</summary>
    Items = 1,
}

/// <summary>
/// LIB.4.3 — portable manual images keyed by gfxID.
/// Paths are always relative to the Library root: <c>Visuals/{Mobs|Items}/{gfxID}.png</c>.
/// </summary>
public sealed class PortableVisualStore
{
    public const string VisualsFolderName = "Visuals";
    public const string MobsFolderName = "Mobs";
    public const string ItemsFolderName = "Items";
    public const int MaxPreviewEdge = 256;

    private string? _libraryRoot;

    public string? LibraryRoot => _libraryRoot;

    public void ConfigureLibraryRoot(string? libraryRoot)
    {
        _libraryRoot = string.IsNullOrWhiteSpace(libraryRoot) ? null : Path.GetFullPath(libraryRoot);
        if (_libraryRoot is null) return;
        var mobs = Path.Combine(_libraryRoot, VisualsFolderName, MobsFolderName);
        var items = Path.Combine(_libraryRoot, VisualsFolderName, ItemsFolderName);
        Directory.CreateDirectory(mobs);
        Directory.CreateDirectory(items);
    }

    public void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_libraryRoot))
            return;
        var lib = RufusLibraryPaths.TryResolveEffectiveLibrary(out _);
        if (lib is not null)
            ConfigureLibraryRoot(lib);
    }

    public static string CategoryFolderName(VisualAssetCategory category) => category switch
    {
        VisualAssetCategory.Mobs => MobsFolderName,
        VisualAssetCategory.Items => ItemsFolderName,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public string? GetCategoryDirectory(VisualAssetCategory category)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_libraryRoot))
            return null;
        return Path.Combine(_libraryRoot, VisualsFolderName, CategoryFolderName(category));
    }

    /// <summary>Absolute path for IO only — never persisted as a user setting.</summary>
    public string? GetPngPath(VisualAssetCategory category, int gfxId)
    {
        if (gfxId <= 0) return null;
        var dir = GetCategoryDirectory(category);
        if (dir is null) return null;
        return Path.Combine(dir, gfxId.ToString(CultureInfo.InvariantCulture) + ".png");
    }

    /// <summary>Portable relative path like <c>Visuals/Mobs/1607.png</c>.</summary>
    public static string GetRelativePath(VisualAssetCategory category, int gfxId) =>
        Path.Combine(VisualsFolderName, CategoryFolderName(category),
            gfxId.ToString(CultureInfo.InvariantCulture) + ".png").Replace('\\', '/');

    public bool Exists(VisualAssetCategory category, int gfxId)
    {
        var path = GetPngPath(category, gfxId);
        return path is not null && File.Exists(path);
    }

    public bool TryReadPng(VisualAssetCategory category, int gfxId, out byte[]? png)
    {
        png = null;
        var path = GetPngPath(category, gfxId);
        if (path is null || !File.Exists(path))
            return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
                return false;
            png = bytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ImportFromFile(VisualAssetCategory category, int gfxId, string sourceFilePath)
    {
        if (gfxId <= 0)
            throw new ArgumentOutOfRangeException(nameof(gfxId));
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("Archivo de imagen no encontrado.", sourceFilePath);

        EnsureConfigured();
        var dest = GetPngPath(category, gfxId)
                   ?? throw new InvalidOperationException(
                       "Library root no resuelto. Abre/selecciona la biblioteca RUFUS primero.");

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var png = VisualImageNormalizer.NormalizeToPng(sourceFilePath, MaxPreviewEdge);
        var tmp = dest + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Copy(tmp, dest, overwrite: true);
        File.Delete(tmp);
    }

    public bool Delete(VisualAssetCategory category, int gfxId)
    {
        var path = GetPngPath(category, gfxId);
        if (path is null || !File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }
}

/// <summary>LIB.4.3 — convert user PNG/JPG into a square-fit PNG preserving aspect + transparency.</summary>
public static class VisualImageNormalizer
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg",
    };

    public static bool IsSupportedExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return AllowedExtensions.Contains(Path.GetExtension(path));
    }

    public static byte[] NormalizeToPng(string sourcePath, int maxEdge = PortableVisualStore.MaxPreviewEdge)
    {
        if (!IsSupportedExtension(sourcePath))
            throw new NotSupportedException("Formato no soportado. Usa PNG o JPG/JPEG.");

        using var src = Image.FromFile(sourcePath);
        using var normalized = FitPreserveAspect(src, maxEdge);
        using var ms = new MemoryStream();
        normalized.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static Bitmap FitPreserveAspect(Image src, int maxEdge)
    {
        maxEdge = Math.Clamp(maxEdge, 32, 1024);
        var scale = Math.Min(1f, Math.Min((float)maxEdge / src.Width, (float)maxEdge / src.Height));
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));

        // Canvas = content size (no forced square stretch / no deformation).
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return bmp;
    }
}
