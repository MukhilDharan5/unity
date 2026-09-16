using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PhoneCompanion.Windows;

public sealed class TrayIconController : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _demo;
    public event Action? ToggleRequested;
    public event Action? ShowRequested;
    public event Action? ConnectRequested;
    public event Action<bool>? DemoRequested;
    public event Action? ExitRequested;
    public bool Visible => _tray.Visible;
    public TrayIconController()
    {
        _icon = CreateIcon();
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Show phone", null, (_, _) => ShowRequested?.Invoke());
        _menu.Items.Add("Connect phone", null, (_, _) => ConnectRequested?.Invoke());
        _demo = new Forms.ToolStripMenuItem("Use sample data") { CheckOnClick = false };
        _demo.Click += (_, _) => DemoRequested?.Invoke(!_demo.Checked);
        _menu.Items.Add(_demo);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        _tray = new Forms.NotifyIcon { Text = "Phone Companion · Not connected", Icon = _icon, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ToggleRequested?.Invoke(); };
    }
    public void SetDemo(bool enabled) => _demo.Checked = enabled;
    public void SetBusy(bool busy) => _demo.Enabled = !busy;
    public void SetStatus(string status) => _tray.Text = $"Phone Companion · {status}";
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(117, 198, 255));
        graphics.FillEllipse(background, 0, 0, 32, 32);
        using var phone = new Pen(Color.FromArgb(10, 10, 11), 2.2f) { LineJoin = LineJoin.Round };
        graphics.DrawRectangle(phone, 10, 5, 12, 22);
        graphics.DrawLine(phone, 14, 23, 18, 23);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public void Dispose() { _tray.Visible = false; _tray.Dispose(); _menu.Dispose(); _icon.Dispose(); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
