# Development and reproducible verification

Baseline audited and checkpoints 1A/1B-W/1B-A1/1B-A2 completed through 19 September 2026. [PROGRESS.md](../PROGRESS.md) is the resume log. Build artifacts/caches are ignored. Stage 0 preserved the existing user edits; do not reset them as part of foundation work.

## Current prerequisites

Windows: .NET SDK 10 (audit used 10.0.401), Windows desktop runtime/targeting support, Microsoft Windows SDK .NET reference pack 10.0.26100.57. `Directory.Build.props` enables nullable, deterministic builds and warnings as errors. There is no `global.json` SDK pin or CI configuration.

Android: JDK 17, SDK platform 34, accepted SDK licenses, Gradle wrapper 8.10.2, AGP 8.2.2, Kotlin 1.9.20 and compatible cached/downloaded AndroidX/Compose dependencies. Local SDK configuration may use ignored `local.properties` or `ANDROID_HOME`. `build_app.bat` resolves its own project directory, respects configured SDK/JDK variables, detects common local installs and forwards optional Gradle arguments.

## Existing Windows workflow

```powershell
.\scripts\build.ps1
# If the pinned SDK targeting package is available locally:
.\scripts\build.ps1 -OfflinePackageSource "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.net.ref\10.0.26100.57"
```

The script restores, builds Release, runs both executable check projects and publishes to `artifacts/PhoneCompanion`. It sets CLI home to a workspace artifact directory. It does not create an installer, self-contained runtime or ZIP. `-SkipTests` explicitly skips checks.

For cached-dependency audit checks without publishing:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'artifacts/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build PhoneCompanion.slnx -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tests/PhoneCompanion.Tests -c Release --no-build
dotnet run --project tests/PhoneCompanion.Windows.SmokeTests -c Release --no-build -- artifacts/stage0-audit/ui-smoke
```

These are executable harnesses, not test-SDK projects. `dotnet test` is not a substitute for running them. Smoke checks first run real live-transport/secure-handshake lifecycle scenarios with in-memory wires, then temporarily create/dispose a tray icon and render real WPF content; run them on Windows with desktop support. For checkpoint 1B-W renders use `artifacts/stage1bw/ui-smoke` in place of the historical audit output path.

## Android workflow

```powershell
# Set JAVA_HOME to your JDK 17 and ANDROID_HOME to your local SDK.
Set-Location Unity_Connect_Android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --no-daemon
# Use --offline only if this exact plugin/dependency toolchain is fully cached.
```

The debug APK is `app/build/outputs/apk/debug/app-debug.apk`. JVM reports are `app/build/test-results/testDebugUnitTest/` and `app/build/reports/tests/testDebugUnitTest/`; lint reports are `app/build/reports/lint-results-debug.*`. Debug assembly uses the standard debug-signing mechanism; no release signing/distribution validation was done. No Android instrumentation test project exists.

Stage 1B-A2 runs 34 regular JVM tests and one optional interop skip. The owner/collection cases use real secure handshakes with fictional identities/fake pipes, controlled cleanup barriers and a close-interrupted IO latch. Four listener cases exercise the production Kotlin lifecycle seam with fake resources: stop during startup, stale callback suppression, replacement isolation, duplicate start and cleanup continuation after failure. No new test dependencies were added. JVM tests do not instantiate Android Service/UI/SharedPreferences or actual Bluetooth/NSD APIs. See [testing.md](testing.md) for exact checkpoint evidence.

The audit's first offline lint attempt lacked AGP 8.2.2's `lint-gradle:31.2.2`, although app compilation was cached. Resolving tooling online and rerunning unit/lint checks succeeded. Do not change AGP simply to make an old cache work. Gradle still emits SDK XML/deprecation warnings.

## Optional cross-runtime fixture

The ordinary core and Android JVM suites both consume `tests/Fixtures/protocol-v1.json` (67 cases at Stage 1A). The .NET project copies it to output; Android adds the same directory to test resources. Add agreed application regression payloads there rather than maintaining two independent case lists. This corpus is distinct from the optional secure-session TCP fixture below.

`CrossRuntimeInteropTest` is skipped unless `UNITY_CONNECT_INTEROP_READY` is set to a temporary file path. The JVM fixture writes its TCP port there, waits up to 60 seconds for a client, automatically approves its fictional identities, and deletes the file after completion. While the fixture is running, invoke:

```powershell
dotnet run --project tests/PhoneCompanion.Tests -c Release --no-build -- --interop-client "127.0.0.1:<port-from-ready-file>"
```

Run the JVM fixture in a separate process with the compiled test runtime classpath or a dedicated Gradle test selection. Auto-approval is test-only, never an application pairing option. The audit used JUnitCore against the current compiled Kotlin classes and cached runtime dependencies, preserving regular Gradle reports. A checked-in orchestration helper is a useful Stage 1 addition; it does not exist yet.

## Proposed foundation work

Shared application fixtures and strict metadata/validation correction are complete in 1A. Remaining work: pin compatible toolchains; make helpers relative/configurable; centralize runtime policy settings; build one testable connection ownership path per platform; add redacted diagnostics. Any new dependency/security library, UI migration, compatibility floor or persistence schema needs the relevant decision before implementation. See [roadmap](roadmap.md) and [decision records](decisions/ADR-001-transport-architecture.md).
