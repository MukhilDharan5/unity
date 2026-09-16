# Companion validation

Validated locally on Windows 11 x64 with .NET SDK 10.0.401, Microsoft OpenJDK 17, Android Gradle Plugin 8.8.0, Gradle 8.10.2, and Android SDK 34.

## Automated checks

- Windows Release application build: 0 warnings, 0 errors.
- Windows core checks: **25/25 passed**. Coverage includes the v1 codec, malformed and oversized input, state routing, media capability gating, DND semantics, clipboard routing, lifecycle fencing, authenticated transport contracts, mutual identity proof, matching confirmation codes, encrypted bidirectional messages, and maximum-size BLE fragmentation with sequence wrap.
- Cross-runtime secure-session check: **passed**. A real .NET TCP client and Kotlin/JVM server completed the v1 commitment, P-256 identity proof, matching six-digit SAS, mutual consent, encrypted hello, and bidirectional AES-GCM application exchange.
- Windows WPF smoke checks: passed. They instantiate the real XAML and view model, verify the tray icon, both theme palettes, active-only media visibility, controls, phone labels, DND separation, clipboard opt-in and echo suppression, compact sizing, and disconnect cleanup.
- Android debug compilation and APK assembly: passed.
- Android lint: passed with 0 errors; its remaining notices only report newer available dependency versions.
- Android JVM unit tests: **15/15 regular tests passed**, 0 failures and 0 errors. The additional opt-in cross-runtime test also passed in its dedicated run. Coverage includes logical messages, Unicode/null state, limits, DND-rule separation, clipboard, normalization, secure pairing/code agreement, and encrypted bidirectional messages.
- Packaged APK metadata: package `com.unity.connect.android`, minimum API 31, target API 34.

## Manual Windows checks

1. Launch `artifacts/PhoneCompanion/PhoneCompanion.exe`; verify one tray icon and no taskbar window.
2. Open the tray flyout and choose **Connect phone**. Verify both Wi-Fi endpoint entry and BLE discovery are offered.
3. Enable **Use sample data** and verify battery, network, DND, sound, and media controls. Pause playback and verify the media card collapses.
4. Check Windows light and dark themes and verify the white/black/light-blue palette follows the system.
5. Disable sample data and confirm unavailable values replace sample data.

## Physical-device validation still required

No physical Android phone was connected during this run. The implemented BLE peripheral/client and LAN server/client compile and their shared secure sessions pass automated tests, but radio and Android system behavior cannot be proven without hardware. Validate both routes, code confirmation, remembered reconnect, DND settings across the target phone, clipboard system UI, notification-listener media access, sleep/resume, OEM background restrictions, firewall behavior, and multi-SIM reporting on the intended devices.
