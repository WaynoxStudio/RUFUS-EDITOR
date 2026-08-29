namespace RufusMapEditor.LegacyCompatibility.World;

/// <summary>Identifies a cell within a specific world-owned map document.</summary>
public readonly record struct WorldCellRef(string DocumentKey, int CellId)
{
    public string StrokeKey => $"{DocumentKey}:{CellId}";

    public override string ToString() => $"{DocumentKey}#{CellId}";
}
