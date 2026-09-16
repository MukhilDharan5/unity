using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Transports;

public abstract class SessionPhoneTransport : IPhoneTransport
{
    private readonly IPhoneSessionFactory _factory;
    private readonly TrustedPhone _phone;
    private readonly int _maxFrameBytes;
    private readonly CancellationTokenSource _lifetime = new();
    private IAuthenticatedPhoneSession? _session;
    private Task? _receiver;
    private bool _started;
    private bool _disposed;
    private volatile ConnectionState _state;

    protected SessionPhoneTransport(IPhoneSessionFactory factory, TrustedPhone phone, int maxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        _factory = factory; _phone = phone; _maxFrameBytes = maxFrameBytes;
    }
    public abstract TransportKind Kind { get; }
    public ConnectionState State => _state;
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;
    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<Exception>? Faulted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("Create a new transport for each connection attempt.");
        _started = true;
        SetState(ConnectionState.Connecting);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var session = await _factory.ConnectAsync(_phone, _maxFrameBytes, connect.Token).ConfigureAwait(false);
            _session = session;
            connect.Token.ThrowIfCancellationRequested();
            if (!string.Equals(session.VerifiedIdentity, _phone.Identity, StringComparison.Ordinal))
                throw new IOException("The connected peer is not the enrolled phone.");
            SetState(ConnectionState.Connected);
            _receiver = Task.Run(() => ReceiveAsync(session, _lifetime.Token), CancellationToken.None);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            await ReleaseSessionAsync().ConfigureAwait(false);
            throw;
        }
    }
    private async Task ReceiveAsync(IAuthenticatedPhoneSession session, CancellationToken token)
    {
        try
        {
            await foreach (var frame in session.ReadFramesAsync(token).WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (frame.IsEmpty || frame.Length > _maxFrameBytes)
                    throw new IOException("The incoming message exceeds the frame limits.");
                // Copy so a session may safely reuse its receive buffer after the callback.
                FrameReceived?.Invoke(frame.ToArray());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { Faulted?.Invoke(e); }
        finally
        {
            SetState(ConnectionState.Disconnected);
            try { await ReleaseSessionAsync().ConfigureAwait(false); }
            catch (Exception e) { Faulted?.Invoke(e); }
        }
    }
    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (frame.IsEmpty || frame.Length > _maxFrameBytes) throw new ArgumentException("Invalid frame size.", nameof(frame));
        var session = _session;
        if (State != ConnectionState.Connected || session is null) throw new IOException("Phone is disconnected.");
        using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try { await session.SendFrameAsync(frame, send.Token).ConfigureAwait(false); }
        catch
        {
            // Delivery is ambiguous after a send failure. Never retry a toggle/skip automatically.
            SetState(ConnectionState.Disconnected);
            _lifetime.Cancel();
            await ReleaseSessionAsync().ConfigureAwait(false);
            throw;
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        _lifetime.Cancel();
        SetState(ConnectionState.Disconnected);
        try { await ReleaseSessionAsync().ConfigureAwait(false); }
        finally { if (_receiver is not null) await _receiver.ConfigureAwait(false); }
    }
    private async ValueTask ReleaseSessionAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
    }
    private void SetState(ConnectionState value)
    {
        if (_state == value) return;
        _state = value;
        ConnectionChanged?.Invoke(value);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await StopAsync().ConfigureAwait(false); }
        finally { _disposed = true; _lifetime.Dispose(); }
    }
}

public sealed class BleTransport(IBleSessionFactory factory, TrustedPhone phone, int maxFrameBytes)
    : SessionPhoneTransport(factory, phone, maxFrameBytes)
{
    public override TransportKind Kind => TransportKind.Ble;
}

public sealed class WifiTransport(IWifiSessionFactory factory, TrustedPhone phone, int maxFrameBytes)
    : SessionPhoneTransport(factory, phone, maxFrameBytes)
{
    public override TransportKind Kind => TransportKind.Wifi;
}
