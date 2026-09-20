using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.State;
using PhoneCompanion.Windows.Brightness;
using PhoneCompanion.Windows.Audio;
using PhoneCompanion.Windows.Clipboard;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows.ViewModels;

public sealed class PhoneViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PhoneStateManager _manager;
    private readonly Dispatcher _dispatcher;
    private readonly ClipboardSyncCoordinator? _clipboard;
    private readonly Func<string?>? _openPhone;
    private readonly LaptopAdaptiveBrightnessController? _laptopBrightness;
    private readonly AndroidSettingsAssistant? _androidSettings;
    private readonly InternetConnectivityMonitor? _internetConnectivity;
    private readonly LaptopAudioStreamer? _laptopAudio;
    private PhoneState _state;
    private bool _busy;
    private bool _disposed;
    private string? _commandNotice;
    private int? _pendingPhoneBrightness;
    private CancellationTokenSource? _brightnessDebounce;
    public PhoneViewModel(PhoneStateManager manager, Dispatcher dispatcher, ClipboardSyncCoordinator? clipboard = null,
        Func<string?>? openPhone = null, LaptopAdaptiveBrightnessController? laptopBrightness = null,
        AndroidSettingsAssistant? androidSettings = null, InternetConnectivityMonitor? internetConnectivity = null,
        LaptopAudioStreamer? laptopAudio = null)
    {
        _manager = manager; _dispatcher = dispatcher; _clipboard = clipboard; _openPhone = openPhone;
        _laptopBrightness = laptopBrightness; _androidSettings = androidSettings;
        _internetConnectivity = internetConnectivity; _laptopAudio = laptopAudio; _state = manager.Current;
        Previous = new AsyncCommand(() => SendAsync(MediaCommand.PreviousTrack), () => CanControl && _state.Media?.Capabilities.PreviousTrack == true);
        PlayPause = new AsyncCommand(() => SendAsync(MediaCommand.PlayPause), () => CanControl && _state.Media?.Capabilities.PlayPause == true);
        Next = new AsyncCommand(() => SendAsync(MediaCommand.NextTrack), () => CanControl && _state.Media?.Capabilities.NextTrack == true);
        ToggleDndRule = new AsyncCommand(ToggleDndRuleAsync, () => !_busy && CanControlDndRule);
        ToggleClipboard = new AsyncCommand(ToggleClipboardAsync, () => !_busy && _clipboard is not null);
        OpenPhone = new AsyncCommand(OpenPhoneAsync, () => !_busy && _openPhone is not null);
        TogglePhoneAdaptive = new AsyncCommand(TogglePhoneAdaptiveAsync, () => !_busy && CanControlPhoneBrightness);
        ToggleLaptopAdaptive = new AsyncCommand(ToggleLaptopAdaptiveAsync, () => !_busy && CanUseLaptopAdaptive);
        MoveHeadphones = new AsyncCommand(MoveHeadphonesAsync, () => !_busy && CanMoveHeadphones);
        UsePhoneInternet = new AsyncCommand(UsePhoneInternetAsync, () => !_busy && CanUsePhoneInternet);
        ToggleLaptopAudio = new AsyncCommand(ToggleLaptopAudioAsync, () => !_busy && CanToggleLaptopAudio);
        manager.StateChanged += OnStateChanged;
        if (_laptopBrightness is not null) _laptopBrightness.Changed += OnLaptopBrightnessChanged;
        if (_internetConnectivity is not null) _internetConnectivity.Changed += OnInternetConnectivityChanged;
        if (_laptopAudio is not null) _laptopAudio.Changed += OnLaptopAudioChanged;
        if (_clipboard is not null)
        {
            _clipboard.Changed += OnClipboardChanged;
            _clipboard.Notice += OnClipboardNotice;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public AsyncCommand Previous { get; }
    public AsyncCommand PlayPause { get; }
    public AsyncCommand Next { get; }
    public AsyncCommand ToggleDndRule { get; }
    public AsyncCommand ToggleClipboard { get; }
    public AsyncCommand OpenPhone { get; }
    public AsyncCommand TogglePhoneAdaptive { get; }
    public AsyncCommand ToggleLaptopAdaptive { get; }
    public AsyncCommand MoveHeadphones { get; }
    public AsyncCommand UsePhoneInternet { get; }
    public AsyncCommand ToggleLaptopAudio { get; }
    public bool IsConnected => _state.Connection == ConnectionState.Connected;
    public bool IsDemo => _state.IsDemo;
    public bool HasMedia => _state.Media?.IsPlaying == true;
    private bool CanControl => !_busy && IsConnected;
    public string Status => _state.Connection switch
    {
        ConnectionState.Connecting => "Connecting", ConnectionState.Connected when IsDemo => "Sample",
        ConnectionState.Connected => "Connected", _ => "Not connected"
    };
    public string TrayStatus
    {
        get
        {
            if (!IsConnected) return Status;
            var battery = _state.Battery is { } b ? $"{b.Level}%{(b.Charging ? " charging" : string.Empty)}" : null;
            var data = _state.Cellular is null ? null : DataConnectionText;
            return string.Join(" · ", new[] { Status, battery, data }.Where(value => value is not null));
        }
    }
    public string BatteryText => _state.Battery is { } b ? $"{b.Level}% · {(b.Charging ? "Charging" : "Not charging")}" : "Battery unavailable";
    public string BatteryLevelText => _state.Battery is { } b ? $"{b.Level}%" : "—";
    public int BatteryLevel => _state.Battery?.Level ?? 0;
    public bool HasBattery => _state.Battery is not null;
    public bool HasPhoneBrightness => _state.Brightness is not null;
    public bool HasAudioOutput => _state.AudioOutput is not null;
    public bool CanMoveHeadphones => IsConnected && !IsDemo && _state.AudioOutput is not null;
    public string HeadphoneName => _state.AudioOutput?.DeviceName ?? "Bluetooth headphones";
    public string HeadphoneActionText => $"Move {HeadphoneName} to laptop";
    public string HeadphoneHandoffDetail => _state.AudioOutput?.CanRelease == true
        ? "One-tap release is ready on your phone"
        : "Your phone will guide the disconnect step";
    public bool CanUsePhoneInternet => IsConnected && !IsDemo && _internetConnectivity?.HasInternet == false;
    public string PhoneInternetDetail => "Laptop internet unavailable · Use Android tethering";
    public bool IsLaptopAudioStreaming => _laptopAudio?.IsStreaming == true;
    public bool CanToggleLaptopAudio => _laptopAudio is not null && (IsLaptopAudioStreaming || IsConnected && !IsDemo);
    public string LaptopAudioActionText => IsLaptopAudioStreaming ? "Stop" : "Play on phone";
    public string LaptopAudioStatus => _laptopAudio?.Status ?? "Laptop audio streaming unavailable";
    public bool CanControlPhoneBrightness => IsConnected && !IsDemo && _state.Brightness?.CanControl == true;
    public double PhoneBrightnessLevel
    {
        get => _pendingPhoneBrightness ?? _state.Brightness?.Level ?? 0;
        set
        {
            var level = Math.Clamp((int)Math.Round(value), 1, 100);
            if (!CanControlPhoneBrightness || _pendingPhoneBrightness == level ||
                (_pendingPhoneBrightness is null && _state.Brightness?.Level == level)) return;
            _pendingPhoneBrightness = level;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneBrightnessLevel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhoneBrightnessText)));
            _brightnessDebounce?.Cancel(); _brightnessDebounce?.Dispose();
            _brightnessDebounce = new CancellationTokenSource();
            _ = SendPhoneBrightnessAfterDelayAsync(level, _brightnessDebounce.Token);
        }
    }
    public string PhoneBrightnessText => $"{(int)Math.Round(PhoneBrightnessLevel)}%";
    public bool IsPhoneAdaptive => _state.Brightness?.Adaptive == true;
    public string PhoneAdaptiveText => IsPhoneAdaptive ? "Auto" : "Manual";
    public string PhoneBrightnessAccessText => CanControlPhoneBrightness ? "Control available" : "Allow control in the Android app";
    public string AmbientLightText => _state.Brightness switch
    {
        { AmbientStatus: AmbientLightStatus.Valid, AmbientLux: double lux } => $"Ambient light · {lux:0.#} lux",
        { AmbientStatus: AmbientLightStatus.Covered } => "Ambient light · Phone covered",
        { AmbientStatus: AmbientLightStatus.Unavailable } => "Ambient light sensor unavailable",
        _ => "Ambient light unavailable"
    };
    public bool CanUseLaptopAdaptive => _laptopBrightness?.IsSupported == true;
    public bool IsLaptopAdaptive => _laptopBrightness?.Enabled == true;
    public string LaptopAdaptiveState => IsLaptopAdaptive ? "On" : "Off";
    public string LaptopAdaptiveAction => IsLaptopAdaptive ? "Turn laptop adaptive brightness off" : "Turn laptop adaptive brightness on";
    public string LaptopAdaptiveStatus => _laptopBrightness?.Status ?? "Laptop brightness control unavailable";
    public string ConnectionSummary => IsDemo ? "Sample data preview" : _state.Connection switch
    {
        ConnectionState.Connected => "Your phone is available",
        ConnectionState.Connecting => "Looking for your phone",
        _ => "Connect to get started"
    };
    public string ChargingText => _state.Battery is { } b ? (b.Charging ? "Charging" : "Not charging") : "Battery unavailable";
    public string DataConnectionText => _state.Cellular is not { } c ? "Unavailable" : c.IsUsingCellularData ? "Mobile data" : c.IsUsingWifi switch
    {
        true => "Wi-Fi", false => "No data connection", null => "Unavailable"
    };
    public string CellularDetailsText
    {
        get
        {
            if (_state.Cellular is not { } c) return "Connection details unavailable";
            var network = c.Network switch
            {
                CellularNetwork.TwoG => "2G", CellularNetwork.ThreeG => "3G", CellularNetwork.FourG => "LTE",
                CellularNetwork.FiveG => "5G", CellularNetwork.Cellular => "Cellular", _ => null
            };
            var signal = c.Signal switch
            {
                SignalStrength.None => "No signal", SignalStrength.Poor => "Weak signal", SignalStrength.Fair => "Fair signal",
                SignalStrength.Good => "Good signal", SignalStrength.Excellent => "Strong signal", _ => null
            };
            var details = string.Join(" · ", new[] { network, signal }.Where(value => value is not null));
            return details.Length > 0 ? details : "Cellular details unavailable";
        }
    }
    public string CellularText
    {
        get
        {
            if (_state.Cellular is not { } c) return "Mobile connection unavailable";
            var dataConnection = c.IsUsingCellularData ? "Mobile data" : c.IsUsingWifi switch
            {
                true => "Wi-Fi",
                false => "No data connection",
                null => "Data connection unavailable"
            };
            var network = c.Network switch
            {
                CellularNetwork.TwoG => "2G", CellularNetwork.ThreeG => "3G", CellularNetwork.FourG => "LTE",
                CellularNetwork.FiveG => "5G", CellularNetwork.Cellular => "Cellular", _ => null
            };
            var signal = c.Signal switch
            {
                SignalStrength.None => "No signal", SignalStrength.Poor => "Weak signal", SignalStrength.Fair => "Fair signal",
                SignalStrength.Good => "Good signal", SignalStrength.Excellent => "Strong signal", _ => null
            };
            return string.Join(" · ", new[] { dataConnection, network, signal }.Where(value => value is not null));
        }
    }
    public string Source => _state.Media?.Source ?? "Now playing";
    public string Title => _state.Media?.Title ?? (_state.Media is null ? "Nothing playing" : "Untitled media");
    public string Artist => _state.Media?.Artist ?? (_state.Media is not null ? "Artist unavailable"
        : IsConnected ? "Play something on your phone" : "Connect your phone to see media");
    public string Dnd => _state.Dnd is { } d ? (d.Enabled ? "On" : "Off") : "Unavailable";
    public bool CanControlDndRule => IsConnected && !IsDemo && _state.Dnd?.CanControlCompanionRule == true &&
        _state.Dnd.CompanionRuleActive is not null;
    public bool IsDndRuleActive => _state.Dnd?.CompanionRuleActive == true;
    public string DndRuleState => IsDndRuleActive ? "On" : "Off";
    public string DndRuleAction => IsDndRuleActive ? "Turn companion DND off" : "Turn companion DND on";
    public string Sound => _state.Sound?.ToString() ?? "Unavailable";
    public string PlaybackLabel => _state.Media?.IsPlaying == true ? "Pause" : "Play";
    public string PlaybackStateText => _state.Media?.IsPlaying == true ? "Playing" : "Paused";
    public string PlaybackPath => _state.Media?.IsPlaying == true
        ? "M 6,4 L 10,4 L 10,20 L 6,20 Z M 14,4 L 18,4 L 18,20 L 14,20 Z"
        : "M 7,3 L 21,12 L 7,21 Z";
    public bool ClipboardEnabled => _clipboard?.Enabled == true;
    public string ClipboardStatus => ClipboardEnabled ? (_clipboard?.CanTransfer == true ? "On" : "Waiting") : "Off";
    public string ClipboardAction => ClipboardEnabled ? "Turn clipboard sync off" : "Turn clipboard sync on";
    public string Footer => _commandNotice ?? (IsDemo ? "Sample data · No phone connected" : IsConnected
        ? "Your phone, a little closer." : "Your phone will appear here when connected.");
    private void OnStateChanged(PhoneState state)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _state = state; _commandNotice = null;
            if (_pendingPhoneBrightness == state.Brightness?.Level) _pendingPhoneBrightness = null;
            Refresh();
        });
    }
    private async Task SendAsync(MediaCommand command)
    {
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var result = await _manager.SendCommandAsync(command);
            if (result == CommandResult.Failed) _commandNotice = "Couldn't reach your phone. Try again.";
            else if (result == CommandResult.Unavailable) _commandNotice = "This control isn't available right now.";
        }
        finally { _busy = false; Refresh(); }
    }
    private async Task ToggleDndRuleAsync()
    {
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var result = await _manager.SetCompanionDndRuleAsync(!IsDndRuleActive);
            if (result == CommandResult.Failed) _commandNotice = "Couldn't update companion DND. Try again.";
            else if (result == CommandResult.Unavailable) _commandNotice = "Companion DND isn't available right now.";
        }
        finally { _busy = false; Refresh(); }
    }
    private Task ToggleClipboardAsync()
    {
        if (_clipboard is null) return Task.CompletedTask;
        try { _clipboard.SetEnabled(!_clipboard.Enabled); }
        catch (Exception) { _commandNotice = "Clipboard sync couldn't be started."; }
        Refresh();
        return Task.CompletedTask;
    }
    private Task OpenPhoneAsync()
    {
        _commandNotice = _openPhone?.Invoke();
        if (_commandNotice is null) _commandNotice = "Opening your phone…";
        Refresh();
        return Task.CompletedTask;
    }
    private async Task SendPhoneBrightnessAfterDelayAsync(int level, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            var result = await _manager.SetPhoneBrightnessAsync(level, null, token);
            if (result != CommandResult.Sent)
            {
                _pendingPhoneBrightness = null;
                _commandNotice = result == CommandResult.Failed
                    ? "Phone brightness could not be changed."
                    : "Allow brightness control in the Android app first.";
            }
            if (!_disposed && !_dispatcher.HasShutdownStarted) await _dispatcher.InvokeAsync(Refresh);
        }
        catch (OperationCanceledException) { }
    }
    private async Task TogglePhoneAdaptiveAsync()
    {
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var result = await _manager.SetPhoneBrightnessAsync(null, !IsPhoneAdaptive);
            if (result == CommandResult.Failed) _commandNotice = "Adaptive brightness could not be changed.";
            else if (result == CommandResult.Unavailable) _commandNotice = "Allow brightness control in the Android app first.";
        }
        finally { _busy = false; Refresh(); }
    }
    private Task ToggleLaptopAdaptiveAsync()
    {
        _laptopBrightness?.SetEnabled(!IsLaptopAdaptive);
        Refresh();
        return Task.CompletedTask;
    }
    private async Task MoveHeadphonesAsync()
    {
        var audio = _state.AudioOutput;
        if (audio is null || _androidSettings is null) return;
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var result = await _manager.RequestHeadphoneHandoffAsync();
            if (result == CommandResult.Failed)
            {
                _commandNotice = "Couldn't ask your phone to release the headphones.";
                return;
            }
            if (result == CommandResult.Unavailable)
            {
                _commandNotice = "Headphone handoff isn't available right now.";
                return;
            }

            var openedPhoneSettings = !audio.CanRelease &&
                await Task.Run(_androidSettings.TryOpenPhoneBluetoothSettings);
            var openedWindowsSettings = _androidSettings.OpenWindowsBluetoothSettings();
            _commandNotice = audio.CanRelease
                ? $"Release requested. Select {audio.DeviceName} in Windows Bluetooth settings."
                : openedPhoneSettings
                    ? $"Phone Bluetooth settings opened. Disconnect {audio.DeviceName}, then select it in Windows."
                    : $"Finish disconnecting {audio.DeviceName} on your phone, then select it in Windows Bluetooth settings.";
            if (!openedWindowsSettings) _commandNotice += " Open Windows Bluetooth settings manually.";
        }
        finally { _busy = false; Refresh(); }
    }
    private async Task UsePhoneInternetAsync()
    {
        if (_androidSettings is null) return;
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var result = await _manager.RequestHotspotAsync();
            if (result == CommandResult.Failed)
            {
                _commandNotice = "Couldn't ask your phone to open hotspot settings.";
                return;
            }
            if (result == CommandResult.Unavailable)
            {
                _commandNotice = "Phone internet isn't available right now.";
                return;
            }

            var openedPhoneSettings = await Task.Run(_androidSettings.TryOpenPhoneHotspotSettings);
            var openedWindowsSettings = _androidSettings.OpenWindowsWifiSettings();
            _commandNotice = openedPhoneSettings
                ? "Turn on internet tethering on your phone. Windows will use a saved hotspot automatically."
                : "Use the notification on your phone to turn on tethering. Windows will use a saved hotspot automatically.";
            if (!openedWindowsSettings) _commandNotice += " Open Windows Wi-Fi settings manually if needed.";
        }
        finally { _busy = false; Refresh(); }
    }
    private async Task ToggleLaptopAudioAsync()
    {
        if (_laptopAudio is null) return;
        if (_laptopAudio.IsStreaming)
        {
            await _laptopAudio.StopAsync();
            _commandNotice = "Laptop audio stopped.";
            Refresh();
            return;
        }
        var answer = MessageBox.Show(
            "Unity Connect will capture all audio playing through the current Windows output and stream it to your phone over the local network. Protected content may be silent. Start streaming?",
            "Play laptop audio on phone", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _busy = true; _commandNotice = null; Refresh();
        try
        {
            var error = await _laptopAudio.StartAsync();
            _commandNotice = error ?? "Laptop audio is playing on your phone.";
        }
        finally { _busy = false; Refresh(); }
    }
    private void OnLaptopBrightnessChanged()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(Refresh);
    }
    private void OnInternetConnectivityChanged()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(Refresh);
    }
    private void OnLaptopAudioChanged()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(Refresh);
    }
    private void OnClipboardChanged()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(Refresh);
    }
    private void OnClipboardNotice(string notice)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => { _commandNotice = notice; Refresh(); });
    }
    private void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Previous.Refresh(); PlayPause.Refresh(); Next.Refresh(); ToggleDndRule.Refresh(); ToggleClipboard.Refresh();
        OpenPhone.Refresh(); TogglePhoneAdaptive.Refresh();
        ToggleLaptopAdaptive.Refresh(); MoveHeadphones.Refresh(); UsePhoneInternet.Refresh();
        ToggleLaptopAudio.Refresh();
    }
    public void Dispose()
    {
        _disposed = true;
        _brightnessDebounce?.Cancel(); _brightnessDebounce?.Dispose(); _brightnessDebounce = null;
        _manager.StateChanged -= OnStateChanged;
        if (_laptopBrightness is not null) _laptopBrightness.Changed -= OnLaptopBrightnessChanged;
        if (_internetConnectivity is not null) _internetConnectivity.Changed -= OnInternetConnectivityChanged;
        if (_laptopAudio is not null) _laptopAudio.Changed -= OnLaptopAudioChanged;
        if (_clipboard is not null)
        {
            _clipboard.Changed -= OnClipboardChanged;
            _clipboard.Notice -= OnClipboardNotice;
        }
    }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
