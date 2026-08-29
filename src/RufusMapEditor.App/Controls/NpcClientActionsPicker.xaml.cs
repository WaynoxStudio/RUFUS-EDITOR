using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace RufusMapEditor.App.Controls;

/// <summary>ADMIN.UI.4B.2A.2 — compact multiselect for npc_es client actions.</summary>
public partial class NpcClientActionsPicker : UserControl
{
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(
            nameof(Actions),
            typeof(IEnumerable),
            typeof(NpcClientActionsPicker),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(NpcClientActionsPicker),
            new PropertyMetadata("(ninguna)"));

    public NpcClientActionsPicker()
    {
        InitializeComponent();
    }

    public IEnumerable? Actions
    {
        get => (IEnumerable?)GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }
}
