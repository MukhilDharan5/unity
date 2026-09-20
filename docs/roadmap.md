# Incremental roadmap

Status: Stage 0 and the MVP-essential Stage 1 checkpoints are complete through 19 September 2026. Structured diagnostics, broad validation and release hardening remain deferred under the MVP pacing decision. [PROGRESS.md](../PROGRESS.md) defines the next checkpoint. Existing code already contains parts of pairing, transport, state, UI, media, DND and clipboard, so later phases validate/refine them rather than pretend they are absent.

## Stage 0 — Audit (complete)

Objective: inspect structure, dependencies, platform APIs, state, UI, communications, trust and tests before significant changes. Delivered root `CURRENT_STATE.md`, current-baseline platform/security/capability/development/testing docs, corrected legacy statements, proposed ADRs and local validation evidence. Preserved app code and existing edits. No feature implementation or architecture decision was committed.

## Stage 1 — Foundation consolidation (MVP essentials complete)

Objective: make existing foundations coherent and testable. Implement shared positive/negative wire fixtures, validation parity/metadata normalization, a single production lifecycle with fake-frame test seams, bounded event-aware reconnect coordination, a small Android coordinator, explicit capability/permission/enablement state, redacted structured diagnostics and typed settings, and reproducible build helpers/pins.

Stage 1A delivered strict application validation, metadata normalization and 67 shared fixtures. Stage 1B-W consolidated Windows live/core ownership (31 core tests, six live scenarios, UI smoke/build checks). Stage 1B-A1 added Android generation-fenced session/collector ownership. Stage 1B-A2 added per-run LAN/BLE resource ownership and stale callback fencing (34 regular tests, debug assembly/lint pass). Stage 1C added event-aware reconnect/listener recovery; 1D added explicit Android access state; the MVP parts of 1E added typed timing policy and a portable Android build helper. Security/UI migrations and protocol extensions still require separate discussion.

Defer all new user-facing features, framework migration, privileged APIs, clipboard history, cloud, storage engines and Windows services. Discuss wire additions, storage/permissions and compatibility before changing them. Security channel replacement must be an explicitly approved focused substage with pairing/storage/migration feasibility work; it is not routine cleanup.

Constraints: Android foreground/background policy and clipboard privacy, optional/revocable access, BLE peripheral variability, Windows user-process/API boundaries, C#/Kotlin validation parity and idle radio use. Exit criteria: both builds/checks remain runnable; agreed fixtures match; actual ownership/cancellation/revocation paths are tested; generic capabilities have truthful unavailable reasons; no sensitive diagnostics; docs match implementation.

## Subsequent phases

| Phase | Objective and next implementation | Deferred work / exit boundary |
| --- | --- | --- |
| 2 — Trusted pairing/presence | Implement approved trust/channel plan; timed discovery, remembered identity, revoke/recovery, connection state and conservative presence/reconnect | No proximity authentication or lock/unlock; adversarial and physical approval/revocation checks required |
| 3 — LAN reliability | Automatic bootstrap/discovery, approved authenticated sessions, route selection/promotion, snapshots and bounded liveness | No audio/files; repeated sleep/network-change tests and idle measurements first |
| 4 — Phone status (implemented, validation deferred) | Battery, connection route, telephony generation/5G override/signal callbacks and truthful unavailable state now feed Windows UI/tray | No brightness/sensors; actual phone/multi-SIM validation |
| 5 — Windows shell | Implement selected WPF/WinUI direction, tray-first flyout, setup-focused full app, native materials/controls/accessibility | No dashboard expansion; light/dark/high-contrast, keyboard/screen reader, actual 100/125/150/200% DPI review |
| 6 — Media (MVP implemented, validation deferred) | Android and Windows now exchange direction-specific media state/commands through platform media sessions | No audio transport; runtime session/capability targeting and expanded fixtures remain |
| 7 — DND | Define synchronization ownership/triggers, use app-owned Android rule, research supported Windows behavior | No global/manual DND takeover; permission/rule/manual-override integration tests |
| 8 — Clipboard | Harden opt-in, remote availability, privacy/route policy and immediate disable; preserve Android manual send constraint | History/filtering require separately scoped contract/privacy decisions |
| 9 — OEM profiles | Discover generic/Lenovo providers and expose actual available modes | No fake mapping to ordinary power plans; supported hardware evidence |
| 10 — Open phone (launcher implemented) | Capability-detected scrcpy launch is available from Windows UI/tray | Executable distribution and wireless-debugging authorization setup remain; ADB stays optional and outside companion state |
| 11 — Brightness/sensors (MVP implemented, validation deferred) | Android brightness state/control, filtered ambient lux and simple covered detection now feed Windows UI | Physical sensor/OEM validation remains |
| 12 — Shared adaptive brightness (MVP implemented, validation deferred) | Opt-in WMI laptop brightness, independent logarithmic lux mapping, smoothing/hysteresis/hold/transition limits and Windows policy restore | Integrated laptop panels only; hardware comfort/power tests remain |
| 13 — Headphone handoff (MVP implemented, validation deferred) | Optional audio-output state, authenticated handoff request, API 37+ Companion Device association/public release, guided fallback, Windows settings launch and isolated optional ADB accelerator | Physical API 37/older-device/headset validation remains; Windows still requires device selection; Shizuku excluded |
| 14 — Phone internet (MVP implemented, validation deferred) | Windows internet-status gate, authenticated explicit request, Android system tethering notification/button, Windows saved-network settings flow and isolated optional ADB settings accelerator | No silent tether toggle, credential exchange or assumed Samsung routine API; physical/OEM validation remains |
| 15 — Audio | Prototype supported playback/loopback capture after stable transport; measure latency/battery | DRM/capture/call limitations and consent must be explicit |
| 16 — Advanced security | Separately approved time/state-based lock safeguards and Credential Provider feasibility/security research | No fake unlock; biometric challenge response alone does not prove Windows authentication integration |

Do not silently start another major phase. Each phase must state objective/scope/deferred work/constraints before implementation and report changes, files, tests, limitations, technical debt, documentation and next decisions afterward. If scope changes substantially, revise the plan and obtain the relevant major decision first.
