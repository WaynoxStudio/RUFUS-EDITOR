using RufusMapEditor.LegacyCompatibility.Rufmap;

namespace RufusMapEditor.LegacyCompatibility.Rufmap;

/// <summary>
/// Placeholder for future format migrations (v1 → v2 → …).
/// Callers should run this after DeserializeDto and before ToDocument.
/// </summary>
public static class RufmapMigrator
{
    public static RufmapFileDto MigrateToCurrent(RufmapFileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.FormatVersion > RufmapFormat.CurrentVersion)
            throw new RufmapException(
                RufmapLoadErrorKind.UnsupportedFutureVersion,
                $"formatVersion {dto.FormatVersion} is newer than supported v{RufmapFormat.CurrentVersion}.");

        // v1 is current — no transforms yet.
        // Future: if (dto.FormatVersion == 1) dto = MigrateV1ToV2(dto);
        return dto;
    }
}
