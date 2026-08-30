namespace RufusMapEditor.App.Services;

/// <summary>Ghost cell while dragging a selection across the map.</summary>
public readonly record struct SelectionMovePreviewItem(
    double CenterX,
    double CenterY,
    int? TargetCellId)
{
    public bool IsOutside => TargetCellId is null;
}
