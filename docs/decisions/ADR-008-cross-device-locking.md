# ADR-008 — Cross-device locking

## Status

Accepted and implemented at MVP scope on 20 September 2026. The user approved lock-only behavior in both directions. Phone-assisted Windows unlock is excluded.

## Context

Locking and unlocking have different security boundaries. Windows documents `LockWorkStation` for an interactive application, and Android device administration exposes a narrow `force-lock` policy after a system-owned user consent flow. Neither API grants the companion a way to authenticate or unlock the device.

Bluetooth signal strength is too unstable to act as a single leave-range threshold. Normal route loss can also come from interference, Android power management, laptop sleep, network changes, or a temporary listener restart. Automatic locking therefore needs time, connection, and local activity safeguards.

Windows Credential Providers collect and serialize credentials for the Windows authentication system; they do not let a tray application declare a remote phone authenticated and unlock the workstation. Any later unlock design requires a separate Credential Provider/service architecture, challenge-response keys and independent review.

## Decision

Add two explicit authenticated, fieldless commands. `phone_lock_request` lets Windows ask Android to lock. Android performs it only while the user has enabled Unity Connect as a device administrator with the sole `<force-lock>` policy. `pc_lock_request` lets Android ask the current interactive Windows app to call `LockWorkStation`. The commands carry no credential, PIN, biometric result, reusable token, or unlock capability.

Windows also offers an optional **Lock Windows when phone is away** switch. It is off on every application launch and is never persisted. Enabling it does not arm the feature until this process has observed a real authenticated phone connection. After that connection disappears, a two-minute reconnect grace begins. Reconnection cancels the pending lock. After the grace period, Windows must also report at least 30 seconds without local input before the app attempts one lock for that absence episode. A detected suspend/resume gap restarts the full grace period. A new authenticated connection resets the episode. Demo/sample state never arms it, and RSSI is not used.

Unlock remains entirely with each device's normal secure sign-in. The feature does not install a Windows service or Credential Provider and does not accept Android biometrics as Windows authentication.

## Consequences

Android users must approve a visible system device-admin screen before Windows can lock the phone. If the phone has no secure screen lock configured, Android's platform behavior may only turn the display off; Unity Connect does not create or change a PIN. Removing device-admin access immediately makes remote phone lock unavailable.

The Windows proximity option runs only while Unity Connect is running in the interactive user session. It is a convenience safeguard, not a presence proof or theft-resistant control. A sleeping, killed, power-restricted, or unreachable companion can delay or prevent correct presence updates, so physical-device sleep, OEM power management and reconnection behavior require later validation.
