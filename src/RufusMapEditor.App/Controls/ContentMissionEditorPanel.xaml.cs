using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class ContentMissionEditorPanel : UserControl
{
    private ContentMissionEditorViewModel? _debugSubscribedVm;

    public ContentMissionEditorPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDebugDataContextChanged;
        Loaded += OnDebugLoaded;
    }

    private ContentMissionEditorViewModel? Vm => DataContext as ContentMissionEditorViewModel;

    private void OnDebugLoaded(object sender, RoutedEventArgs e) => RefreshDebugPanelChrome();

    private void OnDebugDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_debugSubscribedVm is not null)
            _debugSubscribedVm.PropertyChanged -= OnDebugVmPropertyChanged;

        _debugSubscribedVm = e.NewValue as ContentMissionEditorViewModel;
        if (_debugSubscribedVm is not null)
            _debugSubscribedVm.PropertyChanged += OnDebugVmPropertyChanged;

        RefreshDebugPanelChrome();
    }

    private void OnDebugVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContentMissionEditorViewModel.DebugMissionStateText))
            ApplyDebugStateTextToTextBox();
    }

    /// <summary>
    /// WPF TextBox ignores OneWay source-to-target updates; push text explicitly.
    /// </summary>
    private void RefreshDebugPanelChrome()
    {
        if (DebugPanelDataContextTypeText is not null)
            DebugPanelDataContextTypeText.Text = "DataContext: " + (DataContext?.GetType().FullName ?? "NULL");

        ApplyDebugStateTextToTextBox();
    }

    private void ApplyDebugStateTextToTextBox()
    {
        if (DebugMissionStateTextBox is null)
            return;

        if (Vm is null)
        {
            var dpDiag = FindAncestorContentDialogEditorPanel()?.MissionEditorDpDiagnostic;
            DebugMissionStateTextBox.Text = "=== DEBUG ACTIVO ===" + Environment.NewLine +
                "DataContext: NULL" +
                (string.IsNullOrWhiteSpace(dpDiag) ? "" : Environment.NewLine + dpDiag);
            return;
        }

        var text = Vm.DebugMissionStateText;
        DebugMissionStateTextBox.Text = string.IsNullOrWhiteSpace(text)
            ? "=== DEBUG ACTIVO ===" + Environment.NewLine + "(getter devolvió vacío)"
            : text;
    }

    internal void ApplyMissionEditorDpDiagnostic(string? diagnostic)
    {
        if (DebugMissionStateTextBox is null)
            return;
        DebugMissionStateTextBox.Text = "=== DEBUG ACTIVO ===" + Environment.NewLine +
            "DataContext: NULL" +
            (string.IsNullOrWhiteSpace(diagnostic) ? "" : Environment.NewLine + diagnostic);
        if (DebugPanelDataContextTypeText is not null)
            DebugPanelDataContextTypeText.Text = "DataContext: NULL";
    }

    private ContentDialogEditorPanel? FindAncestorContentDialogEditorPanel()
    {
        for (var parent = VisualTreeHelper.GetParent(this);
             parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ContentDialogEditorPanel panel)
                return panel;
        }
        return null;
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => Vm?.NotifyEdited();

    private void Rewards_Changed(object sender, TextChangedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ApplyRewardsFromUi();
        Vm.NotifyEdited();
    }

    private void RewardItemId_Changed(object sender, TextChangedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ApplyRewardsFromUi();
        Vm.NotifyEdited();
    }

    private void RewardQty_Changed(object sender, TextChangedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ApplyRewardsFromUi();
        Vm.NotifyEdited();
    }

    private void RewardRemove_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is Button { Tag: MissionRewardItemRowVm row })
            Vm.RemoveRewardRow(row);
    }

    private void ObjectiveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is Button { Tag: MissionObjectiveRowVm row })
            Vm.EditObjective(row.Objective);
    }

    private void ObjectiveDelete_Click(object sender, RoutedEventArgs e)
    {
        DeleteObjectiveFromSender(sender);
    }

    /// <summary>
    /// PreviewMouseDown runs before TextBox LostFocus on inline Map ID.
    /// Without this, LostFocus rebuild/persist can swallow the Click and leave DiscoverMap in the draft.
    /// </summary>
    private void ObjectiveDelete_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DeleteObjectiveFromSender(sender))
            e.Handled = true;
    }

    private bool DeleteObjectiveFromSender(object sender)
    {
        if (Vm is null) return false;
        if (sender is not FrameworkElement { Tag: MissionObjectiveRowVm row })
            return false;
        Vm.TryDeleteObjective(row.Objective);
        return true;
    }

    private void ObjectiveDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is Button { Tag: MissionObjectiveRowVm row })
            Vm.DuplicateObjective(row.Objective);
    }

    private void ObjectiveTypePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || !Vm.ShowObjectiveTypePicker) return;
        if (sender is ComboBox { SelectedItem: ObjectiveTypeChoice choice })
            Vm.CompleteAddObjectiveFromPicker(choice);
    }
}
