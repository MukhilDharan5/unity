using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.Transports;

namespace PhoneCompanion.Core.State;

// Keeps authenticated BLE and Wi-Fi routes alive together. Application messages use
// one deterministic route (Wi-Fi, then BLE) so a command is never executed twice.
public sealed class PhoneStateManager(IPhoneMessageCodec codec) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendOperations = new(1, 1);
    private readonly SemaphoreSlim _lifecycleOperations = new(1, 1);
    private readonly Dictionary<TransportKind, Route> _routes = [];
    private PhoneState _current = PhoneState.Empty;
    private long _nextGeneration;
    private bool _disposed;

    private sealed class Route(IPhoneTransport transport, long generation)
    {
        public IPhoneTransport Transport { get; } = transport;
        public long Generation { get; } = generation;
        public ConnectionState State { get; set; } = ConnectionState.Disconnected;
        public Action? Unsubscribe { get; set; }
        public int DisposeStarted;
    }

    public PhoneState Current { get { lock (_sync) return _current; } }
    public bool HasRoute(TransportKind kind) { lock (_sync) return _routes.ContainsKey(kind); }
    public bool HasConnectedRoute(TransportKind kind)
    {
        lock (_sync) return _routes.TryGetValue(kind, out var route) && route.State == ConnectionState.Connected;
    }

    public event Action<PhoneState>? StateChanged;
    public event Action<DecodeError>? MessageRejected;
    public event Action<Exception>? TransportFaulted;
    public event Action<ClipboardContent>? ClipboardReceived;
    public event Action<MediaCommand>? PcMediaCommandReceived;
    public event Action<AudioSinkReadyMessage>? AudioSinkReadyReceived;
    public event Action<Guid>? AudioStreamStopReceived;
    public event Action? PcLockRequested;

    // Replaces every route. Pairing reset, demo mode, Forget and shutdown use this path.
    public async Task<bool> SetTransportAsync(IPhoneTransport? transport, CancellationToken cancellationToken = default)
    {
        await _lifecycleOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _sendOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                await DetachAllAsync().ConfigureAwait(false);
                return transport is null || await AttachTransportAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            finally { _sendOperations.Release(); }
        }
        finally { _lifecycleOperations.Release(); }
    }

    // Adds the other physical route without interrupting an authenticated route already in use.
    public async Task<bool> AddTransportAsync(IPhoneTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (transport.Kind == TransportKind.BleAndWifi) throw new ArgumentException("A combined route is display state only.");
        await _lifecycleOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool? existingResult = null;
            lock (_sync)
            {
                if (_routes.TryGetValue(transport.Kind, out var existing))
                    existingResult = existing.State == ConnectionState.Connected;
                if (transport.Kind == TransportKind.Mock && _routes.Count > 0 ||
                    transport.Kind != TransportKind.Mock && _routes.ContainsKey(TransportKind.Mock)) existingResult = false;
            }
            if (existingResult is { } result)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                return result;
            }
            return await AttachTransportAsync(transport, cancellationToken).ConfigureAwait(false);
        }
        finally { _lifecycleOperations.Release(); }
    }

    private async Task<bool> AttachTransportAsync(IPhoneTransport transport, CancellationToken cancellationToken)
    {
        Route route;
        lock (_sync)
        {
            if (_routes.ContainsKey(transport.Kind)) return false;
            route = new Route(transport, ++_nextGeneration);
            _routes.Add(transport.Kind, route);
        }

        void OnFrame(ReadOnlyMemory<byte> frame) => Receive(route, frame);
        void OnConnection(ConnectionState state) => ConnectionChanged(route, state);
        void OnFault(Exception error)
        {
            lock (_sync) { if (!IsCurrentLocked(route)) return; }
            TransportFaulted?.Invoke(error);
        }
        transport.FrameReceived += OnFrame;
        transport.ConnectionChanged += OnConnection;
        transport.Faulted += OnFault;
        route.Unsubscribe = () =>
        {
            transport.FrameReceived -= OnFrame;
            transport.ConnectionChanged -= OnConnection;
            transport.Faulted -= OnFault;
        };
        ConnectionChanged(route, ConnectionState.Connecting);

        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(ConnectionPolicy.PairingTimeout);
            await transport.StartAsync(connect.Token).ConfigureAwait(false);
            var connected = false;
            lock (_sync)
                connected = IsCurrentLocked(route) && route.State == ConnectionState.Connected;
            if (!connected) await DetachRouteAsync(route).ConfigureAwait(false);
            return connected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DetachRouteAsync(route).ConfigureAwait(false);
            throw;
        }
        catch (Exception error)
        {
            TransportFaulted?.Invoke(error);
            await DetachRouteAsync(route).ConfigureAwait(false);
            return false;
        }
    }

    private void ConnectionChanged(Route route, ConnectionState connection)
    {
        PhoneState? changed;
        var ended = false;
        lock (_sync)
        {
            if (!IsCurrentLocked(route)) return;
            route.State = connection;
            if (connection == ConnectionState.Disconnected)
            {
                RemoveRouteLocked(route);
                ended = true;
            }
            changed = RebuildStateLocked();
        }
        if (changed is not null) StateChanged?.Invoke(changed);
        if (ended) _ = DisposeRouteAsync(route);
    }

    private void Receive(Route route, ReadOnlyMemory<byte> frame)
    {
        lock (_sync)
        {
            if (!IsCurrentLocked(route) || route.State != ConnectionState.Connected ||
                !ReferenceEquals(route, PreferredRouteLocked())) return;
            var decoded = codec.Decode(frame);
            if (!decoded.Success) { MessageRejected?.Invoke(decoded.Error); return; }
            var isDemo = route.Transport.Kind == TransportKind.Mock;
            if (decoded.Message is ClipboardUpdate clipboard)
            {
                if (!isDemo) ClipboardReceived?.Invoke(clipboard.Content);
                return;
            }
            if (decoded.Message is PcMediaCommandMessage pcMediaCommand)
            {
                if (!isDemo) PcMediaCommandReceived?.Invoke(pcMediaCommand.Command);
                return;
            }
            if (decoded.Message is AudioSinkReadyMessage audioReady)
            {
                if (!isDemo) AudioSinkReadyReceived?.Invoke(audioReady);
                return;
            }
            if (decoded.Message is AudioStreamStopCommandMessage audioStop)
            {
                if (!isDemo) AudioStreamStopReceived?.Invoke(audioStop.StreamId);
                return;
            }
            if (decoded.Message is PcLockRequestMessage)
            {
                if (!isDemo) PcLockRequested?.Invoke();
                return;
            }
            var next = decoded.Message switch
            {
                BatteryUpdate message => _current with { Battery = message.State },
                MediaUpdate message => _current with { Media = message.State },
                CellularUpdate message => _current with { Cellular = message.State },
                DndUpdate message => _current with { Dnd = message.State },
                SoundModeUpdate message => _current with { Sound = message.State },
                StateSnapshot message => _current with
                {
                    Battery = message.Battery, Media = message.Media, Cellular = message.Cellular,
                    Dnd = message.Dnd, Sound = message.Sound, Brightness = message.Brightness,
                    AudioOutput = message.AudioOutput
                },
                _ => _current
            };
            if (next == _current) return;
            _current = next;
            StateChanged?.Invoke(next);
        }
    }

    public async Task<CommandResult> SendCommandAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        if (!await _sendOperations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || !Supports(_current.Media, command))
                    return CommandResult.Unavailable;
                transport = PreferredRouteLocked()?.Transport;
            }
            if (transport is null) return CommandResult.Unavailable;
            return await SendMessageAsync(transport, new MediaCommandMessage(command), cancellationToken).ConfigureAwait(false);
        }
        finally { _sendOperations.Release(); }
    }

    public async Task<CommandResult> SetCompanionDndRuleAsync(bool active, CancellationToken cancellationToken = default)
    {
        if (!await _sendOperations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                var dnd = _current.Dnd;
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.IsDemo ||
                    dnd?.CanControlCompanionRule != true || dnd.CompanionRuleActive is null || dnd.CompanionRuleActive == active)
                    return CommandResult.Unavailable;
                transport = PreferredRouteLocked()?.Transport;
            }
            return transport is null ? CommandResult.Unavailable :
                await SendMessageAsync(transport, new DndRuleCommandMessage(active), cancellationToken).ConfigureAwait(false);
        }
        finally { _sendOperations.Release(); }
    }

    public async Task<CommandResult> SendClipboardAsync(ClipboardContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return await SendRealConnectedMessageAsync(new ClipboardUpdate(content), cancellationToken).ConfigureAwait(false);
    }

    public Task<CommandResult> SendPcMediaAsync(MediaState? state, CancellationToken cancellationToken = default) =>
        SendRealConnectedMessageAsync(new PcMediaUpdate(state), cancellationToken);

    public async Task<CommandResult> SetPhoneBrightnessAsync(int? level, bool? adaptive,
        CancellationToken cancellationToken = default)
    {
        if (level is not null && level is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(level));
        if (level is null && adaptive is null) throw new ArgumentException("A brightness value or adaptive state is required.");
        if (!await _sendOperations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.IsDemo ||
                    _current.Brightness?.CanControl != true) return CommandResult.Unavailable;
                transport = PreferredRouteLocked()?.Transport;
            }
            return transport is null ? CommandResult.Unavailable :
                await SendMessageAsync(transport, new PhoneBrightnessCommandMessage(level, adaptive), cancellationToken)
                    .ConfigureAwait(false);
        }
        finally { _sendOperations.Release(); }
    }

    public async Task<CommandResult> RequestHeadphoneHandoffAsync(CancellationToken cancellationToken = default)
    {
        if (!await _sendOperations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.IsDemo ||
                    _current.AudioOutput is null) return CommandResult.Unavailable;
                transport = PreferredRouteLocked()?.Transport;
            }
            return transport is null ? CommandResult.Unavailable :
                await SendMessageAsync(transport, new HeadphoneHandoffCommandMessage(), cancellationToken).ConfigureAwait(false);
        }
        finally { _sendOperations.Release(); }
    }

    public Task<CommandResult> RequestHotspotAsync(CancellationToken cancellationToken = default) =>
        SendRealConnectedMessageAsync(new HotspotRequestCommandMessage(), cancellationToken);

    public Task<CommandResult> StartAudioStreamAsync(AudioStreamStartCommandMessage request,
        CancellationToken cancellationToken = default) => SendRealConnectedMessageAsync(request, cancellationToken);

    public Task<CommandResult> StopAudioStreamAsync(Guid streamId,
        CancellationToken cancellationToken = default) =>
        SendRealConnectedMessageAsync(new AudioStreamStopCommandMessage(streamId), cancellationToken);

    public Task<CommandResult> RequestPhoneLockAsync(CancellationToken cancellationToken = default) =>
        SendRealConnectedMessageAsync(new PhoneLockRequestCommandMessage(), cancellationToken);

    private async Task<CommandResult> SendRealConnectedMessageAsync(PhoneMessage message,
        CancellationToken cancellationToken)
    {
        if (!await _sendOperations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.IsDemo)
                    return CommandResult.Unavailable;
                transport = PreferredRouteLocked()?.Transport;
            }
            return transport is null ? CommandResult.Unavailable :
                await SendMessageAsync(transport, message, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendOperations.Release(); }
    }

    private async Task<CommandResult> SendMessageAsync(IPhoneTransport transport, PhoneMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            send.CancelAfter(ConnectionPolicy.CommandTimeout);
            await transport.SendAsync(codec.Encode(message), send.Token).ConfigureAwait(false);
            return CommandResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { TransportFaulted?.Invoke(error); return CommandResult.Failed; }
    }

    private Route? PreferredRouteLocked()
    {
        if (_routes.TryGetValue(TransportKind.Wifi, out var wifi) && wifi.State == ConnectionState.Connected) return wifi;
        if (_routes.TryGetValue(TransportKind.Ble, out var ble) && ble.State == ConnectionState.Connected) return ble;
        if (_routes.TryGetValue(TransportKind.Mock, out var mock) && mock.State == ConnectionState.Connected) return mock;
        return null;
    }

    private PhoneState? RebuildStateLocked()
    {
        var wifi = _routes.TryGetValue(TransportKind.Wifi, out var wifiRoute) ? wifiRoute.State : ConnectionState.Disconnected;
        var ble = _routes.TryGetValue(TransportKind.Ble, out var bleRoute) ? bleRoute.State : ConnectionState.Disconnected;
        var mock = _routes.TryGetValue(TransportKind.Mock, out var mockRoute) ? mockRoute.State : ConnectionState.Disconnected;
        var connected = wifi == ConnectionState.Connected || ble == ConnectionState.Connected || mock == ConnectionState.Connected;
        var connecting = wifi == ConnectionState.Connecting || ble == ConnectionState.Connecting || mock == ConnectionState.Connecting;
        var kind = mock == ConnectionState.Connected || !connected && mock == ConnectionState.Connecting
            ? TransportKind.Mock
            : wifi == ConnectionState.Connected && ble == ConnectionState.Connected ||
              !connected && wifi == ConnectionState.Connecting && ble == ConnectionState.Connecting
                ? TransportKind.BleAndWifi
                : wifi is ConnectionState.Connected or ConnectionState.Connecting ? TransportKind.Wifi
                : ble is ConnectionState.Connected or ConnectionState.Connecting ? TransportKind.Ble
                : (TransportKind?)null;
        var next = connected
            ? _current with { Connection = ConnectionState.Connected, Transport = kind }
            : connecting ? new PhoneState(ConnectionState.Connecting, kind) : PhoneState.Empty;
        if (next == _current) return null;
        _current = next;
        return next;
    }

    private bool IsCurrentLocked(Route route) =>
        _routes.TryGetValue(route.Transport.Kind, out var current) && ReferenceEquals(current, route) &&
        current.Generation == route.Generation;

    private void RemoveRouteLocked(Route route)
    {
        if (!IsCurrentLocked(route)) return;
        _routes.Remove(route.Transport.Kind);
        route.Unsubscribe?.Invoke();
        route.Unsubscribe = null;
    }

    private async Task DetachRouteAsync(Route route)
    {
        PhoneState? changed;
        lock (_sync)
        {
            RemoveRouteLocked(route);
            changed = RebuildStateLocked();
        }
        if (changed is not null) StateChanged?.Invoke(changed);
        await DisposeRouteAsync(route).ConfigureAwait(false);
    }

    private async Task DetachAllAsync()
    {
        Route[] routes;
        PhoneState? changed;
        lock (_sync)
        {
            routes = [.. _routes.Values];
            foreach (var route in routes)
            {
                route.Unsubscribe?.Invoke();
                route.Unsubscribe = null;
            }
            _routes.Clear();
            changed = RebuildStateLocked();
        }
        if (changed is not null) StateChanged?.Invoke(changed);
        foreach (var route in routes) await DisposeRouteAsync(route).ConfigureAwait(false);
    }

    private async Task DisposeRouteAsync(Route route)
    {
        if (Interlocked.Exchange(ref route.DisposeStarted, 1) != 0) return;
        try { await route.Transport.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { TransportFaulted?.Invoke(error); }
    }

    private static bool Supports(MediaState? media, MediaCommand command) => command switch
    {
        MediaCommand.PlayPause => media?.Capabilities.PlayPause == true,
        MediaCommand.NextTrack => media?.Capabilities.NextTrack == true,
        MediaCommand.PreviousTrack => media?.Capabilities.PreviousTrack == true,
        _ => false
    };

    public async ValueTask DisposeAsync()
    {
        await _lifecycleOperations.WaitAsync().ConfigureAwait(false);
        await _sendOperations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await DetachAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendOperations.Release();
            _lifecycleOperations.Release();
        }
    }
}
