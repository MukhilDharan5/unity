using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Transports;

// One owner for a connection attempt, its receiver, sends, and session release.
// Factories and the live platform adapter supply an already authenticated session.
public abstract class SessionPhoneTransport : IPhoneTransport
{
    private readonly IPhoneSessionFactory? _factory;
    private readonly TrustedPhone? _phone;
    private readonly int _maxFrameBytes;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IAuthenticatedPhoneSession? _session;
    private Task? _start, _receiver, _release, _stop, _dispose;
    private TaskCompletionSource? _sendsDrained;
    private int _activeSends;
    private bool _stopping;
    private volatile ConnectionState _state;

    protected SessionPhoneTransport(int maxFrameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        _maxFrameBytes = maxFrameBytes;
    }
    protected SessionPhoneTransport(IPhoneSessionFactory factory, TrustedPhone phone, int maxFrameBytes)
        : this(maxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(phone);
        _factory = factory; _phone = phone;
    }
    public abstract TransportKind Kind { get; }
    public ConnectionState State => _state;
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;
    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<Exception>? Faulted;

    protected virtual async Task<IAuthenticatedPhoneSession> ConnectSessionAsync(CancellationToken token)
    {
        var session = await _factory!.ConnectAsync(_phone!, _maxFrameBytes, token).ConfigureAwait(false);
        if (string.Equals(session.VerifiedIdentity, _phone!.Identity, StringComparison.Ordinal)) return session;
        await session.DisposeAsync().ConfigureAwait(false);
        throw new IOException("The connected peer is not the enrolled phone.");
    }
    protected virtual void DisposeOwnedResources() { }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            if (_start is not null || _stopping)
                throw new InvalidOperationException("Create a new transport for each connection attempt.");
            completion = NewCompletion(); _start = completion.Task;
        }
        _ = StartCoreAsync(cancellationToken, completion);
        return completion.Task;
    }
    private async Task StartCoreAsync(CancellationToken token, TaskCompletionSource completion)
    {
        IAuthenticatedPhoneSession? pending = null;
        Exception? failure = null;
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            lock (_sync)
            {
                connect.Token.ThrowIfCancellationRequested();
                if (_stopping) throw new OperationCanceledException(connect.Token);
                SetState(ConnectionState.Connecting);
            }
            pending = await ConnectSessionAsync(connect.Token).ConfigureAwait(false);
            lock (_sync)
            {
                connect.Token.ThrowIfCancellationRequested();
                if (_stopping) throw new OperationCanceledException(connect.Token);
                _session = pending;
                var session = pending;
                pending = null; // Release now owns it, including callback failures.
                SetState(ConnectionState.Connected);
                _receiver = Task.Run(() => ReceiveAsync(session, _lifetime.Token));
            }
        }
        catch (Exception error)
        {
            failure = error;
            try { if (pending is not null) await pending.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failure = new AggregateException(failure, cleanup); }
            try { await EndSessionAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failure = new AggregateException(failure, cleanup); }
        }
        Complete(completion, failure);
    }
    private async Task ReceiveAsync(IAuthenticatedPhoneSession session, CancellationToken token)
    {
        Exception? failure = null;
        try
        {
            await foreach (var frame in session.ReadFramesAsync(token).WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (frame.IsEmpty || frame.Length > _maxFrameBytes)
                    throw new IOException("The incoming message exceeds the frame limits.");
                lock (_sync)
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(_session, session)) break;
                    // The session may reuse its buffer after this callback.
                    FrameReceived?.Invoke(frame.ToArray());
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            try { await EndSessionAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        }
        if (failure is not null) Faulted?.Invoke(failure);
    }
    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (frame.IsEmpty || frame.Length > _maxFrameBytes) throw new ArgumentException("Invalid frame size.", nameof(frame));
        IAuthenticatedPhoneSession session;
        CancellationTokenSource send;
        lock (_sync)
        {
            if (_stopping || _dispose is not null || _state != ConnectionState.Connected || _session is null)
                throw new IOException("Phone is disconnected.");
            session = _session;
            send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_activeSends++ == 0) _sendsDrained = NewCompletion();
        }
        try { await session.SendFrameAsync(frame, send.Token).ConfigureAwait(false); }
        catch
        {
            // Delivery is ambiguous. Close the route; never retry a toggle/skip.
            await EndSessionAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            send.Dispose();
            lock (_sync) { if (--_activeSends == 0) _sendsDrained!.TrySetResult(); }
        }
    }
    private async Task EndSessionAsync()
    {
        try { _lifetime.Cancel(); SetState(ConnectionState.Disconnected); }
        finally { await ReleaseSessionAsync().ConfigureAwait(false); }
    }
    private Task ReleaseSessionAsync()
    {
        IAuthenticatedPhoneSession session;
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_release is not null) return _release;
            if (_session is null) return Task.CompletedTask;
            session = _session; _session = null;
            completion = NewCompletion(); _release = completion.Task;
        }
        _ = ReleaseCoreAsync(session, completion);
        return completion.Task;
    }
    private static async Task ReleaseCoreAsync(IAuthenticatedPhoneSession session, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        Complete(completion, failure);
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stop;
        TaskCompletionSource? completion = null;
        lock (_sync)
        {
            if (_stop is null)
            {
                _stopping = true;
                completion = NewCompletion(); _stop = completion.Task;
            }
            stop = _stop;
        }
        if (completion is not null) _ = StopCoreAsync(completion);
        // Canceling this wait never cancels ownership cleanup.
        return cancellationToken.CanBeCanceled ? stop.WaitAsync(cancellationToken) : stop;
    }
    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            try { await EndSessionAsync().ConfigureAwait(false); }
            finally
            {
                Task? start;
                lock (_sync) { start = _start; }
                // The initiating caller observes connect errors; stop still drains ownership.
                if (start is not null) try { await start.ConfigureAwait(false); } catch (Exception) { }
                // A late factory return is disposed by StartCore before its completion.
                try { await ReleaseSessionAsync().ConfigureAwait(false); }
                finally
                {
                    Task? receiver, sends;
                    lock (_sync) { receiver = _receiver; sends = _sendsDrained?.Task; }
                    try { if (receiver is not null) await receiver.ConfigureAwait(false); }
                    finally { if (sends is not null) await sends.ConfigureAwait(false); }
                }
            }
        }
        catch (Exception error) { failure = error; }
        Complete(completion, failure);
    }
    private void SetState(ConnectionState value)
    {
        lock (_sync)
        {
            if (_state == value) return;
            _state = value; ConnectionChanged?.Invoke(value);
        }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_dispose is not null) return new ValueTask(_dispose);
            completion = NewCompletion(); _dispose = completion.Task;
        }
        _ = DisposeCoreAsync(completion);
        return new ValueTask(completion.Task);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            try { await StopAsync().ConfigureAwait(false); }
            finally
            {
                try { DisposeOwnedResources(); }
                finally { _lifetime.Dispose(); }
            }
        }
        catch (Exception error) { failure = error; }
        Complete(completion, failure);
    }
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Complete(TaskCompletionSource completion, Exception? failure)
    {
        if (failure is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
        else if (failure is not null) completion.TrySetException(failure);
        else completion.TrySetResult();
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
