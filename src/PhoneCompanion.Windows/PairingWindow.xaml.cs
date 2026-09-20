using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows;

public partial class PairingWindow : Window
{
    private readonly PhoneStateManager _manager;
    private readonly IdentityAndTrustStore _store;
    private CancellationTokenSource? _attempt;
    private CancellationTokenSource? _discovery;
    private TaskCompletionSource<bool>? _approval;
    public event Action? Connected;
    public PairingWindow(PhoneStateManager manager, IdentityAndTrustStore store)
    {
        InitializeComponent(); _manager = manager; _store = store;
        var trusted = store.Load();
        if (trusted?.WifiEndpoint is not null) Endpoint.Text = trusted.WifiEndpoint;
        ForgetButton.Visibility = trusted is null ? Visibility.Collapsed : Visibility.Visible;
        Closed += (_, _) => {
            _approval?.TrySetResult(false); _attempt?.Cancel(); _attempt?.Dispose();
            _discovery?.Cancel(); _discovery?.Dispose();
        };
    }
    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        if (_attempt is not null || _discovery is not null) return;
        _discovery = new CancellationTokenSource(ConnectionPolicy.PairingTimeout);
        string? endpoint = null;
        SetBusy(true); SetStatus("Looking for Unity Connect on your network…");
        try
        {
            var phones = await MdnsPhoneDiscovery.FindAsync(_discovery.Token);
            var phone = phones.Count > 0 ? phones[0] : null;
            if (phone is null) SetStatus("No phone was found automatically. Enter the address shown on your phone or try Bluetooth.");
            else
            {
                endpoint = phone.Endpoint; Endpoint.Text = endpoint;
                SetStatus($"Found {phone.Name}. Opening a secure connection…");
            }
        }
        catch (OperationCanceledException) { SetStatus("Network discovery canceled."); }
        catch { SetStatus("Automatic discovery is unavailable. Enter the phone address or try Bluetooth."); }
        finally { _discovery.Dispose(); _discovery = null; SetBusy(false); }
        if (endpoint is { } discoveredEndpoint)
            await ConnectAsync(TransportKind.Wifi,
                token => ConnectionFactories.OpenWifiAsync(discoveredEndpoint, token), discoveredEndpoint);
    }
    private async void OnWifi(object sender, RoutedEventArgs e)
    {
        var endpoint = Endpoint.Text.Trim();
        await ConnectAsync(TransportKind.Wifi, token => ConnectionFactories.OpenWifiAsync(endpoint, token), endpoint);
    }
    private async void OnBluetooth(object sender, RoutedEventArgs e) =>
        await ConnectAsync(TransportKind.Ble, token => ConnectionFactories.OpenBleAsync(SetStatus, token), null);

    private async Task ConnectAsync(TransportKind kind, Func<CancellationToken, Task<IFrameConnection>> open, string? endpoint)
    {
        if (_attempt is not null) return;
        _attempt = new CancellationTokenSource(ConnectionPolicy.PairingTimeout); SetBusy(true);
        var trusted = _store.Load();
        try
        {
            if (_manager.HasConnectedRoute(kind))
            {
                SetStatus($"{(kind == TransportKind.Wifi ? "Wi-Fi" : "Bluetooth")} is already connected.");
                Connected?.Invoke(); Close(); return;
            }
            SetStatus(kind == TransportKind.Wifi ? "Opening a secure Wi-Fi connection…" : "Looking for your phone…");
            var live = new LivePhoneTransport(kind, open, _store.OpenIdentity(), trusted?.Fingerprint, ApproveAsync);
            var connected = await _manager.AddTransportAsync(live, _attempt.Token);
            if (!connected || live.Peer is null) { await live.DisposeAsync(); throw new InvalidOperationException("The phone did not complete pairing."); }
            _store.Save(new TrustedDevice(live.Peer.Fingerprint, live.Peer.Name, endpoint ?? trusted?.WifiEndpoint));
            SetStatus($"Connected to {live.Peer.Name}."); Connected?.Invoke(); Close();
        }
        catch (OperationCanceledException) { SetStatus("Connection canceled. Try again when your phone is ready."); }
        catch (Exception error) { SetStatus(Friendly(error)); }
        finally { _attempt.Dispose(); _attempt = null; SetBusy(false); CodePanel.Visibility = Visibility.Collapsed; ConnectPanel.Visibility = Visibility.Visible; }
    }
    private Task<bool> ApproveAsync(PeerIdentity peer, CancellationToken token)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            Code.Text = peer.Code; ConnectPanel.Visibility = Visibility.Collapsed; CodePanel.Visibility = Visibility.Visible;
            SetStatus("Check that the same code appears on your phone.");
            _approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => _approval.TrySetCanceled(token));
            try { return await _approval.Task; } finally { _approval = null; }
        }).Task.Unwrap();
    }
    private void OnApprove(object sender, RoutedEventArgs e) { SetStatus("Waiting for confirmation on your phone…"); _approval?.TrySetResult(true); }
    private void OnDecline(object sender, RoutedEventArgs e) => _approval?.TrySetResult(false);
    private async void OnForget(object sender, RoutedEventArgs e)
    {
        _store.Forget(); await _manager.SetTransportAsync(null); Endpoint.Clear(); ForgetButton.Visibility = Visibility.Collapsed;
        SetStatus("Paired phone forgotten.");
    }
    private void SetBusy(bool value)
    {
        DiscoverButton.IsEnabled = !value; WifiButton.IsEnabled = !value; BluetoothButton.IsEnabled = !value; Endpoint.IsEnabled = !value;
        ForgetButton.IsEnabled = !value;
    }
    private void SetStatus(string value) { if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(() => Status.Text = value); else Status.Text = value; }
    private static string Friendly(Exception error) => error switch
    {
        ArgumentException => error.Message,
        TimeoutException => "The phone was not found. Keep Unity Connect open and try again.",
        System.Security.Cryptography.CryptographicException => "Secure pairing failed. Forget the pairing on both devices and try again.",
        _ => "Couldn’t connect. Check that both devices are on the same Wi-Fi, or try Bluetooth."
    };
}
