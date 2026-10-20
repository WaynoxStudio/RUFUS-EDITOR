using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class ContentDialogEditorPanel : UserControl
{
    private const double WideLayoutMinTotalWidth = 720;
    private const double ConversationMinWidth = 260;
    private const double MissionPanelMinWidth = 300;
    private const double CompactPanelWidth = 320;
    private const double BubbleDragThreshold = 4;

    private ContentDialogEditorViewModel? _subscribedVm;
    private bool _wideLayoutActive;
    private bool _actionPanelLayoutScheduled;
    private string? _missionEditorDpDiagnostic;
    private DialogTreeNodeVm? _dragNode;
    private Point _dragStartCanvas;
    private Point _dragStartLayout;
    private bool _dragActive;
    private bool _dragMoved;

    public string? MissionEditorDpDiagnostic => _missionEditorDpDiagnostic;

    public static readonly DependencyProperty AiAssistantProperty =
        DependencyProperty.Register(
            nameof(AiAssistant),
            typeof(ContentAiAssistantViewModel),
            typeof(ContentDialogEditorPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MissionEditorProperty =
        DependencyProperty.Register(
            nameof(MissionEditor),
            typeof(ContentMissionEditorViewModel),
            typeof(ContentDialogEditorPanel),
            new PropertyMetadata(null, OnMissionEditorChanged));

    public static readonly DependencyProperty MissionHostProperty =
        DependencyProperty.Register(
            nameof(MissionHost),
            typeof(ContentWorkspaceViewModel),
            typeof(ContentDialogEditorPanel),
            new PropertyMetadata(null));

    public ContentDialogEditorPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            RefreshMissionEditorPanelDataContext();
            ApplyActionPanelLayout(forceResetSplit: true);
            UpdateConversationViewportWidth();
        };
    }

    public ContentAiAssistantViewModel? AiAssistant
    {
        get => (ContentAiAssistantViewModel?)GetValue(AiAssistantProperty);
        set => SetValue(AiAssistantProperty, value);
    }

    public ContentMissionEditorViewModel? MissionEditor
    {
        get => (ContentMissionEditorViewModel?)GetValue(MissionEditorProperty);
        set => SetValue(MissionEditorProperty, value);
    }

    public ContentWorkspaceViewModel? MissionHost
    {
        get => (ContentWorkspaceViewModel?)GetValue(MissionHostProperty);
        set => SetValue(MissionHostProperty, value);
    }

    private static void OnMissionEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentDialogEditorPanel panel)
            return;

        panel._missionEditorDpDiagnostic =
            "MissionEditor DP Old=" + (e.OldValue?.GetType().FullName ?? "NULL") +
            " New=" + (e.NewValue?.GetType().FullName ?? "NULL");
        panel.RefreshMissionEditorPanelDataContext();
    }

    private void RefreshMissionEditorPanelDataContext()
    {
        foreach (var panel in FindVisualChildren<ContentMissionEditorPanel>(this))
        {
            if (!ReferenceEquals(panel.DataContext, MissionEditor))
                panel.DataContext = MissionEditor;

            if (MissionEditor is null)
                panel.ApplyMissionEditorDpDiagnostic(_missionEditorDpDiagnostic);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null)
            yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private ContentDialogEditorViewModel? Vm => DataContext as ContentDialogEditorViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= Vm_PropertyChanged;
        _subscribedVm = e.NewValue as ContentDialogEditorViewModel;
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged += Vm_PropertyChanged;
        ApplyActionPanelLayout(forceResetSplit: true);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContentDialogEditorViewModel.UseWideMissionLayout)
            or nameof(ContentDialogEditorViewModel.ShowFriendlyQuestEditor)
            or nameof(ContentDialogEditorViewModel.HasLinkedMission)
            or nameof(ContentDialogEditorViewModel.ShowRightPropertiesPanel)
            or nameof(ContentDialogEditorViewModel.PropertiesPanelTitle)
            or nameof(ContentDialogEditorViewModel.ShowMultipleResponseActionTabs)
            or nameof(ContentDialogEditorViewModel.HasAction)
            or nameof(ContentDialogEditorViewModel.SelectedNode))
        {
            ScheduleActionPanelLayout();
        }
    }

    private void ScheduleActionPanelLayout()
    {
        if (_actionPanelLayoutScheduled) return;
        _actionPanelLayoutScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _actionPanelLayoutScheduled = false;
            ApplyActionPanelLayout(forceResetSplit: false);
        });
    }

    private void InteractiveLayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            ApplyActionPanelLayout(forceResetSplit: false);
        UpdateConversationViewportWidth();
    }

    private void ConversationScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateConversationViewportWidth();
    }

    private void UpdateConversationViewportWidth()
    {
        if (Vm is null || ConversationScrollViewer is null)
            return;

        var width = ConversationScrollViewer.ViewportWidth;
        if (double.IsNaN(width) || width <= 0)
            width = ConversationScrollViewer.ActualWidth;
        if (width <= 0)
            return;

        Vm.CanvasViewportWidth = width;
    }

    private void ApplyActionPanelLayout(bool forceResetSplit)
    {
        if (ConversationColumn is null || ActionPanelColumn is null || ActionSplitterColumn is null)
            return;

        var wantWide = Vm?.UseWideMissionLayout == true;
        var total = InteractiveLayoutRoot.ActualWidth;
        if (total <= 0)
            total = ActualWidth;

        var canWide = wantWide && total >= WideLayoutMinTotalWidth;

        if (canWide)
        {
            ConversationColumn.MinWidth = ConversationMinWidth;
            ActionPanelColumn.MinWidth = MissionPanelMinWidth;
            ActionSplitterColumn.Width = new GridLength(6);
            ActionPanelSplitter.Visibility = Visibility.Visible;
            if (HeaderSplitterColumn is not null)
                HeaderSplitterColumn.Width = new GridLength(6);
            if (HeaderConversationColumn is not null)
                HeaderConversationColumn.Width = new GridLength(1, GridUnitType.Star);
            if (HeaderActionColumn is not null)
                HeaderActionColumn.Width = new GridLength(1, GridUnitType.Star);
            ActionPropsPanel.Margin = new Thickness(0);
            ActionPropsPanel.MaxWidth = double.PositiveInfinity;
            ActionPropsPanel.MinWidth = MissionPanelMinWidth;

            if (forceResetSplit || !_wideLayoutActive)
            {
                ConversationColumn.Width = new GridLength(1, GridUnitType.Star);
                ActionPanelColumn.Width = new GridLength(1, GridUnitType.Star);
            }

            _wideLayoutActive = true;
            return;
        }

        _wideLayoutActive = false;
        ConversationColumn.MinWidth = ConversationMinWidth;
        ConversationColumn.Width = new GridLength(1, GridUnitType.Star);
        ActionSplitterColumn.Width = new GridLength(0);
        ActionPanelSplitter.Visibility = Visibility.Collapsed;
        ActionPanelColumn.MinWidth = 0;
        ActionPanelColumn.Width = GridLength.Auto;
        if (HeaderSplitterColumn is not null)
            HeaderSplitterColumn.Width = new GridLength(0);
        if (HeaderConversationColumn is not null)
            HeaderConversationColumn.Width = new GridLength(1, GridUnitType.Star);
        if (HeaderActionColumn is not null)
            HeaderActionColumn.Width = GridLength.Auto;
        ActionPropsPanel.Margin = new Thickness(0);
        ActionPropsPanel.MaxWidth = CompactPanelWidth;
        ActionPropsPanel.MinWidth = wantWide ? 280 : 260;
    }

    private void Bubble_DragPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveChild(e.OriginalSource as DependencyObject, sender as DependencyObject)) return;
        if (sender is not FrameworkElement { DataContext: DialogTreeNodeVm node }) return;
        if (!node.IsQuestion && !node.IsResponse) return;

        _dragNode = node;
        _dragStartCanvas = e.GetPosition(ConversationCanvas);
        _dragStartLayout = new Point(node.LayoutX, node.LayoutY);
        _dragActive = false;
        _dragMoved = false;
        ConversationCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ConversationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || Vm is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(ConversationCanvas);
        var dx = pos.X - _dragStartCanvas.X;
        var dy = pos.Y - _dragStartCanvas.Y;
        if (!_dragActive)
        {
            if (Math.Abs(dx) < BubbleDragThreshold && Math.Abs(dy) < BubbleDragThreshold)
                return;
            _dragActive = true;
            _dragMoved = true;
        }

        Vm.SetBubbleLayout(_dragNode, _dragStartLayout.X + dx, _dragStartLayout.Y + dy, persist: false);
    }

    private void ConversationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode is null)
            return;

        if (_dragMoved && Vm is not null)
            Vm.SetBubbleLayout(_dragNode, _dragNode.LayoutX, _dragNode.LayoutY, persist: true);
        else if (!_dragMoved && Vm is not null)
            SelectBubbleNode(_dragNode);

        _dragNode = null;
        _dragActive = false;
        _dragMoved = false;
        if (ConversationCanvas.IsMouseCaptured)
            ConversationCanvas.ReleaseMouseCapture();
    }

    private void Bubble_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { DataContext: DialogTreeNodeVm node }) return;
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;

        var root = FindBubbleRoot(sender as FrameworkElement);
        var w = root?.ActualWidth ?? e.NewSize.Width;
        var h = root?.ActualHeight ?? e.NewSize.Height;
        Vm.NotifyBubbleMeasured(node, w, h);
    }

    private static FrameworkElement? FindBubbleRoot(FrameworkElement? start)
    {
        while (start is not null)
        {
            if (start.Name == "BubbleRoot")
                return start;
            start = VisualTreeHelper.GetParent(start) as FrameworkElement;
        }

        return null;
    }

    private void SelectBubbleNode(DialogTreeNodeVm node)
    {
        if (Vm is null) return;
        if (node.IsResponse && node.HasActions)
        {
            Vm.SelectedNode = Vm.SelectedNode is DialogTreeNodeVm { IsAction: true } current
                              && node.Children.Contains(current)
                ? current
                : node.Children[0];
        }
        else
        {
            Vm.SelectedNode = node;
        }
    }

    private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Selección la gestiona ConversationCanvas_MouseLeftButtonUp si no hubo arrastre.
        e.Handled = false;
    }

    private void ActionChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        if (IsInteractiveChild(e.OriginalSource as DependencyObject, sender as DependencyObject))
            return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm node })
        {
            Vm.SelectedNode = node;
            e.Handled = true;
        }
    }

    private static bool IsInteractiveChild(DependencyObject? origin, DependencyObject? stop)
    {
        while (origin is not null && origin != stop)
        {
            if (origin is Button or TextBox or ComboBox or Expander or ScrollViewer)
                return true;
            origin = VisualTreeHelper.GetParent(origin);
        }
        return false;
    }

    private static bool IsActionChipClick(DependencyObject? origin, DependencyObject? bubbleStop)
    {
        while (origin is not null && origin != bubbleStop)
        {
            if (origin is FrameworkElement { DataContext: DialogTreeNodeVm { IsAction: true } })
                return true;
            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    private void DeleteActionChip_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm { IsAction: true } node })
        {
            Vm.DeleteActionNode(node);
            e.Handled = true;
        }
    }

    private void DeleteQuestionBubble_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm { IsQuestion: true } node })
        {
            Vm.DeleteQuestionNode(node);
            e.Handled = true;
        }
    }

    private void DeleteResponseBubble_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm { IsResponse: true } node })
        {
            Vm.DeleteResponseNode(node);
            e.Handled = true;
        }
    }

    private void BubbleAi_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || AiAssistant is null)
            return;
        if (sender is not FrameworkElement { DataContext: DialogTreeNodeVm node })
            return;

        Vm.SelectedNode = node;
        AiAssistant.NotifyDialogSelectionChanged();

        // DIALOG.AI.VISUAL_EDITOR.INTEGRATION.1 — ✨ generates this bubble (panel lived only in simple mode).
        if (node.IsQuestion)
        {
            if (AiAssistant.GenerateNpcBubbleCommand.CanExecute(null))
                AiAssistant.GenerateNpcBubbleCommand.Execute(null);
        }
        else if (node.IsResponse)
        {
            if (AiAssistant.GeneratePlayerReplyCommand.CanExecute(null))
                AiAssistant.GeneratePlayerReplyCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void SimpleBubbleAi_Click(object sender, RoutedEventArgs e)
    {
        AiAssistant?.OpenBubbleAiPanel();
        e.Handled = true;
    }

    private void AddResponseOnBubble_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm { IsQuestion: true } node })
        {
            Vm.SelectedNode = node;
            if (Vm.AddResponseCommand.CanExecute(null))
                Vm.AddResponseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AddConditionOnBubble_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is FrameworkElement { DataContext: DialogTreeNodeVm { IsResponse: true } node })
        {
            Vm.BeginEditConditionForResponse(node);
            e.Handled = true;
        }
    }

    private void AddActionOnBubble_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button btn)
            return;

        if (btn.DataContext is DialogTreeNodeVm { IsResponse: true } node && !Vm.HasLinkedMission)
            Vm.SelectedNode = node;

        OpenActionMenu(btn);
        e.Handled = true;
    }

    private void AddActionToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;
        OpenActionMenu(btn);
        e.Handled = true;
    }

    private void OpenActionMenu(Button btn)
    {
        if (btn.ContextMenu is null)
            return;

        if (btn.DataContext is DialogTreeNodeVm node)
            btn.ContextMenu.DataContext = node;
        btn.ContextMenu.Tag = Vm;

        btn.ContextMenu.Placement = PlacementMode.Bottom;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.HorizontalOffset = 0;
        btn.ContextMenu.VerticalOffset = 4;
        btn.ContextMenu.IsOpen = true;
    }

    private void SwitchToSimpleDialog_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.IsSimpleMode = true;
    }

    private void SwitchToInteractiveDialog_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.IsInteractiveMode = true;
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e) =>
        Vm?.NotifyTextEdited();

    private void BubbleText_TextChanged(object sender, TextChangedEventArgs e) =>
        Vm?.NotifyTextEdited();
}
