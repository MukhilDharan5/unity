# Android implementation and constraints

Status: updated through the Stage 16 lock-only MVP, 20 September 2026. Sources are in `Unity_Connect_Android/app/src/main/java/com/unity/connect/android/`. See [PROGRESS.md](../PROGRESS.md) for continuation.

## Runtime and UI

Android 12/API 31 is the declared minimum; compile/target API is 34. The app uses Kotlin 1.9.20, Compose/Material 3, coroutines and kotlinx serialization. `MainActivity` starts the foreground connection service on launch, before its Compose runtime-permission action. `AppViewModel` observes a static service flow and calls static service actions. There is no bound-service/repository abstraction or user-facing stop-service switch.

The UI provides pairing code comparison, connection status/address, reconnect/forget, a combined device-permission request, settings shortcuts for media/DND/tethering access, Windows media state/controls, laptop-audio playback/stop state, current Bluetooth audio output and API 37+ one-tap association setup, companion-rule control and clipboard opt-in/manual send. Theme palettes are hard-coded light/dark schemes rather than dynamic system colors. Device access is requested as one bundle; missing optional phone-state permission continues to show the generic access action.

`ConnectionService` is non-exported, returns `START_STICKY` and hosts GATT/TCP listeners, secure pairing, public trust preferences, notifications and feature controllers. `PhoneSessionOwner` now reserves each pending/connected route, owns its coroutine job and guarded pipe, and fences session publication/trust persistence by generation. Forget invalidates admission before closing/canceling pending work; destruction also prevents further admission. Parent-scope cancellation closes pipes to interrupt blocking IO. Handshake guard, watchdog and heartbeat are children of the route job. Trusted service starts listen automatically. New pairing accepts approvals for two minutes, but expiry does not itself stop listeners/advertising. There is no boot receiver and no tested guarantee of unattended recovery after OEM process termination.

Stage 1B-A2 adds a shared `ListenerLifecycle` fence beneath both providers. Each LAN start owns its coroutine scope, listener, NSD registration and accepted sockets; a resource created after stop closes immediately, and an old completion cannot end a replacement. Each BLE start owns its GATT/advertising callbacks, server, advertiser, device and pipe. Stale callbacks are ignored, and an old pipe only cancels the connection when it is still that run's current pipe. Endpoint/error/connection delivery is serialized with stop. The bind port, NSD name/type, BLE UUIDs, low-power advertising, permissions, foreground policy, trust and wire bytes are unchanged.

## Permissions

| Access | Declared / requested use | Current behavior |
| --- | --- | --- |
| Internet/network/Wi-Fi state | LAN listener and network/address reporting | Declared normal permissions, including `CHANGE_NETWORK_STATE` |
| Nearby devices | `BLUETOOTH_CONNECT`, `BLUETOOTH_ADVERTISE` | Runtime UI request; GATT startup catches unavailable/denied access |
| Phone state | `READ_PHONE_STATE` | Runtime UI request; generation/signal become unknown when unavailable |
| Notifications | `POST_NOTIFICATIONS` on API 33+ | Included in runtime UI request; foreground notification is built by service |
| Foreground service | General and `FOREGROUND_SERVICE_CONNECTED_DEVICE` | Service declares `connectedDevice`; manifest network permission satisfies one documented prerequisite |
| Notification policy | `ACCESS_NOTIFICATION_POLICY` plus settings grant | Enables only the application's automatic DND rule |
| Modify system settings | `WRITE_SETTINGS` plus explicit special-access screen | Optional authenticated phone brightness changes; state/sensors remain readable without it |
| Notification listener | Exported listener service protected by system bind permission | Explicit settings shortcut for active media-session access; no unrelated notification content handling |
| Companion device setup | Optional device feature plus system-owned association consent | API 37+ one-tap release eligibility for the currently connected headset; no Bluetooth address is transmitted |

The Android app adds no location, screen-capture, accessibility, Shizuku or in-app ADB access. The optional ADB settings accelerator lives only in the Windows app. Future target-SDK/minimum-version changes require a compatibility decision and device tests.

Android connected-device foreground services have specific manifest/runtime prerequisites; declarations alone do not guarantee background execution or OEM survival. The implementation's `CHANGE_NETWORK_STATE` declaration is one accepted prerequisite. See [Android foreground service types](https://developer.android.com/develop/background-work/services/fgs/service-types#connected-device).

## State/event collection

`StateCollector` uses battery and ringer-mode broadcasts, default-network and telephony callbacks, active media-session and media-controller callbacks, plus the DND controller's state flow. `PhoneStateCollection` owns observer replacement and latest state. It cancels after the last route closes/Forget, waits for prior observer cleanup before replacement, and checks collection identity/session generation before emitting snapshots. Rapid restart retains the cleanup chain; an old finally block cannot clear new state. The service sends normalized-category full snapshots on changes, not a 500 ms polling loop.

`BrightnessController` observes system brightness/mode settings and the light, proximity and screen-state signals. Light readings use exponential smoothing and publish only after an absolute or relative change threshold. A covered state combines proximity with very low light, or screen-off with near-zero light; covered/unavailable states never transmit a usable lux value. The first implementation intentionally avoids a complex motion/orientation classifier. Authenticated brightness commands require the user-granted special settings access and preserve adaptive mode unless the separate adaptive command changes it.

`BluetoothAudioMonitor` obtains the public A2DP profile proxy and reacts to profile connection broadcasts. With the existing nearby-device permission it reads the first connected output, removes control characters, limits the display label to 80 characters and publishes the optional `audioOutput` snapshot state. An address is used only in process to build a user-visible Companion Device Manager association request; it is never sent to Windows. On API 37+, an associated headset reports `canRelease` and an authenticated handoff command calls the public `BluetoothDevice.disconnect()` method. The compile-SDK 34 build invokes that API reflectively after runtime and association checks. Older, unassociated or failed releases post a notification that opens Android Bluetooth settings. Shizuku and accessibility automation are excluded; see [ADR-005](decisions/ADR-005-headphone-handoff.md).

An authenticated `hotspot_request` never changes tethering state directly. `HotspotSettings` resolves a system-owned tethering settings activity when available and falls back to Android's public wireless/settings screens. The service exposes that flow through a user-action notification, and the Android app also has a visible **Open tethering settings** button. No password, SSID or tethering state is read or transmitted, and `LocalOnlyHotspot` is not presented as internet access. See [ADR-006](decisions/ADR-006-phone-internet.md).

`LaptopAudioReceiver` handles the laptop-to-phone Stage 15 prototype. After an authenticated start message it opens an ephemeral TCP listener, reports that port over the active secure session, validates the stream's one-use random token, decrypts sequence-checked AES-256-GCM records and writes PCM16 to `AudioTrack`. It requests media audio focus, stops on permanent focus loss/session loss/errors, and exposes a visible Stop action that also tells Windows to end capture. The feature adds no permission and does not capture phone apps, microphones or calls. See [ADR-007](decisions/ADR-007-laptop-audio-streaming.md).

Stage 16 adds a visible **Lock laptop** action and authenticated `pc_lock_request`. Incoming `phone_lock_request` uses `DevicePolicyManager.lockNow()` only after the user approves Android's system device-admin screen. The declared admin policy contains only `<force-lock>`; it cannot reset credentials, wipe data or unlock the phone. The Access card reports and opens this consent. Removing admin access makes the command unavailable immediately. A configured secure Android screen lock is still required for credential protection; Unity Connect never creates or handles it. See [ADR-008](decisions/ADR-008-cross-device-locking.md).

When phone-state access is available, `TelephonyCallback` reports data-network generation, 5G display overrides and normalized signal-strength changes. Default-network callbacks independently report whether Wi-Fi or mobile data is active. Missing permission keeps radio details unknown while preserving available connection-route state. Multi-SIM behavior still follows Android's default `TelephonyManager` and needs physical-device validation.

Media selection prefers playing, then paused controllers. `MediaMetadataNormalizer` replaces control characters with spaces, trims blank values to unavailable, and bounds source to 80 and title/artist to 256 UTF-16 code units without cutting a surrogate pair. `StateCollector` applies it before building protocol state. Controller selection compares object references rather than tokens, potentially replacing callbacks unnecessarily. The dispatcher targets the selected controller; actual app action support/session disappearance needs integration tests.

Authenticated `pc_media` updates populate the Android **Playing on laptop** card. Previous, play/pause and next are enabled only when Windows advertises the matching action. Android sends one `pc_media_command` at a time, does not update playback optimistically, and clears laptop media when the companion session ends.

## DND and clipboard

`CompanionDndController` creates/owns one `AutomaticZenRule` and changes its condition through `CompanionDndConditionProvider`. Effective DND is read separately. No global interruption-filter setter exists in this code. Rule condition is stored in preferences; actual provider binding, manual overrides and system rule settings remain device-test items.

Clipboard sync is independently opt-in, off by default, persists only its preference, and keeps a bounded 64-ID in-memory suppression window. The app reads its clipboard only through the visible send action; incoming authenticated text can be applied while enabled. Ordinary background apps cannot freely read the clipboard on Android 10+, which constrains automatic phone-to-PC synchronization. See [Android clipboard privacy](https://developer.android.com/about/versions/10/privacy/changes#clipboard-data).

Sensitive clipboard marker filtering, history and support/enablement negotiation do not exist. `coerceToText` may obtain a text representation of a clip rather than checking that its original content was plain text.

## Trust and routes

`IdentityStore` creates a P-256 Android Keystore signing identity without mandatory hardware-backed attestation or user authentication for each connection. Public peer identity/name live in private `secure_peers_v1` preferences. Backup is disabled in the manifest. Trust is committed after secure consent/hello, but the returned commit result is ignored.

GATT and TCP carry the same bespoke v1 secure records. LAN binds port 38471 and advertises `_phonecomp._tcp.`; the UI displays an IPv4 Wi-Fi/hotspot address. Service address refresh is not a dedicated network-address observer. Two routes can authenticate; Wi-Fi has application priority. Both routes heartbeat every 10 seconds; a 5-second watchdog closes a route once received-record silence exceeds 35 seconds.

The owner selects authenticated Wi-Fi before BLE, preserves BLE after Wi-Fi closes, and closes an ambiguous failed send without retrying it on another route. Incoming features are accepted only from the current active lease. Secure-session and pipe close are idempotent; encryption/key erasure remains separate debt. Ten JVM regressions exercise actual owner/collection helpers, including real secure handshakes with fake pipes. Android Service/SharedPreferences/system interactions still need device/instrumentation validation.

The codec remains a serialization singleton rather than a replaceable protocol interface. `V1MessageValidator` enforces the existing strict application contract, including nested/escaped duplicate keys and canonical clipboard UUIDs. Named application encoders and incoming command decoding use it; malformed command input returns null. Generic legacy serializers are structural helpers, and unused acknowledgment models are not a live contract. Both runtimes pass 67 shared application fixtures; see [testing.md](testing.md). Secure-envelope/control parsing runs before this command boundary and was not consolidated in Stage 1A. Proposed work remains coordinator extraction, event-aware capability/permissions, lifecycle tests and approved secure-channel integration. Physical BLE, DND, media, clipboard, sleep, permission revocation and OEM behavior remain unverified.
