# ADR-005 — Bluetooth headphone handoff control path

## Status

**Accepted and implemented for the MVP: A + C, with Shizuku excluded.** Recorded 20 September 2026.

## Context

Stage 13 aims to move a Bluetooth audio device from the phone to the laptop. Android's public `BluetoothA2dp` API can report connected audio devices with the already-granted nearby-device permission, but it does not expose a disconnect method on the project's Android 12–14 compatibility range. Android API 37 adds `BluetoothDevice.disconnect()`, but the caller must also hold privileged Bluetooth access or have a user-approved Companion Device Manager association with that audio device. Windows can enumerate and pair devices, while its documented public device APIs do not provide a general command that transfers an arbitrary A2DP headset away from another host. Enabling or disabling a Bluetooth service installs or removes its driver and is not a handoff operation.

The foundation observes Android A2DP connection changes through callbacks and normalizes a bounded display name. The completed MVP sends that label and public-release availability in optional authenticated snapshot state. Bluetooth addresses remain local to Android.

## Decision

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| A — Public API plus assisted fallback (recommended) | API 37+ can use a user-approved Companion Device association; Android 12–16 can guide the user through Bluetooth settings; no privileged helper | Older phones are not one-tap; association adds a system consent flow; requires a small protocol extension |
| B — Optional Shizuku provider | Could call privileged Bluetooth services on more Android versions | Rejected for the MVP and explicitly excluded from this implementation |
| C — Optional ADB accelerator | Reuses an authorized developer setup to foreground Android Bluetooth settings | Does not provide a standard per-device disconnect; debugging remains external and optional |
| D — Accessibility/UI automation | Could imitate settings taps on some devices | Fragile, intrusive and difficult to secure or support; not recommended |

Implement A and C. The authenticated v1 snapshot carries `audioOutput.deviceName` and `audioOutput.canRelease`; the authenticated `headphone_handoff` command represents an explicit click. API 37+ offers **Enable one-tap handoff**, launches the system Companion Device Manager consent UI for the current headset, and calls public `BluetoothDevice.disconnect()` only when that association exists. Older, unassociated or failed releases post a notification that opens Android Bluetooth settings.

Windows always opens its Bluetooth settings for device selection. When assisted release is needed, an isolated optional ADB helper may open Android's public Bluetooth settings action if it finds exactly one authorized device. Standard AOSP `cmd bluetooth_manager` has no documented per-device disconnect command, so ADB does not simulate direct release. Shizuku and accessibility automation are excluded. ADB never carries normal companion traffic.

## Consequences

The public path preserves the app's permission baseline and gives every supported phone an understandable flow, while automatic release is limited to API 37+ and a separately associated headset. The optional ADB accelerator improves the guided path without promising an unsupported disconnect operation. The user must still select/connect the headset in Windows. Physical headset, API 37 association and older-device notification/ADB behavior remain to be validated.
