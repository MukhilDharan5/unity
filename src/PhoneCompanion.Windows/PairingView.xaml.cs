using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows;

public partial class PairingView : UserControl
{
    private PhoneStateManager? _manager;
    private IdentityAndTrustStore? _store;
    private CancellationTokenSource? _attempt;
    private CancellationTokenSource? _discovery;
    private TaskCompletionSource<bool>? _approval;
    private bool _busy;

    public PairingView() => InitializeComponent();

    public event Action? Connected;
    public event Action<bool>? ActivityChanged;

    public void Configure(PhoneStateManager manager, IdentityAndTrustStore store)
    {
        _manager = manager;
        _store = store;
        RefreshTrustedState();
    }

    public void Prepare()
    {
        RefreshTrustedState();
        if (!_busy)
        {
            ConnectPanel.Visibility = Visibility.Visible;
            CodePanel.Visibility = Visibility.Collapsed;
        }
        Dispatcher.BeginInvoke(() => DiscoverButton.Focus());
    }

    public void Stop()
    {
        _approval?.TrySetResult(false);
        _attempt?.Cancel();
        _discovery?.Cancel();
    }

    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        if (!ReadyForOperation()) return;
        SetBusy(true);
        _discovery = new CancellationTokenSource(ConnectionPolicy.PairingTimeout);
        try
        {
            SetStatus("Looking for Unity Connect on your network…");
            var phones = await MdnsPhoneDiscovery.FindAsync(_discovery.Token);
            var phone = phones.Count > 0 ? phones[0] : null;
            if (phone is null)
            {
                SetStatus("No phone was found automatically. Enter its address or try Bluetooth.");
                return;
            }
            Endpoint.Text = phone.Endpoint;
            SetStatus($"Found {phone.Name}. Opening a secure connection…");
            await ConnectCoreAsync(TransportKind.Wifi,
                token => ConnectionFactories.OpenWifiAsync(phone.Endpoint, token), phone.Endpoint);
        }
        catch (OperationCanceledException) { SetStatus("Network discovery canceled."); }
        catch { SetStatus("Automatic discovery is unavailable. Enter the phone address or try Bluetooth."); }
        finally
        {
            _discovery.Dispose();
            _discovery = null;
            SetBusy(false);
        }
    }

    private async void OnWifi(object sender, RoutedEventArgs e)
    {
        if (!ReadyForOperation()) return;
        var endpoint = Endpoint.Text.Trim();
        SetBusy(true);
        try { await ConnectCoreAsync(TransportKind.Wifi, token => ConnectionFactories.OpenWifiAsync(endpoint, token), endpoint); }
        finally { SetBusy(false); }
    }

    private async void OnBluetooth(object sender, RoutedEventArgs e)
    {
        if (!ReadyForOperation()) return;
        SetBusy(true);
        try { await ConnectCoreAsync(TransportKind.Ble, token => ConnectionFactories.OpenBleAsync(SetStatus, token), null); }
        finally { SetBusy(false); }
    }

    private bool ReadyForOperation() => !_busy && _manager is not null && _store is not null;

    private async Task ConnectCoreAsync(TransportKind kind, Func<CancellationToken, Task<IFrameConnection>> open, string? endpoint)
    {
        if (_attempt is not null || _manager is null || _store is null) return;
        _attempt = new CancellationTokenSource(ConnectionPolicy.PairingTimeout);
        var trusted = _store.Load();
        try
        {
            if (_manager.HasConnectedRoute(kind))
            {
                SetStatus($"{(kind == TransportKind.Wifi ? "Wi-Fi" : "Bluetooth")} is already connected.");
                Connected?.Invoke();
                return;
            }
            SetStatus(kind == TransportKind.Wifi ? "Opening a secure Wi-Fi connection…" : "Looking for your phone…");
            var live = new LivePhoneTransport(kind, open, _store.OpenIdentity(), trusted?.Fingerprint, ApproveAsync);
            var connected = await _manager.AddTransportAsync(live, _attempt.Token);
            if (!connected || live.Peer is null)
            {
                await live.DisposeAsync();
                throw new InvalidOperationException("The phone did not complete pairing.");
            }
            _store.Save(new TrustedDevice(live.Peer.Fingerprint, live.Peer.Name, endpoint ?? trusted?.WifiEndpoint));
            SetStatus($"Connected to {live.Peer.Name}.");
            RefreshTrustedState();
            Connected?.Invoke();
        }
        catch (OperationCanceledException) { SetStatus("Connection canceled. Try again when your phone is ready."); }
        catch (Exception error) { SetStatus(Friendly(error)); }
        finally
        {
            _attempt.Dispose();
            _attempt = null;
            CodePanel.Visibility = Visibility.Collapsed;
            ConnectPanel.Visibility = Visibility.Visible;
        }
    }

    private Task<bool> ApproveAsync(PeerIdentity peer, CancellationToken token) => Dispatcher.InvokeAsync(async () =>
    {
        Code.Text = peer.Code;
        ConnectPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Visible;
        Animate(CodePanel);
        SetStatus("Check that the same code appears on your phone.");
        _approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => _approval.TrySetCanceled(token));
        try { return await _approval.Task; }
        finally { _approval = null; }
    }).Task.Unwrap();

    private void OnApprove(object sender, RoutedEventArgs e)
    {
        SetStatus("Waiting for confirmation on your phone…");
        _approval?.TrySetResult(true);
    }

    private void OnDecline(object sender, RoutedEventArgs e) => _approval?.TrySetResult(false);

    private async void OnForget(object sender, RoutedEventArgs e)
    {
        if (_manager is null || _store is null || _busy) return;
        SetBusy(true);
        try
        {
            _store.Forget();
            await _manager.SetTransportAsync(null);
            Endpoint.Clear();
            RefreshTrustedState();
            SetStatus("Paired phone forgotten.");
        }
        finally { SetBusy(false); }
    }

    private void RefreshTrustedState()
    {
        var trusted = _store?.Load();
        if (string.IsNullOrWhiteSpace(Endpoint.Text) && trusted?.WifiEndpoint is not null)
            Endpoint.Text = trusted.WifiEndpoint;
        ForgetButton.Visibility = trusted is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBusy(bool value)
    {
        if (_busy == value) return;
        _busy = value;
        DiscoverButton.IsEnabled = !value;
        WifiButton.IsEnabled = !value;
        BluetoothButton.IsEnabled = !value;
        Endpoint.IsEnabled = !value;
        ForgetButton.IsEnabled = !value;
        ActivityChanged?.Invoke(value);
    }

    private void SetStatus(string value)
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(() => Status.Text = value);
        else Status.Text = value;
    }

    private static void Animate(FrameworkElement element)
    {
        element.Opacity = 0;
        element.RenderTransform = new TranslateTransform(0, 8);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
    }

    private static string Friendly(Exception error) => error switch
    {
        ArgumentException => error.Message,
        TimeoutException => "The phone was not found. Keep Unity Connect open and try again.",
        System.Security.Cryptography.CryptographicException => "Secure pairing failed. Forget the pairing on both devices and try again.",
        _ => "Couldn’t connect. Check that both devices are on the same Wi-Fi, or try Bluetooth."
    };
}
