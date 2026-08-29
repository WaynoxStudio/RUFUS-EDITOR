using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Minimal file-backed image provider: reads bytes on demand, no permanent decode cache.
/// Suitable for tests and as a baseline before a WPF/Skia cache is introduced.
/// </summary>
public sealed class FileGfxImageProvider : IGfxImageProvider
{
    public Task<GfxImageData> GetImageAsync(GfxResource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(resource.FilePath);
        var hint = resource.Extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };

        return Task.FromResult(new GfxImageData
        {
            Bytes = bytes,
            ContentTypeHint = hint,
        });
    }
}
