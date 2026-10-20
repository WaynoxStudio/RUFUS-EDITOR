using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RufusMapEditor.LegacyCompatibility.Logging;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.App.Controls;

/// <summary>CONTENT.ITEMS.PREVIEW.COLLISION.FIX.1B — deferred item icon by SWF preview key.</summary>
public sealed class LazyItemIconImage : Border
{
    public static readonly DependencyProperty GfxIdProperty =
        DependencyProperty.Register(nameof(GfxId), typeof(int), typeof(LazyItemIconImage),
            new PropertyMetadata(0, OnPreviewContextChanged));

    public static readonly DependencyProperty TypeIdProperty =
        DependencyProperty.Register(nameof(TypeId), typeof(int), typeof(LazyItemIconImage),
            new PropertyMetadata(0, OnPreviewContextChanged));

    public static readonly DependencyProperty ReloadTokenProperty =
        DependencyProperty.Register(nameof(ReloadToken), typeof(int), typeof(LazyItemIconImage),
            new PropertyMetadata(0, OnPreviewContextChanged));

    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _placeholder = new()
    {
        Text = "?",
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    private int _loadToken;

    public LazyItemIconImage()
    {
        Width = 64;
        Height = 64;
        Background = TryFindResource("ElevatedSurface") as Brush ?? Brushes.Transparent;
        BorderBrush = TryFindResource("Border") as Brush ?? Brushes.Gray;
        BorderThickness = new Thickness(1);
        Child = _placeholder;
        Loaded += (_, _) => _ = EnsureLoadAsync();
    }

    public int GfxId
    {
        get => (int)GetValue(GfxIdProperty);
        set => SetValue(GfxIdProperty, value);
    }

    public int TypeId
    {
        get => (int)GetValue(TypeIdProperty);
        set => SetValue(TypeIdProperty, value);
    }

    public int ReloadToken
    {
        get => (int)GetValue(ReloadTokenProperty);
        set => SetValue(ReloadTokenProperty, value);
    }

    private static void OnPreviewContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LazyItemIconImage img)
            _ = img.EnsureLoadAsync();
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
        var typeId = TypeId;
        if (gfx <= 0 || !IsLoaded)
        {
            ShowPlaceholder();
            return;
        }

        if (!ItemIconPreviewService.Shared.HasPreview(gfx, typeId))
        {
            ShowPlaceholder();
            return;
        }

        var token = ++_loadToken;
        _placeholder.Text = "…";
        Child = _placeholder;

        byte[]? png = null;
        try
        {
            png = await ItemIconPreviewService.Shared.TryReadPngAsync(gfx, typeId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RufusLog.Error($"Item icon load GFX {gfx} type {typeId} · {ex}");
        }

        if (token != _loadToken)
            return;

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
            RufusLog.Error($"Item icon bitmap GFX {gfx} type {typeId} · {ex}");
            ShowPlaceholder();
        }
    }

    private static BitmapImage CreateFrozenBitmap(byte[] png)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(png, writable: false))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
        }

        if (bmp.CanFreeze)
            bmp.Freeze();
        return bmp;
    }

    private void ShowPlaceholder()
    {
        _placeholder.Text = "?";
        Child = _placeholder;
        _image.Source = null;
    }
}
