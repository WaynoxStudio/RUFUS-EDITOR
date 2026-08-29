using System.Text;
using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.Rendering.Package;

/// <summary>
/// Deterministic Gfx usage list for map packages.
/// Namespaces Ground / Object are separate — the same numeric ID may appear in both.
/// Order: ascending GfxID within each namespace (stable for diffs).
/// Encoding: UTF-8 without BOM.
/// </summary>
public static class GfxUsageListBuilder
{
    public const string FileName = "GfxID utilizados.txt";

    public static string Build(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var ground = new SortedSet<int>();
        var objects = new SortedSet<int>();

        foreach (var cell in map.Cells)
        {
            if (cell.GroundGfxId > 0)
                ground.Add(cell.GroundGfxId);
            if (cell.Object1GfxId > 0)
                objects.Add(cell.Object1GfxId);
            if (cell.Object2GfxId > 0)
                objects.Add(cell.Object2GfxId);
        }

        var sb = new StringBuilder();
        sb.Append("MapId: ").Append(map.Id).Append('\n');
        sb.Append("Background: ").Append(map.BackgroundId).Append('\n');
        sb.Append('\n');
        sb.Append("[Ground]\n");
        foreach (var id in ground)
            sb.Append(id).Append('\n');
        sb.Append('\n');
        sb.Append("[Object]\n");
        foreach (var id in objects)
            sb.Append(id).Append('\n');

        return sb.ToString();
    }
}
