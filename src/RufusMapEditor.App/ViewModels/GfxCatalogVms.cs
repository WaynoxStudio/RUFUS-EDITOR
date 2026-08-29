using System.Collections.ObjectModel;
using System.Windows.Media;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App.ViewModels;

public sealed class GfxItemVm : ViewModelBase
{
    private ImageSource? _thumbnail;
    private bool _isFavorite;
    private bool _isSelected;

    public required int Id { get; init; }
    public required GfxResource Resource { get; init; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void OnThumbnailChanged() => OnPropertyChanged(nameof(Thumbnail));
}

/// <summary>One virtualized row of thumbnails (real ListBox virtualization).</summary>
public sealed class GfxRowVm
{
    public required ObservableCollection<GfxItemVm> Items { get; init; }
}

public sealed class FolderNodeVm
{
    public required string Name { get; init; }
    public GfxCategory? Category { get; init; }
    public bool IsUnifiedFavorites { get; init; }
    public ObservableCollection<FolderNodeVm> Children { get; } = new();
    public override string ToString() => Name;
}
