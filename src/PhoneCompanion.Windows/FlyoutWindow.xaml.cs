using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PhoneCompanion.Windows;

public partial class FlyoutWindow : Window
{
    private bool _allowClose;
    private bool _opening;
    private DateTime _lastDismissed;
    public FlyoutWindow() { InitializeComponent(); }

    public void ToggleAtCursor()
    {
        // Clicking the tray first deactivates the flyout. Do not immediately reopen it.
        if (IsVisible || DateTime.UtcNow - _lastDismissed < TimeSpan.FromMilliseconds(250)) { Hide(); return; }
        ShowAtCursor();
    }
    public void ShowAtCursor()
    {
        _opening = true;
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        Show();
        var handle = new WindowInteropHelper(this).Handle;
        Position(handle, screen.WorkingArea);
        // Moving to another monitor may change WPF's DPI and measured size.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Position(handle, screen.WorkingArea);
            Activate(); Focus(); _opening = false;
        });
    }
    private void Position(IntPtr handle, System.Drawing.Rectangle area)
    {
        var dpi = GetDpiForWindow(handle) / 96.0;
        MaxHeight = Math.Max(180, (area.Height - 16) / dpi);
        UpdateLayout();
        var width = (int)Math.Ceiling(ActualWidth * dpi);
        var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = Math.Max(area.Left, area.Right - width - 4);
        var y = Math.Max(area.Top, area.Bottom - height - 4);
        SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010);
    }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_opening) return;
        _lastDismissed = DateTime.UtcNow; Hide();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } }
    private void OnConnectPhone(object sender, RoutedEventArgs e)
    {
        Hide(); (Application.Current as App)?.ShowPairing();
    }
    private void OnOpenDesktop(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowDesktop();
    protected override void OnClosing(CancelEventArgs e)
    { if (!_allowClose) { e.Cancel = true; Hide(); } base.OnClosing(e); }
    public void CloseForExit() { _allowClose = true; Close(); }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
