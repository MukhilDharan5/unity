# Architecture — audited baseline and proposed foundation

Status: updated through concurrent BLE + Wi-Fi routing on 20 September 2026. [CURRENT_STATE.md](../CURRENT_STATE.md) preserves the historical audit; [PROGRESS.md](../PROGRESS.md) records current work. Foundation recommendations beyond the completed checkpoints remain proposed; security/UI migrations are undecided.

Stage 1A retains both implementations and JSON v1. Android now validates application structure, scalar types, state semantics and canonical UUIDs before command dispatch; platform media text is normalized before encoding. Both test runtimes consume 67 shared application fixtures. Handshake/control validation and lifecycle ownership are separate unfinished work.

## Current boundaries

Stage 15 adds one deliberate data-plane exception to the single companion frame stream. The authenticated BLE/LAN session negotiates laptop audio and carries its fresh key/token, while continuous PCM uses an ephemeral Android-hosted TCP socket reachable only on the local network. AES-GCM sequence records protect that socket independently, and it is torn down with the companion session. This keeps bulk audio from blocking state and commands; it is not a general-purpose secondary transport.

The Windows solution contains platform-neutral Core, a WPF/WinForms Windows executable, a Core test executable and a Windows smoke-test executable. Android is a separate Kotlin/Compose Gradle project. The wire contract is shared by documentation and an optional runtime interoperability test; code models are duplicated across languages.

```text
Windows App composition
  WPF desktop + flyout -> PhoneViewModel
                         PhoneStateManager (one phone, BLE + Wi-Fi route owners)
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
    authenticated BLE + Wi-Fi sessions; Wi-Fi application route preferred
      SecureSession -> FramePipe -> TCP server / GATT server
```

There is no privileged Windows service, Credential Provider, cloud server, message broker, database or plugin system. A Windows process runs as the user. Android uses a foreground service declared as `connectedDevice` and publishes an ongoing notification.

## State ownership

Windows `PhoneStateManager` is authoritative for observed remote phone state. Immutable records include nullable battery, media, cellular, DND and sound sections. It owns at most one authenticated BLE route and one authenticated Wi-Fi route, fences each route independently, and preserves state while either remains connected. Snapshots replace all state sections; deltas change one. Wi-Fi is the deterministic application route and BLE is the warm fallback; commands are serialized and never duplicated across routes. UI notifications are dispatched to WPF's UI thread.

Android combines battery/network/media/sound callbacks and DND `StateFlow` into snapshots. The service holds the latest snapshot plus a separate mutable `UiState`. Local feature preferences, rule state, connection/session state and UI availability are therefore not one unified device-state model yet.

Commands do not optimistically change phone state. A successful write only means sent. Android applies a command locally, then subsequent collector snapshots represent resulting state; there is no explicit application acknowledgment/error message in the live path.

## Connection behavior

Windows can initially pair through a LAN endpoint or the agreed BLE service. After trust is established, it continues until both the authenticated LAN and BLE routes are connected, retrying whichever route is missing with capped backoff. General pairing has a two-minute deadline. BLE scan chooses the first matching advertisement; identity verification still decides trust.

Android listens on TCP 38471, advertises mDNS and GATT, and can authenticate both kinds of route. Windows uses bounded mDNS discovery for `_phonecomp._tcp.local`, with saved/manual endpoints as fallback; discovery never establishes trust. Both peers retain BLE and Wi-Fi concurrently, route application data through Wi-Fi first, and promote BLE immediately when Wi-Fi ends. The missing route reconnects in the background. The BLE route currently allows the same 16 KiB logical payloads as LAN, though bulk audio remains on its separate Wi-Fi-only socket.

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
