using System.ComponentModel;
using System;
using System.Windows;
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
    private void ApplyWindowTheme() => WindowsWindowStyling.Apply(this);
    protected override void OnClosed(EventArgs e)
    {
        if (_themeSubscribed) SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        base.OnClosed(e);
    }
}
