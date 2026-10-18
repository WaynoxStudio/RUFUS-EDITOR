using System.Windows;

namespace RufusMapEditor.App;

public partial class MapIdInputWindow : Window
{
    public int? ResultMapId { get; private set; }

    /// <summary>True when the user chose «Crear mapa nuevo» instead of loading an existing ID.</summary>
    public bool CreateNewRequested { get; private set; }

    public MapIdInputWindow(
        string prompt,
        int proposedId,
        string? hint = null,
        string? title = null,
        bool allowCreateNew = false,
        string? createNewLabel = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
            Title = title;
        PromptText.Text = prompt;
        if (hint is not null)
            HintText.Text = hint;
        IdBox.Text = proposedId > 0
            ? proposedId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "";
        IdBox.SelectAll();

        if (allowCreateNew)
        {
            CreateNewButton.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(createNewLabel))
                CreateNewButton.Content = createNewLabel;
        }

        Loaded += (_, _) => IdBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdBox.Text.Trim(), out var id) || id <= 0)
        {
            MessageBox.Show("Introduce un Map ID numérico válido.", Title);
            return;
        }

        CreateNewRequested = false;
        ResultMapId = id;
        DialogResult = true;
    }

    private void CreateNew_Click(object sender, RoutedEventArgs e)
    {
        CreateNewRequested = true;
        ResultMapId = null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
