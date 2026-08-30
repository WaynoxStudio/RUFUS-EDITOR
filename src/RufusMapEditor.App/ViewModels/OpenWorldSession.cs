namespace RufusMapEditor.App.ViewModels;

/// <summary>
/// One open world document in the MUNDO workspace (independent floating window).
/// </summary>
public sealed class OpenWorldSession : ViewModelBase
{
    public OpenWorldSession(WorldViewModel vm, int cascadeIndex)
    {
        Vm = vm ?? throw new ArgumentNullException(nameof(vm));
        CascadeIndex = cascadeIndex;
        SessionId = vm.World?.WorldId ?? Guid.NewGuid().ToString("D");
        Vm.PresentationChanged += OnVmPresentationChanged;
    }

    public string SessionId { get; private set; }
    public WorldViewModel Vm { get; }
    public int CascadeIndex { get; set; }

    public string WindowTitle
    {
        get
        {
            var dirty = Vm.IsDirty ? " *" : "";
            if (Vm.World is null)
                return $"Mundo{dirty}";
            return $"{Vm.CurrentWorldLabel}{dirty}";
        }
    }

    public void SyncSessionIdFromWorld()
    {
        if (Vm.World?.WorldId is { Length: > 0 } id)
            SessionId = id;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void OnVmPresentationChanged()
    {
        SyncSessionIdFromWorld();
        OnPropertyChanged(nameof(WindowTitle));
    }

    public void Detach()
    {
        Vm.PresentationChanged -= OnVmPresentationChanged;
    }
}
