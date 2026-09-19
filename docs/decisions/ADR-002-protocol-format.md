# ADR-002 — Logical protocol format and evolution

## Status

JSON v1 retained. Strict validation/shared-fixture correction was completed in Stage 1A. On 19 September 2026, the user approved an additive bidirectional-media extension using `pc_media` and `pc_media_command`. Replaceable Kotlin codec and general wire evolution remain proposed. No new protocol version or envelope was implemented.

## Context

The MVP has small typed C# messages and Kotlin serializable models. Windows has `IPhoneMessageCodec`; Android depends directly on its JSON singleton. Version/type exist, identity is session-bound, and media/DND support appears in state. Generic capabilities, correlated commands/errors and version-range negotiation are absent. Android acknowledgment models are unused by live dispatch. Audit probes established validation differences.

## Decision

Retain JSON v1 for existing messages. Stage 1A aligned strict application validation through 67 shared positive/negative fixtures and normalized Android media metadata before encoding. The approved media extension adds `pc_media`, carrying the existing bounded media-state shape from Windows to Android, and `pc_media_command`, carrying the existing three capability-gated commands from Android to Windows. Older peers reject or ignore these unknown application types without changing trust or transport state. Exposing a replaceable Kotlin serialization boundary remains proposed. Do not silently turn unused acknowledgment models into a supported contract.

The existing uppercase `docs/PROTOCOL.md` remains the canonical protocol file to preserve tracked paths and links. Windows resolves the requested lowercase `docs/protocol.md` to that file; no case-only rename or conflicting duplicate was created.

## Alternatives

- JSON with typed codec boundaries and common fixtures: recommended; readable, already implemented, bounded, no immediate new dependencies.
- Protobuf/CBOR immediately: could reduce bytes/schema ambiguity, but introduces tooling/dependencies and coordinated migration before bandwidth evidence justifies it.
- Platform-specific ad hoc JSON: least initial work but preserves incompatibilities and violates the replaceable serializer requirement.

## Consequences

Do not couple features/UI to JSON nodes. Normalize platform metadata before encoding. Changes to rejection rules may narrow accepted input and must be documented; existing Windows contract already requires strict types/duplicates. Capability negotiation, request IDs/acks/errors and downgrade policy need an approved extension design and cross-runtime tests. Format and envelope choices become costly after both deployed apps rely on them.
