# ADR-003 — Trusted identities and secure-channel direction

## Status

**MVP direction accepted; production security review remains required.** Proposed 17 September 2026; accepted as the interim MVP direction on 19 September 2026. The current v1 channel remains isolated behind session/transport interfaces and has not received an independent security review.

## Context

Current pairing uses persistent P-256 CNG/Android Keystore signing identities, a bespoke committed ephemeral-key/proof exchange, transcript-derived code comparison, mutual encrypted consent/hello and directional AES-GCM records. One peer is persisted on each platform. Tests prove successful interoperability, not adversarial assurance. The user requires cryptographic trust, established primitives, no invented cryptography and explicit approval of major security/transport/storage decisions.

## Decision

Choose a channel direction before expanding sensitive functionality:

| Option | Advantages | Disadvantages |
| --- | --- | --- |
| A — Isolate/retain v1 temporarily and require independent adversarial review/hardening before release | Preserves BLE-only application commands and current wire/pairing behavior; smallest immediate implementation disruption | Continues owning a bespoke handshake/record construction, its audit burden and unresolved malicious-input/lifecycle behavior |
| B — Established TLS sessions for LAN application traffic; BLE initially discovery/presence/bootstrap | Reduces bespoke security code and uses established channel implementations; generic identities/state/providers remain reusable | Requires approved authenticated first-trust provisioning, certificate/key-store feasibility, re-pairing/migration/downgrade policy; BLE-only application commands are unavailable until a reviewed channel is added |

For the MVP, use Option A: retain the current encrypted v1 channel and continue user-facing feature work. Defer TLS/trust migration, adversarial review and release hardening until after the MVP. This decision does not certify the channel as production-ready and does not approve sensitive features that need separate product or permission decisions.

For post-MVP work, Option B remains the recommended direction, with a bounded feasibility/design substage before implementation. Neither accepting arbitrary TLS certificates nor putting an unauthenticated public key into a pin store establishes trust. Do not promise that current identity keys/records migrate unchanged without testing platform APIs.

## Alternatives

An established maintained secure-channel library suitable for both raw BLE frames and LAN could preserve BLE command functionality, but no cross-runtime library/dependency has been selected or vetted in Stage 0. This is follow-up research if BLE-only control is required, not an invented third protocol. Pairing by MAC/IP/name/proximity or plaintext sensitive commands is excluded by the user's requirements.

## Consequences and difficult later changes

Handshake/record compatibility, first-trust UX, key/certificate persistence, recovery and BLE-only functionality become more expensive to change after devices are paired and deployed. A channel replacement must define version behavior, revocation, authenticated provisioning, failure UX and whether users must re-pair. Sensitive new features remain gated until the selected architecture and adversarial/device validation are satisfied. Windows unlock remains separate and unapproved.
