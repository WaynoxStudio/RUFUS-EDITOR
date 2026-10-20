using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Opening a WPF Popup creates an HWND that fires MouseLeave on the source tile.
/// Closing then re-entering loops GetThumbnail on the UI thread and freezes the editor.
/// </summary>
internal sealed class CatalogHoverPopup
{
    private readonly Popup _popup;
    private readonly DispatcherTimer _closeTimer;
    private object? _item;

    public CatalogHoverPopup(Popup popup)
    {
        _popup = popup;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            _popup.IsOpen = false;
            _item = null;
        };
    }

    /// <summary>False when this item is already shown — skip thumbnail work.</summary>
    public bool ShouldBind(object item)
    {
        _closeTimer.Stop();
        if (_popup.IsOpen && ReferenceEquals(_item, item))
            return false;
        _item = item;
        return true;
    }

    public void Show() => _popup.IsOpen = true;

    public void ScheduleClose()
    {
        _closeTimer.Stop();
        _closeTimer.Start();
    }
}
