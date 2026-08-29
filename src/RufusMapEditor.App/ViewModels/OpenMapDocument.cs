using System.Windows.Media;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Swf;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// One open map document in the editor workspace (independent floating window).
/// </summary>
public sealed class OpenMapDocument : ViewModelBase
{
    private ImageSource? _mapImage;

    public OpenMapDocument(
        MapDocument map,
        MapEditSession session,
        IsoHitTester hitTester,
        ImageSource? mapImage,
        FlasmSwfMetadataReader.SwfMapMetadata? swfMeta)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        HitTester = hitTester ?? throw new ArgumentNullException(nameof(hitTester));
        _mapImage = mapImage;
        SwfMeta = swfMeta;
        DocumentId = session.DocumentId;
    }

    public string DocumentId { get; }
    public MapDocument Map { get; }
    public MapEditSession Session { get; }
    public IsoHitTester HitTester { get; }
    public FlasmSwfMetadataReader.SwfMapMetadata? SwfMeta { get; set; }

    public int MapId => Map.Id;

    public ImageSource? MapImage
    {
        get => _mapImage;
        set
        {
            if (SetProperty(ref _mapImage, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool IsDirty => Session.IsDirty;

    public string WindowTitle
    {
        get
        {
            var dirty = IsDirty ? " *" : "";
            return $"Map {Map.Id}{dirty}";
        }
    }

    public void NotifyDirtyChanged() => OnPropertyChanged(nameof(WindowTitle));

    /// <summary>Cascade offset index when first opened (UI only).</summary>
    public int CascadeIndex { get; set; }
}
