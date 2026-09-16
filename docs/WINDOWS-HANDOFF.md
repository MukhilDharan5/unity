# Windows live-connection implementation

The Windows handoff is complete in the packaged application.

- `ConnectionFactories.OpenWifiAsync` opens bounded length-prefixed TCP frames to Android at port 38471.
- `ConnectionFactories.OpenBleAsync` filters the Android service advertisement, opens GATT, subscribes to TX notifications, and writes fragmented packets with response to RX.
- `SecurePhoneSession` implements the mutual P-256 identity proof, transcript-bound confirmation code, HKDF-SHA-256 keys, AES-256-GCM records, replay ordering, hello exchange, and heartbeat.
- `IdentityAndTrustStore` keeps the Windows identity in CNG and persists only the approved phone fingerprint, name, and Wi-Fi endpoint.
- `PairingWindow` exposes Wi-Fi and BLE connection choices and requires code confirmation for new trust.
- `App.xaml.cs` performs bounded trusted reconnects and feeds `LivePhoneTransport` into the shared state manager.

The remaining handoff is hardware validation on an Android 12+ phone: confirm BLE peripheral support, LAN reachability, reconnect after sleep, Android permission screens, media commands, DND behavior, clipboard behavior, and vendor background restrictions.
