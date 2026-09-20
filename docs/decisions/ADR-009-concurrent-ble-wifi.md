# ADR-009 — Concurrent BLE and Wi-Fi sessions

## Status

Accepted and implemented for the MVP on 20 September 2026 at the user's request.

## Context

Android already allowed one authenticated BLE session and one authenticated Wi-Fi session to coexist, but Windows owned only one route and replaced it when another connected. That prevented warm fallback and made the connection appear to be either Bluetooth or Wi-Fi.

Sending the same state-changing command over both routes would risk duplicate play/pause, lock, clipboard, DND, and other actions. A failed write is also ambiguous: the peer may have performed the command before the failure became visible.

## Decision

Windows and Android may each keep one authenticated BLE route and one authenticated Wi-Fi route connected to the same remembered identity. Wi-Fi is the deterministic primary application route; BLE remains connected as a warm fallback. State and commands use only the primary route. If Wi-Fi ends, later messages use BLE immediately while the reconnect coordinator restores Wi-Fi. A failed command is never replayed automatically on the other route.

Windows reconnect now continues until both routes are authenticated. Losing either route starts recovery without clearing phone state while the other remains connected. The UI reports Wi-Fi, Bluetooth, or Wi-Fi + Bluetooth. Demo transport remains exclusive and cannot coexist with a physical route.

Bulk laptop audio remains Wi-Fi-only on its separately encrypted socket. Keeping BLE authenticated at the same time does not make BLE an audio data path.

## Consequences

The two live routes consume more radio and heartbeat resources than a single route. They improve readiness and failover but do not bond bandwidth or merge packets. Physical testing must measure Android background behavior, battery impact, reconnect churn, sleep/resume, and devices that cannot sustain BLE peripheral activity while Wi-Fi is active.
