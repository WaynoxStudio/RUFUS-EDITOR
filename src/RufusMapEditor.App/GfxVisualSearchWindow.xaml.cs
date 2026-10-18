using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using RufusMapEditor.App.Services;
using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App;

public partial class GfxVisualSearchWindow : Window
{
    private readonly AstriaLibraryService _library;
    private readonly GfxCategory _preferredCategory;
    private readonly GfxThumbnailCache _thumbs = new();
    private readonly ObservableCollection<ResultVm> _results = new();

    private BitmapSource? _source;
    private bool _dragging;
    private Point _dragStart;
    private Rectangle? _cropRect;
    private Int32Rect? _crop;
    private CancellationTokenSource? _searchCts;

    public GfxResource? SelectedResource { get; private set; }

    public GfxVisualSearchWindow(AstriaLibraryService library, GfxCategory preferredCategory)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        _library = library;
        _preferredCategory = preferredCategory;
        ResultsList.ItemsSource = _results;
        StatusText.Text = "Pega una captura del juego o ábrela desde disco.";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Paste_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        var bmp = GfxVisualSearch.FromClipboard();
        if (bmp is null)
        {
            StatusText.Text = "No hay imagen en el portapapeles.";
            return;
        }

        SetSource(bmp);
        StatusText.Text = "Captura pegada · arrastra para recortar el ítem.";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Todos|*.*",
            Title = "Abrir captura del juego",
        };
        if (dlg.ShowDialog(this) != true) return;

        var bmp = GfxVisualSearch.LoadBitmap(dlg.FileName, decodeMax: null);
        if (bmp is null)
        {
            StatusText.Text = "No se pudo abrir la imagen.";
            return;
        }

        SetSource(bmp);
        StatusText.Text = "Imagen cargada · arrastra para recortar el ítem.";
    }

    private void ClearCrop_Click(object sender, RoutedEventArgs e)
    {
        _dragging = false;
        if (ImageHost.IsMouseCaptured)
            ImageHost.ReleaseMouseCapture();

        _crop = null;
        _cropRect = null;
        OverlayCanvas.Children.Clear();

        // Also clear the pasted/opened capture so a new one can be pasted.
        _source = null;
        SourceImage.Source = null;
        ImageHost.Width = double.NaN;
        ImageHost.Height = double.NaN;
        OverlayCanvas.Width = double.NaN;
        OverlayCanvas.Height = double.NaN;

        _results.Clear();
        UseButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Collapsed;
        StatusText.Text = "Captura limpiada · pega (Ctrl+V) o abre otra imagen.";
    }

    private void SetSource(BitmapSource bmp)
    {
        _source = bmp;
        SourceImage.Source = bmp;
        ImageHost.Width = bmp.PixelWidth;
        ImageHost.Height = bmp.PixelHeight;
        OverlayCanvas.Width = bmp.PixelWidth;
        OverlayCanvas.Height = bmp.PixelHeight;
        OverlayCanvas.Children.Clear();
        _cropRect = null;
        _crop = null;
        SearchButton.IsEnabled = true;
        _results.Clear();
        UseButton.IsEnabled = false;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_source is null) return;
        _dragging = true;
        _dragStart = e.GetPosition(ImageHost);
        ImageHost.CaptureMouse();
        OverlayCanvas.Children.Clear();
        _cropRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 160, 40)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 160, 40)),
            IsHitTestVisible = false,
        };
        OverlayCanvas.Children.Add(_cropRect);
        Canvas.SetLeft(_cropRect, _dragStart.X);
        Canvas.SetTop(_cropRect, _dragStart.Y);
        _cropRect.Width = 0;
        _cropRect.Height = 0;
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _cropRect is null) return;
        var pos = e.GetPosition(ImageHost);
        var x = Math.Min(_dragStart.X, pos.X);
        var y = Math.Min(_dragStart.Y, pos.Y);
        var w = Math.Abs(pos.X - _dragStart.X);
        var h = Math.Abs(pos.Y - _dragStart.Y);
        Canvas.SetLeft(_cropRect, x);
        Canvas.SetTop(_cropRect, y);
        _cropRect.Width = w;
        _cropRect.Height = h;
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ImageHost.ReleaseMouseCapture();
        CommitCropFromOverlay();
    }

    private void ImageHost_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ImageHost.ReleaseMouseCapture();
        CommitCropFromOverlay();
    }

    private void CommitCropFromOverlay()
    {
        if (_source is null || _cropRect is null) return;
        var x = (int)Math.Floor(Canvas.GetLeft(_cropRect));
        var y = (int)Math.Floor(Canvas.GetTop(_cropRect));
        var w = (int)Math.Ceiling(_cropRect.Width);
        var h = (int)Math.Ceiling(_cropRect.Height);
        if (w < 8 || h < 8)
        {
            _crop = null;
            OverlayCanvas.Children.Clear();
            _cropRect = null;
            StatusText.Text = "Recorte demasiado pequeño · arrastra un área mayor.";
            return;
        }

        _crop = new Int32Rect(x, y, w, h);
        StatusText.Text = $"Recorte {w}×{h} · pulsa Buscar.";
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _library.Catalog is null)
            return;

        BitmapSource query = _source;
        if (_crop is Int32Rect crop)
        {
            try
            {
                query = GfxVisualSearch.Crop(_source, crop);
            }
            catch
            {
                StatusText.Text = "No se pudo aplicar el recorte.";
                return;
            }
        }

        var scope = ScopeBox.SelectedIndex;
        IEnumerable<GfxResource> catalog = scope switch
        {
            1 => _library.Catalog.Enumerate(GfxCategory.Ground),
            2 => _library.Catalog.Enumerate(GfxCategory.Object),
            3 => _library.Catalog.Enumerate(GfxCategory.Background),
            4 => _library.Catalog.Enumerate(),
            _ => _library.Catalog.Enumerate(_preferredCategory),
        };

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        SearchButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        StatusText.Text = "Buscando en catálogo local…";
        _results.Clear();
        UseButton.IsEnabled = false;

        try
        {
            var progress = new Progress<int>(p => ProgressBar.Value = p);
            var matches = await Task.Run(
                () => GfxVisualSearch.Search(query, catalog, _thumbs, topN: 30, progress, ct),
                ct);

            foreach (var m in matches)
            {
                _results.Add(new ResultVm(
                    m.Resource,
                    m.Thumbnail,
                    $"{CategoryShort(m.Resource.Category)} {m.Resource.Id}",
                    $"{m.Score * 100:0}%"));
            }

            StatusText.Text = matches.Count == 0
                ? "Sin coincidencias claras · prueba otro recorte o cambia el alcance."
                : $"{matches.Count} candidato(s) · doble clic o Usar seleccionado.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Búsqueda cancelada.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ProgressBar.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = _source is not null;
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = ResultsList.SelectedItem is ResultVm;

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultVm)
            Use_Click(sender, e);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ResultVm vm) return;
        SelectedResource = vm.Resource;
        DialogResult = true;
        Close();
    }

    private static string CategoryShort(GfxCategory c) => c switch
    {
        GfxCategory.Ground => "Suelo",
        GfxCategory.Object => "Obj",
        GfxCategory.Background => "Fondo",
        _ => c.ToString(),
    };

    private sealed class ResultVm
    {
        public ResultVm(GfxResource resource, ImageSource? thumbnail, string label, string scoreText)
        {
            Resource = resource;
            Thumbnail = thumbnail;
            Label = label;
            ScoreText = scoreText;
        }

        public GfxResource Resource { get; }
        public ImageSource? Thumbnail { get; }
        public string Label { get; }
        public string ScoreText { get; }
    }
}
