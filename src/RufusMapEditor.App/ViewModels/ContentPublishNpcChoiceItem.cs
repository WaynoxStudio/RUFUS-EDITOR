using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.ViewModels;

/// <summary>Checkbox row for selecting which NPCs enter BD / client publish.</summary>
public sealed class ContentPublishNpcChoiceItem : ViewModelBase
{
    private bool _isSelected;

    public ContentPublishNpcChoiceItem(
        ContentPublishNpcSelection.Candidate candidate,
        bool isSelected,
        Action? selectionChanged)
    {
        Id = candidate.Id;
        Nombre = candidate.Nombre;
        Reason = candidate.Reason;
        _isSelected = isSelected;
        SelectionChanged = selectionChanged;
    }

    public int Id { get; }
    public string Nombre { get; }
    public string Reason { get; }
    public string DisplayLabel => $"{Id} · {Nombre} ({Reason})";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
                return;
            SelectionChanged?.Invoke();
        }
    }

    private Action? SelectionChanged { get; }
}
