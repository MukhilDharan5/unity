# Testing — checkpoint evidence and remaining coverage

Updated: 17 September 2026, through Stage 1B-A1. Results include pre-existing uncommitted changes. Historical validation reports are not evidence of current hardware readiness. [PROGRESS.md](../PROGRESS.md) records exact continuation scope.

## Stage 1B-A1 verification

Android combined `:app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon` passes: **30 regular tests**, one optional interop skip, zero failures/errors; debug assembly passes; lint has 0 errors/5 existing warnings. Gradle reported BUILD SUCCESSFUL in 45s, 45 tasks (7 executed/38 up-to-date). The preceding run packaged the updated APK and found one test-fixture receive-direction mistake; corrected the fixture and removed unnecessary non-null assertions before the passing run. SDK XML/Gradle warnings remain.

Ten new `PhoneSessionOwnerTest` cases use the actual production owner/collection classes. Covered pending-route reservation, cancellation before dispatch/destruction, late publication after revoke/re-enable with a newer same-kind route, Wi-Fi preference/BLE fallback, failed send with no cross-route retry, blocked encrypted-write closure/generation invalidation, parent cancellation unblocking IO, simultaneous route close, combined-flow snapshot emission, and rapid collector restarts waiting for original cleanup. Secure cases use real P-256 handshakes and encrypted records over fake pipes; blocking-IO cancellation uses a close-released latch. Deferred barriers and five-second deadlines control tested interleavings.

The existing 67 shared application fixtures still pass. No Windows source changed in A1; its 31-test/build/live/WPF pass below remains the last checkpoint evidence and was not rerun. Optional .NET/Kotlin TCP interop was not rerun. Evidence is under ignored `artifacts/stage1ba1/` and Android `app/build/` reports. JVM tests do not instantiate Android Service/UI/SharedPreferences or real BLE/TCP adapters. Listener resource startup/shutdown (1B-A2), durable trust-save/revoke failure, cryptographic erasure/adversarial tests, hardware and performance remain open.

## Stage 1B-W verification (prior checkpoint)

Windows Release solution build passes with 0 warnings/errors. Core harness passes **31/31**, including the unchanged 67 shared application fixtures and five new production-owner tests: late factory return/single-use guards, canceled stop wait with concurrent dispose, remote end canceling a blocked send, release before fault reporting, and a canceled reader yielding a late frame. Barriers control interleavings; waits have bounded test deadlines.

The WPF smoke executable passes six new `LiveTransportChecks` scenarios before its existing UI checks and 20 renders. It instantiates the actual `LivePhoneTransport`, real P-256 secure sessions and the state manager with in-memory frame connections. BLE/Wi-Fi kinds test remote close/state clearing, interactive and pinned identity behavior; other cases test late open after disposal, approval cancellation, send failure without retry and stop draining a blocked encrypted command. This does not test real TCP/GATT adapters or a phone. Evidence is under ignored `artifacts/stage1bw/`.

No Android source changed in 1B-W; its 20-test/debug/lint pass below remains 1A evidence and was not rerun. The optional .NET/Kotlin TCP fixture was not rerun. Cryptographic algorithms and wire bytes are unchanged; secure send/heartbeat failure cleanup now calls the existing idempotent session close. App reconnect orchestration, Android coordination, AES-object erasure, malformed controls and adversarial/security/hardware gates remain open.

## Stage 1A verification (prior checkpoint)

| Check | Result | Scope |
| --- | --- | --- |
| Windows Release solution build, `--no-restore` | Pass; 0 warnings/errors | Current sources and cached dependencies |
| Core executable harness | 26/26 pass | Existing suite plus all 67 shared application fixtures and typed round-trips |
| WPF smoke executable | Pass; 20 renders | Existing UI/lifecycle checks; application UI unchanged in 1A |
| Android JVM suite | 20 pass, 1 optional interop skip; 0 failures/errors | Existing 15 plus 5 contract tests: 67 fixtures, direction gating, malformed UTF-8/2,000 seeded random inputs, nullable legacy state, encoder rejection and metadata normalization |
| Android debug assembly | Pass | Updated Kotlin compiled and debug APK packaged; no release-signing/device claim |
| Android lint | Pass; 0 errors, 5 warnings | Existing obsolete custom-lint API and four dependency notices |

Commands are in [development.md](development.md). Android used `:app:testDebugUnitTest :app:assembleDebug :app:lintDebug --offline --no-daemon`; Gradle reported BUILD SUCCESSFUL, 45 tasks (14 executed, 31 up-to-date). Compilation also exposed the existing deprecated `NETWORK_TYPE_IDEN` reference; SDK XML and Gradle warnings remain. The optional live .NET/Kotlin secure-session fixture was not rerun in 1A; its separate Stage 0 pass is historical evidence. No handshake implementation changed.

`tests/Fixtures/protocol-v1.json` is the single fixture source, copied into .NET output and added to Android JVM resources. Raw payload strings preserve duplicates/escapes/malformed JSON. Cases cover all application families, strict scalar/state/UUID semantics, unknown fields, nested/escaped duplicates, UTF-16/UTF-8 limits and logical nesting. Android additionally checks that valid phone-state frames cannot execute as commands. Shared acceptance is bounded evidence, not proof of every possible parser behavior or actual radio delivery.

Android `decodeIncoming` now rejects malformed application input without throwing. ConnectionService still parses secure envelopes/controls first; the new boundary does not guarantee every malformed record leaves a connection alive. The narrower existing secure-parser depth limit is outside these logical fixtures. Hardware, adversarial security and production session ownership/cancellation remain unfinished.

## Stage 0 checks (historical)

| Check | Result | Scope |
| --- | --- | --- |
| Windows Release solution build, `--no-restore` | Pass; 0 warnings/errors | Current sources with cached dependencies |
| Core executable harness | 25/25 pass | Codec, state, commands, lifecycle fencing, alternate authenticated sessions, happy-path secure pairing and BLE fragmentation |
| Windows WPF smoke executable | Pass; 20 PNG renders | Actual XAML/view model/tray, commands/availability, themes, layout, clipboard mock, lifecycle |
| Android debug assembly | Pass, tasks up-to-date | Current debug APK build; no clean/release-signing claim |
| Android JVM unit suite | 15 pass, 1 optional interop skip, 0 failures/errors | 11 codec, 3 normalization, 1 happy-path secure-session tests |
| Android lint | 0 errors, 5 warnings | One obsolete custom-lint API warning; four dependency-version notices; command also emits SDK XML/Gradle deprecation warnings |
| Optional real .NET/Kotlin TCP fixture | Pass; JUnit 1/1 and .NET client pass | Current compiled runtimes agree on code and encrypted bidirectional exchange |
| Additional codec probes | Six compatibility/robustness gaps confirmed | Current compiled Android codec and referenced current C# codec; fictional data only |

The initial offline combined Android command failed for missing cached `com.android.tools.lint:lint-gradle:31.2.2`, after APK assembly succeeded. A separate online unit/lint run resolved tooling and passed. This environmental failure was not fixed by modifying application code or dependency versions.

## Stage 0 reproduced codec differences (corrected in 1A)

| Fixture | Android behavior | Windows behavior |
| --- | --- | --- |
| `version:"1"` in a DND-rule command | Accepts | Rejects invalid payload |
| `active:"true"` | Accepts | Rejects invalid payload |
| Duplicate `active:false, active:true` | Accepts resulting value | Rejects duplicate field |
| `command:{}` in media command | Throws `IllegalArgumentException` from decoder | Returns rejected decode result |
| Clipboard UUID `1-1-1-1-1` | Normalizes and accepts | Rejects noncanonical UUID |
| Media artist with embedded newline | Decodes/encodes snapshot | Rejects control-character metadata |

Probe source/output is under ignored `artifacts/stage0-audit/probes/`, `android-codec-probe.txt` and `windows-codec-probe.txt`. These diagnostics observed Stage 0 behavior; no app/test-source fix was made in that checkpoint. Stage 1A now checks the six regressions in the shared corpus: both codecs reject the invalid wire payloads, Android command decoding does not throw, and platform media controls are sanitized before valid encoding. The table is historical, not current Android behavior.

## UI review evidence

The smoke runner rendered flyout sample/disconnected/paused/no-media/long-metadata states and desktop/pairing light/dark states, including small desktop layouts and 150% raster images. Representative light flyout and dark desktop were visually inspected. Native materials, system accent, high contrast, screen-reader toggle semantics and complete keyboard/mixed-monitor review remain open. 125%/200% actual display scaling was not tested.

See `artifacts/stage0-audit/ui-smoke/` for audit renders. They omit native non-client chrome and do not establish every installed-window interaction.

## Missing security/reliability tests

Add focused tests for commitment/proof/key substitution, altered GCM tags/ciphertext, replay/reorder, peer decline, malformed controls, version downgrade, trust-save failure, forget during pairing, key loss, Android pending-command cancellation and simultaneous route closure. Expand the new `LivePhoneTransport` production coverage to App reconnect/forget/exit races, and test the actual Android service ownership seam. Windows close/cancellation is covered by 1B-W; revocation and platform integration still need proof that old callbacks cannot revive revoked state.

Reconnect coverage needs sample/pairing/exit cancellation, an unrelated BLE advertiser, address changes, no-network suspension, route promotion policy and scan/wakeup bounds. Capability tests need support/permission/user-enabled/route-availability distinctions and updates after revocation. Metadata normalization now has production-helper tests; cellular mapping still needs tests against pure production functions rather than independently constructed values.

## Physical-device release criteria

Use the intended Windows 11 PC and Android 12+ phone. Validate BLE peripheral support, actual fragmentation/notification flow, pairing approval and rejection, both routes, saved reconnect, forgetting/revocation, firewall/network changes, laptop sleep/resume, phone sleep/process death/OEM restrictions, media-session access/actions, automatic-rule/manual-DND interaction, clipboard foreground reads/background application and permission denial/revocation. Define a multi-SIM policy and verify it on compatible hardware.

Measure sustained idle/connected/disconnected CPU, memory, radio scans, wakeups, network bytes, startup latency and Android battery impact. No performance measurements were made by Stage 0. Passing automated checks is necessary but does not replace these gates.
