using RufusMapEditor.LegacyCompatibility.LangMaps;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Loads RO BD catalogs + local dialog_es for mission editor support.</summary>
public static class ContentDbCatalogReader
{
    public static async Task<ContentDbCatalogReadResult> ReadAsync(
        IContentDbCatalogReadRepository repository,
        string? dialogEsCacheDirectory = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        try
        {
            var npcs = await repository.LoadNpcsAsync(ct).ConfigureAwait(false);
            var questions = await repository.LoadQuestionsAsync(ct).ConfigureAwait(false);
            var missions = await repository.LoadMissionsAsync(ct).ConfigureAwait(false);
            var dialogEs = DialogEsSimpleUiResolver.TryLoadSnapshot(
                dialogEsCacheDirectory ?? LangRemoteSyncService.DefaultCacheDirectory,
                out _);
            return new ContentDbCatalogReadResult
            {
                Success = true,
                Snapshot = new ContentDbCatalogSnapshot(npcs, questions, missions, dialogEs, true),
            };
        }
        catch (Exception ex)
        {
            return new ContentDbCatalogReadResult
            {
                Success = false,
                Snapshot = ContentDbCatalogSnapshot.Empty,
                ErrorMessage = ex.Message,
            };
        }
    }
}
