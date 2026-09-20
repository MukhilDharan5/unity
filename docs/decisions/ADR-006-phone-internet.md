# ADR-006 — Phone internet workflow

## Status

**Accepted and implemented for the MVP as a user-assisted platform-settings flow.** Recorded 20 September 2026.

## Context

Stage 14 asks Windows to use the phone's internet when the laptop has none. Android's local-only hotspot is intentionally unsuitable because it does not provide upstream internet. Ordinary third-party apps also do not have a stable public API to silently enable carrier tethering or read the user's hotspot credentials across the supported Android range. Windows owns its saved Wi-Fi profiles and exposes a documented Wi-Fi settings URI.

## Decision

Windows observes the platform connectivity level and shows **Use phone internet** only for a connected real phone when Windows does not report `InternetAccess`. The click sends an authenticated fieldless `hotspot_request`. Android posts a user-action notification and exposes a local button that resolve the closest system-owned tethering screen, falling back to public wireless/general settings.

Windows then opens `ms-settings:network-wifi`. If the phone hotspot is already saved and configured for automatic connection, Windows can reconnect using its own profile; otherwise the user chooses it and enters credentials in Windows. Unity Connect does not request, store or transmit the hotspot SSID or password.

The existing optional ADB discovery is generalized as `AndroidSettingsAssistant`. When exactly one authorized device is attached it can foreground the same tethering/wireless settings action. It never toggles tethering, runs an OEM or hidden service command, or carries companion traffic. No Samsung Modes & Routines contract is assumed.

## Consequences

This provides a dependable, understandable MVP on the supported baseline without privileged access. It still requires a phone-side tap and may require initial Windows network selection. The Windows connectivity level is a hint and OEM settings destinations vary, so captive portals, automatic saved-profile connection and physical Android settings behavior remain for device testing.
