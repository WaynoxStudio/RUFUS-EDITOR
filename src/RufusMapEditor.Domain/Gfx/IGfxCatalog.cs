namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// O(1) lookup catalog keyed by <see cref="GfxCategory"/> + GfxID.
/// </summary>
public interface IGfxCatalog
{
    int BackgroundCount { get; }
    int GroundCount { get; }
    int ObjectCount { get; }
    int TotalCount { get; }

    bool TryGet(GfxCategory category, int id, out GfxResource? resource);
    bool TryGetBackground(int id, out GfxResource? resource);
    bool TryGetGround(int id, out GfxResource? resource);
    bool TryGetObject(int id, out GfxResource? resource);

    /// <summary>
    /// Astria <c>Get_Ground_Pos</c> / <c>Get_Object_Pos</c>: first XML Pos for the ID,
    /// even when no image file exists (used e.g. for background pivots).
    /// </summary>
    bool TryGetAnchor(GfxCategory category, int id, out GfxAnchor anchor);

    IEnumerable<GfxResource> Enumerate(GfxCategory? category = null);
    IEnumerable<GfxResource> EnumerateById(int id);
}
