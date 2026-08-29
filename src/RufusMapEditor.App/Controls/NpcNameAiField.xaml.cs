using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (IsWithin(this, e.OriginalSource as DependencyObject)
            || IsWithin(AiPopup.Child, e.OriginalSource as DependencyObject))
            return;
        SetPopupOpen(false);
    }

    private static bool IsWithin(DependencyObject? root, DependencyObject? source)
    {
        if (root is null || source is null)
            return false;
        for (var node = source; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, root))
                return true;
        }
        return false;
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
