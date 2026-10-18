using System.Collections.ObjectModel;
using System.Windows.Media;
using RufusMapEditor.App.Services;
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

public sealed class FolderNodeVm : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;

    public required string Name { get; init; }
    public GfxCategory? Category { get; init; }
    public bool IsUnifiedFavorites { get; init; }
    public ObservableCollection<FolderNodeVm> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public override string ToString() => Name;
}

/// <summary>One distinct GFX found inside the current cell selection (mass selector).</summary>
public sealed class SelectionGfxItemVm : ViewModelBase
{
    private ImageSource? _thumbnail;
    private bool _isSelected;

    public required int Id { get; init; }
    public required PaintLayer Layer { get; init; }
    public required int Count { get; init; }
    public GfxResource? Resource { get; init; }

    public string LayerLabel => Layer switch
    {
        PaintLayer.Ground => "Suelo",
        PaintLayer.Object1 => "Capa 1",
        PaintLayer.Object2 => "Capa 2",
        _ => "Capa",
    };

    public string Summary => $"GFX {Id} · {LayerLabel} · ×{Count}";

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
