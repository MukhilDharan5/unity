using System.ComponentModel;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using PhoneCompanion.Core.State;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _themeSubscribed;
    private bool _pairingConfigured;
    private bool _pairingActive;
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
    public event Action<bool>? PairingActivityChanged;
    public void ConfigurePairing(PhoneStateManager manager, IdentityAndTrustStore store)
    {
        if (_pairingConfigured) return;
        _pairingConfigured = true;
        InlinePairing.Configure(manager, store);
        InlinePairing.ActivityChanged += SetPairingActive;
    }
    public void ShowPairing()
    {
        ShowDashboard();
        SetPairingActive(true);
        SelectPage(true);
        InlinePairing.Prepare();
        InlinePairing.BringIntoView();
    }
    private void OnPhone(object sender, RoutedEventArgs e)
    {
        InlinePairing.Stop();
        SetPairingActive(false);
        SelectPage(false);
    }
    private void OnConnection(object sender, RoutedEventArgs e) => SelectPage(true);
    private void SelectPage(bool connection)
    {
        PhonePage.Visibility = connection ? Visibility.Collapsed : Visibility.Visible;
        ConnectionPage.Visibility = connection ? Visibility.Visible : Visibility.Collapsed;
        PhoneNavigation.Tag = connection ? null : "Selected";
        ConnectionNavigation.Tag = connection ? "Selected" : null;
        PageTitle.Text = connection ? "Connection" : "Phone";
        PageSubtitle.Text = connection ? "Pair and manage your trusted phone." : "Phone status and connected experiences.";
        AnimatePage(connection ? ConnectionPage : PhonePage);
    }
    private void OnConnectPhone(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowPairing();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            InlinePairing.Stop();
            SetPairingActive(false);
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
    public void CloseForExit() { InlinePairing.Stop(); SetPairingActive(false); _allowClose = true; Close(); }
    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ApplyWindowTheme);
    }
    private void ApplyWindowTheme() => WindowsWindowStyling.Apply(this);
    protected override void OnClosed(EventArgs e)
    {
        InlinePairing.Stop();
        if (_themeSubscribed) SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        base.OnClosed(e);
    }
    private static void AnimatePage(FrameworkElement page)
    {
        page.Opacity = 0;
        page.RenderTransform = new TranslateTransform(0, 10);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
        ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
    }
    private void SetPairingActive(bool active)
    {
        if (_pairingActive == active) return;
        _pairingActive = active;
        PairingActivityChanged?.Invoke(active);
    }
}
