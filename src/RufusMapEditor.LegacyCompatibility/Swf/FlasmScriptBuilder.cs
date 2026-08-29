using System.Globalization;
using System.Text;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Swf;

/// <summary>
/// Builds Astria-compatible Flasm assembly (.flm) for a map SWF.
/// Mirrors <c>AstriaMapEditor.Flasm.Get_FlasmCode</c>.
/// </summary>
public static class FlasmScriptBuilder
{
    /// <summary>
    /// Generates FLM text. <paramref name="blankSwfRelativePath"/> is the movie path
    /// relative to the Flasm working directory (typically <c>blank.swf</c>).
    /// </summary>
    public static string Build(MapDocument map, string blankSwfRelativePath = "blank.swf")
    {
        ArgumentNullException.ThrowIfNull(map);
        ValidateForExport(map);

        MapCellEditor.SyncMapDataString(map);
        var mapData = map.MapData;
        var expectedLen = MapGeometry.ExpectedMapDataLength(map.Width, map.Height);
        if (mapData.Length != expectedLen)
            throw new SwfExportException(
                $"MapData inválido: longitud {mapData.Length}, esperado {expectedLen}.");
        ValidateMapDataAlphabet(mapData);

        // Astria VB Boolean.ToString → True/False; Flasm accepts and disassembles as TRUE/FALSE.
        var outdoor = map.Outdoor!.Value ? "TRUE" : "FALSE";

        var sb = new StringBuilder(mapData.Length + 512);
        sb.Append("movie '").Append(EscapeFlasmPath(blankSwfRelativePath)).Append("' compressed");
        sb.Append("\r\n");
        sb.Append("  frame 0\r\n");
        sb.Append("\r\n");
        sb.Append("    constants '_parent', '_url', 'System', 'security', 'allowDomain', 'id', 'width', 'height', 'backgroundNum', 'ambianceId', 'musicId', 'bOutdoor', 'capabilities', 'mapData', '");
        sb.Append(mapData);
        sb.Append("'\r\n");
        sb.Append("    push c:0\r\n");
        sb.Append("    getVariable\r\n");
        sb.Append("    push c:1\r\n");
        sb.Append("    getMember\r\n");
        sb.Append("    push 1, c:2\r\n");
        sb.Append("    getVariable\r\n");
        sb.Append("    push c:3\r\n");
        sb.Append("    getMember\r\n");
        sb.Append("    push c:4\r\n");
        sb.Append("    callMethod\r\n");
        sb.Append("    pop\r\n");
        sb.Append("    push c:5\r\n");
        sb.Append("    push ").Append(map.Id.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:6\r\n");
        sb.Append("    push ").Append(map.Width.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:7\r\n");
        sb.Append("    push ").Append(map.Height.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:8\r\n");
        sb.Append("    push ").Append(map.BackgroundId.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:9\r\n");
        sb.Append("    push ").Append(map.AmbianceId.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:10\r\n");
        sb.Append("    push ").Append(map.MusicId.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:11\r\n");
        sb.Append("    push ").Append(outdoor).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:12\r\n");
        sb.Append("    push ").Append(map.Capabilities.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("    push c:13\r\n");
        sb.Append("    push c:14\r\n");
        sb.Append("    setVariable\r\n");
        sb.Append("  end\r\n");
        sb.Append("end\r\n");
        return sb.ToString();
    }

    public static void ValidateForExport(MapDocument map)
    {
        if (map.Id <= 0)
            throw new SwfExportException("Map ID inválido para exportación SWF.");
        if (map.Width <= 0 || map.Height <= 0)
            throw new SwfExportException("Width/Height inválidos para exportación SWF.");
        if (map.Outdoor is null)
            throw new SwfExportException(
                "Metadata obligatoria ausente: bOutdoor (Outdoor). " +
                "DATO PENDIENTE DE CONFIRMAR — no se inventa un valor. " +
                "Cargue metadata desde un SWF Astria o un .rufmap que la contenga.");
        if (map.Cells is null || map.Cells.Count == 0)
            throw new SwfExportException("El documento no tiene celdas para codificar MapData.");

        var expected = MapGeometry.CellCount(map.Width, map.Height);
        if (map.Cells.Count != expected)
            throw new SwfExportException(
                $"MapData/celdas incoherentes: {map.Cells.Count} celdas, esperado {expected} para {map.Width}x{map.Height}.");
    }

    private static void ValidateMapDataAlphabet(string mapData)
    {
        foreach (var ch in mapData)
        {
            if (!IsMapDataChar(ch))
                throw new SwfExportException($"MapData inválido: carácter no permitido '{ch}'.");
        }
    }

    private static bool IsMapDataChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '-' or '_';

    private static string EscapeFlasmPath(string path) =>
        path.Replace("'", "", StringComparison.Ordinal);
}

public sealed class SwfExportException : Exception
{
    public SwfExportException(string message) : base(message) { }
    public SwfExportException(string message, Exception inner) : base(message, inner) { }
}
