using System.Windows;
using System.Windows.Controls;
using RufusMapEditor.App.ViewModels;

namespace RufusMapEditor.App.Controls;

public partial class ContentMissionEditorPanel : UserControl
{
    public ContentMissionEditorPanel()
    {
        InitializeComponent();
    }

    private ContentMissionEditorViewModel? Vm => DataContext as ContentMissionEditorViewModel;

    private void Field_Changed(object sender, RoutedEventArgs e) => Vm?.NotifyEdited();

    private void Rewards_Changed(object sender, TextChangedEventArgs e)
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
        {
            Vm.SelectedRewardRow = row;
            if (Vm.RemoveRewardItemCommand.CanExecute(null))
                Vm.RemoveRewardItemCommand.Execute(null);
        }
    }
}
