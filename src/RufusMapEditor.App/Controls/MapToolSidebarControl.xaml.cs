using System.Windows;
using System.Windows.Controls;

namespace RufusMapEditor.App.Controls;

/// <summary>
/// Shared MAPA tool strip (HERRAMIENTAS / CELDA / COMBATE / CAPA / TRANSFORM).
/// Used on the MAPA tab and when editing maps inside MUNDO.
/// </summary>
public partial class MapToolSidebarControl : UserControl
{
    public static readonly DependencyProperty ToolGroupNameProperty =
        DependencyProperty.Register(
            nameof(ToolGroupName),
            typeof(string),
            typeof(MapToolSidebarControl),
            new PropertyMetadata("MapTool"));

    public static readonly DependencyProperty LayerGroupNameProperty =
        DependencyProperty.Register(
            nameof(LayerGroupName),
            typeof(string),
            typeof(MapToolSidebarControl),
            new PropertyMetadata("Layer"));

    public MapToolSidebarControl()
    {
        InitializeComponent();
    }

    /// <summary>RadioButton.GroupName for paint/select tools (must be unique per host).</summary>
    public string ToolGroupName
    {
        get => (string)GetValue(ToolGroupNameProperty);
        set => SetValue(ToolGroupNameProperty, value);
    }

    /// <summary>RadioButton.GroupName for GFX layer 1/2.</summary>
    public string LayerGroupName
    {
        get => (string)GetValue(LayerGroupNameProperty);
        set => SetValue(LayerGroupNameProperty, value);
    }
}
