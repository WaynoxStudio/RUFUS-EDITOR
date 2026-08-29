using System.Drawing.Imaging;
using System.Globalization;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Swf;

namespace RufusMapEditor.Rendering.Package;

public sealed class OfficialMapSaveOptions
{
    /// <summary>Effective Master/Portable Library root (contains Maps\).</summary>
    public required string LibraryRoot { get; init; }

    public string DocumentId { get; init; } = Guid.NewGuid().ToString("D");
    public RufmapSourceDto? Source { get; init; }
    public string? ProjectName { get; init; }

    public string? FlasmExePath { get; init; }
    public string? BlankSwfTemplatePath { get; init; }

    public Action<string>? Progress { get; init; }
}

public sealed class OfficialMapSaveResult
{
    public required bool Success { get; init; }
    public required int MapId { get; init; }
    public required string OfficialDirectory { get; init; }
    public string? RufmapPath { get; init; }
    public string? PngPath { get; init; }
    public string? MapDataTxtPath { get; init; }
    public string? AmeSwfPath { get; init; }
    public string? ErrorMessage { get; init; }
    public bool AmeSwfGenerated { get; init; }
    public string? AmeSwfWarning { get; init; }
    public int PngWidth { get; init; }
    public int PngHeight { get; init; }
    public int MapDataLength { get; init; }
}

/// <summary>
/// Official Map Save (Fase 9S.2/9S.3): rebuilds Library\Maps\&lt;MapId&gt; with the minimal package:
/// .rufmap + .png + _MapData.txt (+ optional &lt;MapId&gt;_AME.swf). Atomic folder replace.
/// Distinct from external diagnostic MapPackageBuilder (ModeCell/GfxList/manifest).
/// </summary>
public sealed class OfficialMapSave
{
    private readonly AstriaMapRenderer _renderer;

    public OfficialMapSave(AstriaMapRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public static OfficialMapSave CreateWithoutGfx() =>
        new(new AstriaMapRenderer(EmptyGfxCatalog.Instance));

    public OfficialMapSaveResult Save(MapDocument map, OfficialMapSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        if (map.Id <= 0)
            return Fail(map.Id, "", "MapId inválido. El documento debe tener un MapId > 0.");

        if (string.IsNullOrWhiteSpace(options.LibraryRoot) || !Directory.Exists(options.LibraryRoot))
            return Fail(map.Id, "", "Biblioteca RUFUS no configurada o inexistente.");

        MapCellEditor.SyncDocument(map);

        var mapsRoot = LibraryMapPaths.GetMapsRoot(options.LibraryRoot);
        Directory.CreateDirectory(mapsRoot);

        var mapId = map.Id;
        var officialDir = LibraryMapPaths.GetOfficialMapDirectory(options.LibraryRoot, mapId);
        var staging = Path.Combine(mapsRoot, $".{mapId}.tmp-{Guid.NewGuid():N}");
        var backup = Path.Combine(mapsRoot, $".{mapId}.old-{Guid.NewGuid():N}");

        CleanupStaleSidecars(mapsRoot, mapId);

        try
        {
            Directory.CreateDirectory(staging);

            var report = options.Progress;
            report?.Invoke("Generando RUFMAP...");
            var rufmapName = $"{mapId}.rufmap";
            var rufmapPath = Path.Combine(staging, rufmapName);
            WriteRufmap(map, options, rufmapPath);

            report?.Invoke("Generando PNG...");
            var pngName = $"{mapId}.png";
            var pngPath = Path.Combine(staging, pngName);
            var (pngW, pngH) = WriteCleanPng(map, pngPath);

            report?.Invoke("Generando MapData TXT...");
            var mapData = map.MapData ?? "";
            var mapDataName = MapDataPlainText.FileName(mapId);
            var mapDataPath = Path.Combine(staging, mapDataName);
            MapDataPlainText.WriteFile(mapDataPath, mapData);

            if (!File.Exists(rufmapPath) || new FileInfo(rufmapPath).Length == 0)
                return Fail(mapId, officialDir, "No se pudo generar el .rufmap (CORE).");
            if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                return Fail(mapId, officialDir, "No se pudo generar el PNG (CORE).");
            if (!File.Exists(mapDataPath) || MapDataPlainText.ReadFile(mapDataPath) != mapData)
                return Fail(mapId, officialDir, "No se pudo generar el MapData TXT (CORE).");

            report?.Invoke("Generando SWF AME...");
            var swfName = $"{mapId}_AME.swf";
            var swfAttempt = TryWriteAmeSwf(map, Path.Combine(staging, swfName), options);

            // Atomic replace: official → backup, staging → official, delete backup
            string? createdBackup = null;
            try
            {
                if (Directory.Exists(officialDir))
                {
                    if (Directory.Exists(backup))
                        Directory.Delete(backup, recursive: true);
                    Directory.Move(officialDir, backup);
                    createdBackup = backup;
                }

                Directory.Move(staging, officialDir);
                staging = ""; // moved; don't delete in finally

                if (createdBackup is not null)
                {
                    try { Directory.Delete(createdBackup, recursive: true); }
                    catch { /* best-effort; leftover .old can be cleaned next save */ }
                    createdBackup = null;
                }
            }
            catch (Exception ex)
            {
                // Restore previous official folder if we moved it away
                try
                {
                    if (createdBackup is not null && Directory.Exists(createdBackup))
                    {
                        if (Directory.Exists(officialDir))
                            Directory.Delete(officialDir, recursive: true);
                        Directory.Move(createdBackup, officialDir);
                    }
                }
                catch
                {
                    // leave .old for manual recovery
                }

                return Fail(mapId, officialDir, $"Error al reemplazar carpeta oficial: {ex.Message}");
            }

            return new OfficialMapSaveResult
            {
                Success = true,
                MapId = mapId,
                OfficialDirectory = officialDir,
                RufmapPath = Path.Combine(officialDir, rufmapName),
                PngPath = Path.Combine(officialDir, pngName),
                MapDataTxtPath = Path.Combine(officialDir, mapDataName),
                AmeSwfPath = swfAttempt.Ok ? Path.Combine(officialDir, swfName) : null,
                AmeSwfGenerated = swfAttempt.Ok,
                AmeSwfWarning = swfAttempt.Ok ? null : swfAttempt.Warning,
                PngWidth = pngW,
                PngHeight = pngH,
                MapDataLength = mapData.Length,
            };
        }
        catch (Exception ex)
        {
            return Fail(mapId, officialDir, ex.Message);
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static void CleanupStaleSidecars(string mapsRoot, int mapId)
    {
        var id = mapId.ToString(CultureInfo.InvariantCulture);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(mapsRoot, $".{id}.*"))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith($".{id}.tmp-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith($".{id}.old-", StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch { /* ignore locked leftovers */ }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void WriteRufmap(MapDocument map, OfficialMapSaveOptions options, string path)
    {
        var dto = RufmapSerializer.FromDocument(
            map,
            options.DocumentId,
            DateTimeOffset.UtcNow,
            options.Source,
            projectName: options.ProjectName ?? $"map_{map.Id}");
        dto.ModifiedUtc = DateTimeOffset.UtcNow;
        RufmapIo.SaveAtomic(path, RufmapSerializer.Serialize(dto), writeBackup: false);
    }

    private (int W, int H) WriteCleanPng(MapDocument map, string path)
    {
        var result = _renderer.Render(map, new MapRenderOptions
        {
            CropToExportBounds = true,
            AstriaLogoPath = null,
            DrawBackground = true,
            DrawGround = true,
            DrawObjectLayer1 = true,
            DrawObjectLayer2 = true,
        });
        using (result.Image)
        {
            result.Image.Save(path, ImageFormat.Png);
            return (result.Image.Width, result.Image.Height);
        }
    }

    private static (bool Ok, string? Warning) TryWriteAmeSwf(
        MapDocument map,
        string destinationPath,
        OfficialMapSaveOptions options)
    {
        try
        {
            if (map.Outdoor is null)
                return (false, "SWF AME no generado: Outdoor (bOutdoor) ausente en el documento.");

            var flasm = options.FlasmExePath;
            var blank = options.BlankSwfTemplatePath;
            if ((string.IsNullOrWhiteSpace(flasm) || string.IsNullOrWhiteSpace(blank))
                && !string.IsNullOrWhiteSpace(options.LibraryRoot))
            {
                flasm ??= SwfMapExporter.ResolveFlasmExe(options.LibraryRoot);
                blank ??= SwfMapExporter.ResolveBlankSwf(options.LibraryRoot);
            }

            if (string.IsNullOrWhiteSpace(flasm) || !File.Exists(flasm))
                return (false, "SWF AME no generado: Flasm no disponible.");
            if (string.IsNullOrWhiteSpace(blank) || !File.Exists(blank))
                return (false, "SWF AME no generado: blank.swf no disponible.");

            var swf = SwfMapExporter.Export(new SwfExportRequest
            {
                Document = map,
                DestinationSwfPath = destinationPath,
                FlasmExePath = flasm,
                BlankSwfTemplatePath = blank,
            });

            if (!swf.Success)
                return (false, $"SWF AME no generado: {swf.ErrorMessage}");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"SWF AME no generado: {ex.Message}");
        }
    }

    private static OfficialMapSaveResult Fail(int mapId, string dir, string message) => new()
    {
        Success = false,
        MapId = mapId,
        OfficialDirectory = dir,
        ErrorMessage = message,
    };

    private sealed class EmptyGfxCatalog : Domain.Gfx.IGfxCatalog
    {
        public static readonly EmptyGfxCatalog Instance = new();
        public int BackgroundCount => 0;
        public int GroundCount => 0;
        public int ObjectCount => 0;
        public int TotalCount => 0;
        public bool TryGet(Domain.Gfx.GfxCategory category, int id, out Domain.Gfx.GfxResource? resource)
        {
            resource = null;
            return false;
        }
        public bool TryGetBackground(int id, out Domain.Gfx.GfxResource? resource)
        {
            resource = null;
            return false;
        }
        public bool TryGetGround(int id, out Domain.Gfx.GfxResource? resource)
        {
            resource = null;
            return false;
        }
        public bool TryGetObject(int id, out Domain.Gfx.GfxResource? resource)
        {
            resource = null;
            return false;
        }
        public bool TryGetAnchor(Domain.Gfx.GfxCategory category, int id, out Domain.Gfx.GfxAnchor anchor)
        {
            anchor = default;
            return false;
        }
        public IEnumerable<Domain.Gfx.GfxResource> Enumerate(Domain.Gfx.GfxCategory? category = null) =>
            Array.Empty<Domain.Gfx.GfxResource>();
        public IEnumerable<Domain.Gfx.GfxResource> EnumerateById(int id) =>
            Array.Empty<Domain.Gfx.GfxResource>();
    }
}
