using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RufusMapEditor.App;

public sealed class MapPickerItemVm : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _isLoading = true;

    public MapPickerItemVm(int mapId) => MapId = mapId;

    public int MapId { get; }

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
