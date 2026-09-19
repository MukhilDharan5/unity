# Incremental roadmap

Status: Stage 0, Stage 1A, Stage 1B-W, Stage 1B-A1 and Stage 1B-A2 complete through 19 September 2026; Stage 1 overall remains in progress. [PROGRESS.md](../PROGRESS.md) and the [small-stage plan](stage-1-plan.md) define the next checkpoint. Existing code already contains parts of pairing, transport, state, UI, media, DND and clipboard, so later phases validate/refine them rather than pretend they are absent.

## Stage 0 — Audit (complete)

Objective: inspect structure, dependencies, platform APIs, state, UI, communications, trust and tests before significant changes. Delivered root `CURRENT_STATE.md`, current-baseline platform/security/capability/development/testing docs, corrected legacy statements, proposed ADRs and local validation evidence. Preserved app code and existing edits. No feature implementation or architecture decision was committed.

## Stage 1 — Foundation consolidation (authorized; first checkpoint complete)

Objective: make existing foundations coherent and testable. Implement shared positive/negative wire fixtures, validation parity/metadata normalization, a single production lifecycle with fake-frame test seams, bounded event-aware reconnect coordination, a small Android coordinator, explicit capability/permission/enablement state, redacted structured diagnostics and typed settings, and reproducible build helpers/pins.

Stage 1A delivered strict application validation, metadata normalization and 67 shared fixtures. Stage 1B-W consolidated Windows live/core ownership (31 core tests, six live scenarios, UI smoke/build checks). Stage 1B-A1 added Android generation-fenced session/collector ownership. Stage 1B-A2 added per-run LAN/BLE resource ownership and stale callback fencing (34 regular tests, debug assembly/lint pass). Next: Stage 1C conservative reconnect and lifecycle coordination. Later checkpoints remain planned; security/UI decisions and protocol extensions require separate discussion.

Defer all new user-facing features, framework migration, privileged APIs, clipboard history, cloud, storage engines and Windows services. Discuss wire additions, storage/permissions and compatibility before changing them. Security channel replacement must be an explicitly approved focused substage with pairing/storage/migration feasibility work; it is not routine cleanup.

Constraints: Android foreground/background policy and clipboard privacy, optional/revocable access, BLE peripheral variability, Windows user-process/API boundaries, C#/Kotlin validation parity and idle radio use. Exit criteria: both builds/checks remain runnable; agreed fixtures match; actual ownership/cancellation/revocation paths are tested; generic capabilities have truthful unavailable reasons; no sensitive diagnostics; docs match implementation.

## Subsequent phases

| Phase | Objective and next implementation | Deferred work / exit boundary |
| --- | --- | --- |
| 2 — Trusted pairing/presence | Implement approved trust/channel plan; timed discovery, remembered identity, revoke/recovery, connection state and conservative presence/reconnect | No proximity authentication or lock/unlock; adversarial and physical approval/revocation checks required |
| 3 — LAN reliability | Automatic bootstrap/discovery, approved authenticated sessions, route selection/promotion, snapshots and bounded liveness | No audio/files; repeated sleep/network-change tests and idle measurements first |
| 4 — Phone status | Validate collectors, telephony callbacks/permissions, truthful signal/network/unavailable state | No brightness/sensors; actual phone/multi-SIM validation |
| 5 — Windows shell | Implement selected WPF/WinUI direction, tray-first flyout, setup-focused full app, native materials/controls/accessibility | No dashboard expansion; light/dark/high-contrast, keyboard/screen reader, actual 100/125/150/200% DPI review |
| 6 — Media | Refine phone state/actions and add Windows media provider plus reverse commands | No audio transport; correct session/capability targeting and metadata fixtures |
| 7 — DND | Define synchronization ownership/triggers, use app-owned Android rule, research supported Windows behavior | No global/manual DND takeover; permission/rule/manual-override integration tests |
| 8 — Clipboard | Harden opt-in, remote availability, privacy/route policy and immediate disable; preserve Android manual send constraint | History/filtering require separately scoped contract/privacy decisions |
| 9 — OEM profiles | Discover generic/Lenovo providers and expose actual available modes | No fake mapping to ordinary power plans; supported hardware evidence |
| 10 — Open phone | Capability-detected scrcpy integration with approved enhanced authorization lifecycle | ADB remains optional and hidden; no normal companion state routed through it |
| 11 — Brightness/sensors | Android permission/state, ambient filtering and simple covered/pocket state | No UI until permission/state is reliable; no sensor assumptions |
| 12 — Shared adaptive brightness | Independent laptop lux mapping, smoothing/hysteresis/hold/transition limits | Hold last valid brightness on invalid readings; hardware comfort/power tests |
| 13 — Headphone handoff | Research public/OEM routes, optional enhanced providers and graceful fallback | No unsupported disconnect promises or premature accessibility automation |
| 14 — Phone internet | Supported/user-assisted tethering request and saved-network workflow | Local-only hotspot is not tethering; no assumed Samsung routine API |
| 15 — Audio | Prototype supported playback/loopback capture after stable transport; measure latency/battery | DRM/capture/call limitations and consent must be explicit |
| 16 — Advanced security | Separately approved time/state-based lock safeguards and Credential Provider feasibility/security research | No fake unlock; biometric challenge response alone does not prove Windows authentication integration |

Do not silently start another major phase. Each phase must state objective/scope/deferred work/constraints before implementation and report changes, files, tests, limitations, technical debt, documentation and next decisions afterward. If scope changes substantially, revise the plan and obtain the relevant major decision first.
