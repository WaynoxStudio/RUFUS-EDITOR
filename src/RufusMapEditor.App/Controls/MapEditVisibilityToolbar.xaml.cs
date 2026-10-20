using System.Windows.Controls;

namespace RufusMapEditor.App.Controls;

/// <summary>
/// Shared MAPA visibility toggles (Fondo, capas, rejilla, no transitables, LoS, combate).
/// Bound to <see cref="ViewModels.MainViewModel"/> DataContext.
/// </summary>
public partial class MapEditVisibilityToolbar : UserControl
{
    public MapEditVisibilityToolbar()
    {
        InitializeComponent();
    }
}
