namespace RufusMapEditor.App.Services;

public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed class MapViewVisibilitySettings
{
    public bool ShowBackground { get; set; } = true;
    public bool ShowGround { get; set; } = true;
    public bool ShowObject1 { get; set; } = true;
    public bool ShowObject2 { get; set; } = true;
    public bool ShowGrid { get; set; }
    public bool ShowCellIds { get; set; }
    public bool ShowUnwalkableMarkers { get; set; } = true;
    public bool ShowLosBlockMarkers { get; set; } = true;
    public bool ShowFightMarkers { get; set; } = true;

    public MapViewVisibilitySettings Clone() => new()
    {
        ShowBackground = ShowBackground,
        ShowGround = ShowGround,
        ShowObject1 = ShowObject1,
        ShowObject2 = ShowObject2,
        ShowGrid = ShowGrid,
        ShowCellIds = ShowCellIds,
        ShowUnwalkableMarkers = ShowUnwalkableMarkers,
        ShowLosBlockMarkers = ShowLosBlockMarkers,
        ShowFightMarkers = ShowFightMarkers,
    };
}
