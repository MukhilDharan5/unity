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
    public event Action? OpenRequested;
    public event Action<bool>? DemoRequested;
    public event Action? ExitRequested;
    public bool Visible => _tray.Visible;
    public TrayIconController()
    {
        _icon = CreateIcon();
        _menu = new Forms.ContextMenuStrip();
        _menu.Font = new Font("Segoe UI", 9);
        _menu.ShowImageMargin = false;
        _menu.ShowCheckMargin = true;
        _menu.Opening += (_, _) => ApplyMenuTheme();
        _menu.Items.Add("Open Unity Connect", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add("Show phone", null, (_, _) => ShowRequested?.Invoke());
        _menu.Items.Add("Connect phone", null, (_, _) => ConnectRequested?.Invoke());
        _demo = new Forms.ToolStripMenuItem("Use sample data") { CheckOnClick = false };
        _demo.Click += (_, _) => DemoRequested?.Invoke(!_demo.Checked);
        _menu.Items.Add(_demo);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        foreach (Forms.ToolStripItem item in _menu.Items) item.Padding = new Forms.Padding(7,5,12,5);
        ApplyMenuTheme();
        _tray = new Forms.NotifyIcon { Text = "Unity Connect · Not connected", Icon = _icon, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ToggleRequested?.Invoke(); };
    }
    public void SetDemo(bool enabled) => _demo.Checked = enabled;
    public void SetBusy(bool busy) => _demo.Enabled = !busy;
    public void SetStatus(string status)
    {
        const int maxTooltipLength = 63;
        var tooltip = $"Unity Connect · {status}";
        _tray.Text = tooltip.Length <= maxTooltipLength ? tooltip : tooltip[..maxTooltipLength];
    }
    private void ApplyMenuTheme()
    {
        var dark = SystemThemeService.ReadSystemTheme() == AppTheme.Dark;
        _menu.BackColor = dark ? Color.FromArgb(32,32,32) : Color.White;
        _menu.ForeColor = dark ? Color.FromArgb(245,245,245) : Color.FromArgb(10,10,11);
        _menu.Renderer = new Forms.ToolStripProfessionalRenderer(new MenuColors(dark));
        foreach (Forms.ToolStripItem item in _menu.Items) item.ForeColor = _menu.ForeColor;
    }
    private sealed class MenuColors(bool dark) : Forms.ProfessionalColorTable
    {
        private Color Background => dark ? Color.FromArgb(32,32,32) : Color.White;
        private Color Highlight => dark ? Color.FromArgb(45,45,45) : Color.FromArgb(231,231,231);
        private Color Border => dark ? Color.FromArgb(61,61,61) : Color.FromArgb(229,229,229);
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Highlight;
        public override Color CheckSelectedBackground => Highlight;
    }
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
