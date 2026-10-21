using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.Rendering;

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
            .Select(r =>
            {
                var disk = GfxImageDimensions.TryRead(r.FilePath);
                var w = disk?.Width ?? r.PixelWidth;
                var h = disk?.Height ?? r.PixelHeight;
                return new BackgroundItemVm(r.Id, w, h, thumbs.GetThumbnail(r, 96));
            })
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
        public BackgroundItemVm(int id, int? pixelWidth, int? pixelHeight, ImageSource? thumbnail)
        {
            Id = id;
            Thumbnail = thumbnail;
            Label = $"BG {id}";
            SizeLabel = FormatSize(pixelWidth, pixelHeight);
        }

        public int Id { get; }
        public ImageSource? Thumbnail { get; }
        public string Label { get; }
        public string SizeLabel { get; }

        private static string FormatSize(int? pixelWidth, int? pixelHeight)
        {
            if (pixelWidth is not int w || pixelHeight is not int h || w <= 0 || h <= 0)
                return "";
            var fit = GuessMapSize(w, h);
            return string.IsNullOrEmpty(fit) ? $"{w}×{h}" : $"{w}×{h} · {fit}";
        }

        private static string GuessMapSize(int w, int h)
        {
            foreach (var (mw, mh) in new[] { (15, 17), (19, 22) })
            {
                var (cw, ch) = IsoGeometry.ExportImageSize(mw, mh);
                var (fw, fh) = IsoGeometry.FullCanvasSize(mw, mh);
                if (Near(w, h, cw, ch) || Near(w, h, fw, fh))
                    return $"{mw}×{mh}";
            }

            return w >= 900 && h >= 520 ? "grande" : "pequeño";
        }

        private static bool Near(int w, int h, int tw, int th) =>
            Math.Abs(w - tw) <= 40 && Math.Abs(h - th) <= 40;
    }
}
