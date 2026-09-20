# Phone Companion

The [progress and resume log](PROGRESS.md) records the current MVP checkpoint: automatic LAN discovery, concurrent BLE + Wi-Fi connections, reconnect/lifecycle coordination, live phone status, bidirectional media control, optional scrcpy launch, phone brightness/light sensing, opt-in laptop adaptive brightness, public/assisted headphone handoff, phone-internet assistance, laptop-to-phone audio streaming, bidirectional device locking, explicit Android access state, portable Android builds and typed connection policy are implemented. Work is proceeding implementation-first with minimal compile checks; broad validation is deferred. The [Stage 0 audit](CURRENT_STATE.md) is the historical assessment. The current encrypted v1 channel is the accepted interim MVP direction; any compatibility-changing security migration remains a separate post-MVP decision.

A Windows desktop control app with a compact tray flyout and an Android companion. The two apps pair over BLE or Wi-Fi/LAN, verify a six-digit code, remember the approved device identity, and then keep authenticated BLE and Wi-Fi sessions connected together when both are available. Every application message is protected by an authenticated encrypted session.

## Try it

The verified Windows release is available as `artifacts/PhoneCompanion-Windows.zip`; extract it and run `PhoneCompanion.exe`. The unpacked copy is also at `artifacts/PhoneCompanion/PhoneCompanion.exe`. Keep its files together. It requires the **.NET 10 Desktop Runtime** and a supported Windows installation. The Android debug installer is `artifacts/UnityConnect-Android-debug.apk`. This build was tested on Windows 11 x64; the code's minimum Windows API level is Windows 10 build 19041.

```powershell
# From this repository, after building:
.\scripts\run.ps1 -Demo
```

Launch `PhoneCompanion.exe` to open the desktop dashboard, or use `--demo` for sample data. Use `--tray` to start quietly in the tray, or `--flyout` to show quick controls. Click the blue phone tray icon to open or dismiss the flyout. If Windows places it in the overflow area, open the tray's up-arrow menu. Choose **Connect phone** to pair. Starting the app again opens the existing desktop window. Closing the desktop window hides it to the tray; **Open Unity Connect** restores it, and **Exit** in the tray menu quits the application.

The resizable desktop window provides Phone and Connection pages for the same existing controls and state as the flyout. Both views share one phone session and follow the system light/dark theme. The dashboard follows a Windows Settings layout: a compact icon rail, battery/network/connection summaries, grouped setting rows, neutral gray surfaces, and light-blue accents. Its native title bar, pairing dialog, and tray menu also follow the system theme. On supported integrated displays, an opt-in switch can use the phone's filtered light reading to adjust laptop brightness for the current app session.

The sample is explicitly labeled **Sample** and **Sample data · No phone connected**. Previous/next cycle through three fictional sample tracks; play/pause changes sample playback. These controls use the same codec and state manager as the BLE and Wi-Fi paths. No sample values are presented as real phone data. The sample choice is not persisted.

The flyout follows the Windows app theme while running: white with black text in light mode, near-black with white text in dark mode, and light-blue accents in both. The media player appears only while audio is playing on the phone. Escape, clicking outside, and Alt+F4 dismiss the flyout while keeping the tray app running. Use Exit to release it. Media buttons have keyboard focus, tooltips, and accessible labels. Effective DND and sound remain truthful phone-state labels; a separate **Companion DND** switch controls only the Android app's own automatic rule. The clipboard switch is off by default and handles new plain text only, with no history.

Each app can explicitly lock its trusted peer. Android requires a one-time system device-admin approval limited to force-lock before Windows can lock the phone. Windows also has a session-only **Lock Windows when phone is away** option with a two-minute reconnect grace and local-idle gate. It is off on every launch and arms only after a real authenticated phone connection. Unlock always uses the device's normal sign-in.

## Build and verify

Install the .NET 10 SDK, then run:

```powershell
.\scripts\build.ps1
```

The script restores, builds Release with warnings treated as errors, runs the core checks and Windows UI smoke checks, then publishes to `artifacts/PhoneCompanion`. It puts CLI/cache files under `artifacts/dotnet-home`. Windows uses Microsoft's `System.Management` package for integrated-display brightness, NAudio 3.1 for WASAPI loopback capture, and the pinned `Microsoft.Windows.SDK.NET.Ref` targeting pack, version `10.0.26100.57`.

For an offline machine with the targeting pack's `.nupkg` cached locally:

```powershell
.\scripts\build.ps1 -OfflinePackageSource "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.net.ref\10.0.26100.57"
```

The dependency-free test executables return a nonzero exit code on failure. They are run by the script; they are not `dotnet test` projects.

```powershell
dotnet run --project tests/PhoneCompanion.Tests -c Release --no-build
dotnet run --project tests/PhoneCompanion.Windows.SmokeTests -c Release --no-build -- artifacts/ui-smoke
```

UI checks instantiate the actual XAML, view model and tray controller, exercise commands, verify media-player visibility, apply both theme palettes, and render disconnected, light, dark, paused, long-metadata and no-media states. A 150% raster render checks scale clarity; it does not replace mixed-monitor hardware testing. The smoke-test tray icon is disposed at the end.

## What works now

| Area | Phase 1 status |
| --- | --- |
| Tray and flyout | Implemented, with single-instance behavior and clean exit |
| Battery, Wi-Fi/mobile data, cellular, media, DND, sound | Models, display, unavailable states, Android collectors, inbound updates |
| Previous / play-pause / next | Capability-gated live commands plus working sample provider |
| Windows media on Android | Current Windows media source/title/artist/playback and capability-gated controls |
| Open phone | Launches a detected scrcpy installation without using ADB as the companion transport |
| Brightness and light | Reports and controls phone brightness; optionally maps valid phone ambient light to supported laptop displays with smoothing, hysteresis and bounded transitions |
| Bluetooth audio | Reports the current Android A2DP output; Windows requests release, opens Bluetooth settings, and can optionally foreground the phone flow through an authorized ADB installation |
| Phone internet | When Windows reports no internet, an explicit action requests Android tethering settings and opens Windows Wi-Fi for a saved hotspot; optional ADB only foregrounds the phone settings screen |
| Laptop audio on phone | Explicitly confirmed Windows WASAPI loopback capture, separately encrypted Wi-Fi PCM stream, Android `AudioTrack` playback, and stop controls on both devices |
| Device locking | Explicit authenticated lock requests in both directions; optional session-only Windows proximity lock with reconnect and idle safeguards; no remote unlock |
| Companion DND | App-owned Android `AutomaticZenRule`; Windows control never changes manual/global DND directly |
| Clipboard | Opt-in new-text sync, 12 KiB limit, loop suppression, no stored history; Android sends only on a visible user action |
| Message layer | JSON v1 behind `IPhoneMessageCodec`; authenticated encryption and replay rejection below it |
| BLE connection | Windows central/client and Android peripheral/server with bounded fragmentation |
| Wi-Fi connection | Windows discovers Android mDNS automatically, with manual endpoint fallback; TCP sessions still require secure identity verification |
| Concurrent routes | BLE and Wi-Fi remain authenticated together; Wi-Fi carries application messages first and BLE stays warm for fallback without duplicate command replay |
| Pairing and trust | Mutual P-256 identity proofs, transcript-bound six-digit code, confirmation on both devices, remembered identity |
| Live incoming state | Android snapshots feed the Windows state manager; both routes stay connected while application messages use one deterministic primary route |
| Android app | Builds with permissions, pairing UI, state collection, media commands, DND rule, and clipboard transfer |
| Hardware validation | Requires an Android 12+ physical phone with BLE peripheral support and this Windows PC |

Wi-Fi is the easiest first connection: the Android screen shows the address to enter on Windows. BLE discovery is also available from **Connect phone**. After either route authenticates the remembered phone, Windows continues connecting the other route and keeps both alive. Wi-Fi is preferred for messages; BLE is ready if Wi-Fi drops. Use **Connect phone** if the phone's address changed. No discovered device becomes trusted until both screens confirm the same code.

**Open phone** looks for `scrcpy.exe` beside the Windows app, under `scrcpy/` or `tools/scrcpy/`, in common install locations, on `PATH`, or at the path set in `UNITY_CONNECT_SCRCPY`. scrcpy and Android debugging authorization remain optional and separate from Unity Connect pairing.

## Code map

```text
src/PhoneCompanion.Windows/
  App.xaml.cs                    Composition, sample selection, process lifetime
  TrayIconController.cs          Native tray icon and minimal context menu
  MainWindow.xaml               Resizable desktop dashboard and connection page
  FlyoutWindow.xaml              Compact WPF view
  UiResources.xaml              Shared controls, icons, and visual styles
  ViewModels/PhoneViewModel.cs    User-facing labels and media commands
  Connection/                     Live BLE/Wi-Fi framing, secure pairing, identity storage
src/PhoneCompanion.Core/
  Models/                        Phone state and media commands
  State/PhoneStateManager.cs      Active route, updates, capability checks, stale-session fencing
  Protocol/                      Typed messages and replaceable JSON v1 codec
  Transports/                    Mock, secure records, BLE/TCP framing, transport contracts
tests/                           Core behavior and rendered WPF checks
Unity_Connect_Android/           Android companion app and JVM tests
docs/PROTOCOL.md                 Exact v1 wire proposal and examples
docs/SECURE-SESSION-v1.md        Exact implemented handshake and encrypted record format
docs/VALIDATION.md               Verified checks and remaining hardware validation
```

Silent hotspot toggling, remote device unlock, phone-to-laptop audio, file transfer, computer monitoring, and clipboard history are not included. scrcpy and the narrow ADB settings accelerator remain optional local tools outside the companion transport.

Windows implementation references: [Microsoft NotifyIcon overview](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms) and [Microsoft BLE advertisement watcher documentation](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher).
