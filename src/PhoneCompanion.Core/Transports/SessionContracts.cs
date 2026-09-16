namespace PhoneCompanion.Core.Transports;

// Alternative session factories use the same verified-identity boundary as the packaged live transport.
// A BLE address, device name, IP address, or discovery result is never a trusted identity.
public sealed record TrustedPhone
{
    public string Identity { get; }
    public TrustedPhone(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
    }
}

public interface IAuthenticatedPhoneSession : IAsyncDisposable
{
    // The factory must verify this identity cryptographically before returning a session.
    string VerifiedIdentity { get; }
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync(CancellationToken cancellationToken);
    Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
}

public interface IPhoneSessionFactory
{
    // Must enforce maxFrameBytes while assembling frames, before allocating unbounded buffers.
    // Dispose closes the underlying connection and unblocks any pending reads/writes.
    Task<IAuthenticatedPhoneSession> ConnectAsync(TrustedPhone phone, int maxFrameBytes,
        CancellationToken cancellationToken);
}

// Discovery alone must never create an authenticated session.
public interface IBleSessionFactory : IPhoneSessionFactory;

// LAN endpoints are routing hints; factories remain responsible for authenticated secure sessions.
public interface IWifiSessionFactory : IPhoneSessionFactory;
