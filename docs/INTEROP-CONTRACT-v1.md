# Phone Companion - Implemented Interoperability Contract v1

This document defines the implemented version 1 contract shared by the Windows Phone Companion and Unity Connect for Android. `SECURE-SESSION-v1.md` specifies the byte-level secure handshake and record format.

## 1. BLE Roles and Configuration
*   **Roles:** Windows acts as the BLE Central (GATT Client); Android acts as the BLE Peripheral (GATT Server, Advertiser).
*   **Service UUID:** `8ec8b6c0-eb89-4b2a-881b-a5d5a7114b0b`
*   **Characteristics:**
    *   **RX Characteristic (Windows to Android):** `8ec8b6c1-eb89-4b2a-881b-a5d5a7114b0b` - Properties: `Write` (with response)
    *   **TX Characteristic (Android to Windows):** `8ec8b6c2-eb89-4b2a-881b-a5d5a7114b0b` - Properties: `Notify`
*   **Advertising:** Android advertises the Service UUID. Its readable Bluetooth name is optional scan-response data and is never treated as identity.
*   **MTU and Framing:** The current GATT MTU/PDU size is capped at 512 for fragmentation calculations.
*   **Fragmentation (Logical over BLE):**
    *   1-byte header per fragment: Bit 7 (First Fragment), Bit 6 (Last Fragment), Bits 0-5 (Sequence number 0-63).
    *   Max wire packet: 16,408 bytes (16,384-byte application message plus secure-record overhead).
    *   Reassembly timeout: 15 seconds. Invalid, overlapping, incomplete, or over-limit frames close the route.

## 2. LAN Roles and Configuration
*   **Roles:** Windows acts as the TCP Client; Android acts as the TCP Server.
*   **Endpoint Discovery:** Android advertises `_phonecomp._tcp` with mDNS and displays its reachable Wi-Fi `address:port`. Windows v1 accepts the displayed address, so it also works on networks where mDNS browsing is blocked.
*   **Port:** Android listens on TCP 38471 and shows `address:38471` while the server is active.
*   **Framing:** 4-byte big-endian length prefix followed by one secure-session packet. Length is 1 through 16,408 bytes.
*   **Untrusted Networks:** LAN discovery and connections are permitted on any network, but security/encryption applies unconditionally.

## 3. Security, Enrollment, and Authentication
*   **Identity:** Both devices generate static P-256 ECDSA identity key pairs. Android keeps its private key in Android Keystore; Windows keeps its non-exportable private key in the Windows CNG key store.
*   **Pairing Flow (Both Transports):**
    *   Commit/reveal prevents either peer from choosing its ephemeral P-256 ECDH key after seeing the other peer's key.
    *   Each peer signs the role-ordered transcript with its ECDSA identity.
    *   HKDF-SHA-256 derives directional keys and a transcript-bound six-digit code.
    *   New trust requires explicit confirmation of the identical code on both screens. A known identity reconnects without another prompt.
*   **Storage:** Only peer public identity, friendly name, and the last Wi-Fi endpoint are persisted. No shared session secret or clipboard content is stored.
*   **Encryption and Replay Protection:** AES-256-GCM. A strictly incrementing 64-bit sequence is maintained independently for each direction per session. The transcript and sequence are authenticated; duplicate or out-of-order records are rejected.
*   **Revocation and Repair:**
    *   "Forget" action on either device deletes the trusted public key.
    *   Subsequent connection attempts fail authentication, requiring the user to re-initiate the pairing flow.
    *   App reset/uninstall clears local keys; peering must be repeated.

## 4. Session Lifecycle and Routing
*   **Version Negotiation:** First encrypted message after handshake is `{"type": "hello", "version": 1}`. If `version` is not 1, the session is terminated.
*   **Route Preference:** Android prefers an authenticated LAN session when both LAN and BLE are live. Application state and commands use one active route, with BLE as fallback.
*   **Initial State:** Upon a successful, authenticated session establishment, Android immediately sends a `snapshot` frame containing all 5 state categories.
*   **Updates:** Android sends the latest normalized snapshot whenever collected phone state changes.
*   **Stale State and Disconnection:** If the transport disconnects, Windows immediately clears all state to disconnected.
*   **Liveness:**
    *   LAN: TCP Keep-alive + Application-level ping every 10 seconds. Timeout at 30 seconds.
    *   BLE: Relies on Android's GATT disconnect callback.
*   **Reconnect Behavior:** Windows makes one short startup attempt to the last Wi-Fi endpoint, followed by a bounded BLE discovery attempt. The user can reconnect immediately from **Connect phone**.

## 5. Command Semantics
*   **Confirmation:** Media and DND commands do not update Windows state optimistically. The resulting Android snapshot is the authoritative confirmation. Version 1 does not retry toggle/skip commands after an ambiguous transport failure.
*   **Media Targeting:**
    *   Android caches the `MediaSession.Token` for the session currently providing the active state.
    *   Commands are dispatched strictly to that cached token. If the session is dead, the command fails and Android triggers a media state update with `null`.

## 6. Android Permissions and State Mapping
*   **Permissions:** Prompts explain why access is needed. Denied permissions result in `null` (unavailable) for that specific category in the `snapshot`/updates.
*   **Cellular Mapping:**
    *   *Network (TelephonyManager DataNetworkType):* `NR` -> `5g`, `LTE` -> `4g`, `UMTS/HSPA/HSDPA/etc` -> `3g`, `EDGE/GPRS/CDMA` -> `2g`, else `unknown`.
    *   *Signal (CellSignalStrength Level 0-4):* 0 -> `none`, 1 -> `poor`, 2 -> `fair`, 3 -> `good`, 4 -> `excellent`.
    *   *In Use:* Determined by `ConnectivityManager.getActiveNetwork()` having the `TRANSPORT_CELLULAR` capability.
*   **Battery:** Uses `ACTION_BATTERY_CHANGED`. Clamps percentage to 0-100.
*   **DND (NotificationManager):**
    *   `INTERRUPTION_FILTER_ALL` -> `false`.
    *   `INTERRUPTION_FILTER_PRIORITY/ALARMS/NONE` -> `true`.
*   **Sound (AudioManager RingerMode):**
    *   `RINGER_MODE_NORMAL` -> `normal`.
    *   `RINGER_MODE_VIBRATE` -> `vibrate`.
    *   `RINGER_MODE_SILENT` -> `silent`.

## 7. Companion DND synchronization

*   Android reports both effective DND and the app-owned rule state: `enabled`, nullable `companionRuleActive`, and `canControlCompanionRule`.
*   Windows sends `{"version":1,"type":"dnd_rule_command","active":true|false}` only after an authenticated session reports the control capability.
*   Android owns one `AutomaticZenRule` and changes only that rule's condition. It does not call a global/manual interruption-filter setter. A false companion condition therefore does not disable DND requested by the user or another rule.
*   Windows does not update the displayed effective DND value optimistically. Android sends the resulting DND state after processing the command.
*   Notification-policy access is user-granted and revocable. Missing access produces `canControlCompanionRule:false`; it does not fabricate an effective state.

## 8. Clipboard synchronization

*   Clipboard sync is independently opt-in on both devices and off by default. Enabling it does not send the current clipboard.
*   Only non-empty plain text is supported. Maximum text size is 12,288 UTF-8 bytes; NUL-containing and oversized values are rejected.
*   The bidirectional logical frame is `{"version":1,"type":"clipboard","updateId":"<canonical UUID>","text":"..."}`.
*   Each side keeps a bounded in-memory window of 64 update IDs for duplicate and echo suppression. Clipboard content and history are never persisted or logged.
*   Clipboard messages are allowed only after peer authentication and travel inside the same encrypted, replay-protected session as state and commands.
*   Windows observes future local text changes while sync is enabled. Because Android 10+ restricts clipboard reads by ordinary background apps, Android reads and sends its clipboard only after the user taps **Send current clipboard** while the activity is visible. Android may apply authenticated incoming laptop text while sync is enabled.
*   Future privacy filters or history controls require a new compatible protocol addition. Version 1 contains no history request or history payload.

## 9. Implementation status

The logical messages, BLE/TCP framing, mutual identity proof, code confirmation, encrypted records, replay ordering, route selection, Android collectors/controllers, and Windows live adapters are implemented. Automated .NET and JVM tests cover secure pairing and bidirectional encrypted messages. Physical-device validation has not been performed in this environment and remains the last hardware-specific check.

Platform references: [AutomaticZenRule](https://developer.android.com/reference/android/app/AutomaticZenRule), [Android clipboard guidance](https://developer.android.com/develop/ui/views/touch-and-input/copy-paste), and [Android 10 clipboard privacy changes](https://developer.android.com/about/versions/10/privacy/changes).
