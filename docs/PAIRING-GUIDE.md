# Pairing and device setup

The packaged apps implement pairing over Wi-Fi/LAN and BLE. Both routes use the same identity verification and encrypted session.

## Android development setup

1. Open `Unity_Connect_Android` in Android Studio.
2. Use JDK 17, Gradle 8.10.2, and Android SDK 34.
3. Build and install the debug app on Android 12 or newer.
4. Tap **Allow device access** for the Bluetooth, cellular, and notification runtime permissions you want to use.
5. Tap **Allow media access** and enable Unity Connect if you want the current media session to be reported.
6. Tap **Allow DND access** before using Companion DND. Android may show Unity Connect as an automatic DND rule.
7. Turn on **Clipboard sync** only when desired. Laptop-to-phone text can be applied while enabled; phone-to-laptop transfer requires tapping **Send current clipboard** while the app is visible.

## Pairing

1. Keep the phone and laptop on the same Wi-Fi network. Open Unity Connect and tap **Start pairing**.
2. Open the Windows tray flyout and select **Connect phone**.
3. Open **Connection** in the Windows app. Copy the `address:port` shown on Android into the embedded pairing panel and choose **Connect to this address**. You can choose **Connect with Bluetooth** instead if the phone supports BLE peripheral mode.
4. Compare the six-digit code. Select **Codes match** on both devices only when it is identical.
5. The Android app sends a complete phone-state snapshot after pairing. Windows then shows battery, network, DND, sound, and any playing audio.
6. Leave Bluetooth and Wi-Fi available. Windows authenticates the second route automatically and shows **Wi-Fi + Bluetooth** when both are ready.

If a saved phone was reset or reinstalled, choose **Forget paired phone** on Windows and **Forget laptop** on Android, then pair again. The Android foreground notification keeps the connection service available; vendor battery-saving settings can still stop it. Physical-device validation remains necessary for each phone model and network environment.
