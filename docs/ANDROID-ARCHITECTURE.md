# Android companion architecture and permissions

Current implementation and limitations are maintained in [android.md](android.md), [architecture.md](architecture.md) and [PROGRESS.md](../PROGRESS.md); [CURRENT_STATE.md](../CURRENT_STATE.md) preserves Stage 0. Stage 1A aligns strict application validation with shared fixtures. The codec remains a singleton, and handshake/control validation remains separate; successful secure-session tests do not constitute adversarial security assurance.

The Android project lives in `Unity_Connect_Android` and supports Android 12/API 31 or newer. It uses Kotlin, Jetpack Compose, coroutines, immutable protocol models, and a foreground service for connection work.

## Packages

- `core`: v1 models, strict logical-frame codec, and cryptographic primitives.
- `state`: battery, active default network, cellular generation/signal, media-session, and sound collectors.
- `dnd`: the app-owned `AutomaticZenRule` controller and its condition provider.
- `clipboard`: opt-in, plain-text clipboard transfer with a 12 KiB UTF-8 limit and 64-ID in-memory duplicate window.
- `ble` and `lan`: Android GATT peripheral and bounded length-prefixed TCP server.
- `core/SecureSession` and `core/IdentityStore`: mutual identity proof, code confirmation, encrypted records, replay ordering, and Android Keystore identity.
- root package: Compose UI, view model, and foreground connection service.

## DND ownership

`CompanionDndController` reports Android's effective interruption filter separately from the companion rule condition. After the user grants notification-policy access, it creates one rule owned by Unity Connect. Switching Companion DND off publishes a false condition for that rule; it does not call a global interruption-filter setter, remove other rules, or turn off manual DND.

The UI opens Android's notification-policy access screen only after the user taps **Allow DND access**. If access is missing or the rule cannot be read, the Windows control capability is false.

## Clipboard privacy

Clipboard sync is off by default and persists only that preference. Clipboard content and history are never stored. Incoming authenticated text is applied only while sync is enabled. The Android app does not register a clipboard listener: phone-to-laptop transfer reads the current clipboard only after the user taps **Send current clipboard** while the activity is visible. This follows Android 10+ restrictions on clipboard reads by ordinary background apps.

## Permissions

- Nearby-device Bluetooth permissions support advertising and GATT.
- Phone-state permission supports friendly cellular generation/signal state.
- Notification permission supports the foreground-service notification on Android 13+.
- Notification-policy access enables the app-owned DND rule.
- Notification-listener access enables read-only active media-session discovery; the app does not transmit unrelated notification text.

Unavailable access produces unavailable state for that feature rather than fabricated battery, network, DND, sound, or media values.

## Security boundary

The protocol and UI reject DND, clipboard, and media commands until the service has a verified trusted identity and current encrypted session. BLE and LAN carry the same secure packets, and Android routes application data through one active authenticated session. Automated JVM and .NET tests exercise identity proof, code agreement, and bidirectional encrypted records. Radio behavior, Android background limits, and system integrations still require physical-device testing.
