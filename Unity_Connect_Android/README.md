# Unity Connect Android

Android companion for the Windows Phone Companion tray app.

## Build

Use Android Studio with Microsoft/OpenJDK 17, Gradle 8.10.2, Android Gradle Plugin 8.8.0, and Android SDK 34.

```powershell
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :app:assembleDebug
```

The minimum supported version is Android 12/API 31. API 31 provides the nearby-device Bluetooth permissions used by the GATT peripheral and the platform APIs selected for the connection service.

## Included features

- Battery percentage and charging state.
- Active Wi-Fi/mobile-data state, friendly cellular generation, and simple signal level.
- Active media source/title/artist, playing state, capabilities, and session-targeted controls.
- Effective DND reporting plus an app-owned `AutomaticZenRule` condition controlled separately from manual/global DND.
- Normal/vibrate/silent reporting.
- Opt-in, text-only clipboard transfer with no history. Android reads its clipboard only after a visible **Send current clipboard** tap.
- Matching version 1 logical JSON messages for the Windows codec.
- Live BLE peripheral and LAN server connections with mutual identity proofs, a code confirmed on both devices, AES-256-GCM records, and replay rejection.

## Pair with Windows

1. Install and open the app, allow device access, then tap **Start pairing**.
2. On Windows, open the Phone Companion tray flyout and choose **Connect phone**.
3. Enter the Wi-Fi address shown by Android, or choose Bluetooth discovery.
4. Confirm the same six-digit code on both devices. Trust is stored only after both confirmations complete.

The transport and protocol paths are implemented and covered by JVM and .NET tests. A physical Android 12+ phone is still required to validate its BLE peripheral support, vendor battery restrictions, and the local network/firewall environment.
