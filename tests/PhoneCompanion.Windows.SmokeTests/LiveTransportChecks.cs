using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;
using PhoneCompanion.Windows.Connection;

namespace PhoneCompanion.Windows.SmokeTests;

// Real live transport and handshake; only the BLE/TCP frame connection is replaced.
internal static class LiveTransportChecks
{
    public static async Task RunAsync()
    {
        foreach (var kind in new[] { TransportKind.Ble, TransportKind.Wifi })
        {
            await using var pair = await ConnectedPair.OpenAsync(kind, pinned: kind == TransportKind.Wifi);
            var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Manager.TransportFaulted += error => fault.TrySetResult(error);
            await pair.Server.SendFrameAsync(new JsonPhoneMessageCodec().Encode(new BatteryUpdate(new(68, false))), CancellationToken.None);
            await Eventually(() => pair.Manager.Current.Battery?.Level == 68);
            await pair.Server.DisposeAsync();
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(pair.Manager.Current.Connection == ConnectionState.Disconnected &&
                pair.Manager.Current.Battery is null && pair.ClientWire.DisposeCount == 1,
                $"{kind} live remote close clears state and releases its wire");
            await pair.Live.DisposeAsync();
            Check(pair.ClientWire.DisposeCount == 1, $"{kind} live repeated disposal does not close twice");
        }
        await LateOpenAsync();
        await CancelApprovalAsync();
        await SendFailureAsync(blocked: false);
        await SendFailureAsync(blocked: true);
        Console.WriteLine("PASS 6 live transport lifecycle scenarios");
    }
    private static async Task LateOpenAsync()
    {
        var (wire, other) = TestWire.Pair();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<IFrameConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openToken = default;
        await using var live = new LivePhoneTransport(TransportKind.Wifi,
            token => { openToken = token; entered.SetResult(); return result.Task; },
            ECDsa.Create(ECCurve.NamedCurves.nistP256), null, (_, _) => Task.FromResult(true));
        var connected = 0;
        live.ConnectionChanged += state => { if (state == ConnectionState.Connected) connected++; };
        var start = live.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var dispose = live.DisposeAsync().AsTask();
        Check(openToken.IsCancellationRequested && !dispose.IsCompleted, "Live disposal cancels and waits for pending open");
        result.SetResult(wire);
        await ExpectCanceled(start);
        await dispose.WaitAsync(TimeSpan.FromSeconds(3));
        Check(connected == 0 && live.Peer is null && wire.DisposeCount == 1 && wire.WriteAttempts == 0,
            "Late live open is disposed before any handshake or connected notification");
        await other.DisposeAsync();
    }
    private static async Task CancelApprovalAsync()
    {
        var (wire, other) = TestWire.Pair();
        using var phoneIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var live = new LivePhoneTransport(TransportKind.Ble, _ => Task.FromResult<IFrameConnection>(wire),
            ECDsa.Create(ECCurve.NamedCurves.nistP256), null, async (_, token) =>
            {
                entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return true;
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = SecurePhoneSession.ConnectAsync(other, phoneIdentity, "Fictional phone", false,
            (_, _) => Task.FromResult(true), timeout.Token);
        var start = live.StartAsync(timeout.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var dispose = live.DisposeAsync().AsTask();
        await ExpectCanceled(start);
        await dispose.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await using var unexpected = await server.WaitAsync(TimeSpan.FromSeconds(3));
            throw new Exception("Canceled approval unexpectedly completed pairing");
        }
        catch (ChannelClosedException) { }
        catch (IOException) { }
        Check(wire.DisposeCount == 1 && live.State == ConnectionState.Disconnected && live.Peer is null,
            "Live approval cancellation closes once and cannot publish a peer");
    }
    private static async Task SendFailureAsync(bool blocked)
    {
        await using var pair = await ConnectedPair.OpenAsync(TransportKind.Wifi, pinned: true);
        var before = pair.ClientWire.WriteAttempts;
        pair.ClientWire.FailWrites = !blocked;
        pair.ClientWire.BlockWrites = blocked;
        var send = pair.Live.SendAsync(Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"dnd_rule_command\",\"active\":true}"));
        if (blocked)
        {
            await pair.ClientWire.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await pair.Live.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(send.IsCompleted, "Live stop drains a blocked command write");
            await ExpectCanceled(send);
        }
        else
        {
            try { await send.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected write failure"); }
            catch (IOException) { }
            await pair.Live.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        Check(pair.Live.State == ConnectionState.Disconnected && pair.ClientWire.DisposeCount == 1 &&
            pair.ClientWire.WriteAttempts == before + 1, blocked
                ? "Live shutdown cancels the command without retry or duplicate wire close"
                : "Live send failure closes the route without retry or duplicate wire close");
    }
    private static async Task ExpectCanceled(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
    }
    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private static void Check(bool valid, string description)
    {
        if (!valid) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }

    private sealed class ConnectedPair(LivePhoneTransport live, TestWire wire, SecurePhoneSession server,
        ECDsa phoneIdentity, PhoneStateManager manager) : IAsyncDisposable
    {
        public LivePhoneTransport Live => live;
        public TestWire ClientWire => wire;
        public SecurePhoneSession Server => server;
        public PhoneStateManager Manager => manager;

        public static async Task<ConnectedPair> OpenAsync(TransportKind kind, bool pinned)
        {
            var (wire, other) = TestWire.Pair();
            var phoneIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var fingerprint = Convert.ToHexString(SHA256.HashData(phoneIdentity.ExportSubjectPublicKeyInfo()));
            PeerIdentity? approved = null;
            var live = new LivePhoneTransport(kind, _ => Task.FromResult<IFrameConnection>(wire),
                ECDsa.Create(ECCurve.NamedCurves.nistP256), pinned ? fingerprint : null,
                (peer, _) => { approved = peer; return Task.FromResult(true); });
            var manager = new PhoneStateManager(new JsonPhoneMessageCodec());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            PeerIdentity? phoneSaw = null;
            var serverTask = SecurePhoneSession.ConnectAsync(other, phoneIdentity, "Fictional phone", false,
                (peer, _) => { phoneSaw = peer; return Task.FromResult(true); }, timeout.Token);
            try
            {
                var start = manager.SetTransportAsync(live, timeout.Token);
                await Task.WhenAll(start, serverTask).WaitAsync(timeout.Token);
                var server = await serverTask;
                Check(await start && live.Peer?.Fingerprint == fingerprint &&
                    (pinned ? approved is null : approved?.Code == phoneSaw?.Code),
                    $"{kind} live pairing retains identity and confirmation behavior");
                return new(live, wire, server, phoneIdentity, manager);
            }
            catch
            {
                await manager.DisposeAsync();
                try { await using var session = await serverTask.WaitAsync(timeout.Token); } catch (Exception) { }
                phoneIdentity.Dispose(); throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            await manager.DisposeAsync();
            await server.DisposeAsync(); phoneIdentity.Dispose();
        }
    }

    private sealed class TestWire(Channel<byte[]> incoming, Channel<byte[]> outgoing) : IFrameConnection
    {
        private int _closed, _disposeCount, _writeAttempts;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int WriteAttempts => Volatile.Read(ref _writeAttempts);
        public bool FailWrites { get; set; }
        public bool BlockWrites { get; set; }
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static (TestWire, TestWire) Pair()
        {
            var left = Channel.CreateUnbounded<byte[]>(); var right = Channel.CreateUnbounded<byte[]>();
            return (new(left, right), new(right, left));
        }
        public async ValueTask<byte[]> ReadAsync(CancellationToken token) => await incoming.Reader.ReadAsync(token);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            Interlocked.Increment(ref _writeAttempts);
            if (FailWrites) throw new IOException("Fictional write failure");
            if (BlockWrites) { WriteEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            await outgoing.Writer.WriteAsync(frame.ToArray(), token);
        }
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            { incoming.Writer.TryComplete(); outgoing.Writer.TryComplete(); }
            return ValueTask.CompletedTask;
        }
    }
}
