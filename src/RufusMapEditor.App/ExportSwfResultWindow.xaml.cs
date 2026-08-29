using System.Windows;

namespace RufusMapEditor.App;

public partial class ExportSwfResultWindow : Window
{
    public ExportSwfResultWindow(string summary)
    {
        InitializeComponent();
        SummaryText.Text = summary;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
