using System.Buffers.Binary;
using System.Net.Sockets;

namespace PhoneCompanion.Core.Transports;

public interface IFrameConnection : IAsyncDisposable
{
    ValueTask<byte[]> ReadAsync(CancellationToken token);
    ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token);
}

// One bounded packet, including the secure record overhead. Never JSON delimiting over TCP.
public sealed class TcpFrameConnection(TcpClient client) : IFrameConnection
{
    public const int MaxWireBytes = 16_384 + 24;
    private readonly NetworkStream _stream = client.GetStream();
    private readonly SemaphoreSlim _writes = new(1);
    public async ValueTask<byte[]> ReadAsync(CancellationToken token)
    {
        var header = new byte[4];
        await _stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is < 1 or > MaxWireBytes) throw new IOException("Invalid packet length.");
        var frame = new byte[size];
        await _stream.ReadExactlyAsync(frame, token).ConfigureAwait(false);
        return frame;
    }
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        if (frame.IsEmpty || frame.Length > MaxWireBytes) throw new IOException("Invalid packet length.");
        await _writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, frame.Length);
            await _stream.WriteAsync(header, token).ConfigureAwait(false);
            await _stream.WriteAsync(frame, token).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
}

public static class BleFraming
{
    public static IEnumerable<byte[]> Fragment(byte[] frame, int mtu)
    {
        if (frame.Length is < 1 or > TcpFrameConnection.MaxWireBytes || mtu < 23) throw new IOException("Invalid BLE frame.");
        var size = Math.Min(mtu, 512) - 4;
        for (int offset = 0, seq = 0; offset < frame.Length; seq = (seq + 1) & 63)
        {
            int count = Math.Min(size, frame.Length - offset);
            var part = new byte[count + 1];
            part[0] = (byte)(seq | (offset == 0 ? 128 : 0) | (offset + count == frame.Length ? 64 : 0));
            frame.AsSpan(offset, count).CopyTo(part.AsSpan(1));
            offset += count;
            yield return part;
        }
    }
}

public sealed class BleReassembler
{
    private readonly MemoryStream _buffer = new();
    private int _sequence;
    private bool _active;
    private long _started;
    public byte[]? Accept(ReadOnlySpan<byte> part)
    {
        if (part.Length < 2) throw new IOException("Empty BLE fragment.");
        int header = part[0];
        if ((header & 128) != 0)
        {
            if (_active) throw new IOException("Overlapping BLE frame.");
            _buffer.SetLength(0); _sequence = 0; _active = true; _started = Environment.TickCount64;
        }
        if (!_active || Environment.TickCount64 - _started > 15000 || (header & 63) != _sequence ||
            _buffer.Length + part.Length - 1 > TcpFrameConnection.MaxWireBytes)
        { _active = false; throw new IOException("Invalid BLE fragment sequence."); }
        _buffer.Write(part[1..]); _sequence = (_sequence + 1) & 63;
        if ((header & 64) == 0) return null;
        _active = false;
        return _buffer.ToArray();
    }
}
