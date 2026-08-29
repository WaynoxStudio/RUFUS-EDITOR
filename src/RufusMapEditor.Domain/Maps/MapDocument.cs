namespace RufusMapEditor.Domain.Maps;

/// <summary>
/// Editable map document. Cells + MapData are the classical MapData model;
/// optional metadata fields come from SWF/Astria when available.
/// </summary>
public sealed class MapDocument
{
    public int Id { get; set; }
    public int Width { get; set; } = 15;
    public int Height { get; set; } = 17;
    public string DateMap { get; set; } = "AME";
    public string Key { get; set; } = string.Empty;
    public string MapData { get; set; } = string.Empty;
    public string FightPlaces { get; set; } = string.Empty;

    /// <summary>
    /// Astria <c>BackgroundID</c> / SWF <c>backgroundNum</c>. 0 means no background when defined.
    /// </summary>
    public int BackgroundId { get; set; }

    /// <summary>True when BackgroundId was set by the editor, SWF import, or BD sync — not a legacy default.</summary>
    public bool BackgroundDefined { get; set; }

    /// <summary>SWF <c>musicId</c> when known.</summary>
    public int MusicId { get; set; }

    public bool MusicDefined { get; set; }

    /// <summary>SWF <c>ambianceId</c> when known.</summary>
    public int AmbianceId { get; set; }

    public bool AmbianceDefined { get; set; }

    /// <summary>SWF <c>capabilities</c> when known.</summary>
    public int Capabilities { get; set; }

    public bool CapabilitiesDefined { get; set; }

    /// <summary>SWF <c>bOutdoor</c> when known. Null = not defined (distinct from false).</summary>
    public bool? Outdoor { get; set; }

    /// <summary>
    /// World grid X for BD <c>mapas.X</c>. Negatives allowed (e.g. -47).
    /// Only meaningful when <see cref="WorldCoordinatesSet"/> is true.
    /// </summary>
    public int WorldX { get; set; }

    /// <summary>
    /// World grid Y for BD <c>mapas.Y</c>. Negatives allowed (e.g. 33).
    /// Only meaningful when <see cref="WorldCoordinatesSet"/> is true.
    /// </summary>
    public int WorldY { get; set; }

    /// <summary>
    /// True when the user (or import/sync) explicitly set world X/Y.
    /// Distinguishes "undefined" from the valid coordinate (0,0).
    /// </summary>
    public bool WorldCoordinatesSet { get; set; }

    public IList<CellData> Cells { get; set; } = new List<CellData>();
}
