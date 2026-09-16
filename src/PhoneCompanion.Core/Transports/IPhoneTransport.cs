using PhoneCompanion.Core.Models;

namespace PhoneCompanion.Core.Transports;

// Each instance represents one ordered session with one phone. Frames are complete messages,
// not BLE packets or arbitrary stream chunks. Callers own and dispose the transport.
public interface IPhoneTransport : IAsyncDisposable
{
    TransportKind Kind { get; }
    ConnectionState State { get; }
    event Action<ReadOnlyMemory<byte>>? FrameReceived;
    event Action<ConnectionState>? ConnectionChanged;
    event Action<Exception>? Faulted;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
