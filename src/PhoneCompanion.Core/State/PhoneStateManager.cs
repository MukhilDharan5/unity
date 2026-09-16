using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.Transports;

namespace PhoneCompanion.Core.State;

// Owns one active route. BLE/Wi-Fi selection is a host concern, not a UI/message concern.
// Switching routes clears old state and fences late callbacks from the previous session.
public sealed class PhoneStateManager(IPhoneMessageCodec codec) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private IPhoneTransport? _transport;
    private PhoneState _current = PhoneState.Empty;
    private long _generation;
    private Action? _unsubscribe;
    private bool _disposed;
    public PhoneState Current { get { lock (_sync) return _current; } }
    public event Action<PhoneState>? StateChanged;
    public event Action<DecodeError>? MessageRejected;
    public event Action<Exception>? TransportFaulted;
    public event Action<ClipboardContent>? ClipboardReceived;

    public async Task<bool> SetTransportAsync(IPhoneTransport? transport, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await DetachAsync().ConfigureAwait(false);
            if (transport is null) return true;
            long generation;
            lock (_sync) { _transport = transport; generation = ++_generation; }
            void OnFrame(ReadOnlyMemory<byte> frame) => Receive(generation, frame);
            void OnConnection(ConnectionState state) => ConnectionChanged(generation, transport.Kind, state);
            void OnFault(Exception e) { lock (_sync) { if (generation == _generation) TransportFaulted?.Invoke(e); } }
            transport.FrameReceived += OnFrame;
            transport.ConnectionChanged += OnConnection;
            transport.Faulted += OnFault;
            _unsubscribe = () =>
            {
                transport.FrameReceived -= OnFrame;
                transport.ConnectionChanged -= OnConnection;
                transport.Faulted -= OnFault;
            };
            ConnectionChanged(generation, transport.Kind, ConnectionState.Connecting);
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // First pairing waits for the user to compare and confirm the code on both devices.
                connect.CancelAfter(TimeSpan.FromMinutes(2));
                await transport.StartAsync(connect.Token).ConfigureAwait(false);
                return transport.State == ConnectionState.Connected;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { await DetachAsync().ConfigureAwait(false); throw; }
            catch (Exception e)
            {
                TransportFaulted?.Invoke(e);
                await DetachAsync().ConfigureAwait(false);
                return false;
            }
        }
        finally { _operations.Release(); }
    }
    private void ConnectionChanged(long generation, TransportKind kind, ConnectionState connection)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            // Never display a stale battery, track or phone setting after losing the session.
            _current = connection == ConnectionState.Connected
                ? _current with { Connection = connection, Transport = kind }
                : new PhoneState(connection, kind);
            StateChanged?.Invoke(_current);
        }
    }
    private void Receive(long generation, ReadOnlyMemory<byte> frame)
    {
        lock (_sync)
        {
            if (generation != _generation || _current.Connection != ConnectionState.Connected) return;
            var decoded = codec.Decode(frame);
            if (!decoded.Success) { MessageRejected?.Invoke(decoded.Error); return; }
            if (decoded.Message is ClipboardUpdate clipboard)
            {
                // Sample mode is never trusted for clipboard access.
                if (_current.Transport != TransportKind.Mock) ClipboardReceived?.Invoke(clipboard.Content);
                return;
            }
            var next = decoded.Message switch
            {
                BatteryUpdate m => _current with { Battery = m.State },
                MediaUpdate m => _current with { Media = m.State },
                CellularUpdate m => _current with { Cellular = m.State },
                DndUpdate m => _current with { Dnd = m.State },
                SoundModeUpdate m => _current with { Sound = m.State },
                StateSnapshot m => _current with { Battery = m.Battery, Media = m.Media,
                    Cellular = m.Cellular, Dnd = m.Dnd, Sound = m.Sound },
                _ => _current // Incoming commands never mutate state or execute Windows actions.
            };
            if (next == _current) return;
            _current = next;
            StateChanged?.Invoke(next);
        }
    }
    public async Task<CommandResult> SendCommandAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        // Drop repeated clicks instead of queuing toggles/skips behind a slow connection.
        if (!await _operations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || !Supports(_current.Media, command))
                    return CommandResult.Unavailable;
                transport = _transport;
            }
            if (transport is null) return CommandResult.Unavailable;
            using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            send.CancelAfter(TimeSpan.FromSeconds(5));
            await transport.SendAsync(codec.Encode(new MediaCommandMessage(command)), send.Token).ConfigureAwait(false);
            // A successful write is not an acknowledgment; wait for an incoming media state.
            return CommandResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) { TransportFaulted?.Invoke(e); return CommandResult.Failed; }
        finally { _operations.Release(); }
    }
    public async Task<CommandResult> SetCompanionDndRuleAsync(bool active, CancellationToken cancellationToken = default)
    {
        if (!await _operations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                var dnd = _current.Dnd;
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.Transport == TransportKind.Mock ||
                    dnd?.CanControlCompanionRule != true || dnd.CompanionRuleActive is null || dnd.CompanionRuleActive == active)
                    return CommandResult.Unavailable;
                transport = _transport;
            }
            if (transport is null) return CommandResult.Unavailable;
            return await SendMessageAsync(transport, new DndRuleCommandMessage(active), cancellationToken).ConfigureAwait(false);
        }
        finally { _operations.Release(); }
    }
    public async Task<CommandResult> SendClipboardAsync(ClipboardContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!await _operations.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return CommandResult.Unavailable;
        try
        {
            IPhoneTransport? transport;
            lock (_sync)
            {
                if (_disposed || _current.Connection != ConnectionState.Connected || _current.Transport == TransportKind.Mock)
                    return CommandResult.Unavailable;
                transport = _transport;
            }
            if (transport is null) return CommandResult.Unavailable;
            return await SendMessageAsync(transport, new ClipboardUpdate(content), cancellationToken).ConfigureAwait(false);
        }
        finally { _operations.Release(); }
    }
    private async Task<CommandResult> SendMessageAsync(IPhoneTransport transport, PhoneMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            send.CancelAfter(TimeSpan.FromSeconds(5));
            await transport.SendAsync(codec.Encode(message), send.Token).ConfigureAwait(false);
            // DND and clipboard delivery are also confirmed only by subsequent phone state/protocol behavior.
            return CommandResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) { TransportFaulted?.Invoke(e); return CommandResult.Failed; }
    }
    private static bool Supports(MediaState? media, MediaCommand command) => command switch
    {
        MediaCommand.PlayPause => media?.Capabilities.PlayPause == true,
        MediaCommand.NextTrack => media?.Capabilities.NextTrack == true,
        MediaCommand.PreviousTrack => media?.Capabilities.PreviousTrack == true,
        _ => false
    };
    private async Task DetachAsync()
    {
        IPhoneTransport? previous;
        lock (_sync)
        {
            ++_generation;
            previous = _transport; _transport = null;
            _unsubscribe?.Invoke(); _unsubscribe = null;
            _current = PhoneState.Empty;
            StateChanged?.Invoke(_current);
        }
        if (previous is not null)
        {
            try { await previous.DisposeAsync().ConfigureAwait(false); }
            catch (Exception e) { TransportFaulted?.Invoke(e); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; await DetachAsync().ConfigureAwait(false); } }
        finally { _operations.Release(); }
    }
}
