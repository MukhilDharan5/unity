# Secure session v1

This documents the application-designed protocol implemented by `SecurePhoneSession.cs` and `SecureSession.kt`; it has not received an independent security review. Happy-path .NET/Kotlin interoperability is verified, but adversarial handshake/record validation is not. See [security.md](security.md) and [ADR-003](decisions/ADR-003-trusted-device-model.md) before extending sensitive functionality.

Binary lengths/sequences are big-endian. Transcript lengths are bounded four-byte values, and records encode eight-byte positive sequences; Windows uses an unsigned sequence counter while Kotlin limits its counter to positive signed `Long` values. JSON is UTF-8. Handshake fields may be parsed independent of object-property order; raw reveal bytes are retained for transcript hashing. Strict duplicate/type/depth parsing is not uniformly enforced between runtimes.

## Roles and packet framing

Windows is `windows` and initiates the transport. Android is `android` and accepts it. TCP packets have a four-byte length prefix. BLE transports the same packet as ordered fragments described in `INTEROP-CONTRACT-v1.md`. The largest application plaintext is 16,384 bytes; the largest encrypted record is 16,408 bytes.

## Handshake

Each device has a persistent P-256 ECDSA identity and creates a fresh P-256 ECDH key plus 32 random bytes for every route.

1. Both send `{"version":1,"type":"commit","hash":"base64(SHA256(key-json-bytes))"}` and read the peer commitment.
2. Both send their committed key JSON and read the peer key JSON: `{"version":1,"type":"key","role":"windows|android","identity":"base64(SPKI)","ephemeral":"base64(SPKI)","nonce":"base64(32 bytes)","name":"..."}`.
3. Each verifies the peer commitment and role. The transcript is `SHA256(domain || u32(len(windows-key)) || windows-key || u32(len(android-key)) || android-key)`, where `domain` is the UTF-8 bytes of `UnityConnect/session/1` followed by the single newline byte `0x0A`. The domain does not contain literal backslash/n bytes.
4. Both send a DER-encoded `SHA256withECDSA` signature over the transcript in `{"version":1,"type":"proof","signature":"base64(DER)"}` and verify the peer proof.
5. P-256 ECDH produces `secret`. HKDF-SHA-256 uses `secret` as input keying material and the transcript as salt. `info` is UTF-8. It derives 32 bytes for `windows-to-android`, 32 bytes for `android-to-windows`, and four bytes for `sas`. The code is `u32(HKDF(..., "sas", 4)) mod 1000000`, padded to six digits.
6. A newly seen identity requires the user to confirm this code on both devices. Each sends its choice as the first encrypted record: `{"version":1,"type":"consent","accepted":true|false}`. Both confirmations must be true.
7. Both exchange encrypted `{"version":1,"type":"hello"}` records. Trust is persisted only after this completes.

## Encrypted records

Every post-proof packet is `u64(sequence) || ciphertext || 16-byte GCM tag`. Sequence starts at 1 independently for each direction and must equal the receiver's next value. AES-GCM uses the derived directional 256-bit key, nonce `0x00000000 || u64(sequence)`, and AAD `transcript || u64(sequence)`. Any authentication, ordering, version, framing, or size failure closes the route.

`ping` and `pong` are encrypted control records. Each side sends a ping every 10 seconds and treats 35 seconds without an incoming record as stale. Application payloads are the JSON messages in `PROTOCOL.md`.
