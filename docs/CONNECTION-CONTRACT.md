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

Windows Wi-Fi connects to the `address:38471` shown by Android. TCP uses a four-byte big-endian length followed by one bounded secure packet. Android also advertises `_phonecomp._tcp` for future automatic browsing.

For BLE, Windows is the central/GATT client and Android is the peripheral/GATT server. Windows filters advertisements by the agreed service UUID, subscribes to the TX notification characteristic, and writes with response to RX. A one-byte sequence/first/last header fragments the same secure packets used by TCP. Both reassemblers enforce ordering, time, queue, and size bounds.

Windows tries a saved Wi-Fi address for eight seconds at startup, followed by a bounded BLE scan. The pairing window offers both routes explicitly. Android starts both listeners automatically for a trusted laptop and when the user taps **Start pairing** for a new laptop.

Android may hold authenticated BLE and Wi-Fi sessions simultaneously but routes application messages through one active session, preferring Wi-Fi. If Wi-Fi closes, it sends the latest snapshot over authenticated BLE. Windows owns one route at a time and clears displayed phone state immediately when that route disconnects.

## Session behavior

`SECURE-SESSION-v1.md` is normative for the handshake and encrypted record format. Application plaintext is limited to 16,384 bytes. Every post-proof packet uses AES-256-GCM with a direction-specific key and exact next sequence; authentication or ordering failure closes the route.

Both sides send encrypted pings every 10 seconds. Android closes a route after 35 seconds without an incoming record; Windows applies the same read deadline. A newly authenticated route receives the latest full phone-state snapshot. Android sends another snapshot after normalized state changes.

The state manager serializes lifecycle changes and commands, fences callbacks from old routes, and gives first pairing up to two minutes for human confirmation. User commands have a five-second application deadline and are never retried after an ambiguous send. Windows waits for Android’s next state snapshot rather than changing media or DND state optimistically.

The earlier `IBleSessionFactory` and `IWifiSessionFactory` abstractions remain useful for tests and alternate transports. The packaged app uses `LivePhoneTransport`, `ConnectionFactories`, and `SecurePhoneSession`.

## Hardware boundary

Automated .NET and JVM tests verify state routing, framing limits, identity proof, code agreement, encryption, and bidirectional messages. A physical Android 12+ phone is required to validate BLE peripheral support, radio behavior, Android notification/DND/clipboard surfaces, OEM background limits, sleep/resume, and the local firewall/network environment.
