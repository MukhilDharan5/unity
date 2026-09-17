using System.ComponentModel;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PhoneCompanion.Windows;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _themeSubscribed;
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ApplyWindowTheme();
            SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
            _themeSubscribed = true;
        };
    }
    public void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void OnPhone(object sender, RoutedEventArgs e) => SelectPage(false);
    private void OnConnection(object sender, RoutedEventArgs e) => SelectPage(true);
    private void SelectPage(bool connection)
    {
        PhonePage.Visibility = connection ? Visibility.Collapsed : Visibility.Visible;
        ConnectionPage.Visibility = connection ? Visibility.Visible : Visibility.Collapsed;
        PhoneNavigation.Tag = connection ? null : "Selected";
        ConnectionNavigation.Tag = connection ? "Selected" : null;
        PageTitle.Text = connection ? "Connection" : "Phone";
        PageSubtitle.Text = connection ? "Pair and manage your trusted phone." : "Phone status and connected experiences.";
    }
    private void OnConnectPhone(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowPairing();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    public void CloseForExit() { _allowClose = true; Close(); }
    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ApplyWindowTheme);
    }
    private void ApplyWindowTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = SystemThemeService.ReadSystemTheme() == AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var caption = dark == 1 ? 0x00202020 : 0x00F3F3F3;
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        var corners = 2;
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }
    protected override void OnClosed(EventArgs e)
    {
        if (_themeSubscribed) SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        base.OnClosed(e);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
