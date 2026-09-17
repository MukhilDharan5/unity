# Project progress and resume log

Last updated: 17 September 2026. **Stage 1B-A1 — Android session publication/revocation is complete.** Stage 1A and 1B-W are complete; Stage 1 overall remains in progress. Next: 1B-A2 listener lifecycle, not started.

## Current instructions

- The user authorized starting Stage 1 on 17 September 2026.
- Work in small stages; report a completed checkpoint before moving to the next one.
- Ask questions directly in this chat, not through separate question widgets.
- Keep this log current so another session can resume without repeating the audit.
- Preserve pre-existing uncommitted UI/build edits. No commits, resets or unrelated rewrites are authorized by this log.

## Approved scope and pending decisions

Stage 1A preserves existing JSON v1 messages, current WPF/Kotlin frameworks, trust storage and secure-session architecture. Aligning validation with the documented strict v1 contract is ordinary corrective work within the authorized foundation stage.

Security-channel direction (ADR-003) and future Windows UI direction (ADR-004) remain undecided. Starting Stage 1 does not approve TLS migration, loss of BLE-only commands, WinUI migration, new permissions, wire fields or persistence formats. Ask directly in chat before those changes. They do not block Stage 1A.

## Small-stage plan

| Checkpoint | Scope | Status |
| --- | --- | --- |
| 0 | Audit and local validation; root CURRENT_STATE.md and baseline/decision docs | Completed |
| 1A | Shared positive/negative JSON v1 fixtures, strict Android command/state validation, safe platform media metadata, regression verification | Completed; results below |
| 1B-W | Windows live/core session ownership, cleanup and cancellation tests; preserve security protocol | Completed; results below |
| 1B-A1 | Android route reservations, session publication/revocation, collector cleanup and cancellation tests | Completed; results below |
| 1B-A2 | Android listener startup/shutdown and remaining provider lifecycle verification | Planned after session-owner checkpoint |
| 1C | Conservative reconnect coordination and lifecycle tests | Planned |
| 1D | Local capability/permission/enablement model; negotiate any wire addition separately | Planned |
| 1E | Redacted structured diagnostics, typed settings, reproducible helpers/toolchain pins | Planned |

The order may change when evidence warrants it. Do not claim the whole foundation complete after one checkpoint.

## Resume instructions

1. Read this file, docs/stage-1-plan.md, CURRENT_STATE.md and relevant ADRs.
2. Inspect git status and current sources; the working tree already contained user changes before Stage 0.
3. Continue the currently marked checkpoint; do not repeat completed checks without a new change/failure.
4. Update this log with actual implementation, commands/results, limitations and the next concrete action.

## Environment and verification

Workspace: C:/Projects/Unity, Windows PowerShell. .NET SDK 10.0.401; JDK 17 available under .tools/jdk-17/jdk-17.0.20.1+1; Android SDK at C:/Users/mukhi/AppData/Local/Android/Sdk. Current Android toolchain: AGP 8.2.2, Gradle 8.10.2, Kotlin 1.9.20, compile/target 34, minimum 31. Gradle cache locking needs filesystem access outside the workspace. Use existing cached dependencies when possible.

Stage 0: Release build passed (0 warnings/errors), core 25/25, WPF smoke passed (20 renders), Android 15 regular tests pass plus one optional skip, separately run .NET/Kotlin interop passed, debug assembly passed, lint 0 errors/5 warnings. Diagnostic artifacts: artifacts/stage0-audit/. No physical Android or emulator UI validation performed.

## Chronological entries

### 17 September 2026 — Stage 0 completed

Recommended partial refactor. Retain immutable state, transport interfaces, collectors, key stores, opt-in clipboard, app-owned DND and useful tests. Found six reproduced codec differences plus session/reconnect ownership, capabilities, diagnostics, security assurance and native UI gaps. Audit is an historical checkpoint; current implementation status belongs in this log and synchronized docs.

### 17 September 2026 — Stage 1A started

Objective: strict v1 acceptance/rejection parity without new features or major architecture changes. Planned changes: shared fixture file consumed by both test runtimes; reject duplicates (including escaped/nested keys), wrong scalar types and noncanonical UUIDs; safe failure for malformed input; enforce state/metadata semantics; sanitize platform metadata within v1 lengths. Tests will exercise real codecs and the metadata helper. Update results/files/resume pointers after verification.

### 17 September 2026 — Stage 1A completed

Completed: corrected the six Stage 0 application-codec regressions. Android rejects quoted version/boolean values, duplicate fields (including escaped/nested unknown keys), object-valued commands and noncanonical clipboard UUIDs. Incoming malformed application commands return null rather than throwing. Application state/encoder validation now honors nullable/legacy optional state, required capabilities, enum/range/route/DND rules and existing size/depth limits. Android's media collector replaces control characters, bounds source/title/artist to v1 UTF-16 limits, preserves surrogate pairs at truncation and maps blank text to unavailable.

Changed files for this checkpoint (paths relative to this repository):

- Added `tests/Fixtures/protocol-v1.json` (67 fictional raw-payload cases) and `tests/Fixtures/README.md`.
- Added Android `core/V1MessageValidator.kt`, `core/MediaMetadataNormalizer.kt` and `V1ContractTest.kt` under the existing main/test source trees.
- Modified Android `core/MessageCodec.kt`, `state/StateCollector.kt`, existing `MessageCodecTest.kt` (valid Unicode fixture) and `app/build.gradle.kts` (shared test resource path only).
- Modified `tests/PhoneCompanion.Tests/Program.cs` and its `.csproj` to consume shared fixtures and check typed round-trips. Windows application source was unchanged by checkpoint 1A.
- Added this log and `docs/stage-1-plan.md`; synchronized README, architecture, Android, security, protocol/interop, development, testing, validation, roadmap and ADR-002. `CURRENT_STATE.md` remains historical with a current-progress pointer.

Tests performed:

| Check / command | Result |
| --- | --- |
| `dotnet build PhoneCompanion.slnx -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false` | Pass; 0 warnings/errors |
| `dotnet run --project tests/PhoneCompanion.Tests -c Release --no-build` | 26/26; all 67 shared fixtures verified |
| `dotnet run --project tests/PhoneCompanion.Windows.SmokeTests -c Release --no-build -- artifacts/stage1a/ui-smoke` | Pass; 20 WPF renders |
| Android `./gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon` | BUILD SUCCESSFUL; 45 tasks, 14 executed/31 up-to-date; 20 regular tests pass, optional interop test skipped, 0 failures/errors; 67 shared fixtures verified; debug APK packaged |
| Android lint | 0 errors/5 existing warnings (obsolete custom-lint API and four dependency notices) |
| `git diff --check` and Markdown local-link check | Pass |

Windows used workspace `artifacts/dotnet-home` for DOTNET_CLI_HOME. Android used the JDK/SDK paths above; Gradle cache access outside the workspace was approved by automatic review. SDK XML/Gradle warnings and the existing deprecated `NETWORK_TYPE_IDEN` reference remain. No dependency/toolchain upgrades were introduced. Unit XML/lint reports are in Android `app/build/`; Windows renders and checkpoint evidence are under ignored `artifacts/stage1a/`. Cached-dependency/debug checks do not establish clean-machine installation or release signing. Root packaged distribution ZIP/APK copies were not refreshed.

Known limitations and technical debt: fixtures establish bounded application acceptance, not every possible parser behavior. `ConnectionService` still parses secure envelopes/controls before `decodeIncoming`; their strict validation and narrower secure-parser nesting limit are separate follow-ups. Generic legacy codec helpers/unused acknowledgment models are not a live acknowledgment contract. No new protocol fields, negotiation, permissions, trust layout, cryptography, transport policy, UI or framework were introduced. Production lifecycle/reconnect/capability/diagnostic work remains. Physical BLE/LAN/Android UI/system behavior and performance are unverified. The real .NET/Kotlin secure-session TCP test passed in Stage 0 and was not rerun in 1A; regular existing secure-session tests passed in both current suites. Adversarial security review remains required before a release claim.

Decisions needed: none for completed 1A. ADR-003 secure-channel direction and ADR-004 future Windows UI direction remain pending and must be discussed directly in chat before dependent implementation.

### 17 September 2026 — Stage 1B-W completed

Authorized by the user's “continue”; split 1B by platform to keep checkpoints small. Completed Windows production ownership and cancellation. `LivePhoneTransport` now derives from the existing Core session owner and supplies only wire opening, secure pairing/identity approval, peer reporting and identity-resource disposal. The shared owner cancels and releases on read end/failure or ambiguous send failure; validates/copies incoming frames; fences late connection completion and frames after cancellation; links sends to lifetime; and drains the attempt, reader and active sends before disposal. Stop, session release and disposal have shared completion tasks so concurrent callers do not return before an ongoing close finishes. Canceling the caller's StopAsync wait leaves cleanup running. Transports remain single-use.

`SecurePhoneSession` send/heartbeat failures now use its existing idempotent DisposeAsync path instead of independently closing the wire. Handshake/record bytes, algorithms, identity approval, trust storage, permissions, UI and reconnect/route policy are unchanged. AES/key-object erasure is not solved by this checkpoint.

Changed files:

- `src/PhoneCompanion.Core/Transports/SessionPhoneTransport.cs`: shared lifecycle/cleanup and platform connect/resource hooks.
- `src/PhoneCompanion.Core/Transports/SecurePhoneSession.cs`: two failure-close call sites use idempotent session disposal.
- `src/PhoneCompanion.Windows/Connection/LivePhoneTransport.cs`: removed duplicate lifecycle loop; retained production pairing/identity behavior.
- `tests/PhoneCompanion.Tests/Program.cs`: five owner regressions with controlled interleavings, fake late factories/readers and blocked send/dispose behavior.
- Added `tests/PhoneCompanion.Windows.SmokeTests/LiveTransportChecks.cs`; modified its `Program.cs` to run six actual live-transport/secure-handshake scenarios before existing UI checks.
- Updated PROGRESS, small-stage plan, README, Windows, architecture, connection, security, testing, validation, development and roadmap docs. Pre-existing UI/build edits were preserved; no commit created.

Tests performed (same .NET environment/commands as 1A):

| Check | Result |
| --- | --- |
| Release solution build, `--no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false` | Pass; 0 warnings/errors |
| Core executable, Release `--no-build` | 31/31; all 67 shared application fixtures still verified |
| Windows smoke executable, Release `--no-build -- artifacts/stage1bw/ui-smoke` | Pass; six live lifecycle scenarios and existing UI checks; 20 PNG renders |
| `git diff --check` / updated Markdown local links | Pass |

Live tests use real secure P-256 handshakes and PhoneStateManager with in-memory IFrameConnection endpoints. Covered both route kinds, matching-code/new-peer approval, remembered fingerprint, remote close clearing battery/state, repeated disposal, late open after cancellation, approval cancellation, write failure without retry, and stop draining a blocked encrypted command. Core tests cover canceled stop waits while concurrent disposal awaits actual release, late factory results/single-use guards, read failure cleanup before fault reporting, blocked-send cancellation on remote end, and a canceled reader yielding a late frame. Test barriers make the targeted interleavings reproducible; waits have short deadlines. Saved output/renders are under ignored `artifacts/stage1bw/`. Initial build checks found missing explicit Windows/test namespace imports; fixed before final build/test passes.

Known limitations/debt: the fake wires do not exercise actual BLE/TCP adapters or phone behavior; no physical/device/performance checks occurred. No Android code changed, so its 1A 20-test/debug/lint results were not rerun. Optional .NET/Kotlin TCP interop was not rerun; Stage 0 remains its last dedicated pass. Android service ownership, App reconnect/sample/forget/exit integration, malformed handshake/control validation, adversarial cryptographic tests and deterministic AES/key cleanup remain. Stop waits for ownership completion: a factory that never returns despite cancellation, or a session that never unblocks after close, can still hold cleanup; these violate the provider contract and need bounded platform behavior. A canceled stop wait does not claim cleanup is complete. Root distribution packages were not refreshed.

Recommended next checkpoint: 1B-A Android session ownership/cancellation. Decisions needed now: none; ADR-003/004 remain pending before their dependent security/UI migrations.

### 17 September 2026 — Stage 1B-A1 completed

Authorized by the user's next “continue”; split Android ownership into session/collector (A1) and listener resource lifecycle (A2). Found Forget closed pipes but did not cancel handshake jobs, allowing a late successful continuation to restore trust. Old last-route cleanup awaited collector cancellation before clearing shared fields, allowing it to erase newer collection/state.

Completed: the actual Service now uses pure Kotlin `PhoneSessionOwner` and `PhoneStateCollection`. The session owner reserves pending/connected routes before dispatch (one BLE/one Wi-Fi), tracks route jobs and guarded pipes, requires current generation/job for publication and the synchronous trust-persistence hook, closes/cancels work on revoke/destruction, and closes pipes on parent cancellation to interrupt blocking IO. Stale completion cannot remove a newer same-kind lease. Active route remains authenticated Wi-Fi then BLE; ambiguous failed sends close the chosen route without retrying on another. Secure close is atomic/idempotent and raw pipes close once. Service retains Android hosting, approval UI, existing preferences, handshake/control parsing, timing and feature dispatch; heartbeat/watchdog/handshake guard now belong to the route job.

The collection owner cancels/replaces observers, resets latest state synchronously on stop and checks collection identity/session generation before snapshots. Replacement retains/waits for the original cleanup chain through rapid restarts; cancellation during the wait cannot start an obsolete observer. The service fences clipboard result notices by generation. No framework/dependency, protocol/crypto bytes, trust schema, permissions, feature or foreground-service policy changed.

Files added/modified:

- Added Android main `core/PhoneSessionOwner.kt` and `core/PhoneStateCollection.kt`.
- Modified Android `ConnectionService.kt` to use both owners and scope/fence lifecycle work.
- Modified Android `core/SecureSession.kt` close flag to atomic/idempotent; keys/record algorithm unchanged.
- Added Android test `PhoneSessionOwnerTest.kt` (ten regressions against the production helpers).
- Updated README, this log, stage plan, Android, architecture, connection, security, development, testing, validation report/checklist and roadmap docs. Windows and prior user UI/build edits were preserved; no commit.

Tests performed:

| Check | Result |
| --- | --- |
| Android Kotlin compile | Pass |
| `./gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon` | BUILD SUCCESSFUL in 45s; 45 tasks, 7 executed/38 up-to-date |
| Android JVM suite | 30/30 regular tests; one optional interop skip; 0 failures/errors |
| Shared v1 corpus | All 67 cases still pass in Android (Windows prior pass unchanged) |
| Debug APK assembly | Pass; updated APK in `app/build/outputs/apk/debug/app-debug.apk` |
| Android lint | 0 errors/5 existing warnings; SDK XML/Gradle warnings remain |
| `git diff --check` and updated Markdown links | Pass |

Ten tests cover pending route reservation/duplicate rejection; cancellation before dispatch/destruction; revoke/re-enable with a late old handshake and newer same-kind route (trust hook not invoked); Wi-Fi priority/BLE fallback; send failure without cross-route retry; blocked encrypted write released by revoke; parent cancellation interrupting close-dependent IO; simultaneous route close; combined-flow snapshot emission; and rapid collector restarts waiting for original cleanup while preserving new state. Secure cases run real P-256 code agreement/encrypted records on fictional pipes; IO cancellation uses a close-released latch. Barriers control interleavings and each scenario has a five-second deadline. The first combined run assembled the APK but one route test timed out because the fixture waited on the wrong receive side; corrected that fixture and two unnecessary non-null assertions before all tests/lint passed. Saved output is under ignored `artifacts/stage1ba1/`; XML/lint reports remain in Android `app/build/`.

Environment unchanged: JDK/SDK/toolchain above and existing offline cache. Gradle cache access outside workspace approved by automatic review. No Windows source changed, so its 1B-W build/31 core/six live/UI smoke checks were not rerun. Optional .NET/Kotlin TCP interop was not rerun; last dedicated pass is Stage 0. Root distribution ZIP/APK copies were not refreshed.

Known limitations/debt: JVM tests exercise the actual Kotlin ownership helpers, not Android Service/UI/SharedPreferences or real BLE/LAN listeners. A1 protects session publication after revocation but does not prove listener resources/advertising stopped. LAN start can overlap stop before resource fields are published; stale provider/port/error/GATT callbacks remain A2. Android callbacks/system settings, OEM behavior and performance require device tests. Preferences still ignore commit success, so durable trust-save/revoke failure policy remains unresolved. Malformed handshake/control validation, adversarial crypto tests and key erasure remain. Non-cooperative IO/observer cleanup can hold draining; providers must unblock on close and cleanup must be bounded. Android onDestroy/Forget remain synchronous lifecycle entry points that revoke/cancel immediately; they do not synchronously await every job/observer cleanup. Future observer replacement waits for that cleanup.

Recommended next checkpoint: 1B-A2 Android listener resource startup/shutdown. Decisions needed now: none. ADR-003 secure-channel direction and ADR-004 future Windows UI remain pending before dependent migrations. Do not claim all of 1B/Stage 1 complete.

## Next concrete resume action

Begin **1B-A2 — Android listener startup/shutdown and stale provider callbacks**, after explaining the bounded scope in chat. Read `lan/LanServer.kt`, `ble/BleManager.kt`, and the service's endpoint/error/start/stop hooks. Address LAN resource initialization racing stop (socket/listener/client/NSD ownership), prevent old listener callbacks/port notices from overwriting a newer lifecycle, and ensure an old BLE pipe/callback cannot cancel a newer connection. Extract only small pure lifecycle seams where tests need controlled startup/stop interleavings; test production paths with fake resources. Preserve bind port 38471, NSD/service UUIDs, low-power BLE settings, foreground policy, permissions, user pairing window, trust and wire format. Keep physical integration requirements explicit. Ask directly in chat if a material policy/security/compatibility change is necessary. Do not repeat the completed session-owner/Windows audit work.

Do not repeat Stage 0 or migrate TLS/WinUI based only on the instruction to begin Stage 1. Preserve the pre-existing Windows UI and Android root build/helper edits. No commit was created.
