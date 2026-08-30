using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

public partial class ReplaceGfxWindow : Window
{
    public int FindId { get; private set; }
    public int ReplaceId { get; private set; }
    public bool WholeMap { get; private set; }
    public IReadOnlyList<PaintLayer> TargetLayers { get; private set; } = new[] { PaintLayer.Ground };
    /// <summary>null = keep existing rotation on each cell.</summary>
    public int? ForceRotation { get; private set; }
    /// <summary>null = keep existing flip on each cell.</summary>
    public bool? ForceFlip { get; private set; }

    private readonly PaintLayer _activeLayer;
    private readonly int? _brushRotation;
    private readonly bool? _brushFlip;

    public ReplaceGfxWindow(
        string layerName,
        int? suggestedFind,
        int? suggestedReplace = null,
        int? brushRotation = null,
        bool? brushFlip = null,
        PaintLayer activeLayer = PaintLayer.Ground,
        bool preferWholeMap = false)
    {
        InitializeComponent();
        _activeLayer = activeLayer;
        _brushRotation = brushRotation;
        _brushFlip = brushFlip;

        LayerHint.Text = preferWholeMap
            ? $"Vas a sustituir este ítem en {layerName}. Escribe el GfxID nuevo y confirma."
            : $"Capa activa ahora: {layerName}. Elige alcance, capas y si quieres forzar rotación/flip al sustituir.";

        if (suggestedFind is int find)
            FindBox.Text = find.ToString();
        if (suggestedReplace is int replace)
            ReplaceBox.Text = replace.ToString();
        else if (suggestedFind is int onlyFind)
            ReplaceBox.Text = onlyFind.ToString();

        if (preferWholeMap)
        {
            ScopeWholeMap.IsChecked = true;
            ScopeSelection.IsChecked = false;
        }

        RotationBox.Items.Add("Mantener rotación actual");
        RotationBox.Items.Add("Usar rotación del pincel");
        RotationBox.Items.Add("Forzar 0");
        RotationBox.Items.Add("Forzar 1");
        RotationBox.Items.Add("Forzar 2");
        RotationBox.Items.Add("Forzar 3");
        RotationBox.SelectedIndex = brushRotation is not null ? 1 : 0;

        FlipBox.Items.Add("Mantener flip actual");
        FlipBox.Items.Add("Usar flip del pincel");
        FlipBox.Items.Add("Sin flip");
        FlipBox.Items.Add("Con flip");
        FlipBox.SelectedIndex = brushFlip is not null ? 1 : 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FindBox.Text.Trim(), out var find) ||
            !int.TryParse(ReplaceBox.Text.Trim(), out var replace))
        {
            MessageBox.Show("Introduce GfxID numéricos válidos.", "Reemplazar GFX");
            return;
        }

        FindId = find;
        ReplaceId = replace;
        WholeMap = ScopeWholeMap.IsChecked == true;
        TargetLayers = ResolveLayers();
        ForceRotation = ResolveRotation();
        ForceFlip = ResolveFlip();
        DialogResult = true;
    }

    private IReadOnlyList<PaintLayer> ResolveLayers()
    {
        if (LayerObject1.IsChecked == true) return new[] { PaintLayer.Object1 };
        if (LayerObject2.IsChecked == true) return new[] { PaintLayer.Object2 };
        if (LayerObjects.IsChecked == true) return new[] { PaintLayer.Object1, PaintLayer.Object2 };
        if (LayerAll.IsChecked == true)
            return new[] { PaintLayer.Ground, PaintLayer.Object1, PaintLayer.Object2 };
        return new[] { _activeLayer };
    }

    private int? ResolveRotation() => RotationBox.SelectedIndex switch
    {
        1 => _brushRotation ?? 0,
        2 => 0,
        3 => 1,
        4 => 2,
        5 => 3,
        _ => null,
    };

    private bool? ResolveFlip() => FlipBox.SelectedIndex switch
    {
        1 => _brushFlip ?? false,
        2 => false,
        3 => true,
        _ => null,
    };
}
