# ADR-005 — Bluetooth headphone handoff control path

## Status

**Provider foundation implemented; transfer path requires user decision.** Recorded 20 September 2026.

## Context

Stage 13 aims to move a Bluetooth audio device from the phone to the laptop. Android's public `BluetoothA2dp` API can report connected audio devices with the already-granted nearby-device permission, but it does not expose a disconnect method on the project's Android 12–14 compatibility range. Android API 37 adds `BluetoothDevice.disconnect()`, but the caller must also hold privileged Bluetooth access or have a user-approved Companion Device Manager association with that audio device. Windows can enumerate and pair devices, while its documented public device APIs do not provide a general command that transfers an arbitrary A2DP headset away from another host. Enabling or disabling a Bluetooth service installs or removes its driver and is not a handoff operation.

The implemented foundation observes Android A2DP connection changes through callbacks, normalizes only a display name, and shows the current local audio output in the Android app. It does not send the device name or address, disconnect anything, add a permission, use hidden APIs, or change the protocol.

## Decision needed

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| A — Public API plus assisted fallback (recommended) | API 37+ can use a user-approved Companion Device association; Android 12–16 can guide the user through Bluetooth settings; no privileged helper | Older phones are not one-tap; association adds a system consent flow; requires a small protocol extension |
| B — Optional Shizuku provider | Can call privileged Bluetooth services on more Android versions after explicit setup | Adds a privileged dependency and setup UX; OEM/version behavior varies; larger trust/support surface |
| C — Optional ADB provider | Reuses the already-optional enhanced path and may automate supported devices | Commands are version/OEM fragile, debugging must remain authorized, and it must not become the normal transport |
| D — Accessibility/UI automation | Could imitate settings taps on some devices | Fragile, intrusive and difficult to secure or support; not recommended |

Recommend Option A for the MVP: add an authenticated optional audio-device state and handoff request, use direct release only where the public API and association allow it, and otherwise open the narrowest user-assisted system flow. Keep Shizuku/ADB as future opt-in providers.

## Consequences

Option A preserves the app's public-API baseline and gives every supported phone an understandable path, but automatic transfer will initially be limited to new Android versions and associated headsets. The protocol must carry a bounded display label, availability reason and explicit user request; Bluetooth addresses must not cross devices. Choosing Shizuku or ADB later would add isolated providers behind the same capability model rather than changing the normal BLE/LAN transport.
