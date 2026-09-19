# Stage 1 — Foundation consolidation in small checkpoints

Authorized to begin on 17 September 2026. [PROGRESS.md](../PROGRESS.md) is the durable current-status and resume log. Ask architectural questions directly in chat. ADR-003 now retains the isolated v1 channel for the MVP; ADR-004 remains pending. Neither decision authorizes a security or UI migration.

## Stage 1A — Strict v1 interoperability

Objective: remove reproduced validation/metadata differences so existing peers agree on the documented v1 contract.

Implement one shared fixture source for both runtimes; strict Android structural/type/state/UUID validation; nonthrowing rejection at the incoming application-command boundary; safe length/control normalization of Android media metadata; regression tests against production codec/helper behavior. Keep version 1, the current serializers/frameworks and existing features.

Defer secure-channel replacement, generic protocol negotiation/acks/errors, new permissions, transport selection changes, trust persistence changes and UI migration. Platform constraints: C# and Kotlin codecs are separate implementations; metadata lengths are UTF-16 code units; Android media apps provide arbitrary strings; unknown object fields remain compatible but cannot hide duplicate keys; malformed input must not execute commands.

Acceptance: common valid/invalid fixtures agree in both runtimes; existing semantic tests pass; Android never throws for a malformed incoming command; platform metadata remains Unicode-safe and Windows-compatible; Release/debug builds remain runnable; docs/log accurately record limitations.

Completed on 17 September 2026: 67 shared fixtures pass on both runtimes, core 26/26, Android 20/20 regular tests plus one optional skip, WPF smoke passes, Release/debug builds pass and lint has 0 errors/5 existing warnings. The six audit regressions are corrected. Secure envelope/control validation, hardware behavior and lifecycle ownership are outside this checkpoint. See [PROGRESS.md](../PROGRESS.md) for the changed files and exact resume action.

## Later checkpoints

1B-W completed: Windows live transport now uses the Core owner, with deterministic session release/draining, five owner tests and six real-handshake/live scenarios. Release build, 31 core tests and WPF smoke checks pass. Cryptographic object erasure and App reconnect policy remain follow-ups.

1B-A1 completed: Android session publication/revocation and collector ownership now use pure Kotlin production helpers. Ten new ownership/collection regressions pass; Android suite 30 regular tests plus one optional skip, debug assembly passes, lint 0 errors/5 existing warnings. Wire/crypto, trust schema, permissions, foreground hosting and Wi-Fi preference are preserved.

1B-A2 completed: Android LAN and BLE listeners use a shared generation-fenced lifecycle seam, per-run platform callbacks/resources, immediate cleanup of resources published after stop, and guarded endpoint/error/connection delivery. Four production-seam regressions pass; the full Android suite has 34 regular tests plus one optional skip, debug assembly passes, and lint remains at 0 errors/5 existing warnings. Physical radio/NSD behavior remains a device-test boundary.

1C completed at MVP pace: Windows reconnect wakes on network changes and is suppressed during sample-mode transitions; Android coordinates pairing expiry, listener retry/backoff, Bluetooth/network changes, permission refresh, Forget and destruction. Android Kotlin and Windows Release compile checks pass; broader validation is deferred.

1D completed: Android exposes explicit local permission and user-enablement state with direct actions. No wire fields or permissions were added.

1E MVP essentials completed: connection timing is centralized in typed policy objects and the Android build helper is portable. Structured diagnostics and broader toolchain work are deferred until after the functional MVP.

These bounded checkpoints complete the MVP-essential Stage 1 work. Structured diagnostics, broad validation and release hardening remain deferred. Continue user-facing phases in small checkpoints, update PROGRESS.md, and ask before new permissions, wire/storage formats, privileged integrations, security migration or Windows UI migration.
