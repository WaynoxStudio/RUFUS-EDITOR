using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Rufmap;
using RufusMapEditor.LegacyCompatibility.Swf;

namespace RufusMapEditor.Rendering.Package;

public sealed class MapPackageOptions
{
    /// <summary>Parent folder chosen by the user. Package is written to ParentDirectory/MapId/.</summary>
    public required string ParentDirectory { get; init; }

    public string DocumentId { get; init; } = Guid.NewGuid().ToString("D");
    public RufmapSourceDto? Source { get; init; }
    public string? ProjectName { get; init; }

    /// <summary>Include Cell IDs on ModeCell PNG (deterministic package option; default true).</summary>
    public bool ShowCellIds { get; init; } = true;

    public string? LibraryRootForSwf { get; init; }
    public string? FlasmExePath { get; init; }
    public string? BlankSwfTemplatePath { get; init; }

    public Action<string>? Progress { get; init; }
}

public sealed class MapPackageResult
{
    public required bool Success { get; init; }
    public required string PackageDirectory { get; init; }
    public required int MapId { get; init; }
    public string? ErrorMessage { get; init; }
    public bool LegacySwfGenerated { get; init; }
    public string? LegacySwfWarning { get; init; }
    public string MapDataSha256 { get; init; } = "";
    public string FightPlacesSha256 { get; init; } = "";
    public int FightTeam1Count { get; init; }
    public int FightTeam2Count { get; init; }
    public int PngWidth { get; init; }
    public int PngHeight { get; init; }
    public int ModeCellWidth { get; init; }
    public int ModeCellHeight { get; init; }
    public IReadOnlyList<string> CoreFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds a local RUFUS map package folder.
/// Core: .rufmap, PNG crop, MapData TXT, ModeCell full, Gfx list, manifest.
/// Optional: Legacy/MapId_AME.swf when Flasm is available.
/// Never writes SQL, .ame, or client SWF. Never touches BD / Master Library / Astria install.
/// </summary>
public sealed class MapPackageBuilder
{
    private readonly AstriaMapRenderer _renderer;

    public MapPackageBuilder(AstriaMapRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>Core package without GFX assets (missing tiles + overlays). For unit tests.</summary>
    public static MapPackageBuilder CreateWithoutGfx() =>
        new(new AstriaMapRenderer(EmptyGfxCatalog.Instance));

    public MapPackageResult Build(MapDocument map, MapPackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        if (map.Id <= 0)
            return Fail("", map.Id, "MapId inválido. El documento debe tener un MapId > 0.");

        if (string.IsNullOrWhiteSpace(options.ParentDirectory))
            return Fail("", map.Id, "Carpeta destino no especificada.");

        MapCellEditor.SyncDocument(map);

        var parent = Path.GetFullPath(options.ParentDirectory);
        Directory.CreateDirectory(parent);
        var packageDir = Path.Combine(parent, map.Id.ToString());
        var staging = Path.Combine(parent, $".{map.Id}.pkg.tmp-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(Path.Combine(staging, "Legacy"));

            var report = options.Progress;
            var core = new List<string>();

            report?.Invoke("Generando RUFMAP...");
            var rufmapName = $"{map.Id}.rufmap";
            WriteRufmap(map, options, Path.Combine(staging, rufmapName));
            core.Add(rufmapName);

            report?.Invoke("Generando PNG...");
            var pngName = $"{map.Id}.png";
            var (pngW, pngH) = WriteNormalPng(map, Path.Combine(staging, pngName));
            core.Add(pngName);

            report?.Invoke("Generando MapData TXT...");
            var mapDataName = MapDataPlainText.FileName(map.Id);
            MapDataPlainText.WriteFile(Path.Combine(staging, mapDataName), map.MapData ?? "");
            core.Add(mapDataName);

            report?.Invoke("Generando ModeCell...");
            var modeName = $"{map.Id}_ModeCell.png";
            var (modeW, modeH) = WriteModeCellPng(map, Path.Combine(staging, modeName), options.ShowCellIds);
            core.Add(modeName);

            report?.Invoke("Generando lista GFX...");
            var gfxText = GfxUsageListBuilder.Build(map);
            File.WriteAllText(
                Path.Combine(staging, GfxUsageListBuilder.FileName),
                gfxText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            core.Add(GfxUsageListBuilder.FileName);

            var mapDataHash = Sha256Hex(map.MapData ?? "");
            var fightHash = Sha256Hex(map.FightPlaces ?? "");
            var team1 = map.Cells.Count(c => c.FightCell == 1);
            var team2 = map.Cells.Count(c => c.FightCell == 2);

            report?.Invoke("Generando SWF AME...");
            var swfAttempt = TryWriteLegacySwf(map, staging, options);
            string? swfRelative = swfAttempt.Ok ? $"Legacy\\{map.Id}_AME.swf" : null;
            var swfWarning = swfAttempt.Ok ? null : swfAttempt.Warning;

            report?.Invoke("Escribiendo manifest...");
            var manifestName = "manifest.txt";
            File.WriteAllText(
                Path.Combine(staging, manifestName),
                BuildManifest(map, mapDataHash, fightHash, team1, team2, pngName, modeName, rufmapName, mapDataName, swfRelative, swfWarning),
                new UTF8Encoding(false));
            core.Add(manifestName);

            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(Path.Combine(packageDir, "Legacy"));

            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(staging, file);
                var dest = Path.Combine(packageDir, rel);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(file, dest, overwrite: true);
            }

            return new MapPackageResult
            {
                Success = true,
                PackageDirectory = packageDir,
                MapId = map.Id,
                LegacySwfGenerated = swfAttempt.Ok,
                LegacySwfWarning = swfWarning,
                MapDataSha256 = mapDataHash,
                FightPlacesSha256 = fightHash,
                FightTeam1Count = team1,
                FightTeam2Count = team2,
                PngWidth = pngW,
                PngHeight = pngH,
                ModeCellWidth = modeW,
                ModeCellHeight = modeH,
                CoreFiles = core,
            };
        }
        catch (Exception ex)
        {
            return Fail(packageDir, map.Id, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static void WriteRufmap(MapDocument map, MapPackageOptions options, string path)
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

    private (int W, int H) WriteNormalPng(MapDocument map, string path)
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

    private (int W, int H) WriteModeCellPng(MapDocument map, string path, bool showCellIds)
    {
        var result = _renderer.Render(map, new MapRenderOptions
        {
            CropToExportBounds = false,
            AstriaLogoPath = null,
            DrawBackground = true,
            DrawGround = true,
            DrawObjectLayer1 = true,
            DrawObjectLayer2 = true,
        });

        using (result.Image)
        {
            using var g = Graphics.FromImage(result.Image);
            var corners = IsoGeometry.BuildCellCorners(map.Width, map.Height);
            ModeCellOverlayPainter.Paint(g, map, corners, showCellIds);
            result.Image.Save(path, ImageFormat.Png);
            return (result.Image.Width, result.Image.Height);
        }
    }

    private static (bool Ok, string? Warning) TryWriteLegacySwf(
        MapDocument map,
        string staging,
        MapPackageOptions options)
    {
        try
        {
            if (map.Outdoor is null)
                return (false, "Legacy SWF: NO GENERADO — Outdoor (bOutdoor) ausente en el documento.");

            var flasm = options.FlasmExePath;
            var blank = options.BlankSwfTemplatePath;
            if ((string.IsNullOrWhiteSpace(flasm) || string.IsNullOrWhiteSpace(blank))
                && !string.IsNullOrWhiteSpace(options.LibraryRootForSwf))
            {
                flasm ??= SwfMapExporter.ResolveFlasmExe(options.LibraryRootForSwf);
                blank ??= SwfMapExporter.ResolveBlankSwf(options.LibraryRootForSwf);
            }

            if (string.IsNullOrWhiteSpace(flasm) || !File.Exists(flasm))
                return (false, "Legacy SWF: NO GENERADO — Flasm no disponible.");
            if (string.IsNullOrWhiteSpace(blank) || !File.Exists(blank))
                return (false, "Legacy SWF: NO GENERADO — blank.swf no disponible.");

            var dest = Path.Combine(staging, "Legacy", $"{map.Id}_AME.swf");
            var swf = SwfMapExporter.Export(new SwfExportRequest
            {
                Document = map,
                DestinationSwfPath = dest,
                FlasmExePath = flasm,
                BlankSwfTemplatePath = blank,
            });

            if (!swf.Success)
                return (false, $"Legacy SWF: NO GENERADO — {swf.ErrorMessage}");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Legacy SWF: NO GENERADO — {ex.Message}");
        }
    }

    private static string BuildManifest(
        MapDocument map,
        string mapDataHash,
        string fightHash,
        int team1,
        int team2,
        string pngName,
        string modeName,
        string rufmapName,
        string mapDataTxtName,
        string? swfRelative,
        string? swfWarning)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RUFUS Map Package");
        sb.AppendLine($"MapId: {map.Id}");
        sb.AppendLine($"Width: {map.Width}");
        sb.AppendLine($"Height: {map.Height}");
        sb.AppendLine($"Cells: {map.Cells.Count}");
        sb.AppendLine($"Background: {map.BackgroundId}");
        sb.AppendLine($"MapData SHA256: {mapDataHash}");
        sb.AppendLine($"FightPlaces SHA256: {fightHash}");
        sb.AppendLine($"Team1 cells: {team1}");
        sb.AppendLine($"Team2 cells: {team2}");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Rufmap: {rufmapName}");
        sb.AppendLine($"PNG: {pngName}");
        sb.AppendLine($"MapData TXT: {mapDataTxtName}");
        sb.AppendLine($"ModeCell: {modeName}");
        sb.AppendLine($"GfxList: {GfxUsageListBuilder.FileName}");
        if (swfRelative is not null)
            sb.AppendLine($"Legacy SWF: {swfRelative}");
        else
            sb.AppendLine("Legacy SWF: NO GENERADO");
        if (swfWarning is not null)
            sb.AppendLine($"Motivo: {swfWarning}");
        sb.AppendLine();
        sb.AppendLine("SWF Astria/AME de compatibilidad. No confirmado como SWF cliente RUFUS.");
        sb.AppendLine("SQL producción: no incluido.");
        sb.AppendLine("SQL legacy: no incluido.");
        sb.AppendLine("AME BinaryFormatter: no incluido.");
        sb.AppendLine("Guardar (.rufmap) ≠ Exportar paquete ≠ Publicar (futuro).");
        return sb.ToString();
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static MapPackageResult Fail(string dir, int mapId, string message) => new()
    {
        Success = false,
        PackageDirectory = dir,
        MapId = mapId,
        ErrorMessage = message,
    };

    private sealed class EmptyGfxCatalog : IGfxCatalog
    {
        public static readonly EmptyGfxCatalog Instance = new();
        public int BackgroundCount => 0;
        public int GroundCount => 0;
        public int ObjectCount => 0;
        public int TotalCount => 0;
        public bool TryGet(GfxCategory category, int id, out GfxResource? resource) { resource = null; return false; }
        public bool TryGetBackground(int id, out GfxResource? resource) { resource = null; return false; }
        public bool TryGetGround(int id, out GfxResource? resource) { resource = null; return false; }
        public bool TryGetObject(int id, out GfxResource? resource) { resource = null; return false; }
        public bool TryGetAnchor(GfxCategory category, int id, out GfxAnchor anchor) { anchor = default; return false; }
        public IEnumerable<GfxResource> Enumerate(GfxCategory? category = null) => Array.Empty<GfxResource>();
        public IEnumerable<GfxResource> EnumerateById(int id) => Array.Empty<GfxResource>();
    }
}
