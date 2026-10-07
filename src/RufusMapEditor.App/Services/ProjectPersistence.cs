using System.IO;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Swf;
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
        path = Path.GetFullPath(path);
        if (path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
            return OpenSwfFile(path);

        var loaded = RufmapIo.LoadFile(path);
        FightPlacesCodec.ApplyToCells(loaded.Document.Cells, loaded.Document.FightPlaces);
        var hit = new IsoHitTester(loaded.Document.Width, loaded.Document.Height);
        var session = new MapEditSession(loaded.Document, hit)
        {
            DocumentId = loaded.File.DocumentId,
            FilePath = path,
            CreatedUtc = loaded.File.CreatedUtc == default ? DateTimeOffset.UtcNow : loaded.File.CreatedUtc,
            ProjectName = loaded.File.ProjectName ?? Path.GetFileNameWithoutExtension(path),
            Source = loaded.File.Source,
        };
        session.MarkSaved();
        return (loaded.Document, session);
    }

    private static (MapDocument Document, MapEditSession Session) OpenSwfFile(string path)
    {
        var flasm = ResolveFlasmNear(path)
                    ?? throw new FileNotFoundException(
                        "No se encontró Flasm/flasm.exe para leer el SWF. Colócalo en Library/Flasm/.");

        var meta = FlasmSwfMetadataReader.Read(path, flasm, includeMapData: true);
        var fallbackId = 0;
        var folderName = Path.GetFileName(Path.GetDirectoryName(path));
        _ = int.TryParse(folderName, out fallbackId);
        if (fallbackId <= 0)
            _ = int.TryParse(Path.GetFileNameWithoutExtension(path).Split('_')[0], out fallbackId);

        var map = FlasmSwfMetadataReader.CreateDocument(meta, fallbackMapId: fallbackId);
        FightPlacesCodec.ApplyToCells(map.Cells, map.FightPlaces);
        var hit = new IsoHitTester(map.Width, map.Height);
        var session = new MapEditSession(map, hit)
        {
            DocumentId = Guid.NewGuid().ToString("N"),
            FilePath = path,
            CreatedUtc = DateTimeOffset.UtcNow,
            ProjectName = Path.GetFileNameWithoutExtension(path),
            Source = new RufmapSourceDto { Kind = "AstriaSwf", OriginalMapId = map.Id },
        };
        session.MarkSaved();
        return (map, session);
    }

    private static string? ResolveFlasmNear(string swfPath)
    {
        var dir = Path.GetDirectoryName(swfPath);
        // Library/Maps/<id>/<file>.swf → Library
        var maps = dir is null ? null : Directory.GetParent(dir);
        var library = maps?.Parent;
        if (library is not null)
        {
            var fromLib = SwfMapExporter.ResolveFlasmExe(library.FullName);
            if (fromLib is not null)
                return fromLib;
        }

        return SwfMapExporter.ResolveFlasmExe(Path.GetDirectoryName(swfPath) ?? "")
               ?? SwfMapExporter.ResolveFlasmExe(AppContext.BaseDirectory);
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
