# Architecture — audited baseline and proposed foundation

Status: Stage 1A, 1B-W and 1B-A1 complete, 17 September 2026. [CURRENT_STATE.md](../CURRENT_STATE.md) preserves the historical audit; [PROGRESS.md](../PROGRESS.md) records current work. Foundation recommendations beyond the completed checkpoints remain proposed; security/UI migrations are undecided.

Stage 1A retains both implementations and JSON v1. Android now validates application structure, scalar types, state semantics and canonical UUIDs before command dispatch; platform media text is normalized before encoding. Both test runtimes consume 67 shared application fixtures. Handshake/control validation and lifecycle ownership are separate unfinished work.

## Current boundaries

Stage 15 adds one deliberate data-plane exception to the single companion frame stream. The authenticated BLE/LAN session negotiates laptop audio and carries its fresh key/token, while continuous PCM uses an ephemeral Android-hosted TCP socket reachable only on the local network. AES-GCM sequence records protect that socket independently, and it is torn down with the companion session. This keeps bulk audio from blocking state and commands; it is not a general-purpose secondary transport.

The Windows solution contains platform-neutral Core, a WPF/WinForms Windows executable, a Core test executable and a Windows smoke-test executable. Android is a separate Kotlin/Compose Gradle project. The wire contract is shared by documentation and an optional runtime interoperability test; code models are duplicated across languages.

```text
Windows App composition
  WPF desktop + flyout -> PhoneViewModel
                         PhoneStateManager (one phone, one active route)
                           IPhoneMessageCodec / typed messages
                           IPhoneTransport
                             LivePhoneTransport (production)
                               SecurePhoneSession
                                 IFrameConnection
                                   TCP client / GATT client
                             MockPhoneTransport
                             SessionPhoneTransport (alternate factory/test path)

Android Compose -> AppViewModel -> static ConnectionService calls/UI StateFlow
  ConnectionService
    StateCollector -> combined PhoneSnapshot
    media dispatch / CompanionDndController / ClipboardBridge
    trust preferences + IdentityStore
    active secure session: Wi-Fi preferred over BLE
      SecureSession -> FramePipe -> TCP server / GATT server
```

There is no privileged Windows service, Credential Provider, cloud server, message broker, database or plugin system. A Windows process runs as the user. Android uses a foreground service declared as `connectedDevice` and publishes an ongoing notification.

## State ownership

Windows `PhoneStateManager` is authoritative for observed remote phone state. Immutable records include nullable battery, media, cellular, DND and sound sections. Disconnect clears them. Snapshots replace all five sections; deltas change one. The manager fences callbacks using a route generation and serializes route changes/commands with a semaphore. UI notifications are dispatched to WPF's UI thread.

Android combines battery/network/media/sound callbacks and DND `StateFlow` into snapshots. The service holds the latest snapshot plus a separate mutable `UiState`. Local feature preferences, rule state, connection/session state and UI availability are therefore not one unified device-state model yet.

Commands do not optimistically change phone state. A successful write only means sent. Android applies a command locally, then subsequent collector snapshots represent resulting state; there is no explicit application acknowledgment/error message in the live path.

## Connection behavior

Windows manually selects a LAN endpoint or scans the agreed BLE service and owns one authenticated route at a time. When disconnected from a remembered device it repeatedly tries saved LAN for up to 8 seconds, then BLE for up to 15 seconds, followed by capped backoff. General pairing has a two-minute deadline. BLE scan chooses the first matching advertisement; it does not enumerate and verify all candidates.

Android listens on TCP 38471, advertises mDNS and GATT, and can authenticate both kinds of route. Windows uses bounded mDNS discovery for `_phonecomp._tcp.local`, with saved/manual endpoints as fallback; discovery never establishes trust. Android routes application data through Wi-Fi first and BLE second. Windows has no in-session route promotion or independent BLE presence plane. The BLE route currently allows the same 16 KiB logical payloads as LAN.

## Proposed foundation consolidation

Preserve the current project split. Use small components at actual ownership/platform boundaries:

| Boundary | Proposed responsibility | Existing material to retain |
| --- | --- | --- |
| Device state | Immutable local/remote state, explicit unavailable/permission reasons, observed capability facts | C# records, Kotlin snapshot/flows, generation fencing |
| Connection coordinator | Route lifecycle, cancellation, trust checks, bounded retry policy, active route events | Transport/frame interfaces, platform factories |
| Protocol | Typed commands/events and interchangeable encoding with identical validation fixtures | Windows codec interface and v1 logical contract |
| Trust/security | Persistent identity, peer authorization/revocation, secure-channel interface | CNG and Android Keystore identities; public peer records |
| Feature providers | Platform event collection and command application, independent of UI/wire representation | Android feature packages, Windows clipboard listener |
| Diagnostics/settings | Redacted events, bounded storage policy and typed preferences | Current feature defaults and atomic Windows trust save pattern |
| Presentation | Observe state and invoke capability commands; setup/permissions/diagnostics in full app | Existing shared view model semantics, UI fixtures |

Stage 1B-W consolidated Windows live/factory session ownership in Core `SessionPhoneTransport`. The platform adapter retains secure pairing/wire opening; the owner handles cancellation, receive/send termination, release and draining. Stage 1B-A1 extracted Android `PhoneSessionOwner` (route reservations/jobs, generation-fenced publication/revocation, active-route sending) and `PhoneStateCollection` (observer replacement/latest state). Stage 1B-A2 added `ListenerLifecycle` beneath `LanServer` and `BleManager`: every listener run owns its platform resources and callbacks, late resources self-close, stop and callback delivery are serialized, and an old finish cannot retire a replacement. Service retains Android hosting, approvals, trust persistence, control parsing and feature dispatch. Real secure handshakes with fake wires exercise session owners; fake cleanup resources exercise the production listener lifecycle seam. No new transport framework, runtime-shared protocol project or dependency injection framework was introduced.

## Approval boundaries

[ADR-001](decisions/ADR-001-transport-architecture.md) and [ADR-002](decisions/ADR-002-protocol-format.md) document observed transport/format and pending evolution. [ADR-003](decisions/ADR-003-trusted-device-model.md) asks for the security direction. [ADR-004](decisions/ADR-004-windows-ui-direction.md) asks for WPF modernization versus a later WinUI shell.

No security migration, new wire envelope, storage layout, permission increase, framework switch or project move was made in Stage 0. New privileged/OEM/security features need their own approval and isolated providers.
