# Stage 1 — Foundation consolidation in small checkpoints

Authorized to begin on 17 September 2026. [PROGRESS.md](../PROGRESS.md) is the durable current-status and resume log. Ask architectural questions directly in chat. Pending ADR-003/ADR-004 choices do not authorize security/UI migrations and do not block corrective v1 work.

## Stage 1A — Strict v1 interoperability

Objective: remove reproduced validation/metadata differences so existing peers agree on the documented v1 contract.

Implement one shared fixture source for both runtimes; strict Android structural/type/state/UUID validation; nonthrowing rejection at the incoming application-command boundary; safe length/control normalization of Android media metadata; regression tests against production codec/helper behavior. Keep version 1, the current serializers/frameworks and existing features.

Defer secure-channel replacement, generic protocol negotiation/acks/errors, new permissions, transport selection changes, trust persistence changes and UI migration. Platform constraints: C# and Kotlin codecs are separate implementations; metadata lengths are UTF-16 code units; Android media apps provide arbitrary strings; unknown object fields remain compatible but cannot hide duplicate keys; malformed input must not execute commands.

Acceptance: common valid/invalid fixtures agree in both runtimes; existing semantic tests pass; Android never throws for a malformed incoming command; platform metadata remains Unicode-safe and Windows-compatible; Release/debug builds remain runnable; docs/log accurately record limitations.

Completed on 17 September 2026: 67 shared fixtures pass on both runtimes, core 26/26, Android 20/20 regular tests plus one optional skip, WPF smoke passes, Release/debug builds pass and lint has 0 errors/5 existing warnings. The six audit regressions are corrected. Secure envelope/control validation, hardware behavior and lifecycle ownership are outside this checkpoint. See [PROGRESS.md](../PROGRESS.md) for the changed files and exact resume action.

## Later checkpoints

1B-W completed: Windows live transport now uses the Core owner, with deterministic session release/draining, five owner tests and six real-handshake/live scenarios. Release build, 31 core tests and WPF smoke checks pass. Cryptographic object erasure and App reconnect policy remain follow-ups.

1B-A1 completed: Android session publication/revocation and collector ownership now use pure Kotlin production helpers. Ten new ownership/collection regressions pass; Android suite 30 regular tests plus one optional skip, debug assembly passes, lint 0 errors/5 existing warnings. Wire/crypto, trust schema, permissions, foreground hosting and Wi-Fi preference are preserved.

1B-A2 next: Android listener startup/shutdown resource ownership and stale provider callbacks/endpoints. Explain bounded scope first; no handshake redesign. Session fencing in A1 does not prove radio/listener resources stopped correctly.

1C: event-aware bounded reconnection and lifecycle/power-related behavior, with cancellation and sample/pairing/exit tests.

1D: local capability/permission/user-enablement state. A new negotiation wire contract needs an explicit architectural decision first.

1E: redacted diagnostics, typed policy/settings and reproducible build helpers/pins. Persistence, new dependencies and compatibility changes must be discussed when material.

These are bounded checkpoints inside Stage 1, not permission to implement all at once. Keep apps runnable, validate each checkpoint, update PROGRESS.md, report completion/limitations and propose the next checkpoint. Security and Windows UI migrations need their own approved design and stage.
