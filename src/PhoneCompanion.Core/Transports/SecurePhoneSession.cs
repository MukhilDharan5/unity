using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhoneCompanion.Core.Transports;

public sealed record PeerIdentity(string PublicKey, string Name, string Code)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(PublicKey)));
}

// The exact cross-platform binding is specified in docs/SECURE-SESSION-v1.md.
// Identities sign the complete committed transcript. Every application packet is authenticated.
public sealed class SecurePhoneSession : IAuthenticatedPhoneSession
{
    private readonly IFrameConnection _wire;
    private readonly AesGcm _tx;
    private readonly AesGcm _rx;
    private readonly byte[] _transcript;
    private readonly SemaphoreSlim _writes = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private ulong _txSequence, _rxSequence;
    private int _disposed;
    public string VerifiedIdentity { get; }
    public PeerIdentity Peer { get; }
    private SecurePhoneSession(IFrameConnection wire, byte[] tx, byte[] rx, byte[] transcript, PeerIdentity peer)
    {
        _wire = wire; _tx = new(tx, 16); _rx = new(rx, 16); _transcript = transcript;
        Peer = peer; VerifiedIdentity = peer.Fingerprint;
        CryptographicOperations.ZeroMemory(tx); CryptographicOperations.ZeroMemory(rx);
    }
    private static byte[] Pack(object value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
    private static string Field(JsonElement root, string name) => root.GetProperty(name).GetString() ?? throw new IOException("Missing handshake field.");
    private static void Expect(JsonElement root, string type)
    {
        if (root.GetProperty("version").GetInt32() != 1 || Field(root, "type") != type) throw new IOException("Incompatible secure session.");
    }
    private static byte[] Combine(params byte[][] arrays) => arrays.SelectMany(x => x).ToArray();
    public static async Task<SecurePhoneSession> ConnectAsync(IFrameConnection wire, ECDsa identity, string name,
        bool client, Func<PeerIdentity, CancellationToken, Task<bool>> approve, CancellationToken token)
    {
        SecurePhoneSession? session = null;
        try
        {
            using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var reveal = Pack(new { version = 1, type = "key", role = client ? "windows" : "android",
                identity = Convert.ToBase64String(identity.ExportSubjectPublicKeyInfo()),
                ephemeral = Convert.ToBase64String(ephemeral.ExportSubjectPublicKeyInfo()),
                nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), name = name[..Math.Min(64, name.Length)] });
            await wire.WriteAsync(Pack(new { version = 1, type = "commit", hash = Convert.ToBase64String(SHA256.HashData(reveal)) }), token);
            using var commitment = Parse(await wire.ReadAsync(token));
            Expect(commitment.RootElement, "commit");
            var committedHash = Convert.FromBase64String(Field(commitment.RootElement, "hash"));
            await wire.WriteAsync(reveal, token);
            var remoteReveal = await wire.ReadAsync(token);
            if (remoteReveal.Length > 2048 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(remoteReveal), committedHash))
                throw new CryptographicException("Pairing commitment failed.");
            using var remote = Parse(remoteReveal);
            var key = remote.RootElement; Expect(key, "key");
            if (Field(key, "role") != (client ? "android" : "windows") || Convert.FromBase64String(Field(key, "nonce")).Length != 32)
                throw new CryptographicException("Invalid peer role or nonce.");
            byte[] first = client ? reveal : remoteReveal, second = client ? remoteReveal : reveal;
            var a = new byte[4]; var b = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(a, first.Length); BinaryPrimitives.WriteInt32BigEndian(b, second.Length);
            var transcript = SHA256.HashData(Combine(Encoding.UTF8.GetBytes("UnityConnect/session/1\n"), a, first, b, second));
            await wire.WriteAsync(Pack(new { version = 1, type = "proof", signature = Convert.ToBase64String(identity.SignData(transcript,
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) }), token);
            using var proof = Parse(await wire.ReadAsync(token)); Expect(proof.RootElement, "proof");
            string remoteIdentity = Field(key, "identity");
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(remoteIdentity), out _);
            if (verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !verifier.VerifyData(transcript, Convert.FromBase64String(Field(proof.RootElement, "signature")),
                    HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) throw new CryptographicException("Peer identity proof failed.");
            using var remoteEphemeral = ECDiffieHellman.Create();
            remoteEphemeral.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Field(key, "ephemeral")), out _);
            if (remoteEphemeral.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new CryptographicException("Unsupported key.");
            var secret = ephemeral.DeriveRawSecretAgreement(remoteEphemeral.PublicKey);
            byte[] Derive(string info, int length) => HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, length, transcript, Encoding.UTF8.GetBytes(info));
            var sas = BinaryPrimitives.ReadUInt32BigEndian(Derive("sas", 4)) % 1_000_000;
            var peer = new PeerIdentity(remoteIdentity, Field(key, "name")[..Math.Min(64, Field(key, "name").Length)], sas.ToString("D6", CultureInfo.InvariantCulture));
            session = new(wire, Derive(client ? "windows-to-android" : "android-to-windows", 32),
                Derive(client ? "android-to-windows" : "windows-to-android", 32), transcript, peer);
            CryptographicOperations.ZeroMemory(secret);
            bool accepted = await approve(peer, token).ConfigureAwait(false);
            await session.SendFrameAsync(Pack(new { version = 1, type = "consent", accepted }), token);
            if (!accepted) throw new CryptographicException("Pairing was declined.");
            using var consent = Parse(await session.ReadRecordAsync(token)); Expect(consent.RootElement, "consent");
            if (!consent.RootElement.GetProperty("accepted").GetBoolean()) throw new CryptographicException("Pairing was declined on the phone.");
            await session.SendFrameAsync(Pack(new { version = 1, type = "hello" }), token);
            using var hello = Parse(await session.ReadRecordAsync(token)); Expect(hello.RootElement, "hello");
            return session;
        }
        catch { if (session is not null) await session.DisposeAsync(); else await wire.DisposeAsync(); throw; }
    }
    public async Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
    {
        if (frame.Length is < 1 or > 16384) throw new IOException("Invalid message size.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _writes.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var seq = checked(++_txSequence);
            var record = new byte[frame.Length + 24];
            BinaryPrimitives.WriteUInt64BigEndian(record, seq);
            var nonce = new byte[12]; record.AsSpan(0, 8).CopyTo(nonce.AsSpan(4));
            _tx.Encrypt(nonce, frame.Span, record.AsSpan(8, frame.Length), record.AsSpan(8 + frame.Length, 16), Combine(_transcript, record[..8]));
            await _wire.WriteAsync(record, linked.Token).ConfigureAwait(false);
        }
        catch { await DisposeAsync(); throw; }
        finally { _writes.Release(); }
    }
    private async Task<byte[]> ReadRecordAsync(CancellationToken token)
    {
        var record = await _wire.ReadAsync(token).ConfigureAwait(false);
        if (record.Length is < 25 or > TcpFrameConnection.MaxWireBytes || BinaryPrimitives.ReadUInt64BigEndian(record) != checked(_rxSequence + 1))
            throw new CryptographicException("Invalid or replayed record.");
        var nonce = new byte[12]; record.AsSpan(0, 8).CopyTo(nonce.AsSpan(4));
        var plain = new byte[record.Length - 24];
        _rx.Decrypt(nonce, record.AsSpan(8, plain.Length), record.AsSpan(record.Length - 16), plain, Combine(_transcript, record[..8]));
        _rxSequence++;
        return plain;
    }
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFramesAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var heartbeat = HeartbeatAsync(linked.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                using var read = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                read.CancelAfter(ConnectionPolicy.PeerTimeout);
                var frame = await ReadRecordAsync(read.Token).ConfigureAwait(false);
                using var packet = Parse(frame);
                if (packet.RootElement.GetProperty("version").GetInt32() != 1) throw new IOException("Unsupported message version.");
                var type = Field(packet.RootElement, "type");
                if (type == "ping") await SendFrameAsync(Pack(new { version = 1, type = "pong" }), linked.Token);
                else if (type != "pong") yield return frame;
            }
        }
        finally
        {
            linked.Cancel();
            try { await heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
    private async Task HeartbeatAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(ConnectionPolicy.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(token))
                await SendFrameAsync(Pack(new { version = 1, type = "ping" }), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { await DisposeAsync(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); await _wire.DisposeAsync();
        // AES objects may still be in use by an unwinding writer; session keys are process-local only.
    }
}
