using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Transports;

namespace PhoneCompanion.Windows.Connection;

public sealed class LivePhoneTransport(
    TransportKind kind,
    Func<CancellationToken, Task<IFrameConnection>> open,
    ECDsa identity,
    string? trustedFingerprint,
    Func<PeerIdentity, CancellationToken, Task<bool>> approve) : IPhoneTransport
{
    private readonly CancellationTokenSource _lifetime = new();
    private SecurePhoneSession? _session;
    private Task? _reader;
    private int _disposed;
    public PeerIdentity? Peer { get; private set; }
    public TransportKind Kind => kind;
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;
    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<Exception>? Faulted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        SetState(ConnectionState.Connecting);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        IFrameConnection? wire = null;
        try
        {
            wire = await open(linked.Token).ConfigureAwait(false);
            var session = await SecurePhoneSession.ConnectAsync(wire, identity, Environment.MachineName, true,
                async (peer, token) =>
                {
                    if (trustedFingerprint is not null)
                        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(trustedFingerprint), Convert.FromHexString(peer.Fingerprint));
                    return await approve(peer, token).ConfigureAwait(false);
                }, linked.Token).ConfigureAwait(false);
            wire = null;
            Peer = session.Peer; _session = session;
            SetState(ConnectionState.Connected);
            _reader = Task.Run(() => ReadAsync(session, _lifetime.Token));
        }
        catch { if (wire is not null) await wire.DisposeAsync(); SetState(ConnectionState.Disconnected); throw; }
    }

    private async Task ReadAsync(SecurePhoneSession session, CancellationToken token)
    {
        try { await foreach (var frame in session.ReadFramesAsync(token)) FrameReceived?.Invoke(frame); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Faulted?.Invoke(error); }
        finally { SetState(ConnectionState.Disconnected); }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null || State != ConnectionState.Connected) throw new IOException("Phone is disconnected.");
        await session.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _lifetime.Cancel();
        var session = Interlocked.Exchange(ref _session, null);
        if (session is not null) await session.DisposeAsync();
        if (_reader is not null) try { await _reader.WaitAsync(cancellationToken); } catch (OperationCanceledException) { }
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state; ConnectionChanged?.Invoke(state);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(); identity.Dispose(); _lifetime.Dispose();
    }
}
