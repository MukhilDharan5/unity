using System;
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
    Func<PeerIdentity, CancellationToken, Task<bool>> approve) : SessionPhoneTransport(16 * 1024)
{
    public PeerIdentity? Peer { get; private set; }
    public override TransportKind Kind => kind;

    protected override async Task<IAuthenticatedPhoneSession> ConnectSessionAsync(CancellationToken token)
    {
        IFrameConnection? wire = await open(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var ownedWire = wire;
            wire = null; // ConnectAsync releases the wire on handshake failure.
            var session = await SecurePhoneSession.ConnectAsync(ownedWire, identity, Environment.MachineName, true,
                async (peer, approvalToken) =>
                {
                    if (trustedFingerprint is not null)
                        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(trustedFingerprint), Convert.FromHexString(peer.Fingerprint));
                    return await approve(peer, approvalToken).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            Peer = session.Peer;
            return session;
        }
        finally { if (wire is not null) await wire.DisposeAsync().ConfigureAwait(false); }
    }

    protected override void DisposeOwnedResources() => identity.Dispose();
}
