# Android companion validation report

## Automated

- `:app:compileDebugKotlin`: passed.
- `:app:testDebugUnitTest`: passed, **15 regular tests / 0 failures / 0 errors**. The opt-in cross-runtime case is skipped during an ordinary unit run and passed separately against the actual .NET implementation.
- `:app:assembleDebug`: passed.
- `:app:lintDebug`: passed with 0 errors. Five informational dependency-version warnings remain because this build keeps the mutually compatible pinned Kotlin/Compose toolchain.
- Codec tests cover full/null snapshots, Unicode media metadata, unknown fields, logical-frame and nesting limits, media commands, DND semantics, clipboard UUID round-trip, and the 12 KiB text limit.
- Secure-session tests perform simultaneous client/server commitment, P-256 identity proof, matching six-digit code derivation, confirmation, encrypted hello exchange, and application messages in both directions.
- Cross-runtime testing connected the compiled Windows .NET client to the Kotlin/JVM TCP server and verified the same pairing code plus encrypted traffic in both directions.
- Normalization tests cover battery clamping, friendly cellular categories, and media capabilities.
- The packaged APK reports package `com.unity.connect.android`, minimum API 31, and target API 34.

## Hardware boundary

No emulator UI run or physical-device run was performed. Android notification-policy settings, `AutomaticZenRule` activation, notification-listener/media access, system clipboard behavior, BLE GATT peripheral behavior, LAN NSD/TCP reachability, OEM battery policies, and multi-SIM behavior require device testing.

The Android and Windows transport code now carries application messages inside the implemented authenticated, encrypted, replay-ordered session. Automated results establish protocol behavior and successful packaging; they do not replace physical radio and Android integration tests.
