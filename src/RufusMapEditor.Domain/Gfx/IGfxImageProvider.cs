namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// Future image decode/cache boundary. Domain stays free of WPF / GDI types.
/// </summary>
public interface IGfxImageProvider
{
    /// <summary>
    /// Loads image bytes (or a handle opaque to Domain) for <paramref name="resource"/> on demand.
    /// Implementations may cache; callers must not assume permanent residency in RAM.
    /// </summary>
    Task<GfxImageData> GetImageAsync(GfxResource resource, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raw image payload without UI framework coupling.
/// </summary>
public sealed class GfxImageData
{
    public required byte[] Bytes { get; init; }
    public required string ContentTypeHint { get; init; }
    public int? PixelWidth { get; init; }
    public int? PixelHeight { get; init; }
}
