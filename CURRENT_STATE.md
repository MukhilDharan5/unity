# Stage 0 — Existing MVP audit

Audit date: 17 September 2026. Scope: the current working tree, including the existing uncommitted Windows UI and Android build changes. This is an assessment of implemented code and local verification, not a claim of physical-device readiness.

Historical checkpoint: the findings and counts below describe Stage 0. Stage 1A subsequently corrected the application validation/metadata regressions; see [PROGRESS.md](PROGRESS.md) for current status, files, results and the next resume action.

## Assessment

**Prefer a partial refactor.** The MVP already has runnable Windows and Android applications, typed state, platform boundaries, bounded framing, remembered identities, and useful tests. Rebuilding everything would discard working behavior without solving the actual problems. The connection lifecycle, cross-platform validation, capability model, security boundary, diagnostics, and Windows presentation need focused work.

Stage 0 changed documentation only. Diagnostic fixtures and rendered images are under ignored `artifacts/stage0-audit/`; application source, tests, project settings, existing user edits, and packaged releases were preserved during that checkpoint. Stage 1 had not begun at audit completion. The security and Windows UI directions below require the user's decision before architectural changes.

## What the MVP actually does

| Area | Implemented behavior | Verification boundary |
| --- | --- | --- |
| Windows app | .NET 10 WPF desktop window, compact tray flyout, WinForms `NotifyIcon`, one instance, close-to-tray and explicit exit | Release build and real-XAML smoke checks pass |
| Android app | Kotlin/Compose configuration UI; foreground connected-device service; pairing, reconnect, forget, permission shortcuts, DND and clipboard controls | Debug APK assembly, JVM tests and lint pass; no Android UI/device run |
| Connections | Windows TCP client or BLE central/GATT client; Android TCP listener and BLE peripheral/GATT server | Framing tests and TCP JVM/.NET interop pass; BLE radio behavior unverified |
| Pairing | Persistent P-256 signing identities, ephemeral key agreement, commitment/proof exchange, matching six-digit code, confirmation on both devices, encrypted hello | Happy-path interoperability verified; no independent security review |
| Remembered trust | One phone on Windows, one laptop on Android; CNG/Android Keystore private identities; peer identity and routing hints persisted | Storage and revocation inspected; no lifecycle/revocation integration tests |
| Phone state | Battery/charging, default Wi-Fi/mobile-data route, cellular generation/signal categories, effective DND, sound and media state | Models/codec/state tests pass; collectors' system behavior needs a phone |
| Media | Phone metadata on Windows and previous/play-pause/next commands to Android; controls use reported media capabilities | Mock and state routing tested; actual media apps unverified |
| DND | Windows can request the Android application's own automatic rule; effective phone DND is reported separately | Protocol/UI separation tested; actual rule/manual-DND interactions unverified |
| Clipboard | Independently opt-in, new text only, 12 KiB text limit, bounded ID suppression, no history; Windows listens for future clipboard events, Android sends on a visible tap | Protocol/coordinator tests pass; real Android clipboard behavior unverified |
| Sample mode | Explicit fictional state and three sample tracks through the same codec/state manager; no sensitive operations | Core and UI checks pass |

This is broader than a status-only MVP, but several product capabilities remain absent. Windows-to-phone media, PC activity, actual Windows/Android DND synchronization, OEM performance modes, scrcpy launch/control, brightness/sensors, shared adaptive brightness, headphone handoff, internet tethering, audio streaming, proximity lock and Windows unlock are not implemented. The ignored `scrcpy/` directory contains tools; the applications do not integrate them.

The full Windows window currently duplicates phone status and controls in a dashboard and opens by default. `--tray` starts quietly. The tray tooltip contains connection status only, not battery or OEM mode. Media sections disappear when playback is paused, so the user cannot resume through the visible media section after pausing. These are current presentation policies to reconsider during the UI stage.

## Architecture and dependencies

```text
PhoneCompanion.slnx
  src/PhoneCompanion.Core           net10.0; immutable records, codec, state, framing/session abstractions
  src/PhoneCompanion.Windows        net10.0-windows10.0.26100.0; WPF + WinForms tray + WinRT/Win32 adapters
  tests/PhoneCompanion.Tests        executable test harness, not a dotnet test project
  tests/PhoneCompanion.Windows.SmokeTests
Unity_Connect_Android/              separate Gradle project; Kotlin, Compose, coroutines, serialization
scripts/                           Windows restore/build/check/publish and launch helpers
docs/                              existing protocol, connection, Android and validation documents
artifacts/                         ignored packages, caches, renders and audit diagnostics
```

There is no shared language/runtime library across Android and Windows. The wire specification is shared; models and codecs are implemented separately in C# and Kotlin.

Windows has no third-party application/test packages. It uses the pinned Microsoft Windows SDK .NET reference pack `10.0.26100.57`, WPF, WinForms, WinRT Bluetooth, CNG, user32 and DWM. Warnings are errors via `Directory.Build.props`. Its declared minimum API level is Windows 10 build 19041, while the requested product targets Windows 11. No .NET SDK pin (`global.json`) exists.

Android currently uses AGP **8.2.2**, Gradle **8.10.2**, Kotlin/serialization plugin **1.9.20**, Compose compiler **1.5.4**, Compose BOM **2024.02.00**, JDK **17**, compile/target API **34**, and minimum API **31**. Dependencies include AndroidX core/lifecycle/activity, Material 3, kotlinx serialization, JUnit 4 and coroutines-test. Older documentation claimed AGP 8.8.0; that does not describe this working tree.

## State and message flow

Windows composes objects in `App.xaml.cs`. `PhoneStateManager` owns one active `IPhoneTransport` and immutable `PhoneState`; a dispatcher-bound `PhoneViewModel` translates it to labels and commands shared by the desktop and flyout. The manager serializes lifecycle/command operations, clears state on disconnect and fences callbacks from earlier routes. Platform adapters do not directly edit phone UI controls.

The production path is `LivePhoneTransport -> SecurePhoneSession -> IFrameConnection -> TCP/BLE`. A second `SessionPhoneTransport`/session-factory path lives in Core and is heavily tested but is not the packaged app's production connection path. These parallel lifecycles increase the chance that successful tests miss a production cleanup defect.

Android's `ConnectionService` combines transport startup, pairing approvals, trust persistence, route selection, liveness, collector lifecycle, dispatch, and UI status. `StateCollector` combines callback/broadcast flows into a `PhoneSnapshot`; the service sends full snapshots on state changes. `AppViewModel` wraps a static service `StateFlow` and invokes static service methods. The feature packages are useful boundaries, but the service is carrying too many policies.

Both platforms carry version 1 JSON application messages through the same secure-record scheme over either route. Windows exposes `IPhoneMessageCodec`; Android's service is tied to the `MessageCodec` singleton and also parses secure controls directly. There is no general session capability exchange, request correlation, command acknowledgment/error contract, or protocol-version range negotiation. Identity is supplied by the authenticated session rather than a payload field.

Android can hold BLE and Wi-Fi sessions and prefers Wi-Fi for application data. Windows owns only one route and has no connected-session promotion from BLE to Wi-Fi, no simultaneous lightweight presence plane, and no automatic mDNS browsing. Android advertises mDNS; Windows accepts a manually entered endpoint.

## Findings and technical debt

| Priority | Finding and evidence | Consequence / appropriate response |
| --- | --- | --- |
| Release gate | `SecurePhoneSession.cs` and `SecureSession.kt` implement an application-designed authenticated handshake using established primitives. Automated secure-session tests exercise successful pairing/exchange; they do not establish adversarial security. | Choose an established secure-channel direction or explicitly commission review of v1 before expanding sensitive features. Do not claim a broken cipher or exploit: neither was established by this audit. |
| High | Codec probes reproduced quoted versions/booleans, duplicate fields and shortened UUIDs accepted on Android and rejected on Windows. An object-valued media command throws out of `decodeIncoming`. | A shared positive/negative fixture contract and strict decoding are required. Malformed authenticated messages can currently terminate an Android route instead of producing a bounded rejection. |
| High | Android accepts/encodes metadata containing control characters; Windows rejects it. `StateCollector` forwards raw app metadata without Windows-compatible length/control normalization. | A legitimate media app can produce a snapshot Windows discards, leaving previous state visible. Normalize at the platform boundary and verify interoperability for all message families. |
| High | Production connection cleanup differs from the tested factory path. `LivePhoneTransport.ReadAsync` changes status in `finally` but does not release its session there. Secure-session close leaves AES/key material objects for later collection. | Centralize ownership and deterministic close/cancellation; test actual production adapters with fake frame connections. Avoid assuming the old route is released when only state changed. |
| High | There is no generic capability negotiation. Only media action flags and companion-rule control are advertised in state. Clipboard availability assumes a real connected route. | Capability, permission, user opt-in and route suitability need explicit distinctions; unsupported features must not be sent blindly. |
| High | Windows retries saved LAN then BLE with 2/5/10/20/30-second pauses and continues indefinitely. `StopReconnectLoop` clears task ownership before awaiting completion. BLE discovery chooses the first matching service advertisement. | Bound radio work, add event triggers/jitter and explicit cancellation ownership, test sample/pairing/exit races, and avoid an unrelated advertiser repeatedly intercepting discovery. Idle impact is unmeasured. |
| Medium | `StateCollector.cellularFlow` listens to default-network callbacks but has no telephony generation/signal callbacks. | Signal/generation can remain stale while the default network stays unchanged. Use supported telephony listeners with per-permission availability and a multi-SIM policy. |
| Medium | `ConnectionService` mixes orchestration and policies; static instance/UI calls hide lifecycle ownership. Pairing approval expires, but listener/advertisement shutdown is not scheduled at expiry. | Extract a small testable coordinator; separate trusted listening from timed discovery; test service recreation, permission revocation and forget during handshake. |
| Medium | No structured application logger or diagnostics sink is wired. Broad catches suppress failures; fault events alone are not diagnostics. Settings and deadlines/UUIDs are scattered. Android build helper hard-codes this PC's paths. | Add redacted event logging and typed local configuration without a database/broker or a heavyweight framework. Repair portability and SDK pins in the foundation stage. |
| Medium | The UI uses custom WPF templates, fixed accent palettes, software-drawn shadow and WinForms menus; no Mica/system accent/high-contrast integration. Switch-shaped controls are ordinary buttons. Full app is a dashboard. | Choose WPF modernization versus a WinUI 3 shell before significant UI expansion. Review actual accessibility toggle semantics and tray-first information architecture. |
| Medium | No platform instrumentation, sustained power measurements, actual BLE integration, sleep/resume or adversarial session tests. Normalization tests mostly test constructed data or `coerceIn`, not collector mapping code. | Retain useful tests and replace weak coverage with production-boundary tests. Treat phone/radio/power validation as separate release criteria. |
| Medium | Existing docs describe a single startup reconnect and 10/30-second LAN liveness in places. Actual sessions ping every 10 seconds on both routes with a 35-second deadline; Android checks expiry every 5 seconds. | Correct baseline docs and separate implemented behavior, recommendations, and verification evidence. |

Further storage review is needed: Windows trust JSON is atomic on save but not explicitly schema-validated/integrity-protected by the app, and load errors are treated as unpaired. Android stores the public peer key in private preferences, disables backup, but ignores the Boolean result of trust/revocation `commit()`. Hardware-backed Android identity is not required or attested. Identity recovery, multi-device trust, local-user threat assumptions and revocation races have no documented approved policy. These are design/reliability gaps, not evidence that network names establish trust.

Clipboard text may currently traverse BLE as well as LAN; there is no route-size/capability policy enforcing BLE as a small-control channel. Plaintext/history is not persisted, but live transfer buffers necessarily hold text. Sensitive-content filtering and canceling a send already in progress on disable are not implemented.

## Retain, refactor, and selectively replace

**Retain:** the .NET/Kotlin split, immutable state records and explicit unavailable values, feature packages, `IPhoneTransport`/`IFrameConnection`/codec boundary, generation fencing, mock route, bounded framing, capability-gated media commands, app-owned DND rule, independently opt-in clipboard, platform key stores, and useful core/UI/interop tests.

**Refactor:** production session lifecycle, Windows reconnect orchestration, Android service coordinator and authoritative device state, validation parity, platform metadata normalization and telephony events, generic capabilities, redacted logging, typed settings, portable builds and test coverage. Keep one small process per platform for normal features; no Windows service or cloud is currently justified.

**Selective replacement, pending approval:** the bespoke secure-channel layer if an established protocol is chosen; the Windows presentation shell if WinUI 3 is chosen. Neither requires rewriting the phone collectors, core state or generic feature models.

## Verification performed in this audit

- Current Windows solution Release build, cached dependencies, `--no-restore`: **0 warnings, 0 errors**.
- Existing .NET core executable: **25/25 pass**.
- Existing WPF smoke executable: passes; generates **20 renders** of flyout, desktop and pairing states. Inspected representative light flyout and dark desktop images. Existing tests include a 150% raster render; real 125%/200%, monitor movement, screen reader and high-contrast review were not performed.
- Current Android `assembleDebug`: succeeds (task output up-to-date); `testDebugUnitTest`: **15 pass, 1 optional interop skip, 0 failures/errors**; `lintDebug`: **0 errors, 5 warnings**, including an obsolete custom-lint check and four newer-dependency notices. SDK XML and Gradle deprecation warnings also appear.
- Offline combined Android run initially failed because `lint-gradle:31.2.2` was not cached. After resolving the current plugin's tooling, the separate online unit/lint run succeeds. No dependency versions were changed.
- Existing cross-runtime fixture run separately with JUnit: **1/1 pass**; an actual .NET TCP client and compiled Kotlin server agree on pairing and exchange encrypted traffic in both directions.
- Diagnostic codec probes against current compiled implementations reproduce the six differences above. Logs and fixtures are in `artifacts/stage0-audit/`.

These checks do not demonstrate a clean-machine install, APK release signing, installed packaged-binary equivalence, secure-channel resistance to attack, Android platform behavior, BLE radio interoperability, or all-day power use. No physical Android phone or emulator UI was tested.

## Recommended Stage 1 — Foundation consolidation

Objective: make the existing foundations coherent, testable and safe to extend while preserving runnable apps.

After the major choices are approved, implement in reviewable increments: common wire fixtures and validation parity; one production connection lifecycle with test seams and bounded reconnection; a small Android connection coordinator; explicit capability/permission/enablement models; redacted structured diagnostics and typed settings; toolchain pins/portable helpers; synchronized documentation. A security replacement needs its own approved pairing/migration design and focused substage; do not hide it inside a general cleanup.

Deliberately defer new media directions, DND automation, clipboard history, privileged APIs, OEM controls, scrcpy, sensors, tethering, audio and lock/unlock. Do not start a service, change Android permissions, switch serialization, add storage engines or promise backward compatibility without discussing the relevant decision.

Acceptance: existing builds/tests stay green; malformed/cross-platform fixtures agree; cancellation/disconnect/revocation/sample-mode lifecycle tests exercise production boundaries; capability gating has explicit unavailable reasons; diagnostics contain no clipboard, keys or private metadata; docs describe the resulting implementation.

## Decisions needed

1. **Secure channel:** retain v1 temporarily and require a dedicated adversarial review/hardening stage, or move authenticated application traffic to established TLS on LAN and use BLE initially for discovery/presence/bootstrap. Recommend the established-channel direction, subject to pairing/library feasibility and an explicit migration plan. The latter changes offline BLE command availability and wire compatibility. See [ADR-003](docs/decisions/ADR-003-trusted-device-model.md).
2. **Windows UI direction:** modernize the current WPF shell, or retain the core/adapters and plan a WinUI 3 shell replacement in the UI stage. Recommend WinUI 3 for the requested native Windows 11 standard, with Stage 1 work remaining UI-independent. WPF minimizes immediate work; WinUI introduces runtime/packaging/windowing work. See [ADR-004](docs/decisions/ADR-004-windows-ui-direction.md).

No proposed ADR is approved by this audit. Exact transport evolution, protocol additions, persistence and device compatibility remain follow-up decisions at their relevant stages.

Documentation entry points: [architecture](docs/architecture.md), [protocol](docs/PROTOCOL.md), [Android](docs/android.md), [Windows](docs/windows.md), [security](docs/security.md), [capabilities](docs/capabilities.md), [development](docs/development.md), [testing](docs/testing.md), [roadmap](docs/roadmap.md).
