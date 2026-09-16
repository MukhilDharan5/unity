using PhoneCompanion.Core.Models;
using PhoneCompanion.Core.Protocol;

namespace PhoneCompanion.Core.Transports;

// Explicit, deterministic sample provider. This is not an Android implementation or emulator.
public sealed class MockPhoneTransport(IPhoneMessageCodec codec) : IPhoneTransport
{
    private static readonly (string Title, string Artist)[] Tracks =
    [ ("A little closer", "The Sunday Hours"), ("Open windows", "Paper Satellites"), ("On the way home", "Quiet Company") ];
    private int _track;
    private bool _playing = true;
    private bool _disposed;
    public TransportKind Kind => TransportKind.Mock;
    public ConnectionState State { get; private set; }
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;
    public event Action<ConnectionState>? ConnectionChanged;
    // The deterministic provider cannot fail spontaneously; the event satisfies the transport contract.
    public event Action<Exception>? Faulted { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        State = ConnectionState.Connected;
        ConnectionChanged?.Invoke(State);
        Emit(new StateSnapshot(new(68, false), CurrentMedia(), new(false, CellularNetwork.FiveG, SignalStrength.Good, true),
            new(false), SoundMode.Vibrate));
        return Task.CompletedTask;
    }
    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected) throw new IOException("Sample phone is disconnected.");
        var decoded = codec.Decode(frame);
        if (decoded.Message is not MediaCommandMessage command) throw new IOException("Unsupported sample command.");
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        if (State != ConnectionState.Connected) throw new IOException("Sample phone is disconnected.");
        switch (command.Command)
        {
            case MediaCommand.PlayPause: _playing = !_playing; break;
            case MediaCommand.NextTrack: _track = (_track + 1) % Tracks.Length; break;
            case MediaCommand.PreviousTrack: _track = (_track + Tracks.Length - 1) % Tracks.Length; break;
        }
        Emit(new MediaUpdate(CurrentMedia()));
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Disconnected;
        ConnectionChanged?.Invoke(State);
        return Task.CompletedTask;
    }
    private MediaState CurrentMedia() => new("Sample music", Tracks[_track].Title, Tracks[_track].Artist,
        _playing, new(true, true, true));
    private void Emit(PhoneMessage message) => FrameReceived?.Invoke(codec.Encode(message));
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _disposed = true; }
}
