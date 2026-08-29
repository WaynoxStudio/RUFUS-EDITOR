namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// Metadata for one Astria graphic resource. Does not own decoded bitmap data.
/// </summary>
public sealed class GfxResource
{
    public required int Id { get; init; }
    public required GfxCategory Category { get; init; }

    /// <summary>Absolute path to the image file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Immediate parent folder name as stored by Astria (<c>Tile.Folder</c>).
    /// Empty for backgrounds (flat folder).
    /// </summary>
    public required string Folder { get; init; }

    /// <summary>File extension including the leading dot (e.g. <c>.png</c>).</summary>
    public required string Extension { get; init; }

    /// <summary>
    /// Anchor from the category XML when present.
    /// Backgrounds have no dedicated XML; Astria looks them up via <c>Get_Ground_Pos</c> at draw time.
    /// </summary>
    public GfxAnchor? Anchor { get; init; }

    public bool HasAnchor => Anchor.HasValue;

    /// <summary>
    /// True when objects.xml/grounds.xml contained multiple Pos rows for this ID.
    /// Legacy lookup keeps the first (Astria <c>Get_*_Pos</c>); extras are recorded but unused.
    /// </summary>
    public bool AnchorAmbiguous { get; init; }

    /// <summary>Optional pixel size; not required for catalog indexing.</summary>
    public int? PixelWidth { get; init; }

    /// <summary>Optional pixel size; not required for catalog indexing.</summary>
    public int? PixelHeight { get; init; }
}
