using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RufusMapEditor.App.Licensing;

namespace RufusMapEditor.App;

public partial class StartupHubWindow : Window
{
    private static readonly SolidColorBrush HubBackgroundBrush = CreateFrozenBrush(0x0A, 0x0A, 0x0B);
    private static readonly SolidColorBrush HubForegroundBrush = CreateFrozenBrush(0xF3, 0xF3, 0xF3);

    private const double LensSize = 240;

    private Storyboard? _ambientStoryboard;
    private bool _warpActive;
    private bool _pointerInside;

    private Point _pointerTarget;
    private Point _lensPos;
    private Vector _velocity;

    public StartupHubWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyHubChrome();
        StartAmbientMotion();
        StartBackgroundWarp();
        BindLicenseStatus();
        LicenseRuntimeGate.Blocked += OnLicenseRuntimeBlocked;

        var center = new Point(ActualWidth * 0.5, ActualHeight * 0.4);
        _pointerTarget = center;
        _lensPos = center;
        UpdateLens();
    }

    private void BindLicenseStatus()
    {
        if (App.License is null || LicenseStatusLabel is null)
            return;

        LicenseStatusLabel.Visibility = Visibility.Visible;
        LicenseStatusLabel.Text = App.License.StatusLabel;
        App.License.StatusChanged += OnLicenseStatusChanged;
    }

    private void OnLicenseStatusChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LicenseStatusLabel is not null && App.License is not null)
                LicenseStatusLabel.Text = App.License.StatusLabel;
        });
    }

    private void OnLicenseRuntimeBlocked()
    {
        Dispatcher.BeginInvoke(ShowLicenseBlocked);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        LicenseRuntimeGate.Blocked -= OnLicenseRuntimeBlocked;
        if (App.License is not null)
            App.License.StatusChanged -= OnLicenseStatusChanged;
        StopBackgroundWarp();
        StopAmbientMotion();
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }

    private void ApplyHubChrome()
    {
        Background = HubBackgroundBrush;
        Foreground = HubForegroundBrush;
    }

    private void StartAmbientMotion()
    {
        if (TryFindResource("HubAmbientStoryboard") is not Storyboard storyboard)
            return;

        _ambientStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    private void StopAmbientMotion()
    {
        _ambientStoryboard?.Stop(this);
        _ambientStoryboard = null;
    }

    private void StartBackgroundWarp()
    {
        if (_warpActive)
            return;

        CompositionTarget.Rendering += OnWarpFrame;
        _warpActive = true;
    }

    private void StopBackgroundWarp()
    {
        if (!_warpActive)
            return;

        CompositionTarget.Rendering -= OnWarpFrame;
        _warpActive = false;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        _pointerInside = true;
        _pointerTarget = e.GetPosition(BackdropHost);
        FadeLens(visible: true);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _pointerInside = false;
        FadeLens(visible: false);
        ResetWarp();
    }

    private void FadeLens(bool visible)
    {
        var anim = new DoubleAnimation(visible ? 0.85 : 0.0, TimeSpan.FromSeconds(0.28))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        DistortionLens.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void OnWarpFrame(object? sender, EventArgs e)
    {
        if (BackdropHost.ActualWidth <= 0 || BackdropHost.ActualHeight <= 0)
            return;

        var previous = _lensPos;
        _lensPos = Lerp(_lensPos, _pointerTarget, 0.16);
        _velocity = _lensPos - previous;

        UpdateLens();
        ApplyBackdropWarp();
    }

    private void UpdateLens()
    {
        var half = LensSize * 0.5;
        LensTransform.X = _lensPos.X - half;
        LensTransform.Y = _lensPos.Y - half;

        // Magnified slice of the backdrop under the cursor (= liquid refraction)
        LensBrush.Viewbox = new Rect(_lensPos.X - half, _lensPos.Y - half, LensSize, LensSize);

        var speed = Math.Min(_velocity.Length, 24);
        var stretch = 1.12 + speed * 0.012;
        var squash = 1.12 / Math.Sqrt(stretch / 1.12);
        LensMagnify.ScaleX = stretch;
        LensMagnify.ScaleY = squash;
        LensMagnify.CenterX = half;
        LensMagnify.CenterY = half;
    }

    private void ApplyBackdropWarp()
    {
        if (!_pointerInside)
            return;

        var w = BackdropHost.ActualWidth;
        var h = BackdropHost.ActualHeight;
        if (w < 1 || h < 1)
            return;

        var nx = (_lensPos.X / w) - 0.5;
        var ny = (_lensPos.Y / h) - 0.5;
        var speed = Math.Min(_velocity.Length, 20);

        // Warp origin follows the pointer (relative 0..1)
        AmbientSource.RenderTransformOrigin = new Point(
            Math.Clamp(_lensPos.X / w, 0, 1),
            Math.Clamp(_lensPos.Y / h, 0, 1));

        WarpScale.CenterX = _lensPos.X;
        WarpScale.CenterY = _lensPos.Y;
        WarpScale.ScaleX = 1.0 + 0.035 + speed * 0.0015;
        WarpScale.ScaleY = 1.0 - 0.018 - speed * 0.0008;

        WarpSkew.AngleX = nx * 3.2 + _velocity.X * 0.10;
        WarpSkew.AngleY = ny * 2.6 + _velocity.Y * 0.08;

        GridWarp.X = -nx * 22 - _velocity.X * 0.4;
        GridWarp.Y = -ny * 16 - _velocity.Y * 0.4;

        PrimaryGlowBrush.Center = new Point(
            0.45 + nx * 0.14,
            0.18 + ny * 0.12);
    }

    private void ResetWarp()
    {
        WarpScale.ScaleX = 1;
        WarpScale.ScaleY = 1;
        WarpSkew.AngleX = 0;
        WarpSkew.AngleY = 0;
        GridWarp.X = 0;
        GridWarp.Y = 0;
        AmbientSource.RenderTransformOrigin = new Point(0.5, 0.5);
    }

    private static Point Lerp(Point from, Point to, double t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

    private void ModuleCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button card)
            return;

        card.ApplyTemplate();
        var pos = e.GetPosition(card);
        if (card.Template?.FindName("SheenTransform", card) is TranslateTransform sheen)
        {
            sheen.X = pos.X;
            sheen.Y = pos.Y;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void MapsCard_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseRuntimeGate.CanUseEditor)
        {
            ShowLicenseBlocked();
            return;
        }

        var editor = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        ShowModule(editor);
    }

    private void ContentCard_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseRuntimeGate.CanUseEditor)
        {
            ShowLicenseBlocked();
            return;
        }

        ShowModule(new ContentWorkspaceWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        });
    }

    private void ShowLicenseBlocked()
    {
        var msg = string.IsNullOrWhiteSpace(LicenseRuntimeGate.BlockMessage)
            ? "La sesión de licencia ya no es válida."
            : LicenseRuntimeGate.BlockMessage;
        var dlg = new LicenseBlockedWindow(msg, App.License) { Owner = this };
        if (dlg.ShowDialog() == true)
            App.License?.StartHeartbeat();
    }

    /// <summary>
    /// Opens a module non-modally so the hub stays usable (ShowDialog would freeze it).
    /// </summary>
    private void ShowModule(Window editor)
    {
        editor.Closed += (_, _) =>
        {
            if (Application.Current?.MainWindow == editor)
                Application.Current.MainWindow = this;
        };

        Application.Current.MainWindow = editor;
        editor.Show();
        editor.Activate();
    }
}
