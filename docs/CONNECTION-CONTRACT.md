# Connection architecture

The Windows app has concrete BLE and Wi-Fi adapters. Both enter the shared `PhoneStateManager` only after the secure session proves both device identities, confirms new trust on both screens, and exchanges encrypted hello records. Android accepts GATT and TCP routes and prefers Wi-Fi while both are authenticated.

```text
Tray flyout / Pairing window
  PhoneStateManager
    JSON message codec
      LivePhoneTransport
        SecurePhoneSession
          TCP frame connection
          BLE frame connection

Android ConnectionService
  StateCollector / media / DND / clipboard
    SecureSession
      LanServer
      BleManager
```

## Trust

Device names, BLE addresses, IP addresses, and mDNS results are routing hints. They never establish trust.

Each installation owns a persistent P-256 signing identity. Windows stores a non-exportable private key in the Windows CNG key store. Android stores its private key in Android Keystore. New devices prove possession of those keys, derive the same transcript-bound six-digit code, and require confirmation on both screens. Only the peer public identity, friendly name, and last Wi-Fi endpoint are persisted after both confirmations and the encrypted hello exchange.

Choosing **Forget paired phone** on Windows deletes the trusted Android identity and endpoint. Choosing **Forget laptop** on Android deletes the trusted Windows identity, stops both listeners, disables clipboard sync, and clears current connection state. Session keys exist only in memory.

## Routes

Windows Wi-Fi can discover Android's `_phonecomp._tcp.local` mDNS advertisement or connect to the displayed `address:38471` manually. TCP uses a four-byte big-endian length followed by one bounded secure packet. Discovered names and addresses remain untrusted until the secure identity handshake succeeds.

For BLE, Windows is the central/GATT client and Android is the peripheral/GATT server. Windows filters advertisements by the agreed service UUID, subscribes to the TX notification characteristic, and writes with response to RX. A one-byte sequence/first/last header fragments the same secure packets used by TCP. Both reassemblers enforce ordering, time, queue, and size bounds.

Windows starts a remembered-device reconnect loop at startup and after disconnect: it tries the saved Wi-Fi endpoint, bounded mDNS-discovered endpoints, then BLE, with capped pauses between rounds. A discovered endpoint replaces the saved endpoint only after authentication as the remembered phone. First pairing has a two-minute overall deadline and offers automatic LAN, manual LAN and BLE. Android starts both listeners automatically for a trusted laptop and when the user taps **Start pairing** for a new laptop; unpaired listeners and approval expire after two minutes.

Android may hold authenticated BLE and Wi-Fi sessions simultaneously but routes application messages through one active session, preferring Wi-Fi. If Wi-Fi closes, it sends the latest snapshot over authenticated BLE. Windows owns one route at a time and clears displayed phone state immediately when that route disconnects.

Since Stage 1B-A1, Android `PhoneSessionOwner` reserves pending/connected routes before coroutine dispatch and tracks their jobs/guarded pipes. Publication and the service's synchronous trust write require a current lease/generation. Forget disables admission and invalidates that generation before closing/canceling work; destruction prevents further admission. A late old job cannot publish trust or remove a newer same-kind route. Ambiguous send failure closes the selected route without retrying the payload on another. `PhoneStateCollection` owns snapshot observers/latest state and waits for prior cleanup during replacement. Since Stage 1B-A2, each LAN/BLE listener start has a generation-fenced resource set and per-run callbacks. Stop invalidates callbacks before cleanup; a late socket/server/registration closes immediately; old completion cannot stop a replacement; and an old BLE pipe cannot cancel a newer run's current connection. Service remains the Android host and approval/control dispatcher.

## Session behavior

`SECURE-SESSION-v1.md` is normative for the handshake and encrypted record format. Application plaintext is limited to 16,384 bytes. Every post-proof packet uses AES-256-GCM with a direction-specific key and exact next sequence; authentication or ordering failure closes the route.

Both sides send encrypted pings every 10 seconds. Android closes a route after 35 seconds without an incoming record; Windows applies the same read deadline. A newly authenticated route receives the latest full phone-state snapshot. Android sends another snapshot after normalized state changes.

The state manager serializes lifecycle changes and commands, fences callbacks from old routes, and gives first pairing up to two minutes for human confirmation. User commands have a five-second application deadline and are never retried after an ambiguous send. Windows waits for Android’s next state snapshot rather than changing media or DND state optimistically.

`IBleSessionFactory` and `IWifiSessionFactory` remain useful for tests and alternate transports. Since Stage 1B-W, they and the packaged `LivePhoneTransport` use the same Core `SessionPhoneTransport` owner. The packaged adapter retains `ConnectionFactories` and `SecurePhoneSession`. Each transport is one attempt; stop cancels and drains pending connection/read/send work. A returned session after cancellation is disposed without publishing Connected. Concurrent cleanup callers await the same release; canceling a stop wait does not stop cleanup. Factories/wires must honor cancellation or bounded completion and disposal must unblock reads/writes.

## Hardware boundary

The [Stage 0 audit](../CURRENT_STATE.md) is historical. [PROGRESS.md](../PROGRESS.md) records corrected application validation and Windows ownership plus remaining Android/reconnect/capability/security work. Six live-transport lifecycle scenarios now exercise the production handshake over fake frame connections; they do not constitute comprehensive reliability/security or physical-radio validation.

Automated .NET and JVM tests verify state routing, framing limits, identity proof, code agreement, encryption, and bidirectional messages. A physical Android 12+ phone is required to validate BLE peripheral support, radio behavior, Android notification/DND/clipboard surfaces, OEM background limits, sleep/resume, and the local firewall/network environment.
