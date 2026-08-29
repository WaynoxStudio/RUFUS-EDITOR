using System.IO;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Builds / saves / loads .rufmap projects for a MapEditSession.
/// </summary>
public static class ProjectPersistence
{
    public static RufmapFileDto BuildDto(MapEditSession session)
    {
        var name = session.ProjectName
                   ?? (session.FilePath is not null ? Path.GetFileNameWithoutExtension(session.FilePath) : null)
                   ?? $"map_{session.Document.Id}";

        return RufmapSerializer.FromDocument(
            session.Document,
            session.DocumentId,
            session.CreatedUtc,
            session.Source,
            projectName: name);
    }

    public static string BuildJson(MapEditSession session)
    {
        var dto = BuildDto(session);
        dto.ModifiedUtc = DateTimeOffset.UtcNow;
        return RufmapSerializer.Serialize(dto);
    }

    public static void SaveToPath(MapEditSession session, string path)
    {
        session.EndStroke();
        var json = BuildJson(session);
        RufmapIo.SaveAtomic(path, json, writeBackup: true);
        session.FilePath = Path.GetFullPath(path);
        session.ProjectName = Path.GetFileNameWithoutExtension(path);
        session.MarkSaved();
    }

    public static void SaveDocument(
        MapDocument document,
        string documentId,
        string path,
        WorldMapOrigin? origin = null)
    {
        var dto = RufmapSerializer.FromDocument(
            document,
            documentId,
            DateTimeOffset.UtcNow,
            new RufmapSourceDto { Kind = "WorldEmbedded", OriginalMapId = document.Id },
            projectName: $"map_{document.Id}");
        dto.ModifiedUtc = DateTimeOffset.UtcNow;
        RufmapIo.SaveAtomic(path, RufmapSerializer.Serialize(dto), writeBackup: true);
    }

    public static (MapDocument Document, MapEditSession Session) OpenFile(string path)
    {
        var loaded = RufmapIo.LoadFile(path);
        FightPlacesCodec.ApplyToCells(loaded.Document.Cells, loaded.Document.FightPlaces);
        var hit = new IsoHitTester(loaded.Document.Width, loaded.Document.Height);
        var session = new MapEditSession(loaded.Document, hit)
        {
            DocumentId = loaded.File.DocumentId,
            FilePath = Path.GetFullPath(path),
            CreatedUtc = loaded.File.CreatedUtc == default ? DateTimeOffset.UtcNow : loaded.File.CreatedUtc,
            ProjectName = loaded.File.ProjectName ?? Path.GetFileNameWithoutExtension(path),
            Source = loaded.File.Source,
        };
        session.MarkSaved();
        return (loaded.Document, session);
    }

    public static (MapDocument Document, MapEditSession Session) OpenAutosave(
        string autosavePath,
        AutosaveMeta meta)
    {
        var loaded = RufmapSerializer.LoadFromJson(File.ReadAllText(autosavePath));
        FightPlacesCodec.ApplyToCells(loaded.Document.Cells, loaded.Document.FightPlaces);
        var hit = new IsoHitTester(loaded.Document.Width, loaded.Document.Height);
        var session = new MapEditSession(loaded.Document, hit)
        {
            DocumentId = meta.DocumentId,
            FilePath = meta.HadProjectFile && !string.IsNullOrWhiteSpace(meta.ProjectPath)
                ? meta.ProjectPath
                : null,
            CreatedUtc = loaded.File.CreatedUtc == default ? DateTimeOffset.UtcNow : loaded.File.CreatedUtc,
            ProjectName = meta.DisplayName ?? loaded.File.ProjectName,
            Source = loaded.File.Source,
        };
        session.MarkRecoveredDirty();
        return (loaded.Document, session);
    }
}
