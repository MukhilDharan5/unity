# Phone Companion

A small Windows tray app with an Android companion. The two apps pair over BLE or Wi-Fi/LAN, verify a six-digit code, remember the approved device identity, and protect every application message with an authenticated encrypted session.

## Try it

The verified Windows release is available as `artifacts/PhoneCompanion-Windows.zip`; extract it and run `PhoneCompanion.exe`. The unpacked copy is also at `artifacts/PhoneCompanion/PhoneCompanion.exe`. Keep its files together. It requires the **.NET 10 Desktop Runtime** and a supported Windows installation. The Android debug installer is `artifacts/UnityConnect-Android-debug.apk`. This build was tested on Windows 11 x64; the code's minimum Windows API level is Windows 10 build 19041.

```powershell
# From this repository, after building:
.\scripts\run.ps1 -Demo
```

Or launch `PhoneCompanion.exe --demo --show`. Without `--demo`, the app starts disconnected. Without `--show`, it starts quietly in the tray. Click the blue phone tray icon to open or dismiss the flyout. If Windows places it in the overflow area, open the tray's up-arrow menu. Choose **Connect phone** in the flyout or the tray menu to pair. Starting the app again opens the existing instance.

The sample is explicitly labeled **Sample** and **Sample data · No phone connected**. Previous/next cycle through three fictional sample tracks; play/pause changes sample playback. These controls use the same codec and state manager as the BLE and Wi-Fi paths. No sample values are presented as real phone data. The sample choice is not persisted.

The flyout follows the Windows app theme while running: white with black text in light mode, near-black with white text in dark mode, and light-blue accents in both. The media player appears only while audio is playing on the phone. Escape, clicking outside, and Alt+F4 dismiss the flyout while keeping the tray app running. Use Exit to release it. Media buttons have keyboard focus, tooltips, and accessible labels. Effective DND and sound remain truthful phone-state labels; a separate **Companion DND** switch controls only the Android app's own automatic rule. The clipboard switch is off by default and handles new plain text only, with no history.

## Build and verify

Install the .NET 10 SDK, then run:

```powershell
.\scripts\build.ps1
```

The script restores, builds Release with warnings treated as errors, runs the core checks and Windows UI smoke checks, then publishes to `artifacts/PhoneCompanion`. It puts CLI/cache files under `artifacts/dotnet-home`. There are no third-party application or test libraries. Windows WinRT APIs use Microsoft's pinned `Microsoft.Windows.SDK.NET.Ref` targeting pack, version `10.0.26100.57`.

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
| Companion DND | App-owned Android `AutomaticZenRule`; Windows control never changes manual/global DND directly |
| Clipboard | Opt-in new-text sync, 12 KiB limit, loop suppression, no stored history; Android sends only on a visible user action |
| Message layer | JSON v1 behind `IPhoneMessageCodec`; authenticated encryption and replay rejection below it |
| BLE connection | Windows central/client and Android peripheral/server with bounded fragmentation |
| Wi-Fi connection | Windows TCP client and Android TCP server with a displayed manual endpoint and Android mDNS advertisement |
| Pairing and trust | Mutual P-256 identity proofs, transcript-bound six-digit code, confirmation on both devices, remembered identity |
| Live incoming state | Android snapshots feed the Windows state manager; Windows commands return through the selected route |
| Android app | Builds with permissions, pairing UI, state collection, media commands, DND rule, and clipboard transfer |
| Hardware validation | Requires an Android 12+ physical phone with BLE peripheral support and this Windows PC |

Wi-Fi is the easiest first connection: the Android screen shows the address to enter on Windows. BLE discovery is also available from **Connect phone**. Windows tries the remembered Wi-Fi address and then BLE when it starts. Use **Connect phone** if the phone's address changed. No discovered device becomes trusted until both screens confirm the same code.

## Code map

```text
src/PhoneCompanion.Windows/
  App.xaml.cs                    Composition, sample selection, process lifetime
  TrayIconController.cs          Native tray icon and minimal context menu
  FlyoutWindow.xaml              Compact WPF view
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

ADB, scrcpy, audio streaming, hotspot control, file transfer, computer monitoring, and clipboard history are not included.

Windows implementation references: [Microsoft NotifyIcon overview](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms) and [Microsoft BLE advertisement watcher documentation](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher).
