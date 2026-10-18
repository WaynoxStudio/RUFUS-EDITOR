using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.ItemIconPreviewPrep;

/// <summary>
/// CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1A — stable preview identity from resolved item icon SWF.
/// </summary>
public readonly struct ItemIconPreviewKey : IEquatable<ItemIconPreviewKey>, IComparable<ItemIconPreviewKey>
{
    public string ResolvedSwfRelativePath { get; }

    /// <summary>Normalized SWF path used for equality (e.g. <c>items/23/10.swf</c>).</summary>
    public string Key => ResolvedSwfRelativePath;

    /// <summary>Filesystem-safe stem (e.g. <c>items_23_10</c>).</summary>
    public string FileStem { get; }

    private ItemIconPreviewKey(string normalizedSwfPath, string fileStem)
    {
        ResolvedSwfRelativePath = normalizedSwfPath;
        FileStem = fileStem;
    }

    public static ItemIconPreviewKey FromResolvedSwfRelativePath(string swfRelativePath)
    {
        if (!TryNormalizeSwfPath(swfRelativePath, out var normalized, out var stem, out var error))
            throw new ArgumentException(error, nameof(swfRelativePath));
        return new ItemIconPreviewKey(normalized, stem);
    }

    public static bool TryFromResolvedSwfRelativePath(string? swfRelativePath, out ItemIconPreviewKey key)
    {
        if (TryNormalizeSwfPath(swfRelativePath, out var normalized, out var stem, out _))
        {
            key = new ItemIconPreviewKey(normalized, stem);
            return true;
        }

        key = default;
        return false;
    }

    public static ItemIconPreviewKey FromItem(int gfxId, int typeId, string? clipsRoot)
    {
        var (rel, _, _) = VisualClipPaths.ResolveItemIcon(clipsRoot, gfxId, typeId);
        return FromResolvedSwfRelativePath(rel);
    }

    public static ItemIconPreviewKey FromCatalogRow(ItemIconPreviewSample row, string? clipsRoot)
    {
        if (!string.IsNullOrWhiteSpace(row.IconRelativePath)
            && row.IconRelativePath.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)
            && TryFromResolvedSwfRelativePath(row.IconRelativePath, out var fromRow))
        {
            return fromRow;
        }

        return FromItem(row.GfxId, row.TypeId, clipsRoot);
    }

    public string LibraryRelativePath => PortableVisualStore.GetItemSwfPreviewRelativePath(FileStem);

    public string StagingPngFileName => FileStem + ".png";

    /// <summary>Alias for filesystem stem (<c>items_23_10</c>).</summary>
    public string PreviewStem => FileStem;

    public bool Equals(ItemIconPreviewKey other) =>
        string.Equals(ResolvedSwfRelativePath, other.ResolvedSwfRelativePath, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ItemIconPreviewKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(ResolvedSwfRelativePath);

    public int CompareTo(ItemIconPreviewKey other) =>
        string.Compare(ResolvedSwfRelativePath, other.ResolvedSwfRelativePath, StringComparison.OrdinalIgnoreCase);

    public static bool operator ==(ItemIconPreviewKey left, ItemIconPreviewKey right) => left.Equals(right);

    public static bool operator !=(ItemIconPreviewKey left, ItemIconPreviewKey right) => !left.Equals(right);

    public override string ToString() => ResolvedSwfRelativePath;

    private static bool TryNormalizeSwfPath(
        string? swfRelativePath,
        out string normalized,
        out string fileStem,
        out string error)
    {
        normalized = "";
        fileStem = "";
        error = "";

        if (string.IsNullOrWhiteSpace(swfRelativePath))
        {
            error = "Ruta SWF vacía.";
            return false;
        }

        normalized = swfRelativePath.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("items/", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
        {
            error = "Ruta SWF inválida (se esperaba items/.../*.swf): " + swfRelativePath;
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "items", StringComparison.OrdinalIgnoreCase)
            || !parts[2].EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
        {
            error = "Ruta SWF inválida (formato items/{folder}/{gfx}.swf): " + swfRelativePath;
            return false;
        }

        var folder = parts[1];
        var gfxFile = parts[2];
        if (!int.TryParse(folder, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(gfxFile.AsSpan(0, gfxFile.Length - 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            error = "Ruta SWF inválida (segmentos no numéricos): " + swfRelativePath;
            return false;
        }

        normalized = $"items/{folder}/{gfxFile.ToLowerInvariant()}";
        fileStem = normalized.Replace('/', '_').AsSpan(0, normalized.Length - 4).ToString();
        return true;
    }
}
