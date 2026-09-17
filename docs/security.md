# Security — implemented baseline and release gates

Status: assessment updated through Stage 1B-A1, 17 September 2026. This document distinguishes code behavior from security assurance. No new cryptographic protocol, trust migration or sensitive feature was implemented.

## Existing trust boundary

Bluetooth names/addresses, service advertisements, IP endpoints and mDNS are routing hints. New trust requires cryptographic possession of persistent signing identities, a transcript-derived six-digit comparison on both devices, mutual encrypted consent and hello. Remembered identities are matched before application state/commands are accepted. Each side supports one remembered peer.

Windows private identity is a non-exportable P-256 ECDSA key in the Microsoft Software Key Storage Provider. Android uses a P-256 signing key in Android Keystore. The app does not guarantee hardware backing or attest it. No biometric approval is required for ordinary companion sessions. Public peer trust lives in a per-user Windows JSON file or private Android preferences; session material is not intentionally persisted.

The existing handshake and record encoding are specified in [SECURE-SESSION-v1.md](SECURE-SESSION-v1.md). They use ephemeral P-256 ECDH, ECDSA proofs, SHA-256/HKDF, separate directional AES-256-GCM keys, transcript-bound associated data and strict record sequences. Using established primitives does **not** make the application's handshake an independently established secure-channel protocol.

## Evidence and limits

Existing .NET/JVM and real cross-runtime tests establish successful code agreement and encrypted bidirectional exchange. The focused crypto suites remain primarily happy-path tests; Stage 1B-W adds live pairing cancellation and ownership interleavings. They do not verify active interception, key/role substitution, commitment/proof tampering, ciphertext tampering, replay/reordering, consent denial, downgrade, cryptographic concurrency, rate limiting or revocation races. No external review or attack resistance was established. The audit did not demonstrate a cipher break or authentication bypass.

Stage 1A corrected the reproduced application duplicate/type/UUID differences and passes 67 shared positive/negative fixtures on both platforms. This bounded evidence does not establish identical behavior for every possible input. Handshake/control parsing still lacks a uniform strict duplicate/type/depth contract; it precedes Android's nonthrowing application decoder. Stage 1B-W makes Windows read termination release the live session, drains ownership on stop, and routes secure send/heartbeat failures through idempotent session close. Six live scenarios test real handshakes, approval cancellation, late open, remote close and send cancellation/failure. Session disposal still does not deterministically dispose/erase all retained AES/key arrays. Android listener lifecycle and adversarial security tests remain unfinished.

Trust save/revoke failures and recovery policy need attention: Windows load catches all errors as unpaired; Android still ignores `commit()` success; neither side has approved schema/version migration, multi-peer management or key-loss UX. Stage 1B-A1 makes Android Forget revoke route admission/jobs before clearing preferences and guards the existing synchronous trust write with the session generation. JVM owner tests prove a revoked handshake cannot invoke the trust-publication hook or remove a newer route. This does not execute Android SharedPreferences or UI, prove durable revocation on storage failure, or complete key-loss/recovery/forget integration validation. Listener resource races remain 1B-A2.

Android secure close now uses an atomic idempotent flag; the owner also guards raw pipe close. No cryptographic transcript/record changes were made. Ownership tests cover route fallback, ambiguous-send closure without retry, parent cancellation interrupting blocking IO, simultaneous close and late collector cleanup. Key erasure, adversarial records, rate limits and actual platform behavior remain unverified.

## Threat assumptions to approve

Protect against an untrusted network peer, impersonated discovery results, interception, replay, unauthorized commands, stale callbacks and accidental privacy leakage. Specify whether a malicious process running as the same local user or an already compromised OS is in scope; current per-user trust storage must not be represented as a defense against those actors. Proximity is not authentication. Availability/rate limits matter because untrusted handshakes occupy bounded listener slots until timeout.

## Proposed secure-channel direction

Two realistic directions need a user choice:

| Option | Advantages | Disadvantages / durable cost |
| --- | --- | --- |
| Retain v1 temporarily, isolate it, and require focused independent/adversarial review before release | Keeps current BLE and LAN encrypted message behavior and paired identities; smallest immediate change | Maintains ownership of a bespoke handshake/record protocol and review burden; growing v1 deployments make eventual wire/trust migration harder |
| Established TLS secure application sessions on LAN; BLE initially discovery/presence/bootstrap | Uses established channel implementations, narrows bespoke security code, keeps identities and providers reusable in principle | Changes BLE-only command availability; requires an explicitly approved authenticated first-pairing method, certificate/key storage feasibility and migration/re-pairing policy; does not secure arbitrary BLE commands by itself |

Recommend the established-channel direction. “Use TLS” alone is not a complete pairing design: mutually pinned peer identity, first-trust approval and platform keystore integration must be resolved in a focused design/feasibility substage. Never accept all certificates, trust an IP/name, silently downgrade to plaintext, or assume old identities can be migrated unchanged. No TLS implementation or compatibility promise was made in this audit. See [ADR-003](decisions/ADR-003-trusted-device-model.md).

Platform secure-stream/socket APIs exist in [.NET SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0) and [Android SSLSocket](https://developer.android.com/reference/javax/net/ssl/SSLSocket). Their existence supports feasibility research; it does not prove the application's key storage, authenticated provisioning or migration design.

If encrypted BLE-only application commands are essential, an established, maintained protocol/library suitable for both runtimes must be assessed or the existing design reviewed before committing. Presence discovery does not authorize a sensitive action.

## Feature privacy

Clipboard is off by default, independently controlled, text-only and without persisted content/history. Android send is a visible action. There is no sensitive-marker filtering/private mode yet. Sensitive buffers necessarily exist during transfer. No automatic Windows locking/unlocking, process-list transmission, audio capture, screen capture or privileged control exists.

Structured diagnostics are not implemented. The foundation logger must record event IDs, outcome/reason, route transitions and permission/capability facts without clipboard text, authentication secrets, key material or private media payloads. Establish bounded retention and safe export before adding a diagnostics UI.

Windows unlock remains a separate later module requiring user approval, Credential Provider research, platform authentication feasibility, hardware-backed challenge response and independent security assessment. A tray app must never simulate secure Windows unlock.
