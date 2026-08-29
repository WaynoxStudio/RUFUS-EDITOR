using System.Security.Cryptography;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Category-scoped GFX resolution — never lookup by GfxID alone.
/// </summary>
public static class GfxResourceResolver
{
    public static bool TryResolve(IGfxCatalog catalog, GfxCategory category, int gfxId, out GfxResource resource)
    {
        resource = null!;
        if (gfxId <= 0)
            return false;
        return catalog.TryGet(category, gfxId, out resource) && resource is not null;
    }

    public static bool TryResolveGround(IGfxCatalog catalog, int gfxId, out GfxResource resource) =>
        TryResolve(catalog, GfxCategory.Ground, gfxId, out resource);

    public static bool TryResolveObject(IGfxCatalog catalog, int gfxId, out GfxResource resource) =>
        TryResolve(catalog, GfxCategory.Object, gfxId, out resource);

    public static IReadOnlyList<GfxCategory> GetCategoriesWithId(IGfxCatalog catalog, int gfxId)
    {
        if (gfxId <= 0)
            return Array.Empty<GfxCategory>();

        var list = new List<GfxCategory>(3);
        if (catalog.TryGet(GfxCategory.Background, gfxId, out _))
            list.Add(GfxCategory.Background);
        if (catalog.TryGet(GfxCategory.Ground, gfxId, out _))
            list.Add(GfxCategory.Ground);
        if (catalog.TryGet(GfxCategory.Object, gfxId, out _))
            list.Add(GfxCategory.Object);
        return list;
    }

    public static (int Width, int Height)? GetNativeDimensions(GfxResource resource)
    {
        if (resource.PixelWidth is int w && resource.PixelHeight is int h && w > 0 && h > 0)
            return (w, h);
        return GfxImageDimensions.TryRead(resource.FilePath);
    }

    /// <summary>Test/debug only — SHA256 of resource file bytes.</summary>
    public static string? ComputeFileHashSha256(GfxResource resource)
    {
        if (!File.Exists(resource.FilePath))
            return null;
        using var stream = File.OpenRead(resource.FilePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
