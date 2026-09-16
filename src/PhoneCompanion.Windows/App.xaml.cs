using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;
using PhoneCompanion.Windows.Clipboard;
using PhoneCompanion.Windows.Connection;
using PhoneCompanion.Windows.ViewModels;

namespace PhoneCompanion.Windows;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private PhoneStateManager? _manager;
    private PhoneViewModel? _viewModel;
    private FlyoutWindow? _flyout;
    private TrayIconController? _tray;
    private SystemThemeService? _theme;
    private ClipboardSyncCoordinator? _clipboard;
    private PairingWindow? _pairing;
    private readonly IdentityAndTrustStore _connectionStore = new();
    private CancellationTokenSource? _reconnect;
    private Task? _reconnectTask;
    private readonly IPhoneMessageCodec _codec = new JsonPhoneMessageCodec();
    private bool _ownsMutex;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\PhoneCompanion.Phase1", out _ownsMutex);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PhoneCompanion.Phase1.Show");
        if (!_ownsMutex) { _showSignal.Set(); Shutdown(); return; }
        _theme = new SystemThemeService(Resources, Dispatcher);
        _theme.Start();
        _manager = new PhoneStateManager(_codec);
        _manager.StateChanged += OnPhoneStateChanged;
        _clipboard = new ClipboardSyncCoordinator(_manager, new WindowsClipboardService(Dispatcher), Dispatcher);
        _viewModel = new PhoneViewModel(_manager, Dispatcher, _clipboard);
        _flyout = new FlyoutWindow { DataContext = _viewModel };
        _tray = new TrayIconController();
        _tray.ToggleRequested += () => _flyout.ToggleAtCursor();
        _tray.ShowRequested += () => _flyout.ShowAtCursor();
        _tray.ConnectRequested += ShowPairing;
        _tray.DemoRequested += async enabled => await SetDemoAsync(enabled);
        _tray.ExitRequested += async () => await ExitAsync();
        _viewModel.PropertyChanged += (_, _) => { _tray?.SetStatus(_viewModel.Status); _tray?.SetDemo(_viewModel.IsDemo); };
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(() => { if (!_exiting) _flyout.ShowAtCursor(); }), null, Timeout.Infinite, false);

        if (_connectionStore.Load() is not null) StartReconnectLoop();
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) await SetDemoAsync(true);
        if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase)) _flyout.ShowAtCursor();
    }
    private void OnPhoneStateChanged(PhoneCompanion.Core.Models.PhoneState state)
    {
        if (_exiting || state.Connection != PhoneCompanion.Core.Models.ConnectionState.Disconnected || state.IsDemo) return;
        Dispatcher.BeginInvoke(StartReconnectLoop);
    }
    private void StartReconnectLoop()
    {
        if (_exiting || _manager is null || _pairing is not null || _connectionStore.Load() is null ||
            _reconnectTask is { IsCompleted: false }) return;
        _reconnect?.Dispose(); _reconnect = new CancellationTokenSource();
        _reconnectTask = ReconnectLoopAsync(_reconnect.Token);
    }
    private void StopReconnectLoop()
    {
        _reconnect?.Cancel(); _reconnect?.Dispose(); _reconnect = null; _reconnectTask = null;
    }
    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        var pauses = new[] { 2, 5, 10, 20, 30 };
        var attempt = 0;
        while (!token.IsCancellationRequested && !_exiting && _manager is not null &&
               _manager.Current.Connection != PhoneCompanion.Core.Models.ConnectionState.Connected)
        {
            var trusted = _connectionStore.Load();
            if (trusted is null) return;
            if (trusted.WifiEndpoint is { } endpoint && await TryTrustedRouteAsync(
                PhoneCompanion.Core.Models.TransportKind.Wifi, value => ConnectionFactories.OpenWifiAsync(endpoint, value), trusted, 8, token)) return;
            if (await TryTrustedRouteAsync(PhoneCompanion.Core.Models.TransportKind.Ble,
                value => ConnectionFactories.OpenBleAsync(null, value), trusted, 15, token)) return;
            await Task.Delay(TimeSpan.FromSeconds(pauses[Math.Min(attempt++, pauses.Length - 1)]), token);
        }
    }
    private async Task<bool> TryTrustedRouteAsync(PhoneCompanion.Core.Models.TransportKind kind,
        Func<CancellationToken, Task<IFrameConnection>> open, TrustedDevice trusted, int seconds, CancellationToken lifetime)
    {
        if (_manager is null) return false;
        try
        {
            var transport = new LivePhoneTransport(kind, open, _connectionStore.OpenIdentity(), trusted.Fingerprint,
                (_, _) => Task.FromResult(false));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            return await _manager.SetTransportAsync(transport, timeout.Token);
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { return false; }
        catch { return false; }
    }
    internal void ShowPairing()
    {
        if (_manager is null || _exiting) return;
        StopReconnectLoop();
        if (_pairing is { IsVisible: true }) { _pairing.Activate(); return; }
        _pairing = new PairingWindow(_manager, _connectionStore);
        _pairing.Connected += () => _flyout?.ShowAtCursor();
        _pairing.Closed += (_, _) => { _pairing = null; StartReconnectLoop(); };
        _pairing.Show(); _pairing.Activate();
    }
    private async Task SetDemoAsync(bool enabled)
    {
        if (_manager is null || _tray is null || _exiting) return;
        StopReconnectLoop(); _tray.SetBusy(true);
        try { await _manager.SetTransportAsync(enabled ? new MockPhoneTransport(_codec) : null); }
        finally { _tray?.SetBusy(false); }
    }
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        StopReconnectLoop();
        _showWait?.Unregister(null);
        _tray?.Dispose(); _tray = null;
        _theme?.Dispose(); _theme = null;
        _viewModel?.Dispose();
        _pairing?.Close(); _pairing = null;
        _clipboard?.Dispose(); _clipboard = null;
        if (_manager is not null) { _manager.StateChanged -= OnPhoneStateChanged; await _manager.DisposeAsync(); }
        _flyout?.CloseForExit();
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _tray?.Dispose();
        _theme?.Dispose();
        _viewModel?.Dispose();
        _clipboard?.Dispose();
        if (_manager is not null) _manager.StateChanged -= OnPhoneStateChanged;
        StopReconnectLoop();
        _showSignal?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
