using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.Controls;

/// <summary>
/// Compact 8-direction orientation picker for NPC locations.
/// Syncs visual selection with the raw orientacion int (0 = unset).
/// </summary>
public partial class NpcOrientationPicker : UserControl
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(int),
            typeof(NpcOrientationPicker),
            new FrameworkPropertyMetadata(
                NpcOrientationCatalog.Unset,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnOrientationChanged));

    private bool _syncing;
    private readonly Dictionary<int, Button> _dirButtons = new();

    public NpcOrientationPicker()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public int Orientation
    {
        get => (int)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public event EventHandler? OrientationEdited;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        CacheButtons();
        RefreshVisual();
    }

    private void CacheButtons()
    {
        _dirButtons.Clear();
        foreach (var btn in FindVisualChildren<Button>(DirGrid))
        {
            if (btn.Tag is string s && int.TryParse(s, out var dir) && NpcOrientationCatalog.IsVisualDirection(dir))
                _dirButtons[dir] = btn;
        }
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NpcOrientationPicker picker)
            picker.RefreshVisual();
    }

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string s } || !int.TryParse(s, out var dir))
            return;
        if (!NpcOrientationCatalog.IsVisualDirection(dir))
            return;

        _syncing = true;
        Orientation = dir;
        NumericBox.Text = dir.ToString();
        _syncing = false;
        RefreshVisual();
        OrientationEdited?.Invoke(this, EventArgs.Empty);
    }

    private void NumericBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (!int.TryParse(NumericBox.Text.Trim(), out var value))
            return;
        if (Orientation == value)
        {
            RefreshVisual();
            return;
        }

        _syncing = true;
        Orientation = value;
        _syncing = false;
        RefreshVisual();
        OrientationEdited?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshVisual()
    {
        if (!IsLoaded) return;
        if (_dirButtons.Count == 0)
            CacheButtons();

        var value = Orientation;
        SelectedLabel.Text = NpcOrientationCatalog.FormatSelectedLabel(value);

        var text = value.ToString();
        if (!_syncing && NumericBox.Text != text)
        {
            _syncing = true;
            NumericBox.Text = text;
            _syncing = false;
        }

        foreach (var (dir, btn) in _dirButtons)
            ApplyButtonStyle(btn, NpcOrientationCatalog.IsVisualDirection(value) && dir == value);
    }

    private static void ApplyButtonStyle(Button btn, bool selected)
    {
        btn.Background = ResolveBrush(selected ? "BrandAccent" : "DialogHeaderBackground",
            selected ? Brushes.Goldenrod : Brushes.DimGray);
        btn.BorderBrush = ResolveBrush(selected ? "BrandAccentHover" : "Border",
            selected ? Brushes.Gold : Brushes.Gray);
        btn.Foreground = ResolveBrush(selected ? "TextPrimary" : "TextSecondary",
            selected ? Brushes.White : Brushes.LightGray);
        btn.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
        btn.Opacity = selected ? 1.0 : 0.9;
        btn.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
    }

    private static Brush ResolveBrush(string key, Brush fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return fallback;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
