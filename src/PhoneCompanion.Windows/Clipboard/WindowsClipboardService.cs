using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PhoneCompanion.Windows.Clipboard;

public interface IWindowsClipboard : IDisposable
{
    event Action<string>? TextChanged;
    void StartMonitoring();
    void StopMonitoring();
    bool TrySetText(string text);
}

public sealed class WindowsClipboardService : IWindowsClipboard
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr MessageOnlyWindow = new(-3);
    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _source;
    private bool _monitoring;
    private bool _disposed;

    public WindowsClipboardService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        var parameters = new HwndSourceParameters("PhoneCompanion.ClipboardListener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = MessageOnlyWindow
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<string>? TextChanged;

    public void StartMonitoring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_monitoring) return;
        if (!AddClipboardFormatListener(_source.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _monitoring = true;
    }

    public void StopMonitoring()
    {
        if (_disposed || !_monitoring) return;
        RemoveClipboardFormatListener(_source.Handle);
        _monitoring = false;
    }

    public bool TrySetText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        if (!_dispatcher.CheckAccess()) return _dispatcher.Invoke(() => TrySetText(text));
        try
        {
            System.Windows.Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch (Exception e) when (e is COMException or System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmClipboardUpdate || !_monitoring || _dispatcher.HasShutdownStarted) return IntPtr.Zero;
        _dispatcher.BeginInvoke(ReadClipboardText, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    private void ReadClipboardText()
    {
        if (!_monitoring || _disposed) return;
        try
        {
            if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
            {
                var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
                if (text.Length > 0) TextChanged?.Invoke(text);
            }
        }
        catch (Exception e) when (e is COMException or System.Runtime.InteropServices.ExternalException)
        {
            // Another process may briefly own the clipboard. A later clipboard event will retry naturally.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopMonitoring();
        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
