using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RufusMapEditor.App;

public sealed class IslaFechaPreviewItemVm : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _isLoading;

    public IslaFechaPreviewItemVm(int mapId, string fecha, int x, int y)
    {
        MapId = mapId;
        Fecha = fecha ?? "";
        X = x;
        Y = y;
    }

    public int MapId { get; }
    public string Fecha { get; }
    public int X { get; }
    public int Y { get; }
    public string CoordText => $"{X},{Y}";

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail == value) return;
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
