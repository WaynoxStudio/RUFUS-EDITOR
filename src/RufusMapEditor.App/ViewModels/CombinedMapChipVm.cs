namespace RufusMapEditor.App.ViewModels;

/// <summary>Inline checkbox for a map in MAPA combinado (no dialog).</summary>
public sealed class CombinedMapChipVm : ViewModelBase
{
    private readonly MainViewModel _owner;
    private bool _isSelected;
    private bool _silent;

    public CombinedMapChipVm(MainViewModel owner, string documentKey, string label, bool isSelected)
    {
        _owner = owner;
        DocumentKey = documentKey;
        Label = label;
        _isSelected = isSelected;
    }

    public string DocumentKey { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            if (!_silent)
                _owner.OnCombinedMapChipToggled();
        }
    }

    public void SetSelectedSilent(bool value)
    {
        _silent = true;
        try
        {
            IsSelected = value;
        }
        finally
        {
            _silent = false;
        }
    }
}
