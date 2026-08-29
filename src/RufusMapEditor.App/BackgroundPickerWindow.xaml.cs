using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App;

public partial class BackgroundPickerWindow : Window
{
    private readonly ObservableCollection<BackgroundItemVm> _filtered = new();

    public int? SelectedBackgroundId { get; private set; }

    public BackgroundPickerWindow(AstriaLibraryService library, int currentBackgroundId)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);

        var thumbs = new GfxThumbnailCache();
        var all = library.Catalog!.Enumerate(GfxCategory.Background)
            .Select(r => new BackgroundItemVm(r.Id, thumbs.GetThumbnail(r, 96)))
            .OrderBy(x => x.Id)
            .ToList();

        BackgroundList.ItemsSource = _filtered;
        foreach (var item in all)
            _filtered.Add(item);

        CountText.Text = $"{all.Count} fondos";
        SearchBox.Tag = all;

        if (currentBackgroundId > 0)
            BackgroundList.SelectedItem = all.FirstOrDefault(x => x.Id == currentBackgroundId);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox.Tag is not List<BackgroundItemVm> all) return;
        var q = SearchBox.Text.Trim();
        _filtered.Clear();
        foreach (var item in all)
        {
            if (string.IsNullOrEmpty(q) || item.Id.ToString().Contains(q, StringComparison.Ordinal))
                _filtered.Add(item);
        }

        CountText.Text = $"{_filtered.Count} / {all.Count} fondos";
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SelectedBackgroundId = 0;
        DialogResult = true;
    }

    private void BackgroundList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (BackgroundList.SelectedItem is BackgroundItemVm item)
        {
            SelectedBackgroundId = item.Id;
            DialogResult = true;
        }
    }

    public sealed class BackgroundItemVm
    {
        public BackgroundItemVm(int id, ImageSource? thumbnail)
        {
            Id = id;
            Thumbnail = thumbnail;
            Label = $"BG {id}";
        }

        public int Id { get; }
        public ImageSource? Thumbnail { get; }
        public string Label { get; }
    }
}
