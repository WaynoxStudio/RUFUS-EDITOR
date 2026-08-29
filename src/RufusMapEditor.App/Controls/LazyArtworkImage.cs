using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.Controls;

/// <summary>
/// LIB.4.2/4.3/4.3.1 — deferred artwork preview with manual import.
/// File/conversion on background; DependencyObject / ImageSource updates on UI Dispatcher only.
/// </summary>
public sealed class LazyArtworkImage : Border
{
    public static readonly DependencyProperty GfxIdProperty =
        DependencyProperty.Register(nameof(GfxId), typeof(int), typeof(LazyArtworkImage),
            new PropertyMetadata(0, OnGfxIdChanged));

    public static readonly DependencyProperty UseNpcSpritePreviewProperty =
        DependencyProperty.Register(nameof(UseNpcSpritePreview), typeof(bool), typeof(LazyArtworkImage),
            new PropertyMetadata(false, OnGfxIdChanged));

    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
    };

    private readonly TextBlock _placeholder = new()
    {
        Text = "Preview no disponible\nHaz clic para importar imagen",
        FontSize = 9,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(4),
        IsHitTestVisible = false,
    };

    private int _loadToken;
    private bool _subscribed;

    public LazyArtworkImage()
    {
        Width = 64;
        Height = 64;
        Background = TryFindResource("ElevatedSurface") as Brush ?? Brushes.Transparent;
        BorderBrush = TryFindResource("Border") as Brush ?? Brushes.Gray;
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        AllowDrop = true;
        ToolTip = "Clic: importar/cambiar · Arrastrar PNG/JPG · Clic derecho: menú";
        Child = _placeholder;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseLeftButtonUp += OnLeftClick;
        Drop += OnDrop;
        DragOver += OnDragOver;

        var menu = new ContextMenu();
        var change = new MenuItem { Header = "Cambiar imagen…" };
        change.Click += async (_, _) => await ImportAsync(replace: true);
        var remove = new MenuItem { Header = "Eliminar imagen" };
        remove.Click += (_, _) => DeleteManual();
        menu.Items.Add(change);
        menu.Items.Add(remove);
        menu.Opened += (_, _) =>
        {
            if (UseNpcSpritePreview)
            {
                change.IsEnabled = false;
                remove.IsEnabled = false;
                return;
            }

            var has = ArtworkPreviewService.Shared.HasManualVisual(GfxId);
            change.Header = has ? "Cambiar imagen…" : "Importar imagen…";
            change.IsEnabled = true;
            remove.IsEnabled = has;
        };
        ContextMenu = menu;
    }

    public int GfxId
    {
        get => (int)GetValue(GfxIdProperty);
        set => SetValue(GfxIdProperty, value);
    }

    /// <summary>NPC apariencias: sprite SWF compositor antes que artwork.</summary>
    public bool UseNpcSpritePreview
    {
        get => (bool)GetValue(UseNpcSpritePreviewProperty);
        set => SetValue(UseNpcSpritePreviewProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            ArtworkPreviewService.Shared.ManualVisualChanged += OnManualVisualChanged;
            _subscribed = true;
        }

        _ = EnsureLoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            ArtworkPreviewService.Shared.ManualVisualChanged -= OnManualVisualChanged;
            _subscribed = false;
        }
    }

    /// <summary>
    /// Event may fire from any thread. Must NOT touch DependencyProperties until on UI Dispatcher.
    /// Causante histórico LIB.4.3: lectura de <see cref="GfxId"/> desde hilo de Task.Run.
    /// </summary>
    private void OnManualVisualChanged(int gfxId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnManualVisualChanged(gfxId)), DispatcherPriority.DataBind);
            return;
        }

        if (gfxId != GfxId) return;
        _ = EnsureLoadAsync();
    }

    private static void OnGfxIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LazyArtworkImage img)
            _ = img.EnsureLoadAsync();
    }

    private async void OnLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_image.Source is not null && ArtworkPreviewService.Shared.HasManualVisual(GfxId))
            return;
        if (_image.Source is not null)
            return;

        e.Handled = true;
        await ImportAsync(replace: false);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (TryGetDroppedImagePath(e, out _))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedImagePath(e, out var path) || path is null)
            return;
        e.Handled = true;
        await ImportFromPathAsync(path);
    }

    private static bool TryGetDroppedImagePath(DragEventArgs e, out string? path)
    {
        path = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return false;
        var candidate = files[0];
        if (!VisualImageNormalizer.IsSupportedExtension(candidate))
            return false;
        path = candidate;
        return true;
    }

    private async Task ImportAsync(bool replace)
    {
        if (GfxId <= 0)
        {
            MessageBox.Show("GFX ID inválido.", "Importar imagen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = replace ? "Cambiar imagen de monstruo" : "Importar imagen de monstruo",
            Filter = "Imágenes|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true)
            return;
        await ImportFromPathAsync(dlg.FileName);
    }

    private async Task ImportFromPathAsync(string path)
    {
        var gfx = GfxId;
        if (gfx <= 0) return;

        try
        {
            // Background: file copy/normalize only — never raise UI events here.
            await Task.Run(() => ArtworkPreviewService.Shared.ImportManualMobVisualFile(gfx, path))
                .ConfigureAwait(true);

            // UI thread: notify other cards + reload this one.
            ArtworkPreviewService.Shared.NotifyManualVisualChanged(gfx);
            await EnsureLoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RufusLog.Error(
                $"Import preview GFX {gfx} falló · {ex.GetType().Name}: {ex.Message}\n{ex}");
            MessageBox.Show(
                "No se pudo importar la imagen.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n" +
                "Detalle registrado en el log de RUFUS.",
                "Importar imagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeleteManual()
    {
        var gfx = GfxId;
        if (gfx <= 0 || !ArtworkPreviewService.Shared.HasManualVisual(gfx))
            return;

        var confirm = MessageBox.Show(
            $"¿Eliminar la imagen manual de GFX {gfx}?\n\n" +
            $"Se borrará únicamente Library/Visuals/Mobs/{gfx}.png\n" +
            "y se volverá al fallback (caché SWF / raster / placeholder).",
            "Eliminar imagen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            // Delete is fast; keep notify on UI (caller is UI).
            ArtworkPreviewService.Shared.DeleteManualMobVisual(gfx);
            _ = EnsureLoadAsync();
        }
        catch (Exception ex)
        {
            RufusLog.Error($"Delete preview GFX {gfx} · {ex}");
            MessageBox.Show(
                $"{ex.GetType().Name}: {ex.Message}",
                "Eliminar imagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private Task EnsureLoadAsync()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.InvokeAsync((Func<Task>)EnsureLoadAsyncCore).Task.Unwrap();
        return EnsureLoadAsyncCore();
    }

    private async Task EnsureLoadAsyncCore()
    {
        var gfx = GfxId;
        if (gfx <= 0 || !IsLoaded)
        {
            ShowPlaceholder();
            return;
        }

        if (UseNpcSpritePreview)
        {
            Cursor = Cursors.Arrow;
            AllowDrop = false;
            ToolTip = "Preview apariencia NPC";
        }

        var token = ++_loadToken;
        ShowBusy();

        byte[]? png = null;
        try
        {
            if (UseNpcSpritePreview)
            {
                png = await NpcGfxPreviewService.Shared.GetOrCreatePngAsync(gfx).ConfigureAwait(true);
            }
            else
            {
                png = await ArtworkPreviewService.Shared.GetOrCreatePngAsync(gfx).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            RufusLog.Error($"Preview load GFX {gfx} · {ex}");
            png = null;
        }

        if (!Dispatcher.CheckAccess())
        {
            var captured = png;
            await Dispatcher.InvokeAsync(() => ApplyLoadedPng(token, captured)).Task.ConfigureAwait(true);
            return;
        }

        ApplyLoadedPng(token, png);
    }

    private void ApplyLoadedPng(int token, byte[]? png)
    {
        if (token != _loadToken) return;
        if (png is null || png.Length == 0)
        {
            ShowPlaceholder();
            return;
        }

        try
        {
            var bmp = CreateFrozenBitmap(png);
            _image.Source = bmp;
            Child = _image;
        }
        catch (Exception ex)
        {
            RufusLog.Error($"BitmapImage GFX apply · {ex}");
            ShowPlaceholder();
        }
    }

    /// <summary>
    /// Build BitmapImage fully (OnLoad) and Freeze so it is thread-safe for UI assignment.
    /// Stream is closed before return.
    /// </summary>
    private static BitmapImage CreateFrozenBitmap(byte[] png)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(png, writable: false))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.None;
            bmp.StreamSource = ms;
            bmp.EndInit();
        }

        if (bmp.CanFreeze)
            bmp.Freeze();
        return bmp;
    }

    private void ShowBusy()
    {
        _placeholder.Text = "…";
        Child = _placeholder;
        _image.Source = null;
    }

    private void ShowPlaceholder()
    {
        _placeholder.Text = UseNpcSpritePreview
            ? "Preview no disponible"
            : "Preview no disponible\nHaz clic para importar imagen";
        Child = _placeholder;
        _image.Source = null;
    }
}
