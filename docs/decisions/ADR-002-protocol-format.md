# ADR-002 — Logical protocol format and evolution

## Status

JSON v1 retained. Strict validation/shared-fixture correction was completed in Stage 1A. The user approved additive bidirectional-media messages, the optional `brightness` snapshot section plus `brightness_command`, and on 20 September 2026 the optional `audioOutput` section plus `headphone_handoff`, explicit `hotspot_request`, and three-message audio stream negotiation. Replaceable Kotlin codec and general wire evolution remain proposed. No new protocol version or envelope was implemented.

## Context

The MVP has small typed C# messages and Kotlin serializable models. Windows has `IPhoneMessageCodec`; Android depends directly on its JSON singleton. Version/type exist, identity is session-bound, and media/DND support appears in state. Generic capabilities, correlated commands/errors and version-range negotiation are absent. Android acknowledgment models are unused by live dispatch. Audit probes established validation differences.

## Decision

Retain JSON v1 for existing messages. Stage 1A aligned strict application validation through 67 shared positive/negative fixtures and normalized Android media metadata before encoding. The approved media extension adds `pc_media` and `pc_media_command`. The approved brightness extension adds an optional bounded `brightness` snapshot section and `brightness_command`. The approved headphone extension adds optional bounded `audioOutput` state and an explicit `headphone_handoff` command without transmitting Bluetooth addresses. Stage 14 adds a fieldless, explicit-user-action `hotspot_request`; credentials and tether state remain outside the protocol. Stage 15 adds `audio_stream_start`, `audio_sink_ready`, and `audio_stream_stop`; continuous audio remains on a separately keyed Wi-Fi socket and never enters the companion frame stream. Older snapshots without optional sections remain valid. Older peers ignore new snapshot members or reject/ignore unknown application types without changing trust or transport state. Exposing a replaceable Kotlin serialization boundary remains proposed. Do not silently turn unused acknowledgment models into a supported contract.

The existing uppercase `docs/PROTOCOL.md` remains the canonical protocol file to preserve tracked paths and links. Windows resolves the requested lowercase `docs/protocol.md` to that file; no case-only rename or conflicting duplicate was created.

## Alternatives

- JSON with typed codec boundaries and common fixtures: recommended; readable, already implemented, bounded, no immediate new dependencies.
- Protobuf/CBOR immediately: could reduce bytes/schema ambiguity, but introduces tooling/dependencies and coordinated migration before bandwidth evidence justifies it.
- Platform-specific ad hoc JSON: least initial work but preserves incompatibilities and violates the replaceable serializer requirement.

## Consequences

Do not couple features/UI to JSON nodes. Normalize platform metadata before encoding. Changes to rejection rules may narrow accepted input and must be documented; existing Windows contract already requires strict types/duplicates. Capability negotiation, request IDs/acks/errors and downgrade policy need an approved extension design and cross-runtime tests. Format and envelope choices become costly after both deployed apps rely on them.
