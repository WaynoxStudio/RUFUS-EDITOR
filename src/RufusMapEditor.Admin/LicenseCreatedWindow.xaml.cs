using System.Windows;

namespace RufusMapEditor.Admin;

public partial class LicenseCreatedWindow : Window
{
    public LicenseCreatedWindow(string licenseCode)
    {
        InitializeComponent();
        CodeBox.Text = licenseCode;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(CodeBox.Text);
    }
}
