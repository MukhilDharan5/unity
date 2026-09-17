using System.Runtime.CompilerServices;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;
using PhoneCompanion.Core.State;
using PhoneCompanion.Core.Transports;

if (args.Length == 2 && args[0] == "--interop-client")
{
    try
    {
        var separator = args[1].LastIndexOf(':');
        if (separator < 1 || !int.TryParse(args[1][(separator + 1)..], out var port))
            throw new ArgumentException("Expected HOST:PORT.");
        var host = args[1][..separator];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var socket = new TcpClient();
        await socket.ConnectAsync(host, port, timeout.Token);
        await using var wire = new TcpFrameConnection(socket);
        using var identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var session = await SecurePhoneSession.ConnectAsync(wire, identity, "Windows interop test", true,
            (_, _) => Task.FromResult(true), timeout.Token);
        var probe = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "interop_probe", code = session.Peer.Code });
        await session.SendFrameAsync(probe, timeout.Token);
        await using var frames = session.ReadFramesAsync(timeout.Token).GetAsyncEnumerator();
        if (!await frames.MoveNextAsync()) throw new IOException("Android did not return the interop reply.");
        using var reply = JsonDocument.Parse(frames.Current);
        if (reply.RootElement.GetProperty("type").GetString() != "interop_reply" ||
            reply.RootElement.GetProperty("code").GetString() != session.Peer.Code)
            throw new CryptographicException("Cross-runtime pairing code or encrypted reply differed.");
        Console.WriteLine($"PASS .NET <-> Kotlin secure session (SAS {session.Peer.Code})");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"FAIL .NET <-> Kotlin secure session\n{error}");
        return 1;
    }
}

var codec = new JsonPhoneMessageCodec();
var clipboardUpdateId = Guid.Parse("c29bd0cb-8476-4202-a328-fdb78d906d5a");
var failures = 0;
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
DecodeResult Decode(string json) => codec.Decode(Encoding.UTF8.GetBytes(json));
MediaState Media(bool playPause = true, bool next = true, bool previous = true) => new("Music", "Title", "Artist", true, new(playPause, next, previous));
async Task Eventually(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!condition()) await Task.Delay(10, timeout.Token);
}

Test("Protocol preserves Unicode metadata and every v1 message", () =>
{
    PhoneMessage[] messages = [new BatteryUpdate(new(68, false)), new DndUpdate(new(true)),
        new DndUpdate(new(true, false, true)), new DndRuleCommandMessage(true),
        new ClipboardUpdate(new(clipboardUpdateId, "Two lines\nwith Unicode 🎵")),
        new SoundModeUpdate(SoundMode.Vibrate), new CellularUpdate(new(false, CellularNetwork.FiveG, SignalStrength.Good)),
        new CellularUpdate(new(false, CellularNetwork.FiveG, SignalStrength.Excellent, true)),
        new CellularUpdate(new(true, CellularNetwork.FourG, SignalStrength.Fair, false)),
        new MediaUpdate(new("संगीत", "夜の光 🎵", "Björk", false, new(true, false, true))), new MediaUpdate(null),
        new StateSnapshot(new(100, true), Media(), new(true, CellularNetwork.FourG, SignalStrength.Fair), new(false), SoundMode.Silent),
        new StateSnapshot(null, null, null, null, null), new MediaCommandMessage(MediaCommand.PlayPause),
        new MediaCommandMessage(MediaCommand.NextTrack), new MediaCommandMessage(MediaCommand.PreviousTrack)];
    foreach (var m in messages) Check(codec.Decode(codec.Encode(m)).Message == m, m.ToString());
    return Task.CompletedTask;
});
Test("Rejects invalid battery, booleans, enums and absent capabilities", () =>
{
    string[] bad = ["{\"version\":1,\"type\":\"battery\",\"level\":101,\"charging\":false}",
        "{\"version\":1,\"type\":\"battery\",\"level\":-1,\"charging\":false}",
        "{\"version\":1,\"type\":\"battery\",\"level\":68.5,\"charging\":false}",
        "{\"version\":1,\"type\":\"battery\",\"level\":68,\"charging\":\"false\"}",
        "{\"version\":1,\"type\":\"cellular\",\"isUsingCellularData\":true,\"isUsingWifi\":true,\"network\":\"5g\",\"signal\":\"good\"}",
        "{\"version\":1,\"type\":\"cellular\",\"isUsingCellularData\":false,\"isUsingWifi\":\"yes\",\"network\":\"5g\",\"signal\":\"good\"}",
        "{\"version\":1,\"type\":\"dnd\",\"enabled\":true,\"canControlCompanionRule\":true}",
        "{\"version\":1,\"type\":\"clipboard\",\"updateId\":\"not-a-guid\",\"text\":\"hello\"}",
        "{\"version\":1,\"type\":\"clipboard\",\"updateId\":\"c29bd0cb-8476-4202-a328-fdb78d906d5a\",\"text\":\"\"}",
        "{\"version\":1,\"type\":\"dnd\"}", "{\"version\":1,\"type\":\"sound_mode\",\"mode\":\"loud\"}",
        "{\"version\":1,\"type\":\"media\",\"state\":{\"title\":\"Title\",\"isPlaying\":true}}",
        "{\"version\":1,\"type\":\"snapshot\",\"battery\":null}"];
    foreach (var json in bad) Check(!Decode(json).Success, json);
    return Task.CompletedTask;
});
Test("Version, unknown types, duplicate fields, limits and optional fields", () =>
{
    Check(Decode("{\"version\":2,\"type\":\"dnd\",\"enabled\":true}").Error == DecodeError.UnsupportedVersion);
    Check(Decode("{\"version\":1,\"type\":\"future\"}").Error == DecodeError.UnknownType);
    Check(!Decode("{\"version\":1,\"version\":2,\"type\":\"dnd\",\"enabled\":true}").Success);
    Check(!Decode("{\"version\":1,\"type\":\"media\",\"state\":{\"isPlaying\":true,\"capabilities\":{\"playPause\":true,\"playPause\":false,\"nextTrack\":true,\"previousTrack\":true}}}").Success);
    Check(codec.Decode(new byte[codec.MaxFrameBytes + 1]).Error == DecodeError.TooLarge);
    Check(codec.Decode(Array.Empty<byte>()).Error == DecodeError.Empty);
    Check(Decode("{\"version\":1,\"type\":\"battery\",\"level\":0,\"charging\":true,\"future\":42}").Success);
    Check(!Decode("{\"version\":1,\"type\":\"media\",\"state\":{\"title\":\"bad\\ntext\",\"isPlaying\":true,\"capabilities\":{\"playPause\":true,\"nextTrack\":true,\"previousTrack\":true}}}").Success);
    var legacy = Decode("{\"version\":1,\"type\":\"cellular\",\"isUsingCellularData\":false,\"network\":\"4g\",\"signal\":\"fair\"}");
    Check(legacy.Message is CellularUpdate { State.IsUsingWifi: null }, "Legacy cellular update should keep Wi-Fi unknown.");
    var legacyDnd = Decode("{\"version\":1,\"type\":\"dnd\",\"enabled\":true}");
    Check(legacyDnd.Message == new DndUpdate(new(true)), "Legacy DND should leave companion rule unavailable.");
    var oversizedClipboard = new ClipboardUpdate(new(clipboardUpdateId, new string('x', JsonPhoneMessageCodec.MaxClipboardTextBytes + 1)));
    try { codec.Encode(oversizedClipboard); throw new Exception("Expected oversized clipboard rejection."); }
    catch (ArgumentException) { }
    return Task.CompletedTask;
});
Test("Malformed and arbitrary frames never crash decoder", () =>
{
    foreach (var json in new[] { "null", "[]", "true", "1", "{}", "{", "\"hi\"", new string('[', 30) + new string(']', 30) }) Check(!Decode(json).Success);
    var random = new Random(9182);
    for (var i = 0; i < 2000; i++) { var bytes = new byte[random.Next(1, 2048)]; random.NextBytes(bytes); codec.Decode(bytes); }
    return Task.CompletedTask;
});
Test("Shared v1 fixtures agree on application acceptance and round-trip semantics", () =>
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "protocol-v1.json")));
    var fixtures = document.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();
    Check(fixtures.Length >= 25, "Shared fixture source is unexpectedly empty.");
    foreach (var fixture in fixtures)
    {
        var name = fixture.GetProperty("name").GetString();
        var expected = fixture.GetProperty("valid").GetBoolean();
        var result = Decode(fixture.GetProperty("payload").GetString()!);
        Check(result.Success == expected, $"Shared fixture {name}: expected valid={expected}, got {result.Error}.");
        if (expected)
        {
            var roundTrip = codec.Decode(codec.Encode(result.Message!));
            Check(roundTrip.Success && roundTrip.Message == result.Message, $"Shared fixture {name}: typed round-trip differed.");
        }
    }
    Console.WriteLine($"Verified {fixtures.Length} shared v1 fixtures.");
    return Task.CompletedTask;
});
Test("Disconnected startup never invents phone values or accepts commands", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    Check(manager.Current == PhoneState.Empty);
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Unavailable);
});
Test("Mock media controls exercise the encoded inbound/outbound pipeline", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    Check(await manager.SetTransportAsync(new MockPhoneTransport(codec)));
    Check(manager.Current.IsDemo && manager.Current.Battery?.Level == 68);
    var first = manager.Current.Media;
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Sent);
    Check(manager.Current.Media?.IsPlaying == false);
    await manager.SendCommandAsync(MediaCommand.NextTrack);
    Check(manager.Current.Media?.Title != first?.Title);
    await manager.SendCommandAsync(MediaCommand.PreviousTrack);
    Check(manager.Current.Media?.Title == first?.Title);
    await manager.SetTransportAsync(null);
    Check(manager.Current == PhoneState.Empty);
});
Test("Partial updates retain other values; snapshot clears unavailable values", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new MediaUpdate(Media())));
    transport.Emit(codec.Encode(new BatteryUpdate(new(10, true))));
    Check(manager.Current.Media?.Title == "Title" && manager.Current.Battery?.Charging == true);
    transport.Emit(codec.Encode(new StateSnapshot(null, null, null, new(true), null)));
    Check(manager.Current.Battery is null && manager.Current.Media is null && manager.Current.Dnd?.Enabled == true);
});
Test("Invalid or reverse-direction messages cannot change UI state", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new BatteryUpdate(new(42, false))));
    var prior = manager.Current;
    var rejected = 0;
    manager.MessageRejected += _ => rejected++;
    transport.Emit(Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"battery\",\"level\":200,\"charging\":false}"));
    transport.Emit(codec.Encode(new MediaCommandMessage(MediaCommand.PlayPause)));
    transport.Emit(codec.Encode(new DndRuleCommandMessage(true)));
    Check(manager.Current == prior && rejected == 1);
});
Test("Companion DND commands are explicit and do not overwrite effective DND", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new DndUpdate(new(true, false, true))));
    Check(await manager.SetCompanionDndRuleAsync(true) == CommandResult.Sent);
    Check(codec.Decode(transport.Sent.Single()).Message == new DndRuleCommandMessage(true));
    Check(manager.Current.Dnd == new DndState(true, false, true), "No optimistic DND mutation expected.");
    transport.Emit(codec.Encode(new DndUpdate(new(true, true, true))));
    Check(await manager.SetCompanionDndRuleAsync(false) == CommandResult.Sent);
    transport.Emit(codec.Encode(new DndUpdate(new(true, false, true))));
    Check(manager.Current.Dnd == new DndState(true, false, true), "Effective user/other-rule DND must remain on.");
});
Test("Companion DND requires phone capability and a real transport", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new DndUpdate(new(false))));
    Check(await manager.SetCompanionDndRuleAsync(true) == CommandResult.Unavailable);
    await manager.SetTransportAsync(new MockPhoneTransport(codec));
    Check(await manager.SetCompanionDndRuleAsync(true) == CommandResult.Unavailable);
});
Test("Clipboard transfers only on a real connected route", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    ClipboardContent? received = null;
    manager.ClipboardReceived += content => received = content;
    await manager.SetTransportAsync(new MockPhoneTransport(codec));
    Check(await manager.SendClipboardAsync(new(clipboardUpdateId, "private text")) == CommandResult.Unavailable);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    Check(await manager.SendClipboardAsync(new(clipboardUpdateId, "private text")) == CommandResult.Sent);
    Check(codec.Decode(transport.Sent.Single()).Message == new ClipboardUpdate(new(clipboardUpdateId, "private text")));
    var remote = new ClipboardContent(Guid.NewGuid(), "from phone\nsecond line");
    transport.Emit(codec.Encode(new ClipboardUpdate(remote)));
    Check(received == remote);
});
Test("Capabilities gate each command and writes do not optimistically change media", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new MediaUpdate(Media(next: false, previous: false))));
    var prior = manager.Current;
    Check(await manager.SendCommandAsync(MediaCommand.NextTrack) == CommandResult.Unavailable);
    Check(await manager.SendCommandAsync(MediaCommand.PreviousTrack) == CommandResult.Unavailable);
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Sent);
    Check(transport.Sent.Count == 1 && manager.Current == prior);
    Check(codec.Decode(transport.Sent[0]).Message == new MediaCommandMessage(MediaCommand.PlayPause));
});
Test("Duplicate clicks are dropped while one command is pending", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport { BlockSend = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new MediaUpdate(Media())));
    var first = manager.SendCommandAsync(MediaCommand.PlayPause);
    Check(await manager.SendCommandAsync(MediaCommand.NextTrack) == CommandResult.Unavailable);
    transport.BlockSend.SetResult();
    Check(await first == CommandResult.Sent && transport.Sent.Count == 1);
});
Test("Disconnect clears stale data and late frames are ignored", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var transport = new TestTransport();
    await manager.SetTransportAsync(transport);
    transport.Emit(codec.Encode(new MediaUpdate(Media())));
    transport.Disconnect();
    transport.Emit(codec.Encode(new BatteryUpdate(new(99, true))));
    Check(manager.Current.Connection == ConnectionState.Disconnected && manager.Current.Media is null && manager.Current.Battery is null);
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Unavailable);
});
Test("Route changes dispose old route and fence already-captured callbacks", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var old = new TestTransport();
    await manager.SetTransportAsync(old);
    var late = old.CaptureCallback();
    await manager.SetTransportAsync(new TestTransport(TransportKind.Wifi));
    late?.Invoke(codec.Encode(new BatteryUpdate(new(99, false))));
    Check(old.Disposed && manager.Current.Transport == TransportKind.Wifi && manager.Current.Battery is null);
});
Test("Failed connections return to disconnected; send failures are surfaced once", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    var faults = 0; manager.TransportFaulted += _ => faults++;
    var broken = new TestTransport { FailStart = true };
    Check(!await manager.SetTransportAsync(broken) && broken.Disposed && manager.Current == PhoneState.Empty);
    var active = new TestTransport { FailSend = true };
    await manager.SetTransportAsync(active);
    active.Emit(codec.Encode(new MediaUpdate(Media())));
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Failed);
    Check(active.Sent.Count == 1 && faults == 2);
});
foreach (var kind in new[] { TransportKind.Ble, TransportKind.Wifi })
{
    Test($"{kind}: authenticated session delivers state and commands, then clears on remote close", async () =>
    {
        var session = new TestSession("enrolled-phone");
        var factory = new TestFactory(session);
        await using var manager = new PhoneStateManager(codec);
        IPhoneTransport transport = kind == TransportKind.Ble
            ? new BleTransport(factory, new("enrolled-phone"), codec.MaxFrameBytes)
            : new WifiTransport(factory, new("enrolled-phone"), codec.MaxFrameBytes);
        Check(await manager.SetTransportAsync(transport));
        session.Feed(codec.Encode(new MediaUpdate(Media())));
        await Eventually(() => manager.Current.Media is not null);
        Check(!manager.Current.IsDemo && manager.Current.Transport == kind);
        Check(await manager.SendCommandAsync(MediaCommand.NextTrack) == CommandResult.Sent);
        Check(codec.Decode(session.Sent.Single()).Message == new MediaCommandMessage(MediaCommand.NextTrack));
        session.End();
        await Eventually(() => manager.Current.Connection == ConnectionState.Disconnected && session.Disposed);
        Check(manager.Current.Media is null);
    });
}
Test("Unenrolled identity is rejected before accepting any state", async () =>
{
    var session = new TestSession("stranger");
    session.Feed(codec.Encode(new BatteryUpdate(new(99, true))));
    await using var manager = new PhoneStateManager(codec);
    Check(!await manager.SetTransportAsync(new BleTransport(new TestFactory(session), new("enrolled-phone"), codec.MaxFrameBytes)));
    Check(session.Disposed && manager.Current == PhoneState.Empty && session.Reads == 0);
});
Test("Oversized session frame closes the connection", async () =>
{
    var session = new TestSession("phone");
    await using var manager = new PhoneStateManager(codec);
    await manager.SetTransportAsync(new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes));
    session.Feed(new byte[codec.MaxFrameBytes + 1]);
    await Eventually(() => manager.Current.Connection == ConnectionState.Disconnected && session.Disposed);
    Check(manager.Current.Battery is null);
});
Test("Session send failure disconnects without retrying a media command", async () =>
{
    var session = new TestSession("phone") { FailSend = true };
    await using var manager = new PhoneStateManager(codec);
    await manager.SetTransportAsync(new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes));
    session.Feed(codec.Encode(new MediaUpdate(Media())));
    await Eventually(() => manager.Current.Media is not null);
    Check(await manager.SendCommandAsync(MediaCommand.PlayPause) == CommandResult.Failed);
    Check(session.Sent.Count == 1 && session.Disposed && manager.Current.Media is null);
});
Test("Cancellation during connection disposes the attempt and resets state", async () =>
{
    await using var manager = new PhoneStateManager(codec);
    using var cancellation = new CancellationTokenSource(50);
    try
    {
        await manager.SetTransportAsync(new BleTransport(new WaitingFactory(), new("phone"), codec.MaxFrameBytes), cancellation.Token);
        throw new Exception("Expected cancellation");
    }
    catch (OperationCanceledException) { Check(manager.Current == PhoneState.Empty); }
});
Test("Disposal cancels a pending session read and releases connection exactly once", async () =>
{
    var session = new TestSession("phone");
    var manager = new PhoneStateManager(codec);
    await manager.SetTransportAsync(new BleTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes));
    await manager.DisposeAsync();
    await manager.DisposeAsync();
    Check(session.DisposeCount == 1 && manager.Current == PhoneState.Empty);
});

Test("Stop fences a factory returning after cancellation and rejects reuse", async () =>
{
    var factory = new GatedFactory();
    var session = new TestSession("phone");
    await using var transport = new BleTransport(factory, new("phone"), codec.MaxFrameBytes);
    var states = new List<ConnectionState>(); transport.ConnectionChanged += states.Add;
    var start = transport.StartAsync();
    await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    try { await transport.StartAsync(); throw new Exception("Expected single-use guard"); }
    catch (InvalidOperationException) { }
    var stop = transport.StopAsync();
    Check(factory.Token.IsCancellationRequested && !stop.IsCompleted);
    factory.Result.SetResult(session);
    try { await start; throw new Exception("Expected canceled start"); }
    catch (OperationCanceledException) { }
    await stop.WaitAsync(TimeSpan.FromSeconds(3));
    Check(session.DisposeCount == 1 && session.Reads == 0 && !states.Contains(ConnectionState.Connected));
    try { await transport.StartAsync(); throw new Exception("Expected stopped guard"); }
    catch (InvalidOperationException) { }
});
Test("Canceled stop wait leaves cleanup running; concurrent dispose waits for release", async () =>
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var session = new TestSession("phone") { BlockDispose = gate };
    var transport = new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes);
    await transport.StartAsync();
    using var wait = new CancellationTokenSource();
    var stop = transport.StopAsync(wait.Token);
    await session.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3)); wait.Cancel();
    try { await stop; throw new Exception("Expected canceled wait"); }
    catch (OperationCanceledException) { }
    var first = transport.DisposeAsync().AsTask(); var second = transport.DisposeAsync().AsTask();
    Check(!first.IsCompleted && !second.IsCompleted && session.DisposeCount == 1);
    gate.SetResult();
    await Task.WhenAll(first, second, transport.StopAsync()).WaitAsync(TimeSpan.FromSeconds(3));
    Check(session.DisposeCompleted && session.DisposeCount == 1);
    try { await transport.StartAsync(); throw new Exception("Expected disposed guard"); }
    catch (ObjectDisposedException) { }
});
Test("Remote end cancels an active send and shutdown drains it without retry", async () =>
{
    var session = new TestSession("phone") { BlockSend = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await using var transport = new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes);
    await transport.StartAsync();
    var send = transport.SendAsync(codec.Encode(new MediaCommandMessage(MediaCommand.PlayPause)));
    await session.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.End();
    try { await send.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected canceled send"); }
    catch (OperationCanceledException) { }
    await transport.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
    Check(session.Sent.Count == 1 && session.DisposeCount == 1 && session.DisposeCompleted);
});
Test("Receiver failure releases the session before reporting the fault", async () =>
{
    var session = new TestSession("phone");
    await using var transport = new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes);
    var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
    transport.Faulted += error => fault.TrySetResult(error);
    await transport.StartAsync(); session.End(new IOException("Fictional read failure"));
    await fault.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(transport.State == ConnectionState.Disconnected && session.DisposeCompleted && session.DisposeCount == 1);
});
Test("A late frame from a canceled reader never reaches the consumer", async () =>
{
    var session = new LateFrameSession();
    await using var transport = new WifiTransport(new TestFactory(session), new("phone"), codec.MaxFrameBytes);
    var delivered = 0; var faults = 0;
    transport.FrameReceived += _ => delivered++; transport.Faulted += _ => faults++;
    await transport.StartAsync(); await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var stop = transport.StopAsync(); Check(!stop.IsCompleted);
    session.Continue.SetResult(); await stop.WaitAsync(TimeSpan.FromSeconds(3));
    Check(delivered == 0 && faults == 0 && session.DisposeCount == 1);
});

Test("Secure session proves identities, agrees on a code, and encrypts both directions", async () =>
{
    var (clientWire, serverWire) = MemoryFrameConnection.Pair();
    using var clientIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var serverIdentity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    PeerIdentity? clientSaw = null, serverSaw = null;
    var clientTask = SecurePhoneSession.ConnectAsync(clientWire, clientIdentity, "Laptop", true,
        (peer, _) => { clientSaw = peer; return Task.FromResult(true); }, CancellationToken.None);
    var serverTask = SecurePhoneSession.ConnectAsync(serverWire, serverIdentity, "Phone", false,
        (peer, _) => { serverSaw = peer; return Task.FromResult(true); }, CancellationToken.None);
    await Task.WhenAll(clientTask, serverTask);
    await using var client = await clientTask;
    await using var server = await serverTask;
    Check(clientSaw?.Code == serverSaw?.Code && clientSaw?.Code.Length == 6, "Pairing codes differ.");
    Check(client.VerifiedIdentity == Convert.ToHexString(SHA256.HashData(serverIdentity.ExportSubjectPublicKeyInfo())));
    var payload = Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"dnd\",\"enabled\":true}");
    await client.SendFrameAsync(payload, CancellationToken.None);
    await using var frames = server.ReadFramesAsync(CancellationToken.None).GetAsyncEnumerator();
    Check(await frames.MoveNextAsync() && frames.Current.Span.SequenceEqual(payload), "Encrypted payload did not round-trip.");
});
Test("BLE fragmentation round-trips the largest secure packet and rejects bad ordering", () =>
{
    var packet = new byte[TcpFrameConnection.MaxWireBytes];
    new Random(731).NextBytes(packet);
    var fragments = BleFraming.Fragment(packet, 23).ToArray();
    Check(fragments.Length > 64, "Sequence-wrap coverage was not exercised.");
    var receiver = new BleReassembler();
    byte[]? completed = null;
    foreach (var fragment in fragments) completed = receiver.Accept(fragment);
    Check(completed is not null && completed.SequenceEqual(packet), "BLE reassembly changed the secure packet.");
    receiver = new BleReassembler();
    receiver.Accept(fragments[0]);
    try { receiver.Accept(fragments[2]); throw new Exception("Expected sequence rejection."); }
    catch (IOException) { }
    return Task.CompletedTask;
});

foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}\n{e}"); }
}
Console.WriteLine($"\n{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

sealed class TestTransport(TransportKind kind = TransportKind.Ble) : IPhoneTransport
{
    public TransportKind Kind => kind;
    public ConnectionState State { get; private set; }
    public bool Disposed { get; private set; }
    public bool FailStart { get; init; }
    public bool FailSend { get; init; }
    public TaskCompletionSource? BlockSend { get; init; }
    public List<byte[]> Sent { get; } = [];
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;
    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<Exception>? Faulted { add { } remove { } }
    public void Emit(byte[] frame) => FrameReceived?.Invoke(frame);
    public Action<ReadOnlyMemory<byte>>? CaptureCallback() => FrameReceived;
    public void Disconnect() { State = ConnectionState.Disconnected; ConnectionChanged?.Invoke(State); }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (FailStart) throw new IOException("Test failure");
        State = ConnectionState.Connected; ConnectionChanged?.Invoke(State); return Task.CompletedTask;
    }
    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        Sent.Add(frame.ToArray());
        if (FailSend) throw new IOException("Test failure");
        if (BlockSend is not null) await BlockSend.Task.WaitAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken = default) { Disconnect(); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; Disconnect(); return ValueTask.CompletedTask; }
}
sealed class TestFactory(IAuthenticatedPhoneSession session) : IBleSessionFactory, IWifiSessionFactory
{
    public Task<IAuthenticatedPhoneSession> ConnectAsync(TrustedPhone phone, int maxFrameBytes, CancellationToken cancellationToken)
        => Task.FromResult<IAuthenticatedPhoneSession>(session);
}
sealed class WaitingFactory : IBleSessionFactory
{
    public async Task<IAuthenticatedPhoneSession> ConnectAsync(TrustedPhone phone, int maxFrameBytes, CancellationToken cancellationToken)
    { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
}
sealed class GatedFactory : IBleSessionFactory
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<IAuthenticatedPhoneSession> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken Token { get; private set; }
    public Task<IAuthenticatedPhoneSession> ConnectAsync(TrustedPhone phone, int maxFrameBytes, CancellationToken cancellationToken)
    { Token = cancellationToken; Entered.SetResult(); return Result.Task; }
}
sealed class LateFrameSession : IAuthenticatedPhoneSession
{
    public string VerifiedIdentity => "phone";
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int DisposeCount { get; private set; }
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    { Entered.SetResult(); await Continue.Task; yield return new byte[] { 1 }; }
    public Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
}
sealed class TestSession(string identity) : IAuthenticatedPhoneSession
{
    private readonly Channel<ReadOnlyMemory<byte>> _frames = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
    public string VerifiedIdentity => identity;
    public bool Disposed { get; private set; }
    public int DisposeCount { get; private set; }
    public int Reads { get; private set; }
    public bool FailSend { get; init; }
    public TaskCompletionSource? BlockSend { get; init; }
    public TaskCompletionSource? BlockDispose { get; init; }
    public TaskCompletionSource SendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool DisposeCompleted { get; private set; }
    public List<byte[]> Sent { get; } = [];
    public void Feed(byte[] frame) => _frames.Writer.TryWrite(frame);
    public void End(Exception? error = null) => _frames.Writer.TryComplete(error);
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken)) { Reads++; yield return frame; }
    }
    public async Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        Sent.Add(frame.ToArray()); SendEntered.TrySetResult();
        if (FailSend) throw new IOException("Test send failure");
        if (BlockSend is not null) await BlockSend.Task.WaitAsync(cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        DisposeCount++; Disposed = true; End(); DisposeEntered.TrySetResult();
        if (BlockDispose is not null) await BlockDispose.Task;
        DisposeCompleted = true;
    }
}
sealed class MemoryFrameConnection(Channel<byte[]> incoming, Channel<byte[]> outgoing) : IFrameConnection
{
    private bool _closed;
    public static (MemoryFrameConnection, MemoryFrameConnection) Pair()
    {
        var left = Channel.CreateUnbounded<byte[]>(); var right = Channel.CreateUnbounded<byte[]>();
        return (new(left, right), new(right, left));
    }
    public async ValueTask<byte[]> ReadAsync(CancellationToken token) => await incoming.Reader.ReadAsync(token);
    public ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        if (_closed) throw new IOException("Closed");
        return outgoing.Writer.WriteAsync(frame.ToArray(), token);
    }
    public ValueTask DisposeAsync() { if (!_closed) { _closed = true; outgoing.Writer.TryComplete(); } return ValueTask.CompletedTask; }
}
