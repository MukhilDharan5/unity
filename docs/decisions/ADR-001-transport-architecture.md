# ADR-001 — Transport architecture

## Status

The Stage 0 baseline is historical. Concurrent BLE and Wi-Fi ownership was later approved and implemented in [ADR-009](ADR-009-concurrent-ble-wifi.md) on 20 September 2026.

## Context

The product needs low-overhead presence/control plus richer local communication. Current Windows production code owns one BLE or TCP route. Android can authenticate both and prefers TCP. Both currently carry the same application payload types and bespoke secure records. mDNS is advertised only on Android; Windows accepts a manual endpoint. The user requires a transport abstraction and optional enhanced control outside the normal path.

## Decision

No new architecture is accepted in Stage 0. Retain transport/frame interfaces as useful baseline boundaries. Propose a single testable coordinator/ownership path and an explicit small-control versus rich-payload route policy. The ultimate BLE/LAN trust/channel role depends on ADR-003. No ADB transport, broker, server/service or extra transport framework is introduced.

## Alternatives

- Extend current route-specific orchestration: least immediate work, but preserves divergent cleanup/reconnect policy and cannot represent independent presence well.
- Consolidate route ownership behind the existing abstractions: recommended; supports conservative reconnect, capability/route gating and later transport promotion with focused changes.
- Replace both transports/frameworks now: high disruption without evidence that frame/platform adapters need total replacement.

## Consequences

Core state/features remain independent of radios. Lifecycle changes need production-path cancellation/close tests and power measurements. Concurrent Windows BLE/LAN behavior is resolved by ADR-009: both sessions remain authenticated, Wi-Fi is primary, BLE is warm fallback, and ambiguous commands are never replayed. Bootstrap format, BLE-only command requirements and secure-channel compatibility still require coordinated decisions before changes.
