# Companion validation

Windows Stage 1B-W validation on 17 September 2026 used .NET SDK 10.0.401 on Windows 11 x64; no Windows source changed in the subsequent Android checkpoints. Android's last validation was Stage 1B-A2 on 19 September 2026 with OpenJDK 17, Android Gradle Plugin **8.2.2**, Gradle 8.10.2 and Android SDK 34. [testing.md](testing.md) records precise current results and corrected historical codec regressions. Earlier AGP 8.8.0 results described a different build configuration.

## Automated checks

- Desktop-window checks: shared phone state and active-only media visibility, sidebar navigation, disconnected screen, light/dark dashboard and pairing dialog renders, long-title truncation, switch-state display, minimum-size scrolling, 150% raster rendering, and close-to-tray/reopen lifecycle.

- Windows Release application build: 0 warnings, 0 errors.
- Windows core checks: **31/31 passed**. Five new owner tests verify late completion, single-use guards, canceled stop waits/concurrent disposal, send cancellation, fault cleanup and late frames. Existing coverage includes all 67 shared application fixtures with typed round-trips, the v1 codec, malformed and oversized input, state routing, media capability gating, DND semantics, clipboard routing, lifecycle fencing, authenticated transport contracts, mutual identity proof, matching confirmation codes, encrypted bidirectional messages, and maximum-size BLE fragmentation with sequence wrap.
- Cross-runtime secure-session check: **passed in Stage 0**, not rerun in 1A. A real .NET TCP client and Kotlin/JVM server completed the v1 commitment, P-256 identity proof, matching six-digit SAS, mutual consent, encrypted hello, and bidirectional AES-GCM application exchange.
- Windows WPF smoke checks: passed, including six live-transport scenarios with real secure handshakes over fake frame connections: remote close on both route kinds, late open, approval cancellation, send failure and blocked command cancellation. Existing checks instantiate real XAML/view model, verify the tray icon, themes, media availability, controls, phone labels, DND separation, clipboard defaults/echo suppression, compact sizing and disconnect cleanup; 20 renders.
- Android debug compilation and APK assembly: passed.
- Current Android lint: passed with **0 errors and 5 warnings**: one obsolete custom-lint API warning and four newer-dependency notices. SDK XML/Gradle deprecation warnings also appear. The first offline lint attempt lacked cached `lint-gradle:31.2.2`; resolving current tooling and rerunning unit/lint checks succeeded without changing dependency versions.
- Android JVM unit tests: **34/34 regular tests passed**, 0 failures and 0 errors; one optional interop skip. Ten production owner/collection cases cover publication/revocation, pending reservations, parent cancellation/IO interruption, route priority/fallback, simultaneous close, encrypted-send failure/cancellation and late observer cleanup. Four listener lifecycle cases cover stop during startup, late resource/callback rejection, replacement isolation, duplicate start and best-effort cleanup. Both runtimes pass the same 67 application fixtures. Existing coverage includes malformed UTF-8/random input, command direction, strict encoders, production media normalization, logical messages, Unicode/null state, limits, DND-rule separation, clipboard, secure pairing/code agreement and encrypted bidirectional messages. The additional opt-in TCP interop case last passed separately in Stage 0.
- Packaged APK metadata: package `com.unity.connect.android`, minimum API 31, target API 34.

## Manual Windows checks

1. Launch `artifacts/PhoneCompanion/PhoneCompanion.exe`; verify the desktop dashboard and one tray icon. Close the window and verify it stays available in the tray; choose **Open Unity Connect** to restore it. Use `--tray` for a quiet start.
2. Open the tray flyout and choose **Connect phone**. Verify both Wi-Fi endpoint entry and BLE discovery are offered.
3. Enable **Use sample data** and verify battery, network, DND, sound, and media controls. Pause playback and verify the media card collapses.
4. Check Windows light and dark themes and verify the dashboard, native title bar, flyout, pairing dialog, and tray menu follow the white/black/light-blue palette.
5. Disable sample data and confirm the desktop shows its connection screen and the flyout stops displaying sample values. Resize the dashboard and verify all lower controls remain reachable by scrolling.

## Physical-device validation still required

No physical Android phone was connected during this run. The implemented BLE peripheral/client and LAN server/client compile and their shared secure sessions pass automated tests, but radio and Android system behavior cannot be proven without hardware. Validate both routes, code confirmation, remembered reconnect, DND settings across the target phone, clipboard system UI, notification-listener media access, sleep/resume, OEM background restrictions, firewall behavior, and multi-SIM reporting on the intended devices.
