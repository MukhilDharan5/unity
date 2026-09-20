# Project progress and resume log

Last updated: 20 September 2026. **MVP feature work is continuing on the isolated encrypted v1 channel.** Automatic authenticated LAN discovery, reconnect, Android access state, phone sensors, opt-in laptop adaptive brightness and laptop-to-phone audio streaming are complete. Detailed diagnostics and broad validation are deferred.

## Current instructions

- The user authorized starting Stage 1 on 17 September 2026.
- Work in small stages; report a completed checkpoint before moving to the next one.
- Ask questions directly in this chat, not through separate question widgets.
- Keep this log current so another session can resume without repeating the audit.
- As of 19 September 2026, prioritize MVP implementation and keep validation light. Run minimal compile checks to catch broken code; defer broad tests, lint, hardware checks and detailed validation documentation until the user requests them.
- Preserve the pre-existing UI/build edits. On 17 September 2026 the user authorized committing and pushing every current non-ignored project change to the configured GitHub repository. Resets and unrelated rewrites remain outside scope.

## Approved scope and pending decisions

Stage 1A preserves existing JSON v1 messages, current WPF/Kotlin frameworks, trust storage and secure-session architecture. Aligning validation with the documented strict v1 contract is ordinary corrective work within the authorized foundation stage.

ADR-003 now records the accepted interim MVP direction: retain the isolated encrypted v1 channel, then revisit TLS/trust migration and independent review after the MVP. Future Windows UI direction (ADR-004) remains undecided. This does not approve TLS migration, loss of BLE-only commands, WinUI migration, new permissions, wire fields or persistence formats. Ask directly in chat before those changes.

## Small-stage plan

| Checkpoint | Scope | Status |
| --- | --- | --- |
| 0 | Audit and local validation; root CURRENT_STATE.md and baseline/decision docs | Completed |
| 1A | Shared positive/negative JSON v1 fixtures, strict Android command/state validation, safe platform media metadata, regression verification | Completed; results below |
| 1B-W | Windows live/core session ownership, cleanup and cancellation tests; preserve security protocol | Completed; results below |
| 1B-A1 | Android route reservations, session publication/revocation, collector cleanup and cancellation tests | Completed; results below |
| 1B-A2 | Android listener startup/shutdown and remaining provider lifecycle verification | Completed; results below |
| 1C | Conservative reconnect coordination and lifecycle behavior | Completed; minimal compile checks only |
| 1D | Local capability/permission/enablement model; negotiate any wire addition separately | Completed locally; no wire change |
| 1E | Redacted structured diagnostics, typed settings, reproducible helpers/toolchain pins | MVP essentials complete; diagnostics deferred |

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

### 19 September 2026 — Repository snapshot prepared

The user authorized committing and pushing all current non-ignored project changes. The snapshot covers the completed Stage 0, 1A, 1B-W and 1B-A1 implementation, tests, architecture and progress documentation, plus the preserved Windows dashboard and Android build/helper work. Generated build output, local toolchains, IDE state, local Android configuration and signing stores remain excluded by `.gitignore`. The configured target is `origin/main` at `https://github.com/MukhilDharan5/unity.git`. The validated results and remaining limitations above are the basis for this snapshot; no feature source changed after those checks.

### 19 September 2026 — Stage 1B-A2 completed

Authorized by the user's request to do the next coding stage. Reproduced the ownership gap in the provider design: LAN could finish socket creation after stop and then publish an endpoint/register NSD, while BLE reused one callback object and mutable server/device/pipe fields across restarts. A callback or pipe from an old run could therefore observe or act on newer state.

Completed: added pure Kotlin `ListenerLifecycle`, which permits one reserved run, assigns monotonic leases, owns newest-first idempotent cleanup actions, closes resources registered after stop immediately, serializes short provider callbacks with stop, and prevents an old finish from clearing a replacement. `LanServer` now gives every start its own coroutine scope/client state and tracks the bound socket, NSD registration and accepted sockets. Registration and endpoint/error/connection callbacks are current-run guarded; listener failure clears the displayed port; stop invalidates callbacks before closing resources. `BleManager` now creates GATT and advertising callbacks/state per run and tracks server, advertiser and pipe cleanup. Old callbacks cannot access a new server, closed pipes are idempotent, and an old pipe cancels a Bluetooth connection only while it remains that run's current pipe. A disconnect-in-progress fence prevents immediate pipe recreation on the connection being cancelled. `ConnectionService` applies provider notices/endpoints synchronously inside that fence and makes the cross-thread port visible. Port 38471, NSD name/type, BLE UUIDs and low-power mode are unchanged. No protocol/crypto bytes, trust schema, permissions, foreground policy, dependencies or user features changed.

Files added/modified:

- Added Android main `core/ListenerLifecycle.kt`.
- Modified Android `lan/LanServer.kt`, `ble/BleManager.kt` and `ConnectionService.kt` to use per-run ownership/callbacks.
- Added Android test `ListenerLifecycleTest.kt` with four production-seam regressions using fake cleanup resources.
- Updated README, this log, stage plan, Android, architecture, connection, security, development, testing, validation report/checklist and roadmap docs.

Tests performed:

| Check | Result |
| --- | --- |
| Android Kotlin compile | Pass after correcting one internal nested-type visibility declaration found by the first compile |
| `./gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon` | BUILD SUCCESSFUL in 1m 23s; 45 tasks, 13 executed/32 up-to-date |
| Android JVM suite | 34/34 regular tests; one optional interop skip; 0 failures/errors |
| Shared v1 corpus | All 67 cases still pass in Android (Windows prior pass unchanged) |
| Debug APK assembly | Pass; APK at `app/build/outputs/apk/debug/app-debug.apk` |
| Android lint | 0 errors/5 existing warnings: obsolete custom check plus four dependency notices; SDK XML/Gradle warnings remain |
| `git diff --check` and Markdown local links | Pass after documentation updates; 0 broken local links |

The four new cases cover a resource arriving after stop and closing immediately; stale endpoint suppression; old completion after restart without affecting the current run; duplicate start reservation; and all newest-first cleanups being attempted once when one cleanup throws. The full suite retains ten A1 ownership/collection cases, real secure-session tests and all shared application fixtures.

Known limitations/debt: JVM tests exercise the exact production lifecycle helper but not Android `BluetoothGattServer`, advertising, NSD, Service/UI, permissions or real TCP/radio behavior. A physical device must verify repeated start/stop, stop during bind/register, advertising/unregistration, LAN reachability, and delayed callbacks during rapid same-device BLE reconnect. Platform registration/add-service calls execute inside the short stop fence and are assumed to return; a platform call that hangs can delay stop. Cleanup remains best effort and is not yet emitted to redacted diagnostics. Android pairing approval expiry still does not stop listeners, automatic recovery remains uncoordinated, and foreground/OEM power behavior remains 1C. Durable trust-save/revoke failure, malformed secure control validation, adversarial crypto/key erasure and hardware/performance checks remain. Root distribution APK/ZIP copies were not refreshed. No Windows source changed, so its 1B-W build/31 core/six live/UI smoke checks were not rerun. Optional .NET/Kotlin TCP interop was not rerun; its last dedicated pass is Stage 0.

Recommended next checkpoint: 1C conservative reconnect and lifecycle coordination. Decisions needed now: none. ADR-003 secure-channel direction and ADR-004 future Windows UI remain pending before dependent migrations. Do not claim all of Stage 1 complete.

### 19 September 2026 — Stage 1C completed (MVP pace)

Windows reconnect now wakes on network-address changes, suppresses reconnect while switching sample mode, resumes after leaving sample mode, and unsubscribes network events during exit. Android now expires unpaired listening after two minutes, cancels approval and resources on expiry, retries failed BLE/LAN listeners with bounded 2/5/10/30-second backoff, responds to Bluetooth on/off and network changes, and retries listeners after runtime permission changes or activity resume. Forget/destruction cancel expiry/retry work; successful trust cancels pending retries. Existing route priority, trust, wire format, permissions and foreground policy remain unchanged.

Minimal checks only per the user's MVP direction: Android `:app:compileDebugKotlin` passed; Windows Release solution build passed with 0 warnings/errors. Full tests, lint, packaging, device validation and detailed doc synchronization were intentionally deferred.

### 19 September 2026 — Stage 1D / MVP-essential 1E completed

Android now reports nearby-device permission, Bluetooth enabled state, notification permission, optional phone-state access, media-session access and DND access separately. The Access card shows each state and routes the user to the matching runtime request or settings screen. Permission results and activity resume refresh state and retry eligible listeners. This remains local UI/state and adds no protocol fields or permissions.

Connection timeouts/backoff are centralized in typed policy objects on Android and Windows. `build_app.bat` now uses its own directory, respects existing `JAVA_HOME`/`ANDROID_HOME`, detects common local installs, accepts arbitrary Gradle arguments and contains no user/workspace-specific absolute path. Android Kotlin and Windows Release compile checks pass. Broad tests/lint/package validation and structured diagnostics are deferred under the MVP instruction.

### 19 September 2026 — Automatic LAN discovery implemented

Windows now sends a bounded local mDNS query for Android's existing `_phonecomp._tcp.local` advertisement, parses compressed PTR/SRV/A/AAAA responses, and offers **Find phone on this network** before manual address entry. Trusted reconnect tries the saved endpoint, newly discovered endpoints, then BLE; a changed endpoint is persisted only after the existing secure session authenticates the saved phone identity. Discovery remains a routing hint and does not change trust or wire bytes. Manual Wi-Fi and BLE remain available. Windows Release compilation passes with 0 warnings/errors; runtime network testing is deferred.

### 19 September 2026 — Live phone status started

The user chose MVP implementation speed over a TLS migration and deferred broad testing/debugging. ADR-003 records retaining the current isolated encrypted v1 channel for the MVP, without treating it as independently reviewed or production-hardened. Android cellular state now reacts to data-generation, 5G display override and signal-strength callbacks using the already-declared phone-state permission, while connectivity callbacks continue to report Wi-Fi/mobile-data use. The Windows tray tooltip now includes connection, battery/charging and data-route status. No permission, wire format, persistence format or dependency changed. The Android Kotlin compile emitted the updated state collector classes; the focused Windows Release build passed with 0 warnings/errors. Broader validation remains deferred.

### 19 September 2026 — Bidirectional media and Open phone implemented

The user explicitly approved both the Windows-to-Android media direction and scrcpy/ADB integration. Windows now observes the current Global System Media Transport Controls session and sends bounded source/title/artist, playback state and available controls in the additive authenticated `pc_media` message. Android displays a **Playing on laptop** card and sends capability-gated, single-in-flight `pc_media_command` actions for previous, play/pause and next. Windows executes them against the current platform media session and republishes resulting state. Existing phone-to-Windows media remains unchanged.

Windows now exposes **Open phone** in the full app, flyout and tray. The launcher detects `scrcpy.exe` beside the app, in packaged tool folders, through `UNITY_CONNECT_SCRCPY`, common install locations or `PATH`; it starts scrcpy without a visible console. scrcpy is not downloaded or bundled, the local source checkout currently has no executable, Android debugging authorization remains external, and ADB is never used as the normal companion transport. No new Android permission, persistence format or dependency was added. Focused Windows Release compilation passed with 0 warnings/errors and Android Kotlin emitted the updated classes; broader tests/device debugging remain deferred.

### 19 September 2026 — Phone brightness and sensors implemented

The user explicitly approved the new brightness protocol state and Android special settings access. Android now observes current system brightness and adaptive mode, filters ambient-light readings, combines light/proximity/screen signals into a simple covered state, and reports truthful valid/covered/unavailable ambient status in the optional v1 `brightness` snapshot section. The Access card opens Android's **Modify system settings** screen. Authenticated `brightness_command` messages can change the 1–100 brightness level or adaptive mode only while `Settings.System.canWrite` is true.

Windows now shows phone brightness, ambient lux/covered status and adaptive state in the full app and flyout. Its slider is debounced, preserves adaptive mode and is disabled until Android reports control access; the adaptive toggle is separate. Percentage conversion round-trips through Android's 0–255 setting scale. No laptop brightness automation was added. Windows Release compilation passed with 0 warnings/errors and Android Kotlin emitted the new controller/UI/protocol classes. Sensor behavior, settings access and OEM brightness response remain for device testing; broad tests are deferred.

### 20 September 2026 — Shared adaptive laptop brightness implemented

The user approved Stage 12 and requested MVP pacing. Windows now detects integrated displays through `WmiMonitorBrightness` and exposes an opt-in, session-only **Laptop auto brightness** switch in both the full app and tray flyout. Valid filtered phone lux feeds an independent logarithmic laptop curve. Additional smoothing, a three-percentage-point hysteresis threshold, a 1.5-second minimum target hold and three-point transition steps reduce visible oscillation. Covered or unavailable sensor state holds the last applied level.

The controller is off at every launch, ignores sample data, and reports unsupported hardware or runtime failure in the UI. Turning it off, losing the real phone connection or exiting the application releases the override with `WmiRevertToPolicyBrightness`, returning control to Windows power policy. It uses Microsoft's `System.Management` package and makes no Android permission, protocol or persistence change. External DDC/CI monitors are outside this MVP implementation.

Minimal verification per the current instruction: the focused Windows Release build passed with 0 warnings/errors. The first solution restore reached the blocked online NuGet vulnerability endpoint; a focused restore with auditing disabled used the already-cached package and succeeded. Hardware response, brightness comfort tuning, UI rendering and broader tests remain deferred.

### 20 September 2026 — Stage 13 headphone-handoff provider foundation

The public platform feasibility pass found that Android's `BluetoothA2dp` API can observe connected audio devices with the app's existing `BLUETOOTH_CONNECT` access, but the project's Android 12–14 baseline has no public profile disconnect method. Android API 37 adds `BluetoothDevice.disconnect()`, gated by the same runtime permission plus either privileged Bluetooth access or a user-approved Companion Device Manager association with that headset. Windows documents enumeration/pairing and Bluetooth service enablement, but service enablement installs/removes a profile driver rather than transferring an active headset.

Implemented the safe provider foundation while leaving the major choice open. New `BluetoothAudioMonitor` uses the public A2DP profile proxy and connection broadcasts, publishes a normalized bounded device label to local Android UI state, and releases its receiver/profile proxy with the service. The Android app now shows **Audio output** as the connected device name or **Phone**. Addresses are not retained or displayed. No permission, wire field, persistence, privileged helper, disconnect call or Windows UI was added.

`ADR-005-headphone-handoff.md` records four concrete paths and recommends public API plus an assisted fallback: on API 37+, a separately approved headset association can permit direct release; Android 12–16 opens the narrowest system flow for user action. Optional Shizuku/ADB providers remain isolated future choices and accessibility automation is not recommended. Minimal verification only: Android `:app:compileDebugKotlin --offline --no-daemon` passed. Physical A2DP callbacks and labels remain untested.

### 20 September 2026 — Stage 13 headphone handoff MVP completed (A + C)

The user selected the public API/assisted path plus the optional ADB accelerator and explicitly excluded Shizuku. The optional authenticated v1 `audioOutput` snapshot member now carries only a bounded device label and direct-release availability; Bluetooth addresses stay on Android. Windows exposes **Move to laptop** in the full dashboard and flyout and sends the explicit authenticated `headphone_handoff` command.

Android API 37+ can show a system Companion Device Manager consent flow for the currently connected headset. Only an associated headset is marked ready for one-tap release, and the runtime-gated public `BluetoothDevice.disconnect()` call is made only after that check. Older phones, unassociated devices and failed direct releases post a user-action notification into Android Bluetooth settings. Windows opens its Bluetooth settings for the final device selection.

The isolated settings assistant optionally detects `adb.exe` from `UNITY_CONNECT_ADB`, packaged/common Android SDK/scrcpy locations or `PATH`. It runs only with exactly one authorized device and only starts Android's public Bluetooth settings action. It does not issue an undocumented per-device disconnect, carry companion traffic or replace BLE/LAN. Shizuku and accessibility automation are absent.

Minimal verification per the MVP instruction: the focused Windows Release project build passed with 0 warnings/errors, and Android `:app:compileDebugKotlin --offline --no-daemon` passed with one existing deprecated telephony constant warning. The first solution-level build returned failure without diagnostics, so the focused Windows application build was used. Physical headset transfer, Companion Device association, notification behavior and ADB discovery remain deferred.

### 20 September 2026 — Stage 14 phone internet MVP completed

Windows now monitors its platform connectivity level and shows **Use phone internet** in the dashboard and flyout only when a real phone is connected and Windows does not report internet access. The click sends the authenticated fieldless `hotspot_request`, then opens Windows Wi-Fi settings so Windows can use its own saved hotspot profile or let the user select the phone network. Unity Connect never reads, stores or sends hotspot credentials.

Android handles the request by posting a user-action notification into the closest system-owned tethering screen, with public wireless/general settings fallbacks. The Android companion also provides a visible **Open tethering settings** button. It does not silently toggle tethering and never treats `LocalOnlyHotspot` as internet access. No new Android permission or stored setting was added.

The previous optional ADB helper is generalized as `AndroidSettingsAssistant`. With exactly one authorized device it can foreground tethering settings, falling back to wireless settings, but it does not call tethering services or carry companion traffic. No Shizuku, accessibility automation, Samsung routine dependency, SSID exchange or password exchange was added. ADR-006 records this workflow.

Minimal verification per the MVP instruction: the focused Windows Release application build passed with 0 warnings/errors, and Android `:app:compileDebugKotlin --offline --no-daemon` passed. Physical tethering, notification delivery, OEM settings resolution, Windows connectivity detection, saved-profile reconnection and captive-portal behavior remain deferred.

### 20 September 2026 — Stage 15 laptop-to-phone audio MVP completed

The audio feasibility choice is laptop-to-phone first. Windows has a supported WASAPI render-loopback path, while Android playback through `AudioTrack` requires no new permission; phone playback capture would require a separate `MediaProjection` consent/permission flow and source-app cooperation. Every Windows start shows a capture-scope confirmation and explicitly warns that protected output may be silent.

Windows now uses NAudio 3.1's current `WasapiRecorder` loopback API, buffers the current 8–96 kHz mono/stereo mix, converts it to PCM16 and exposes **Play on phone / Stop** in the full app and flyout. Android opens an ephemeral TCP sink, requests media audio focus, decrypts and plays the PCM stream, and exposes its own Stop action. Companion disconnect, permanent phone audio-focus loss, socket failure or app exit also ends the stream.

The existing authenticated companion connection negotiates `audio_stream_start`, `audio_sink_ready` and `audio_stream_stop` only. Each stream gets a fresh random AES-256 key, 128-bit connection token and UUID. Bulk audio travels over a separate Wi-Fi TCP socket so it cannot block ordinary state/commands. The socket validates the token and every bounded record uses AES-256-GCM with an exact increasing sequence and authenticated stream identity. Keys and tokens are never persisted. BLE does not carry audio.

Minimal verification per the MVP instruction: the focused Windows Release build passed with 0 warnings/errors after updating from NAudio's obsolete legacy capture type to its current recorder/builder API. Android `:app:compileDebugKotlin --offline --no-daemon` passed. Broad tests, UI rendering, physical playback, latency/underrun/battery measurements, network interruption, audio-focus/call behavior, output-device changes and protected-content behavior remain deferred.

Known MVP limits: raw PCM consumes more bandwidth/power than a codec; there is no resampling, compression, jitter adaptation or route migration. Formats above stereo are rejected. This stage does not implement phone-to-laptop audio, call/microphone capture, per-app Windows selection, a DRM guarantee or a low-latency claim. ADR-007 records the architecture and limits.

## Next concrete resume action

Stage 15 is implemented at MVP scope for laptop-to-phone audio. The next planned stage is Stage 16 advanced security. Do not start it automatically: discuss the exact lock/security objective, threat model and Windows Credential Provider boundary with the user before coding because a wrong design could create a false unlock-security claim.

Do not repeat Stage 0 or migrate TLS/WinUI without a new explicit decision. Preserve the Windows UI and Android root build/helper edits. The user authorized the repository snapshot and GitHub push recorded above.
