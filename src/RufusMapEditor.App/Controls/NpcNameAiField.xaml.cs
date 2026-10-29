using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

/// <summary>ADMIN.UI.4B — Nombre con icono IA que abre popup del asistente de nombres.</summary>
public partial class NpcNameAiField : UserControl
{
    public static readonly DependencyProperty NombreProperty =
        DependencyProperty.Register(
            nameof(Nombre),
            typeof(string),
            typeof(NpcNameAiField),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AiAssistantProperty =
        DependencyProperty.Register(
            nameof(AiAssistant),
            typeof(ContentAiAssistantViewModel),
            typeof(NpcNameAiField),
            new PropertyMetadata(null, OnAiAssistantChanged));

    public static readonly DependencyProperty IsEditorEnabledProperty =
        DependencyProperty.Register(
            nameof(IsEditorEnabled),
            typeof(bool),
            typeof(NpcNameAiField),
            new PropertyMetadata(true));

    private ContentAiAssistantViewModel? _subscribedAssistant;

    public NpcNameAiField()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string Nombre
    {
        get => (string)GetValue(NombreProperty);
        set => SetValue(NombreProperty, value);
    }

    public ContentAiAssistantViewModel? AiAssistant
    {
        get => (ContentAiAssistantViewModel?)GetValue(AiAssistantProperty);
        set => SetValue(AiAssistantProperty, value);
    }

    public bool IsEditorEnabled
    {
        get => (bool)GetValue(IsEditorEnabledProperty);
        set => SetValue(IsEditorEnabledProperty, value);
    }

    private static void OnAiAssistantChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NpcNameAiField field)
            return;
        field.UnsubscribeAssistant();
        field._subscribedAssistant = e.NewValue as ContentAiAssistantViewModel;
        if (field._subscribedAssistant is not null)
            field._subscribedAssistant.NameApplied += field.OnNameApplied;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
            window.PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
            window.PreviewMouseDown -= Window_PreviewMouseDown;
        UnsubscribeAssistant();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AiPopup.IsOpen)
            return;
        // ComboBox drop-downs are a separate Popup HWND. Walking only the visual tree
        // treats a click on "Misterioso" / "Media" as outside, closes this panel
        // before SelectedItem commits, and the choice is lost.
        if (IsWithin(this, e.OriginalSource as DependencyObject)
            || IsWithin(AiPopup.Child, e.OriginalSource as DependencyObject))
            return;
        SetPopupOpen(false);
    }

    private static bool IsWithin(DependencyObject? root, DependencyObject? source)
    {
        if (root is null || source is null)
            return false;
        for (var node = source; node is not null; node = GetTreeParent(node))
        {
            if (ReferenceEquals(node, root))
                return true;
        }
        return false;
    }

    private static DependencyObject? GetTreeParent(DependencyObject node)
    {
        // Logical first: ComboBoxItem → ComboBox (still inside our panel).
        var logical = LogicalTreeHelper.GetParent(node);
        if (logical is not null)
            return logical;

        if (node is FrameworkElement fe)
        {
            if (fe.Parent is not null)
                return fe.Parent;
            if (fe.TemplatedParent is not null)
                return fe.TemplatedParent;
        }

        if (node is ContentElement ce)
        {
            var contentParent = ContentOperations.GetParent(ce);
            if (contentParent is not null)
                return contentParent;
            if (ce is FrameworkContentElement fce && fce.Parent is not null)
                return fce.Parent;
        }

        if (node is Visual or Visual3D)
        {
            var visual = VisualTreeHelper.GetParent(node);
            if (visual is not null)
                return visual;
        }

        // PopupRoot has neither visual nor logical parent; hop via PlacementTarget.
        if (node is Popup popup && popup.PlacementTarget is DependencyObject target)
            return target;

        if (node.GetType().Name == "PopupRoot"
            && node is FrameworkElement
            && VisualTreeHelper.GetChildrenCount(node) > 0
            && VisualTreeHelper.GetChild(node, 0) is DependencyObject decorator)
        {
            var popupChild = VisualTreeHelper.GetChildrenCount(decorator) > 0
                ? VisualTreeHelper.GetChild(decorator, 0)
                : decorator;
            if (LogicalTreeHelper.GetParent(popupChild) is Popup owner && owner.PlacementTarget is DependencyObject placedOn)
                return placedOn;
        }

        return null;
    }

    private void UnsubscribeAssistant()
    {
        if (_subscribedAssistant is null)
            return;
        _subscribedAssistant.NameApplied -= OnNameApplied;
        _subscribedAssistant = null;
    }

    private void OnNameApplied() => SetPopupOpen(false);

    private void AiButton_Click(object sender, RoutedEventArgs e) =>
        SetPopupOpen(!AiPopup.IsOpen);

    private void SetPopupOpen(bool open) => AiPopup.IsOpen = open;
}
